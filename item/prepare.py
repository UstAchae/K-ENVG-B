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
BODY = ['Bridge']  # the device node's own mesh (the mount is jointed, the pods turn, the eyecups are an item)
GLASS = ['Opacity_Top']  # the bridge's window
EYECUPS = ['Eyecup_R', 'Eyecup_L']  # the removable eyecup pair, its own item; mesh kept in the device frame
# The pods. Each hangs by an arm from its own hinge barrel at an end of the bridge, along the device's Y (fore-aft):
# cylinders fitted to the bridge's barrels and the left arm's eye (r 9 mm; the right arm's eye shares the right
# barrel's axis, its knuckle seen around it), and flips up and outward about it (user: "两个目镜是可以横着掰上去的").
# The right pod carries the thermal module (its window Opacity_Mid sits in Tube_R's housing). Pivots are on the
# axes, at the barrels' middle; nodes envgb_pod_R/L under the device node, identity = deployed. Turned by
# AngleAxis(POD_DEG * t, down) for the right and (.., up) for the left, both swing outward and up; at POD_DEG they
# clear the bridge, the mount and the other pod (1.4 mm and more; past ~170 deg they reach the helmet's front).
# The pivots are the model's own: the FBX hangs Tube_L/Tube_R (with their glass and eyecups) from the Bridge with
# their origins on these axes (our fits agree to 0.1 mm); it gives no animation or limits. POD_DEG 90 (user: "先定
# 90°"): the pods stand out level to the sides; at 150 the eyecups reached the helmet's brim.
PODS = {'envgb_pod_R': {'body': ['Tube_R'], 'glass': ['Opacity_R', 'Opacity_Mid'], 'eyecup': 'Eyecup_R',
                        'pivot': (0.04526, -0.04644, -0.02494), 'axis': (0, -1, 0)},
        'envgb_pod_L': {'body': ['Tube_L'], 'glass': ['Opacity_L'], 'eyecup': 'Eyecup_L',
                        'pivot': (-0.03077, -0.04644, -0.02498), 'axis': (0, 1, 0)}}
POD_DEG = 90
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
# Seating on the helmet. The prefab root sits on the helmet's mod_nvg node (the game turns it a further +90 deg about
# X there, as for every NVG; seen in game on the FLUX), which lies on the front face of the NVG shroud: in the root
# frame the shroud face is the plane z = 0 (the helmet behind it, z < 0) and +Y runs up it, checked on the FLUX with
# the GPNVG-18's G24 base, whose back sits flat on that face at the origin. The
# ENVG-B's shroud plate (Mount_01, a flat plate) is turned about X until it lies parallel to that face, its centre
# put on the slot's centre line at PLATE_Y, and the whole ENVG-B moved with it as one rigid body, until its rearmost
# point touches the shroud face. The device stays where the mount holds it (the eyecups no longer chase the GPNVG-18's
# eyes; an adjustable mount can bring them to the eyes later).
# The FLUX shroud face is flat from y -0.03 to +0.045, with lips standing ~6 mm proud at y -0.03..-0.025 and
# +0.03..+0.04. At -0.003 the plate (about 41 mm tall) sits between the lips, 7 mm above the lower one; it was at
# 0.005 (touching the upper lip), which left the height adjustment only 3 mm downward (user: "往下还能给点距离吗").
PLATE_Y = -0.003
SEAT_GAP = 0.0005
# Joints of the mount, for K-3PS-C's adjustments (its NightVisionMount): measured on the seated, deployed model in
# the root frame, kept here in the device node's frame so they move with the seat. The flip hinge is the barrel
# between Mount_02 and Mount_03 (circle fit of Mount_03's eye, 0.14 mm rms), the tilt pin the boss between Mount_03
# and Mount_04 (fits of both, 0.5 mm rms), both along X. Mount_05 slides in Mount_04's rail, which runs 6.6 deg off
# the optical axis.
HINGE_DEV = np.array([0.00229, 0.00507, 0.01291])
TILT_PIN_DEV = np.array([0.00229, -0.03127, 0.01166])
RAIL_DEV = np.array([0.0, -0.9934, -0.1149])  # +Z of the slide node: along the rail, away from the face
# Flip-up: the GPNVG-18's 142 deg about its own hinge laid the device onto the helmet top ("too far"); about the
# ENVG-B's hinge 125 deg leaves it just above the helmet's front edge (about 6 mm clear on the FLUX).
FLIP_DEG = 125.0
SCREW = ['rod', 'knob', 'knob2']  # Mount_01's islands that turn with the height screw (by vertex count below)
MOUNT01_ISLANDS = {518: 'plate', 227: 'rail', 69: 'rod', 108: 'knob', 60: 'knob2', 12: 'stop'}
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


