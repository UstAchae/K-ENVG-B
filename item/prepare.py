# Turns the parts export_parts.py wrote (Blender frame) into what the Unity builder reads, per colour:
#   item/Unity/Assets/Source/<colour>/device.bin  the flipping device (body), in the device node's frame
#                                     glass.bin   its glass (eyepieces, objectives, thermal window, sensor window)
#                                     mount.bin   the shroud adapter, in the prefab root's frame (does not flip)
#                                     info.json   thermal lens marker, bounds
#                                     albedo/spec/normal.png  maps for the game's p0 shader
#
# Frames. The device node copies the GPNVG-18 (nvg_l3_gpnvg-18_anvis_LOD0): eyepieces toward +Y (the eye looks along
# -Y), mount toward +Z, the wearer's right +X. The FBX (Blender, right-handed, Z up) faces -Y with its right at -X,
# so x is mirrored; the eyecup rims are placed where the GPNVG-18's eyecups are (midway between each eye's two
# lenses, at its plate top), which also lands Mount_01 on the GPNVG-18 base's shroud interface. The prefab root and
# the hinge ("axis") are the GPNVG-18's, with the axis at its deployed rotation.
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


def load(parts, colour, names, root_frame=False):
    P, N, U = [], [], []
    off = offset(parts, colour)
    for n in names:
        d = np.load(parts / f'AN-PSQ-42_{colour}_{n}.npz')
        p, nr = mirror(d['pos']) + off, mirror(d['nor'])
        if root_frame:
            R = qmat(AXIS_ROT); p = AXIS_POS + p @ R.T; nr = nr @ R.T
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
        dev = write_mesh(out / 'device.bin', *load(parts, colour, DEVICE))
        glass = write_mesh(out / 'glass.bin', *load(parts, colour, GLASS))
        write_mesh(out / 'mount.bin', *load(parts, colour, MOUNT, root_frame=True))
        # Thermal window: centre of its disc; the device looks along -Y.
        w = load(parts, colour, [THERMAL_WINDOW])[0]
        allp = np.vstack([dev, glass])
        info = {'colour': colour.lower(),
                'thermalLens': ((w.min(0) + w.max(0)) / 2).tolist(),
                'boundsMin': allp.min(0).tolist(), 'boundsMax': allp.max(0).tolist()}
        (out / 'info.json').write_text(json.dumps(info, indent=1))
        print(colour, 'thermal lens at', np.round(info['thermalLens'], 4))
        textures(tex, colour, out, shared)


if __name__ == '__main__':
    main(Path(sys.argv[1]), Path(sys.argv[2]))
