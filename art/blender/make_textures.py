"""Procedural seamless textures for walls/floors. blender -b -P art/blender/make_textures.py"""
import bpy, os
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "unity", "SilentNight", "Assets", "_Project", "Art", "Environment", "Textures")
os.makedirs(OUT, exist_ok=True)
N = 1024
rng = np.random.default_rng(7)


def blur(a, sx, sy=None):
    """Circular (tileable) gaussian blur through FFT."""
    sy = sx if sy is None else sy
    fx = np.fft.fftfreq(a.shape[1])[None, :]
    fy = np.fft.fftfreq(a.shape[0])[:, None]
    g = np.exp(-2 * (np.pi ** 2) * ((sx * fx) ** 2 + (sy * fy) ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g))


def noise(sx, sy=None, seed=None):
    r = np.random.default_rng(seed) if seed is not None else rng
    a = blur(r.standard_normal((N, N)), sx, sy)
    a = (a - a.mean()) / (a.std() + 1e-9)
    return a


def save(name, rgb):
    rgb = np.clip(rgb, 0, 1)
    img = bpy.data.images.new(name, N, N, alpha=False)
    rgba = np.ones((N, N, 4), dtype=np.float32)
    rgba[..., :3] = rgb[::-1]  # flip: Blender origin is bottom-left
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = os.path.join(OUT, name + ".png")
    img.file_format = "PNG"
    img.save()
    print("saved", name)


def col(c):
    return np.array(c, dtype=np.float32)[None, None, :]


def wood(name, base, dark, planks=8, seed=1):
    y, x = np.mgrid[0:N, 0:N]
    pid = (x * planks // N)
    tone = np.array([rng.uniform(-0.12, 0.12) for _ in range(planks)])[pid]
    grain = noise(1.2, 40, seed=seed) * 0.5 + noise(3, 14, seed=seed + 1) * 0.3
    t = 0.5 + 0.12 * grain + tone
    img = col(dark) * (1 - t[..., None]) + col(base) * t[..., None]
    seam = ((x % (N // planks)) < 3).astype(np.float32)
    img *= (1 - 0.55 * seam[..., None])
    # staggered short joints
    for p in range(planks):
        yj = int(rng.uniform(0, N))
        m = (pid == p) & (np.abs(y - yj) < 2)
        img[m] *= 0.6
    img += 0.02 * noise(0.8, seed=seed + 2)[..., None]
    save(name, img)


def wall():
    base = col((0.93, 0.86, 0.70))
    img = base + 0.025 * noise(1.5, seed=3)[..., None] + 0.015 * noise(6, seed=4)[..., None]
    # tiny snowflake dots
    dots = np.zeros((N, N), np.float32)
    for _ in range(260):
        cx, cy = rng.integers(0, N, 2)
        r = rng.integers(2, 4)
        yy, xx = np.ogrid[-r:r + 1, -r:r + 1]
        mask = xx * xx + yy * yy <= r * r
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if mask[dy + r, dx + r]:
                    dots[(cy + dy) % N, (cx + dx) % N] = 1
    img = img * (1 - 0.7 * dots[..., None]) + col((1, 1, 0.97)) * 0.7 * dots[..., None]
    # soft vertical stripes
    y, x = np.mgrid[0:N, 0:N]
    stripe = (np.sin(x * 2 * np.pi * 8 / N) > 0.6).astype(np.float32)
    img = img * (1 - 0.06 * stripe[..., None]) + col((0.6, 0.75, 0.6)) * 0.06 * stripe[..., None]
    save("Wall", img)


def carpet():
    img = col((0.55, 0.10, 0.13)) + 0.08 * noise(0.8, seed=5)[..., None] + 0.04 * noise(2.5, seed=6)[..., None]
    y, x = np.mgrid[0:N, 0:N]
    knit = (np.sin((x + y) * 2 * np.pi * 64 / N) * 0.5 + 0.5)
    img *= (0.9 + 0.12 * knit[..., None])
    save("Carpet", img)


def tile():
    y, x = np.mgrid[0:N, 0:N]
    cell = N // 4
    chk = (((x // cell) + (y // cell)) % 2).astype(np.float32)
    img = col((0.12, 0.12, 0.14)) * (1 - chk[..., None]) + col((0.93, 0.93, 0.9)) * chk[..., None]
    grout = (((x % cell) < 4) | ((y % cell) < 4)).astype(np.float32)
    img = img * (1 - grout[..., None]) + col((0.55, 0.55, 0.52)) * grout[..., None]
    img += 0.03 * noise(1.2, seed=8)[..., None]
    save("Tile", img)


def concrete():
    img = col((0.5, 0.5, 0.52)) + 0.06 * noise(2, seed=9)[..., None] + 0.03 * noise(0.7, seed=10)[..., None]
    save("Concrete", img)


def snow():
    img = col((0.93, 0.96, 1.0)) + 0.02 * noise(3, seed=11)[..., None] - 0.01 * noise(0.8, seed=12)[..., None]
    save("Snow", img)


def brick():
    y, x = np.mgrid[0:N, 0:N]
    bh, bw = N // 8, N // 4
    row = y // bh
    xs = (x + (row % 2) * (bw // 2)) % N
    bid = (xs // bw) + row * 4
    tone = np.array([rng.uniform(-0.1, 0.1) for _ in range(40)])[bid % 40]
    img = col((0.58, 0.24, 0.17)) * (1 + tone[..., None]) + 0.05 * noise(1.5, seed=13)[..., None]
    mortar = (((xs % bw) < 5) | ((y % bh) < 5)).astype(np.float32)
    img = img * (1 - mortar[..., None]) + col((0.7, 0.66, 0.6)) * mortar[..., None]
    save("Brick", img)


wood("Wood", (0.62, 0.42, 0.24), (0.38, 0.22, 0.11), seed=20)
wood("DarkWood", (0.40, 0.24, 0.13), (0.22, 0.12, 0.06), planks=6, seed=30)
wall()
carpet()
tile()
concrete()
snow()
brick()
print("TEXTURES DONE")
