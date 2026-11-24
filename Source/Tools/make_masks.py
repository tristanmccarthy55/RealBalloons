#!/usr/bin/env python3
# RealBalloons - Copyright (c) 2026 tristanmccarthy55. MIT licence, see LICENSE.txt.
# Makes the envelope's unrolled fabric texture and the TexturesUnlimited recolour masks, and prints each
# pattern's _DiffuseNorm for RealBalloon.cfg.
#
#   python3 make_masks.py <GameData/RealBalloons/Parts/RealBalloon> [pattern ...]
#   needs numpy and Pillow (pip install numpy pillow)
#
# Writes, next to the model:
#   RealBalloons_Envelope.dds          the KerBalloons fabric, unrolled (2048 x 1024 DXT1 with mipmaps)
#   Masks/RealBalloons_<p>_mask.dds    each pattern on the same unrolled grid (2048 x 1024)
#   RealBalloons_Cap.dds               the packed cap's art from KBTexKerbin, filling its own square (1024)
#   Masks/RealBalloons_<p>_capmask.dds each pattern in miniature for the cap, on that square
#
# How the envelope is mapped. The model (balloon125.mu, transform "Balloon") projects its fabric straight
# down the envelope's axis, a disc in KBTexKerbin: u = 0.25 - 0.1239 x, v = 0.75 + 0.1239 y. The plugin
# unrolls it at load (ModuleRealBalloon.UnrollEnvelope): u runs once round the envelope, from the angle theta
# the disc gave each vertex about its centre (0.25, 0.749), so u = theta / 360 and the 8 gore seams sit at
# u = k / 8; v runs from one end (0) through the widest part (0.5) to the other end (1), evenly along the
# envelope. Both halves mirror (as the disc mapped them), so a pattern can follow the gores or the height,
# but can't mark the top alone. On this grid band edges run along texel rows and gore seams along columns,
# so they stay straight at any distance (on the disc they cut across the texels and showed as teeth). The
# fabric texture is the disc resampled onto the grid: theta and R = 0.2453 x (1 - |1 - 2v|) back to the disc.
# The packed cap ("Cap") is a disc in KBTexKerbin too, about (0.8181, 0.8829), turned 180 degrees from the
# envelope's: the plugin spreads its UVs at load (ModuleRealBalloon.SpreadCapUVs) so the disc fills CAP_FILL of
# its own square, RealBalloons_Cap (the same art, 4.6 times the texels), and its capmask shows each pattern in
# miniature there. Outside the disc both hold its edge, for the mipmaps.
# The masks' small mip levels fade the load tapes out as they narrow below two texels (faded_chain): DXT1 keeps
# two colours a 4 x 4 block, and a third broke up into blocks on small or distant balloons and caps. Mask channels: R main
# colour, G second colour, B detail (load tapes). Rows are stored with v = row / height, as KSP reads DDS
# files. The blimp fins sample finTexel (0.0625, 0.345), mid first gore: keep that texel main colour in
# every pattern.
import io, os, re, struct, sys
import numpy as np
from PIL import Image

W, H = 2048, 1024           # unrolled envelope textures: round the envelope, then end to end
CAP_N = 1024                # cap masks: the model's own UV square
SS = 4                      # supersamples per axis, for smooth edges
R_MAX = 0.2453              # the disc's radius in KBTexKerbin: the envelope's widest part
FIN_TEXEL = (0.0625, 0.345) # mid first gore, 69% of the way from the end to the widest part
CAP_FILL = 0.48             # the cap disc's radius in its own square (ModuleRealBalloon.capFill)
ENV_C = (0.25, 0.749)
CAP_C = (0.8181, 0.8829)
CAP_ZONE = 0.1043 + 0.012   # cap disc radius plus a margin
CAP_TO_ENV = -2.352         # cap UV offset -> envelope UV offset (cap radius to the envelope's 0.2453)
TAPE_W = 0.010              # load tape width in envelope UV (0.08 of the envelope's radius)
CAP_TAPE_W = 0.010          # and on the cap, in its UV: as many texels as on the envelope, so they last as
                            # far down the mipmaps (drawn to scale, 4 texels, they faded out at VAB zoom)
BAND_R = 0.211              # equator band: the middle 14% of the way from each end to the widest part
GORES = 8

def tape(R, th, w):
    d = (th + 22.5) % 45.0 - 22.5                         # angle to the nearest seam
    return np.abs(R * np.sin(np.radians(d))) < w / 2

