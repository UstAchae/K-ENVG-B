# Turns the parts export_parts.py wrote (Blender frame) into what the Unity builder reads, per colour:
#   item/Unity/Assets/Source/<colour>/device.bin  the flipping device (body), in the device node's frame
#                                     glass.bin   its glass (eyepieces, objectives, thermal window, sensor window)
#                                     mount.bin   the shroud adapter, in the prefab root's frame (does not flip)
#                                     info.json   thermal lens marker, bounds
#                                     albedo/spec/normal.png  maps for the game's p0 shader
#
# Frames. The device node copies the GPNVG-18 (nvg_l3_gpnvg-18_anvis_LOD0): eyepieces toward +Y (the eye looks along
# -Y), mount toward +Z, the wearer's right +X. The FBX (Blender, right-handed, Z up) faces -Y with its right at -X,
# so x is mirrored; the device node's origin is put where the GPNVG-18's eyecups are (a common frame for K-3PS-C's
# eyepiece geometry). Where the whole ENVG-B sits is decided by its mount, not its eyes: Mount_01 is seated flat on
# the helmet shroud at the slot (see "Seating" below), the device hangs from it as modelled and flips at its own
# mount knuckle; the hinge pose goes to info.json for the Unity builder.
#
# Left out: the objective lens caps (closed over the objectives; they are off when the device is in use) and the
# battery pack with its cable (it would sit inside the head).
# Run: python item/prepare.py <parts dir> <textures dir>
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image
from native_look import match_spec, stretch_albedo

ITEM = Path(__file__).resolve().parent
UNITY = ITEM / 'Unity/Assets/Source'
COLOURS = ('Black', 'Tan')
DEVICE = ['Tube_R', 'Tube_L', 'Eyecup_R', 'Eyecup_L', 'Bridge', 'Mount_02', 'Mount_03', 'Mount_04', 'Mount_05']
GLASS = ['Opacity_R', 'Opacity_L', 'Opacity_Mid', 'Opacity_Top']
MOUNT = ['Mount_01']
THERMAL_WINDOW = 'Opacity_Mid'
# Where the eyecup rims go: the GPNVG-18's eyecups, midway between its eyes (x 0), plate top y 0.0505, z -0.0757.
# Each colour sits elsewhere in the FBX (side by side), so the offset is measured per colour from its eyecups.
EYES = np.array([0.0, 0.0505, -0.0757])
AXIS_POS = np.array([-0.000388, -0.032200, 0.045600])  # GPNVG-18 "axis" localPosition
AXIS_ROT = (-0.4786032, 0.0, 0.0, 0.8780313)  # its deployed rotation (CurveRotator.OnRotation, sign flipped)
# GPNVG-18's CurveRotator quaternions as stored (deployed, flipped up); the ENVG-B flips by the same turn.
GP_ON = (0.47860321, -5.5665769e-08, 1.9227921e-08, -0.87803131)
GP_OFF = (0.98575121, -3.6230801e-08, -4.6429719e-08, 0.16821051)
# Seating on the helmet. The prefab root sits on the helmet's mod_nvg node, which lies on the front face of the NVG
# shroud: in the root frame the shroud face is the plane z = 0 (the helmet behind it, z < 0) and +Y runs up it,
# measured on the Ops-Core FAST with the GPNVG-18's G24 base, whose tongue enters the shroud at the origin. The
# ENVG-B's shroud plate (Mount_01, a flat plate) is turned about X until it lies parallel to that face, its centre
# put on the slot's centre line at PLATE_Y, and the whole ENVG-B moved with it as one rigid body, until its rearmost
# point touches the shroud face. The device stays where the mount holds it (the eyecups no longer chase the GPNVG-18's
# eyes; an adjustable mount can bring them to the eyes later).
PLATE_Y = 0.005
SEAT_GAP = 0.0005
TEX_SIZE = 2048
NORMAL_DX = False


