"""Render the website demo's plate thumbnails (web/src/demo/assets/*.png).

Slicer-style plate renders of simple demo parts: transparent background, the parts arranged as on the plate,
shaded in their filament colour. Standard library only (a tiny z-buffered rasterizer). cube.png and clip.png are
real slicer renders taken from our own test fixtures. Run: python3 tools/demo-thumbnails.py
"""

import math
import struct
import zlib
from pathlib import Path

OUT = Path(__file__).resolve().parents[1] / "web/src/demo/assets"
SIZE = 512
SS = 2  # supersampling factor


def hexrgb(value):
    return tuple(int(value[i : i + 2], 16) / 255 for i in (1, 3, 5))


# --- meshes: lists of (v0, v1, v2, n0, n1, n2) --------------------------------------------------------------
def tri(a, b, c, normals=None):
    if normals is None:
        ux, uy, uz = (b[i] - a[i] for i in range(3))
        vx, vy, vz = (c[i] - a[i] for i in range(3))
        n = (uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx)
        normals = (n, n, n)
    return (a, b, c, *normals)


def box(x0, y0, z0, x1, y1, z1):
    v = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    faces = [(0, 2, 1), (0, 3, 2), (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7)]
    return [tri(v[a], v[b], v[c]) for a, b, c in faces]


def frustum(cx, cy, z0, z1, r0, r1, segments=72, caps=True, inward=False):
    """A (tapered) cylinder; smooth sides. inward=True flips the side normals (an inner wall)."""
    out = []
    slope = (r0 - r1) / (z1 - z0)
    for i in range(segments):
        a0 = 2 * math.pi * i / segments
        a1 = 2 * math.pi * (i + 1) / segments
        p = [(cx + r * math.cos(a), cy + r * math.sin(a), z) for a in (a0, a1) for r, z in ((r0, z0), (r1, z1))]
        n0 = (math.cos(a0), math.sin(a0), slope)
        n1 = (math.cos(a1), math.sin(a1), slope)
        if inward:
            n0 = tuple(-x for x in n0)
            n1 = tuple(-x for x in n1)
        out.append(tri(p[0], p[2], p[3], (n0, n1, n1)))
        out.append(tri(p[0], p[3], p[1], (n0, n1, n0)))
        if caps:
            out.append(tri((cx, cy, z1), p[1], p[3]))
            out.append(tri((cx, cy, z0), p[2], p[0]))
    return out


def ring(cx, cy, z, r_in, r_out, segments=72):
    out = []
    for i in range(segments):
        a0 = 2 * math.pi * i / segments
        a1 = 2 * math.pi * (i + 1) / segments
        pi0, pi1 = ((cx + r_in * math.cos(a), cy + r_in * math.sin(a), z) for a in (a0, a1))
        po0, po1 = ((cx + r_out * math.cos(a), cy + r_out * math.sin(a), z) for a in (a0, a1))
        out += [tri(pi0, po0, po1), tri(pi0, po1, pi1)]
    return out


def rotate_x(mesh, degrees, pivot_y, pivot_z):
    c, s = math.cos(math.radians(degrees)), math.sin(math.radians(degrees))

    def rv(p):
        y, z = p[1] - pivot_y, p[2] - pivot_z
        return (p[0], pivot_y + y * c - z * s, pivot_z + y * s + z * c)

    def rn(n):
        return (n[0], n[1] * c - n[2] * s, n[1] * s + n[2] * c)

    return [tuple(rv(p) for p in t[:3]) + tuple(rn(n) for n in t[3:]) for t in mesh]


def moved(mesh, dx, dy):
    return [tuple((p[0] + dx, p[1] + dy, p[2]) for p in t[:3]) + t[3:] for t in mesh]


def grid(mesh, columns, rows, pitch_x, pitch_y):
    out = []
    for c in range(columns):
        for r in range(rows):
            out += moved(mesh, c * pitch_x, r * pitch_y)
    return out