def gore(th):
    return (np.floor(th / 45.0).astype(int) % GORES) % 2 == 0     # True: a main-colour gore

# name: (R, theta, tape width) -> (main, second, detail) as booleans. "Film" has no mask file (TU's default, all main).
PATTERNS = {
    "Film":      lambda R, th, w: (np.ones_like(R, bool), np.zeros_like(R, bool), np.zeros_like(R, bool)),
    "Tapes":     lambda R, th, w: (~tape(R, th, w), np.zeros_like(R, bool), tape(R, th, w)),
    "Gores":     lambda R, th, w: (~tape(R, th, w) & gore(th), ~tape(R, th, w) & ~gore(th), tape(R, th, w)),
    "Band":      lambda R, th, w: (~tape(R, th, w) & (R < BAND_R), ~tape(R, th, w) & (R >= BAND_R), tape(R, th, w)),
    "Harlequin": lambda R, th, w: (~tape(R, th, w) & (gore(th) ^ (R >= BAND_R)),
                                   ~tape(R, th, w) & ~(gore(th) ^ (R >= BAND_R)), tape(R, th, w)),
}

def polar(du, dv):
    return np.hypot(du, dv), np.degrees(np.arctan2(dv, du)) % 360.0

def unrolled(u, v):
    """Unrolled UV -> the disc's (R, theta): what the envelope's fabric and patterns are drawn from."""
    return R_MAX * (1.0 - np.abs(1.0 - 2.0 * v)), (u * 360.0) % 360.0

def env_mask(pattern, w=TAPE_W):
    acc = np.zeros((H, W, 3))
    rows, cols = np.mgrid[0:H, 0:W]
    for oy in (np.arange(SS) + 0.5) / SS:
        for ox in (np.arange(SS) + 0.5) / SS:
            R, th = unrolled((cols + ox) / W, (rows + oy) / H)
            for i, ch in enumerate(PATTERNS[pattern](R, th, w)):
                acc[..., i] += ch
    return acc / (SS * SS)

def cap_offsets(cap_c, cap_half, oy, ox):
    """Cap square texels -> offsets from the cap's centre in KBTexKerbin, held at its edge outside the disc."""
    rows, cols = np.mgrid[0:CAP_N, 0:CAP_N]
    k = CAP_FILL / cap_half
    du, dv = ((cols + ox) / CAP_N - 0.5) / k, ((rows + oy) / CAP_N - 0.5) / k
    r = np.hypot(du, dv); s = np.where(r > cap_half * 0.985, cap_half * 0.985 / np.maximum(r, 1e-9), 1.0)
    return du * s, dv * s

def cap_mask(pattern, cap_c, cap_half, w=CAP_TAPE_W * abs(CAP_TO_ENV)):
    """The packed cap's miniature, on its own square."""
    acc = np.zeros((CAP_N, CAP_N, 3))
    for oy in (np.arange(SS) + 0.5) / SS:
        for ox in (np.arange(SS) + 0.5) / SS:
            cu, cv = cap_offsets(cap_c, cap_half, oy, ox)
            R, th = polar(CAP_TO_ENV * cu, CAP_TO_ENV * cv)
            for i, ch in enumerate(PATTERNS[pattern](R, th, w)):
                acc[..., i] += ch
    return acc / (SS * SS)

def cap_texture(diffuse, cap_c, cap_half):
    """KBTexKerbin's cap disc on the cap's own square."""
    acc = np.zeros((CAP_N, CAP_N, 3))
    for oy in (np.arange(SS) + 0.5) / SS:
        for ox in (np.arange(SS) + 0.5) / SS:
            cu, cv = cap_offsets(cap_c, cap_half, oy, ox)
            acc += bilinear(diffuse, cap_c[0] + cu, cap_c[1] + cv)
    return acc / (SS * SS)

def bilinear(img, u, v):
    """Sample an image (rows = v * height) at UVs, wrapping."""
    h, w = img.shape[:2]
    x = u * w - 0.5; y = v * h - 0.5
    x0 = np.floor(x).astype(int); y0 = np.floor(y).astype(int); fx = (x - x0)[..., None]; fy = (y - y0)[..., None]
    x0 %= w; y0 %= h; x1 = (x0 + 1) % w; y1 = (y0 + 1) % h
    return ((img[y0, x0] * (1 - fx) + img[y0, x1] * fx) * (1 - fy) + (img[y1, x0] * (1 - fx) + img[y1, x1] * fx) * fy)