def qmat(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def mirror(p): return np.stack([-p[:, 0], p[:, 1], p[:, 2]], 1)


def offset(parts, colour):
    cups = mirror(np.vstack([np.load(parts / f'AN-PSQ-42_{colour}_Eyecup_{s}.npz')['pos'] for s in 'RL']))
    lo, hi = cups.min(0), cups.max(0)
    return EYES - np.array([(lo[0] + hi[0]) / 2, hi[1], (lo[2] + hi[2]) / 2])


def rot_x(a):
    c, s = np.cos(a), np.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)


def qx(a): return (np.sin(a / 2), 0.0, 0.0, np.cos(a / 2))


def plate(pts, tol=0.0008, iters=4000):
    """The largest flat face among the points (RANSAC, seeded), refined by a least-squares fit."""
    rng = np.random.default_rng(1)
    pts = np.unique(pts.round(5), axis=0)
    best = None
    for _ in range(iters):
        a, b, c = pts[rng.choice(len(pts), 3, replace=False)]
        n = np.cross(b - a, c - a); l = np.linalg.norm(n)
        if l < 1e-9: continue
        inl = np.abs((pts - a) @ (n / l)) < tol
        if best is None or inl.sum() > best.sum(): best = inl
    P = pts[best]; c = P.mean(0)
    n = np.linalg.svd(P - c)[2][2]
    return (n if n[2] > 0 else -n), c


def seat_for(parts, colour):
    """Rigid correction (rotation about X, translation) seating Mount_01 on the shroud face, applied in the root
    frame after the deployed pose."""
    m = load(parts, colour, MOUNT, root_frame=True, seat=None)[0]
    n, c = plate(m)
    a = np.arctan2(n[1], n[2])  # rotating by a about X turns the plate's normal onto +Z
    R = rot_x(a)
    m2 = (m - c) @ R.T + c
    t = np.array([-c[0], PLATE_Y - c[1], SEAT_GAP - m2[:, 2].min()]) + c - R @ c
    print(f'{colour}: shroud plate normal {n.round(3)}, turned {np.degrees(a):.1f} deg about X, moved {(t + R @ c - c).round(4)}')
    return R, t, a


def load(parts, colour, names, root_frame=False, seat=None):
    P, N, U = [], [], []
    off = offset(parts, colour)
    for n in names:
        d = np.load(parts / f'AN-PSQ-42_{colour}_{n}.npz')
        p, nr = mirror(d['pos']) + off, mirror(d['nor'])
        if root_frame:
            # Deployed, in the root frame: the GPNVG-18's hinge pose, then the seat correction.
            R = qmat(AXIS_ROT); p = AXIS_POS + p @ R.T; nr = nr @ R.T
            if seat is not None:
                Rs, ts = seat; p = p @ Rs.T + ts; nr = nr @ Rs.T
        # The mirror reverses the winding: swap two corners of every triangle.
        idx = np.arange(len(p)).reshape(-1, 3)[:, [0, 2, 1]].ravel()
        P.append(p[idx]); N.append(nr[idx]); U.append(d['uv'][idx])
    return np.concatenate(P), np.concatenate(N), np.concatenate(U)


def weld(P, N, U):
    """Shares corners with identical position, normal and UV (the export has one vertex per triangle corner)."""
    key = np.round(np.hstack([P * 1e5, N * 1e3, U * 1e5])).astype(np.int64)
    _, first, inverse = np.unique(key, axis=0, return_index=True, return_inverse=True)
    return P[first], N[first], U[first], inverse.ravel().astype(np.int32)


