#!/usr/bin/env python3
# RealBalloons - Copyright (c) 2026 tristanmccarthy55. MIT licence, see LICENSE.txt.
# Makes the TexturesUnlimited recolour masks for the balloon envelope from the model's own UVs, and prints
# each pattern's _DiffuseNorm for RealBalloon.cfg.
#
#   python3 make_masks.py <GameData/RealBalloons/Parts/RealBalloon> [pattern ...]
#   needs numpy and Pillow (pip install numpy pillow)
#
# Writes Masks/RealBalloons_<pattern>_mask.dds (1024 DXT1 with mipmaps) next to the model.
#
# How the envelope is mapped (balloon125.mu, transform "Balloon"): the model's UVs are a planar projection
# along its axis (u = 0.25 - 0.1239 x, v = 0.75 + 0.1239 y). The plugin respaces them at load
# (ModuleRealBalloon.EvenEnvelopeUVs, mirrored by even_uvs below): a texel's angle theta about the centre
# (0.25, 0.749) is still the envelope's azimuth, and its distance R is now the distance along the envelope
# from the nearer end (tail or top) as a share of that end's way to the widest part, times 0.2453. So R
# runs evenly from each end (R = 0) to the widest part (R = 0.2453), the top and bottom halves alike: a
# pattern can follow the gores or the height, but can't mark the top alone. The 8 gore seams are at
# theta = k * 45 degrees. The packed cap ("Cap") keeps its planar UVs about (0.8181, 0.8829), turned 180
# degrees from the envelope's, and gets the pattern in miniature. Mask channels: R main colour, G second
# colour, B detail (load tapes). Rows are stored with v = row / height, as KSP reads DDS files. The blimp
# fins sample finTexel (0.4071, 0.8151), mid first gore: keep that texel main colour in every pattern.
import io, os, re, struct, sys
import numpy as np
from PIL import Image

N = 1024                 # mask size, as the diffuse
SS = 4                   # supersamples per axis, for smooth edges
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

def mask(pattern):
    acc = np.zeros((N, N, 3))
    rows, cols = np.mgrid[0:N, 0:N]
    for oy in (np.arange(SS) + 0.5) / SS:
        for ox in (np.arange(SS) + 0.5) / SS:
            u, v = (cols + ox) / N, (rows + oy) / N
            cu, cv = u - CAP_C[0], v - CAP_C[1]
            on_cap = np.hypot(cu, cv) < CAP_ZONE
            R, th = polar(np.where(on_cap, CAP_TO_ENV * cu, u - ENV_C[0]), np.where(on_cap, CAP_TO_ENV * cv, v - ENV_C[1]))
            w = np.where(on_cap, CAP_TAPE_W * abs(CAP_TO_ENV), TAPE_W)       # in envelope terms
            for i, ch in enumerate(PATTERNS[pattern](R, th, w)):
                acc[..., i] += ch
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

def write_dds(rgb, path):
    """DXT1 DDS with a full mip chain: Pillow encodes each level, the header is marked for mipmaps."""
    img = Image.fromarray(np.clip(rgb * 255 + 0.5, 0, 255).astype(np.uint8), "RGB")
    levels, header = [], None
    while True:
        b = io.BytesIO(); img.save(b, "DDS", pixel_format="DXT1"); data = b.getvalue()
        header = header or bytearray(data[:128])
        levels.append(data[128:])
        if img.width == 1: break
        img = img.resize((img.width // 2, img.height // 2), Image.BOX)
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
    for name in wanted:
        if name != "Film":
            write_dds(mask(name), os.path.join(part_dir, "Masks", "RealBalloons_%s_mask.dds" % name))
        n = norms(name, diffuse, mesh)
        print("%-10s vector = _DiffuseNorm, %.3f, %.3f, %.3f" % (name, *n))