def fabric(diffuse):
    """KBTexKerbin's envelope disc resampled onto the unrolled grid."""
    acc = np.zeros((H, W, 3))
    rows, cols = np.mgrid[0:H, 0:W]
    for oy in (np.arange(SS) + 0.5) / SS:
        for ox in (np.arange(SS) + 0.5) / SS:
            R, th = unrolled((cols + ox) / W, (rows + oy) / H)
            t = np.radians(th)
            acc += bilinear(diffuse, ENV_C[0] + R * np.cos(t), ENV_C[1] + R * np.sin(t))
    return acc / (SS * SS)

def read_envelope_mesh(mu_path, name="Balloon"):
    """Vertices, UVs and triangles of one mesh in a KSP .mu (PartTools format 4)."""
    d = open(mu_path, "rb").read()
    k = re.search(re.escape(bytes([len(name)]) + name.encode()), d).end()
    j = d.find(b"\x07\x00\x00\x00\x0d\x00\x00\x00", k, k + 300) + 8      # MeshFilter, mesh start
    nv, _ = struct.unpack_from("<2i", d, j); j += 8
    verts = uv = None; tris = []
    while True:
        t = struct.unpack_from("<i", d, j)[0]; j += 4
        if t == 14: verts = np.frombuffer(d, "<f4", 3 * nv, j).reshape(-1, 3); j += 12 * nv
        elif t == 15: uv = np.frombuffer(d, "<f4", 2 * nv, j).reshape(-1, 2); j += 8 * nv
        elif t == 17: j += 12 * nv                                         # normals
        elif t == 18: j += 16 * nv                                         # tangents
        elif t == 19:
            n = struct.unpack_from("<i", d, j)[0]
            tris.append(np.frombuffer(d, "<i4", n, j + 4).reshape(-1, 3)); j += 4 + 4 * n
        elif t == 22: break
        else: raise ValueError("unexpected mesh entry %d" % t)
    return verts.astype(float), uv.astype(float), np.vstack(tris)

def even_uvs(v, uv, bins=180):
    """The plugin's UV respacing (ModuleRealBalloon.EvenEnvelopeUVs): UV radius -> share of the distance
    along the envelope from the nearer end to the widest part, keeping each UV's angle and the radius range."""
    r = np.hypot(v[:, 0], v[:, 1]); z = v[:, 2]
    rmax = r.max(); zc = z[r > rmax - 1e-3].mean()
    phi = np.arctan2(z - zc, r)                                  # angle about the widest ring's centre
    b = np.clip(((phi + np.pi / 2) / np.pi * bins).astype(int), 0, bins - 1)
    cnt = np.bincount(b, minlength=bins); ok = cnt > 0
    pr, pz, pp = (np.bincount(b, w, bins)[ok] / cnt[ok] for w in (r, z, phi))
    S = np.concatenate([[0], np.cumsum(np.hypot(np.diff(pr), np.diff(pz)))])     # distance along the profile
    s = np.interp(phi, pp, S); s_eq = np.interp(0.0, pp, S); s_end = S[-1]
    frac = np.where(phi <= 0, s / s_eq, (s_end - s) / (s_end - s_eq)).clip(0, 1)
    c = (uv.min(0) + uv.max(0)) / 2; d = uv - c; R = np.hypot(d[:, 0], d[:, 1])
    k = np.where(R > 1e-6, R.max() * frac / np.maximum(R, 1e-9), 1.0)
    return c + d * k[:, None]

def norms(pattern, diffuse, mesh):
    """Average diffuse luminance (TU's weights) under each channel, weighted by the envelope's surface."""
    v, uv, tris = mesh
    uv = even_uvs(v, uv)
    p = v[tris]
    area = 0.5 * np.linalg.norm(np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0]), axis=1)
    L = diffuse @ np.array([0.22, 0.707, 0.071])
    H, W = L.shape
    tot, wt = np.zeros(3), np.zeros(3)
    for a in np.linspace(0.05, 0.9, 5):
        for b in np.linspace(0.05, 0.9, 5):
            if a + b >= 0.95: continue
            t = a * uv[tris[:, 0]] + b * uv[tris[:, 1]] + (1 - a - b) * uv[tris[:, 2]]
            R, th = polar(t[:, 0] - ENV_C[0], t[:, 1] - ENV_C[1])
            lum = L[np.clip((t[:, 1] * H).astype(int), 0, H - 1), np.clip((t[:, 0] * W).astype(int), 0, W - 1)]
            for i, ch in enumerate(PATTERNS[pattern](R, th, TAPE_W)):
                tot[i] += (area * ch * lum).sum(); wt[i] += (area * ch).sum()
    n = np.where(wt > 0, tot / np.maximum(wt, 1e-9), 0.0)
    return np.where(wt > 0, n, n[0])                   # unused channels: same as main