def mat2quat(R):
    U, _, Vt = np.linalg.svd(R); R = U @ Vt  # remove float drift (the stored quaternions are unit only to ~1e-8)
    w = np.sqrt(max(0.0, 1 + R[0, 0] + R[1, 1] + R[2, 2])) / 2
    x = np.sqrt(max(0.0, 1 + R[0, 0] - R[1, 1] - R[2, 2])) / 2
    y = np.sqrt(max(0.0, 1 - R[0, 0] + R[1, 1] - R[2, 2])) / 2
    z = np.sqrt(max(0.0, 1 - R[0, 0] - R[1, 1] + R[2, 2])) / 2
    x = np.copysign(x, R[2, 1] - R[1, 2]); y = np.copysign(y, R[0, 2] - R[2, 0]); z = np.copysign(z, R[1, 0] - R[0, 1])
    q = np.array([x, y, z, w]); q /= np.linalg.norm(q)
    assert np.allclose(qmat(q), R, atol=1e-6)
    return q


def frame(along, axis):
    """Rotation whose local `axis` ('x', 'y' or 'z') points along `along`, keeping X lateral (root X) where it can."""
    along = along / np.linalg.norm(along)
    if axis == 'x': return np.eye(3)  # the pins run along root X already
    lat = np.array([1.0, 0, 0]); lat = lat - (lat @ along) * along; lat /= np.linalg.norm(lat)
    if axis == 'y': return np.column_stack([lat, along, np.cross(lat, along)])
    return np.column_stack([lat, np.cross(along, lat), along])


def islands(p):
    """Triangle labels of the separate mesh islands (corners shared by position)."""
    key = np.round(p * 1e5).astype(np.int64)
    _, inv = np.unique(key, axis=0, return_inverse=True); inv = inv.ravel()
    par = np.arange(inv.max() + 1)
    def find(x):
        while par[x] != x: par[x] = par[par[x]]; x = par[x]
        return x
    for t in inv.reshape(-1, 3):
        r = find(t[0])
        for c in t[1:]: par[find(c)] = r
    return np.array([find(i) for i in inv[::3]])


# The battery pack on the back of the helmet and its cable, as the GPNVG-18 carries its own (one item, on the part
# that does not flip). Measured from the GPNVG-18's base in the root frame (FLUX check): its pack is centred at
# GP_PACK, its long axis X, its height GP_PACK_UP, its thickness GP_PACK_OUT (away from the helmet), 43 mm thick; its
# cable leaves the base on the right and runs back along the helmet's right side. The ENVG-B's own box (the FBX
# Battery's box islands, without its cable, which ran to the flipping device) takes the pack's place with the same
# inner face; a 5 mm cable of our own follows the GPNVG-18's route from the shroud plate to the box.
# The pack's helmet-side face, as the user asked ("照着 GPNVG-18 的法线来做"): its normal (fitted to the face's own
# points; the pack's overall thin axis is 3 deg off it) and its plane, n.p = GP_PACK_INNER. The ENVG-B box's own
# helmet-side face is put on that plane with that normal, centred on the pack.
GP_PACK = np.array([0.0, 0.0972, -0.2595])
GP_PACK_UP = np.array([0.0, 0.8382, 0.54537])
GP_PACK_OUT = np.array([0.0, 0.54537, -0.8382])
GP_PACK_INNER = 0.24767
# The cable is flexible (the user: only its two ends must sit right, at the shroud plate and at the box; the middle
# may bend or be shorter): our own tube from the plate to the box, through the GPNVG-18 cable's centreline (6 mm
# slices of its cable pieces, from where it leaves its base), which lies on the helmet as the GPNVG-18's does.
CABLE_ROUTE = [(0.0453, 0.0280, -0.0101), (0.0478, 0.0370, -0.0153), (0.0538, 0.0417, -0.0197), (0.0609, 0.0424, -0.0224),
               (0.0865, 0.0341, -0.0290), (0.0911, 0.0345, -0.0339), (0.0933, 0.0364, -0.0379), (0.0955, 0.0461, -0.0522),
               (0.1024, 0.0769, -0.0979), (0.1038, 0.0867, -0.1123), (0.1096, 0.0967, -0.1333), (0.1111, 0.1008, -0.1477),
               (0.1103, 0.1029, -0.1651), (0.1075, 0.0993, -0.1854), (0.1039, 0.0924, -0.2038), (0.0988, 0.0782, -0.2269),
               (0.0909, 0.0647, -0.2474)]