def write_mesh(path, P, N, U):
    P, N, U, tri = weld(P, N, U)
    with open(path, 'wb') as f:
        np.array([len(P), len(tri) // 3, 1], np.int32).tofile(f)
        P.astype(np.float32).tofile(f); N.astype(np.float32).tofile(f); U.astype(np.float32).tofile(f)
        np.array([len(tri)], np.int32).tofile(f); tri.tofile(f)
    print(f'{path.relative_to(ITEM)}: {len(P)} vertices, {len(tri) // 3} triangles')
    return P


def channel(path):
    return np.asarray(Image.open(path).convert('L').resize((TEX_SIZE, TEX_SIZE), Image.LANCZOS)).astype(np.float64) / 255


def textures(tex, colour, out, shared):
    base = np.asarray(Image.open(tex / f'AN-PSQ-42_{colour}_albedo.png').convert('RGB').resize((TEX_SIZE, TEX_SIZE), Image.LANCZOS)).astype(np.float64) / 255
    ao, rough, metal = shared
    gloss = 1 - rough
    # Albedo alpha is the p0 shader's cubemap reflection strength (native items ~0.1); spec rgb its specular
    # strength; both from roughness and metalness, the same conversion as the clip-ons.
    albedo = base * (0.25 + 0.75 * ao[..., None])
    painted = base.max(-1) > 2 / 255
    albedo = stretch_albedo(albedo, painted)
    reflect = np.clip(0.04 + 0.30 * metal * gloss + 0.12 * gloss ** 2, 0, 0.5)
    spec = match_spec((0.15 + 0.85 * gloss ** 1.5 * (0.5 + 0.5 * metal)) * (0.4 + 0.6 * ao), painted)
    save = lambda name, rgb, alpha=1.0: Image.fromarray((np.clip(np.dstack([rgb, np.broadcast_to(alpha, rgb.shape[:2])]), 0, 1) * 255).astype(np.uint8), 'RGBA').save(out / f'{name}.png')
    save('albedo', albedo, reflect)
    save('spec', np.dstack([spec] * 3))
    normal = np.asarray(Image.open(tex / 'AN-PSQ-42_normal.png').convert('RGB').resize((TEX_SIZE, TEX_SIZE), Image.LANCZOS)).astype(np.float64) / 255
    if NORMAL_DX: normal[..., 1] = 1 - normal[..., 1]
    save('normal', normal)


def main(parts, tex):
    shared = tuple(channel(tex / f'AN-PSQ-42_{k}.png') for k in ('ao', 'roughness', 'metalness'))
    for colour in COLOURS:
        out = UNITY / colour.lower(); out.mkdir(parents=True, exist_ok=True)
        Rs, ts, a = seat_for(parts, colour)
        dev = write_mesh(out / 'device.bin', *load(parts, colour, DEVICE))
        glass = write_mesh(out / 'glass.bin', *load(parts, colour, GLASS))
        write_mesh(out / 'mount.bin', *load(parts, colour, MOUNT, root_frame=True, seat=(Rs, ts)))
        # Thermal window: centre of its disc; the device looks along -Y.
        w = load(parts, colour, [THERMAL_WINDOW])[0]
        allp = np.vstack([dev, glass])
        # The hinge: the knuckle where Mount_02 turns on the shroud plate (its bounding-box centre), deployed and
        # seated, in the root frame. The axis sits there with the seated deployed rotation; the device node is
        # offset inside it so that, deployed, every point lands where the seated assembly puts it.
        Ron = qmat(AXIS_ROT)
        knuckle_lod = load(parts, colour, ['Mount_02'])[0]
        knuckle_lod = (knuckle_lod.min(0) + knuckle_lod.max(0)) / 2
        def seated(p): return Rs @ (AXIS_POS + Ron @ p) + ts
        pivot = seated(knuckle_lod)
        q_on = qmul(qx(a), GP_ON); q_off = qmul(qx(a), GP_OFF)
        Q = qmat(q_on)
        node = Q.T @ (seated(np.zeros(3)) - pivot)
        info = {'colour': colour.lower(),
                'thermalLens': ((w.min(0) + w.max(0)) / 2).tolist(),
                'boundsMin': allp.min(0).tolist(), 'boundsMax': allp.max(0).tolist(),
                'axisPos': pivot.tolist(), 'onRotation': list(q_on), 'offRotation': list(q_off),
                'devicePos': node.tolist()}
        assert np.allclose(pivot + Q @ (node + knuckle_lod), seated(knuckle_lod), atol=1e-6)
        print(colour, 'hinge at', pivot.round(4), 'device node offset', node.round(4))
        (out / 'info.json').write_text(json.dumps(info, indent=1))
        print(colour, 'thermal lens at', np.round(info['thermalLens'], 4))
        textures(tex, colour, out, shared)


if __name__ == '__main__':
    main(Path(sys.argv[1]), Path(sys.argv[2]))