def box_half(a):
    """Half size, averaging 2 x 2 texels (or 2 along the one side left)."""
    h, w = a.shape[:2]
    if h > 1 and w > 1: return a.reshape(h // 2, 2, w // 2, 2, -1).mean((1, 3))
    if h > 1: return a.reshape(h // 2, 2, w, -1).mean(1)
    if w > 1: return a.reshape(h, w // 2, 2, -1).mean(2)
    return a

def mip_chain(full):
    levels = [full]
    while levels[-1].shape[0] > 1 or levels[-1].shape[1] > 1: levels.append(box_half(levels[-1]))
    return levels

def faded_chain(with_tapes, without_tapes, tape_px):
    """A mask's mip levels with the load tapes fading out as they narrow from two texels to one. DXT1 keeps two
    colours a 4 x 4 block (and blends between them): where a gore edge and a tape share a block, the third
    colour broke up into blocks, so a small or distant balloon looked pixelated."""
    a, b = mip_chain(with_tapes), mip_chain(without_tapes)
    return [np.clip(tape_px / 2 ** L - 1.0, 0.0, 1.0) * a[L] + (1 - np.clip(tape_px / 2 ** L - 1.0, 0.0, 1.0)) * b[L]
            for L in range(len(a))]

def write_dds(levels, path):
    """DXT1 DDS with a full mip chain (an image, or its levels): Pillow encodes each level, the header is marked
    for mipmaps."""
    if not isinstance(levels, list): levels = mip_chain(levels)
    data_levels, header = [], None
    for lv in levels:
        img = Image.fromarray(np.clip(lv * 255 + 0.5, 0, 255).astype(np.uint8), "RGB")
        b = io.BytesIO(); img.save(b, "DDS", pixel_format="DXT1"); data = b.getvalue()
        header = header or bytearray(data[:128])
        data_levels.append(data[128:])
    levels = data_levels
    struct.pack_into("<I", header, 8, struct.unpack_from("<I", header, 8)[0] | 0x20000)        # DDSD_MIPMAPCOUNT
    struct.pack_into("<I", header, 28, len(levels))
    struct.pack_into("<I", header, 108, struct.unpack_from("<I", header, 108)[0] | 0x401008)   # COMPLEX|MIPMAP|TEXTURE
    with open(path, "wb") as f:
        f.write(bytes(header) + b"".join(levels))

if __name__ == "__main__":
    part_dir = sys.argv[1]
    wanted = sys.argv[2:] or list(PATTERNS)
    diffuse = np.asarray(Image.open(os.path.join(part_dir, "KBTexKerbin.dds")).convert("RGB")).astype(float) / 255
    mesh = read_envelope_mesh(os.path.join(part_dir, "balloon125.mu"))
    os.makedirs(os.path.join(part_dir, "Masks"), exist_ok=True)
    _, cap_uv, _ = read_envelope_mesh(os.path.join(part_dir, "balloon125.mu"), "Cap")
    cap_c = (cap_uv.min(0) + cap_uv.max(0)) / 2; cap_half = (cap_uv.max(0) - cap_uv.min(0)).max() / 2   # as the plugin
    write_dds(fabric(diffuse), os.path.join(part_dir, "RealBalloons_Envelope.dds"))
    write_dds(cap_texture(diffuse, cap_c, cap_half), os.path.join(part_dir, "RealBalloons_Cap.dds"))
    for name in wanted:
        if name != "Film":
            env_tape_px = TAPE_W / R_MAX / (2 * np.pi) * W                    # narrowest, round the widest part
            cap_tape_px = CAP_TAPE_W * CAP_FILL / cap_half * CAP_N
            write_dds(faded_chain(env_mask(name), env_mask(name, 0.0), env_tape_px),
                      os.path.join(part_dir, "Masks", "RealBalloons_%s_mask.dds" % name))
            write_dds(faded_chain(cap_mask(name, cap_c, cap_half), cap_mask(name, cap_c, cap_half, 0.0), cap_tape_px),
                      os.path.join(part_dir, "Masks", "RealBalloons_%s_capmask.dds" % name))
        n = norms(name, diffuse, mesh)
        print("%-10s vector = _DiffuseNorm, %.3f, %.3f, %.3f" % (name, *n))