# --- rendering -----------------------------------------------------------------------------------------------
def render(mesh, color, name, azimuth=-40, elevation=38):
    az, el = math.radians(azimuth), math.radians(elevation)
    ca, sa, ce, se = math.cos(az), math.sin(az), math.cos(el), math.sin(el)

    def view(p):  # rotate about Z, then tilt: x right, y up on screen, z toward the viewer
        x = p[0] * ca - p[1] * sa
        y = p[0] * sa + p[1] * ca
        return (x, p[2] * ce + y * se, p[2] * se - y * ce)

    projected = [tuple(view(p) for p in t[:3]) + tuple(view(n) for n in t[3:]) for t in mesh]
    xs = [p[0] for t in projected for p in t[:3]]
    ys = [p[1] for t in projected for p in t[:3]]
    span = max(max(xs) - min(xs), max(ys) - min(ys))
    w = SIZE * SS
    scale = w * 0.84 / span
    ox = w / 2 - (max(xs) + min(xs)) / 2 * scale
    oy = w / 2 + (max(ys) + min(ys)) / 2 * scale
    depth = [-1e9] * (w * w)
    rgba = bytearray(w * w * 4)
    light = (-0.45, 0.62, 0.64)
    ln = math.sqrt(sum(x * x for x in light))
    light = tuple(x / ln for x in light)
    half = tuple(x / math.sqrt(sum(y * y for y in (light[0], light[1], light[2] + 1))) for x in (light[0], light[1], light[2] + 1))
    base = hexrgb(color)
    for t in projected:
        (a, b, c), normals = t[:3], t[3:]
        pa, pb, pc = ((ox + p[0] * scale, oy - p[1] * scale, p[2]) for p in (a, b, c))
        area = (pb[0] - pa[0]) * (pc[1] - pa[1]) - (pb[1] - pa[1]) * (pc[0] - pa[0])
        if abs(area) < 1e-9:
            continue
        x0, x1 = max(int(min(pa[0], pb[0], pc[0])), 0), min(int(max(pa[0], pb[0], pc[0])) + 1, w - 1)
        y0, y1 = max(int(min(pa[1], pb[1], pc[1])), 0), min(int(max(pa[1], pb[1], pc[1])) + 1, w - 1)
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                px, py = x + 0.5, y + 0.5
                w0 = ((pb[0] - px) * (pc[1] - py) - (pb[1] - py) * (pc[0] - px)) / area
                w1 = ((pc[0] - px) * (pa[1] - py) - (pc[1] - py) * (pa[0] - px)) / area
                w2 = 1 - w0 - w1
                if w0 < 0 or w1 < 0 or w2 < 0:
                    continue
                z = w0 * pa[2] + w1 * pb[2] + w2 * pc[2]
                i = y * w + x
                if z <= depth[i]:
                    continue
                depth[i] = z
                n = [w0 * normals[0][k] + w1 * normals[1][k] + w2 * normals[2][k] for k in range(3)]
                nl = math.sqrt(sum(v * v for v in n)) or 1
                n = [v / nl for v in n]
                if n[2] < 0:  # back face seen through: shade as its front
                    n = [-v for v in n]
                diffuse = max(0.0, sum(n[k] * light[k] for k in range(3)))
                spec = max(0.0, sum(n[k] * half[k] for k in range(3))) ** 40
                shade = 0.42 + 0.58 * diffuse
                rgba[i * 4 : i * 4 + 4] = bytes(
                    [min(255, int((base[k] * shade + 0.18 * spec + 0.05 * (1 - base[k]) * diffuse) * 255)) for k in range(3)]
                    + [255]
                )
    # downsample (premultiplied box filter) and write PNG
    out = bytearray()
    for y in range(SIZE):
        out.append(0)
        for x in range(SIZE):
            acc = [0, 0, 0, 0]
            for dy in range(SS):
                for dx in range(SS):
                    j = ((y * SS + dy) * w + x * SS + dx) * 4
                    alpha = rgba[j + 3]
                    for k in range(3):
                        acc[k] += rgba[j + k] * alpha
                    acc[3] += alpha
            n = SS * SS
            alpha = acc[3] / n
            out += bytes([int(acc[k] / acc[3]) if acc[3] else 0 for k in range(3)] + [int(alpha)])

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(out), 9)) + chunk(b"IEND", b"")
    (OUT / f"{name}.png").write_bytes(png)
    print(f"{name}.png", len(png), "bytes")