CABLE_RADIUS = 0.0025
BATTERY_BOX = (250, 73, 32)  # vertex counts of the FBX Battery islands that make the box (the rest is its cable)
CABLE_UV = (0.7029, 0.3021)  # the FBX cable's own texels


def catmull(P, n=24):
    P = np.vstack([P[0], P, P[-1]]); out = []
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for t in np.linspace(0, 1, n, endpoint=False):
            out.append(0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(P[-2]); return np.array(out)


def tube(path, r, sides=10):
    """Triangle corners (positions, normals) of a tube along path, frames carried along it."""
    T = np.gradient(path, axis=0); T /= np.linalg.norm(T, axis=1)[:, None]
    n = np.cross(T[0], [0, 1, 0]); ring_p, ring_n = [], []
    for i, t in enumerate(T):
        n = n - (n @ t) * t; n /= np.linalg.norm(n); b = np.cross(t, n)
        d = [np.cos(2 * np.pi * k / sides) * n + np.sin(2 * np.pi * k / sides) * b for k in range(sides)]
        ring_p.append([path[i] + r * x for x in d]); ring_n.append(d)
    P, N = [], []
    for i in range(len(path) - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            for (a, j) in ((i, k), (i, k2), (i + 1, k), (i, k2), (i + 1, k2), (i + 1, k)):  # the device's winding
                P.append(ring_p[a][j]); N.append(ring_n[a][j])
    return np.array(P), np.array(N)


def battery(parts, colour, plate):
    p, n, u = load(parts, colour, ['Battery'])
    tl = np.repeat(islands(p), 3)
    counts = {l: len(np.unique(p[tl == l].round(6), axis=0)) for l in np.unique(tl)}
    keep = np.isin(tl, [l for l, k in counts.items() if k in BATTERY_BOX])
    box, bn, bu = p[keep], n[keep], u[keep]
    bc = (box.min(0) + box.max(0)) / 2
    # Box frame in the device frame: width -X, height +Z, thickness +Y (outward, away from the head); a proper
    # rotation onto the pack's frame (orthonormalised), so the box is turned, not mirrored.
    B = np.column_stack([[-1, 0, 0], [0, 0, 1], [0, 1, 0]])
    out_ = GP_PACK_OUT / np.linalg.norm(GP_PACK_OUT); up_ = GP_PACK_UP - (GP_PACK_UP @ out_) * out_; up_ /= np.linalg.norm(up_)
    G = np.column_stack([np.cross(up_, out_), up_, out_])
    R = G @ B.T
    assert np.isclose(np.linalg.det(R), 1.0)
    q = (box - bc) @ R.T + GP_PACK
    q = q + (GP_PACK_INNER - (q @ out_).min()) * out_  # the box's helmet-side face on the pack's face plane
    qn = bn @ R.T
    start = np.array([plate[:, 0].max() + 0.002, (plate[:, 1].min() + plate[:, 1].max()) / 2, 0.008])
    end = np.array([q[:, 0].max() + 0.002, 0.092, -0.248])
    cp, cn = tube(catmull(np.array([start, *CABLE_ROUTE, end]), 8), CABLE_RADIUS)
    cu = np.tile(np.array(CABLE_UV, np.float32), (len(cp), 1))
    print(f'{colour}: battery box at {q.mean(0).round(3)}, cable {np.linalg.norm(np.diff(catmull(np.array([start, *CABLE_ROUTE, end])), axis=0), axis=1).sum():.3f} m')
    return np.vstack([q, cp]), np.vstack([qn, cn]), np.vstack([bu, cu]).astype(np.float32)


def rig(parts, colour, out):
    """The jointed prefab: nodes with their rest (deployed, seated) poses in the parent's frame and their meshes in
    their own frame, written for EnvgbBuilder as rig.json and <node>.bin. Returns the joint list for K-3PS-C."""
    Rs, ts, a = seat_for(parts, colour)
    Ron = qmat(AXIS_ROT)
    Rdev = Rs @ Ron                                   # the device (and the deployed arm) in the root frame
    def seated(p): return (p @ Ron.T + AXIS_POS) @ Rs.T + ts   # device-frame points -> root frame
    HINGE, TILT_PIN, RAIL = seated(HINGE_DEV), seated(TILT_PIN_DEV), Rdev @ RAIL_DEV
    # Mount_01's islands: the plate, rail and stop stay; rod and knobs turn with the height screw.
    m1 = load(parts, colour, MOUNT, root_frame=True, seat=(Rs, ts))
    lab = islands(m1[0]); tri_lab = np.repeat(lab, 3)
    counts = {l: len(np.unique(m1[0][tri_lab == l].round(6), axis=0)) for l in np.unique(lab)}
    names = {l: MOUNT01_ISLANDS[c] for l, c in counts.items()}
    screw_m = np.isin(tri_lab, [l for l, n in names.items() if n in SCREW])
    rod = m1[0][tri_lab == [l for l, n in names.items() if n == 'rod'][0]]
    rod_dir = np.linalg.svd(rod - rod.mean(0))[2][0]; rod_dir = rod_dir if rod_dir[1] > 0 else -rod_dir
    rail_isl = m1[0][tri_lab == [l for l, n in names.items() if n == 'rail'][0]]
    up = np.linalg.svd(rail_isl - rail_isl.mean(0))[2][0]; up = up if up[1] > 0 else -up
    # World (root-frame) rest poses: (R, t).
    W = {'nvg_envgb_psq42_battery_LOD0': (np.eye(3), np.zeros(3)),
         'nvg_envgb_psq42_mount_LOD0': (np.eye(3), np.zeros(3)),
         'envgb_height_screw': (frame(rod_dir, 'y'), rod.mean(0)),
         'envgb_carriage': (np.eye(3), HINGE),
         'axis': (Rdev, HINGE),
         'envgb_arm': (Rdev, HINGE),
         'envgb_tilt': (Rdev, TILT_PIN),
         'envgb_bracket': (Rdev, TILT_PIN)}
    m5 = load(parts, colour, ['Mount_05'])[0]
    W['envgb_slide'] = (frame(RAIL, 'z'), seated((m5.min(0) + m5.max(0)) / 2))
    W['nvg_envgb_psq42_LOD0'] = (Rdev, seated(np.zeros(3)))
    parent = {'nvg_envgb_psq42_battery_LOD0': None, 'nvg_envgb_psq42_mount_LOD0': None, 'envgb_height_screw': 'nvg_envgb_psq42_mount_LOD0',
              'envgb_carriage': 'nvg_envgb_psq42_mount_LOD0', 'axis': 'envgb_carriage', 'envgb_arm': 'axis',
              'envgb_tilt': 'envgb_arm', 'envgb_bracket': 'envgb_tilt', 'envgb_slide': 'envgb_bracket',
              'nvg_envgb_psq42_LOD0': 'envgb_slide'}
    # Meshes, as root-frame points.
    def dev_parts(names):
        p, n, u = load(parts, colour, names); return seated(p), n @ Rdev.T, u
    meshes = {'nvg_envgb_psq42_battery_LOD0': battery(parts, colour, m1[0][~screw_m]),
              'nvg_envgb_psq42_mount_LOD0': tuple(x[~screw_m] for x in m1),
              'envgb_height_screw': tuple(x[screw_m] for x in m1),
              'envgb_carriage': dev_parts(['Mount_02']), 'envgb_arm': dev_parts(['Mount_03']),
              'envgb_bracket': dev_parts(['Mount_04']), 'envgb_slide': dev_parts(['Mount_05'])}
    nodes = []
    for name, par in parent.items():
        R, t = W[name]
        if par is None: lr, lt = R, t
        else:
            PR, Pt = W[par]; lr = PR.T @ R; lt = PR.T @ (t - Pt)
        node = {'name': name, 'parent': par or '', 'pos': lt.tolist(), 'rot': mat2quat(lr).tolist(), 'mesh': ''}
        if name in meshes:
            p, n, u = meshes[name]
            write_mesh(out / f'{name}.bin', (p - t) @ R, n @ R, u); node['mesh'] = name
        nodes.append(node)
    # The device node's own mesh stays in its own (unchanged) frame.
    write_mesh(out / 'nvg_envgb_psq42_LOD0.bin', *load(parts, colour, BODY))
    nodes[-1]['mesh'] = 'nvg_envgb_psq42_LOD0'
    assert np.allclose(W['nvg_envgb_psq42_LOD0'][0], Rdev)
    # Flip: CurveRotator turns "axis" in the carriage's frame (the root's orientation) between these.
    q_on = mat2quat(Rdev)
    dq = qmul(GP_OFF, (-GP_ON[0], -GP_ON[1], -GP_ON[2], GP_ON[3]))
    sgn = np.sign((2 * np.arctan2(dq[0], dq[3]) + np.pi) % (2 * np.pi) - np.pi)
    q_off = np.array(qmul(qx(sgn * np.radians(FLIP_DEG)), q_on))
    joints = {'height': {'node': 'envgb_height_screw / envgb_carriage', 'axisInMount': up.tolist(),
                         'screwPivotInMount': rod.mean(0).tolist(), 'screwAxisLocal': 'Y'},
              'flip': {'node': 'axis', 'pivotInCarriage': [0, 0, 0], 'axisLocal': 'X', 'degrees': FLIP_DEG},
              'tilt': {'node': 'envgb_tilt', 'axisLocal': 'X'},
              'slide': {'node': 'envgb_slide', 'axisLocal': 'Z (away from the face)'},
              'pods': {k: {'node': f'nvg_envgb_psq42_LOD0/{k}', 'eyecupNode': f'envgb_eyecup_LOD0/{k.replace("pod", "eyecup_pod")}',
                           'pivotInDevice': list(v['pivot']), 'axisLocal': list(v['axis']), 'degrees': POD_DEG,
                           'rotation': f'Quaternion.AngleAxis({POD_DEG} * t, {"Vector3.down" if v["axis"][1] < 0 else "Vector3.up"})'}
                       for k, v in PODS.items()}}
    return nodes, q_on, q_off, joints, Rs, ts


def main(parts, tex):
    shared = tuple(channel(tex / f'AN-PSQ-42_{k}.png') for k in ('ao', 'roughness', 'metalness'))
    for colour in COLOURS:
        out = UNITY / colour.lower(); out.mkdir(parents=True, exist_ok=True)
        for old in ('device.bin', 'mount.bin'): (out / old).unlink(missing_ok=True)
        nodes, q_on, q_off, joints, Rs, ts = rig(parts, colour, out)
        glass = write_mesh(out / 'glass.bin', *load(parts, colour, GLASS))
        (out / 'eyecup.bin').unlink(missing_ok=True)
        pods = []
        for k, v in PODS.items():
            piv = np.array(v['pivot'])
            def local(names):
                p, n, u = load(parts, colour, names); return p - piv, n, u
            write_mesh(out / f'{k}.bin', *local(v['body']))
            write_mesh(out / f'{k}_glass.bin', *local(v['glass']))
            write_mesh(out / f'{k.replace("pod", "eyecup_pod")}.bin', *local([v['eyecup']]))
            pods.append({'name': k, 'pivot': list(v['pivot'])})
        w = load(parts, colour, [THERMAL_WINDOW])[0]  # thermal window: centre of its disc; the device looks along -Y
        info = {'colour': colour.lower(), 'thermalLens': ((w.min(0) + w.max(0)) / 2).tolist(),
                'onRotation': list(map(float, q_on)), 'offRotation': list(map(float, q_off)), 'nodes': nodes, 'pods': pods}
        (out / 'info.json').write_text(json.dumps(info, indent=1))
        if colour == COLOURS[0]: (ITEM / 'joints.json').write_text(json.dumps({'nodes': nodes, 'joints': joints}, indent=1))
        print(colour, 'thermal lens at', np.round(info['thermalLens'], 4))
        textures(tex, colour, out, shared)


if __name__ == '__main__':
    main(Path(sys.argv[1]), Path(sys.argv[2]))