def hook():
    one = box(0, 0, 0, 18, 4, 56) + box(0, -22, 0, 18, 4, 4) + box(0, -22, 0, 18, -18, 14)
    return grid(one, 4, 1, 30, 0)


def knob():
    one = frustum(0, 0, 0, 14, 11, 10) + frustum(0, 0, 14, 16, 8, 7.5) + box(-1, 2, 14, 1, 9, 16.5)
    return grid(one, 4, 2, 30, 30)


def lid():
    one = box(0, 0, 0, 80, 60, 3) + box(3, 3, 3, 77, 57, 6) + box(6, 6, 3, 74, 54, 6.2)
    for cx, cy in ((7, 7), (73, 7), (7, 53), (73, 53)):
        one += frustum(cx, cy, 6.2, 6.4, 2.2, 2.2, 32)
    return grid(one, 2, 2, 92, 72)


def base():
    one = box(0, 0, 0, 80, 60, 3)
    one += box(0, 0, 3, 3, 60, 30) + box(77, 0, 3, 80, 60, 30) + box(3, 0, 3, 77, 3, 30) + box(3, 57, 3, 77, 60, 30)
    for cx, cy in ((8, 8), (72, 8), (8, 52), (72, 52)):
        one += frustum(cx, cy, 3, 26, 4, 4, 40)
    one += box(30, -1, 10, 50, 0, 18)  # cable slot frame
    return grid(one, 2, 2, 92, 72)


def pot():
    mesh = frustum(0, 0, 0, 90, 34, 44, caps=False) + frustum(0, 0, 4, 90, 31, 41, caps=False, inward=True)
    mesh += ring(0, 0, 90, 41, 44) + frustum(0, 0, 0, 4, 31, 31.4)
    mesh += frustum(0, 0, 82, 90, 44, 46.5, caps=False) + ring(0, 0, 82, 44, 46.5) + ring(0, 0, 90, 44, 46.5)
    return mesh


def stand():
    one = box(0, 0, 0, 70, 80, 5) + box(0, 62, 5, 70, 70, 16)
    one += rotate_x(box(0, 10, 0, 70, 16, 95), -22, 10, 5)
    one += rotate_x(box(0, 10, 0, 70, 30, 6), -22, 10, 5)
    return grid(one, 2, 1, 84, 0)


def coaster():
    one = frustum(0, 0, 0, 4, 46, 46, 6) + frustum(0, 0, 4, 5.5, 40, 40, 6)
    return grid(one, 2, 2, 100, 92)


def bracket():
    one = box(0, 0, 0, 40, 32, 4) + box(0, 0, 4, 40, 4, 32)
    one += rotate_x(box(17, 4, 4, 23, 30, 6), 45, 4, 4)
    for cx in (10, 30):
        one += frustum(cx, 18, 4, 4.3, 3.2, 3.2, 32)
    return grid(one, 2, 2, 52, 44)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    render(hook(), "#FF6A13", "hook")
    render(knob(), "#2A2A2A", "knob")
    render(lid(), "#3A3A3C", "lid")
    render(base(), "#3A3A3C", "base")
    render(pot(), "#F4F4F2", "pot")
    render(stand(), "#2EA6A6", "stand")
    render(coaster(), "#3B6E3B", "coaster")
    render(bracket(), "#4A4A4C", "bracket")
