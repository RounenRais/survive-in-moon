"""Creates a stylized (flat-color, low-poly) space panorama for the moon sky.

The image is an "equirectangular" panorama: x = direction around you (0..360 degrees),
y = height (top = straight up, middle = horizon, bottom = straight down). Unity wraps it around the scene
with the Skybox/Panoramic material.
Run from the project root:  python Tools/make_space_skybox.py
"""
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

W, H = 4096, 2048
OUTPUT = "Assets/Materials/SpaceSky.png"
rng = np.random.default_rng(11)

# Where things are, in degrees. Azimuth is measured along the image width (0..360), elevation above the horizon.
EARTH_AZIMUTH, EARTH_ELEVATION, EARTH_RADIUS_DEG = 180.0, 22.0, 8.5


def to_px(azimuth, elevation):
    return (azimuth % 360) / 360 * W, (90 - elevation) / 180 * H


img = Image.new("RGB", (W, H), (0, 0, 0))
px = np.zeros((H, W, 3), float)

# --- Sky: black on top, a very faint blue-black glow near the horizon (keeps the horizon readable)
ys = np.arange(H)[:, None]
elevation = 90 - ys / H * 180
glow = np.clip(1 - np.abs(elevation) / 25, 0, 1) ** 2
px[:] = (np.array([4, 5, 12]) + glow[..., None] * np.array([10, 12, 26]))

# --- Milky way: a soft, tilted band of dust (painted as blurred blobs)
band = Image.new("L", (W, H), 0)
bd = ImageDraw.Draw(band)
for i in range(700):
    t = rng.uniform(0, 1)
    x = t * W
    y = H * 0.3 + math.sin(t * math.tau) * H * 0.12 + rng.normal(0, H * 0.03)
    r = rng.uniform(10, 40)
    bd.ellipse((x - r, y - r, x + r, y + r), fill=int(rng.uniform(8, 22)))
band = np.asarray(band.filter(ImageFilter.GaussianBlur(28)), float)[..., None] / 255
px += band * np.array([150, 140, 200])

img = Image.fromarray(np.clip(px, 0, 255).astype(np.uint8))
d = ImageDraw.Draw(img)

# --- Stars: lots of tiny dots, fewer bigger ones, and a few cute 4-point sparkles (only above the horizon)
def star_row():
    # More stars higher up (uniform on the sphere would crowd the top of the image, so this keeps it balanced)
    return rng.uniform(0, H * 0.49)

for _ in range(4500):
    x, y = rng.uniform(0, W), star_row()
    b = int(rng.uniform(90, 255))
    tint = rng.choice([(1, 1, 1), (0.8, 0.9, 1), (1, 0.95, 0.8)])
    d.point((x, y), fill=tuple(int(b * c) for c in tint))
for _ in range(500):
    x, y = rng.uniform(0, W), star_row()
    r = rng.uniform(1.0, 2.2)
    d.ellipse((x - r, y - r, x + r, y + r), fill=(235, 240, 255))
for _ in range(45):
    x, y = rng.uniform(0, W), star_row()
    s = rng.uniform(5, 11)
    d.polygon([(x, y - s), (x + s * 0.22, y - s * 0.22), (x + s, y), (x + s * 0.22, y + s * 0.22),
               (x, y + s), (x - s * 0.22, y + s * 0.22), (x - s, y), (x - s * 0.22, y - s * 0.22)], fill=(255, 250, 225))

# --- Earth: flat cartoon planet with continents, clouds, a night side and a thin atmosphere ring
ex, ey = to_px(EARTH_AZIMUTH, EARTH_ELEVATION)
er_y = EARTH_RADIUS_DEG / 180 * H
er_x = er_y / math.cos(math.radians(EARTH_ELEVATION))  # The panorama stretches things sideways away from the horizon
size = int(er_x * 2 + 40), int(er_y * 2 + 40)
earth = Image.new("RGBA", (400, 400), (0, 0, 0, 0))  # Drawn round at 400x400, then squeezed into place
ed = ImageDraw.Draw(earth)
cx = cy = 200; R = 170
ed.ellipse((cx - R - 14, cy - R - 14, cx + R + 14, cy + R + 14), fill=(90, 160, 255, 70))  # Atmosphere glow
ed.ellipse((cx - R, cy - R, cx + R, cy + R), fill=(42, 110, 214, 255))                      # Ocean
mask = Image.new("L", (400, 400), 0)
ImageDraw.Draw(mask).ellipse((cx - R, cy - R, cx + R, cy + R), fill=255)
land = Image.new("RGBA", (400, 400), (0, 0, 0, 0))
ld = ImageDraw.Draw(land)
for (lx, ly, lr, col) in [(150, 150, 70, (86, 170, 84)), (205, 140, 55, (96, 178, 90)), (255, 250, 60, (190, 160, 90)),
                          (130, 260, 45, (86, 170, 84)), (240, 200, 40, (86, 170, 84))]:
    pts = [(lx + math.cos(a) * lr * rng.uniform(0.6, 1.2), ly + math.sin(a) * lr * rng.uniform(0.6, 1.2))
           for a in np.linspace(0, math.tau, 9, endpoint=False)]
    ld.polygon(pts, fill=col + (255,))
for (lx, ly, lw) in [(110, 120, 120), (190, 215, 150), (160, 300, 110), (250, 95, 90)]:
    ld.rounded_rectangle((lx, ly, lx + lw, ly + 16), radius=8, fill=(245, 250, 255, 235))  # Cloud streaks
earth.paste(land, (0, 0), Image.composite(land, Image.new("RGBA", (400, 400)), mask).split()[3])
shade = Image.new("RGBA", (400, 400), (0, 0, 0, 0))
ImageDraw.Draw(shade).ellipse((cx - R + 170, cy - R - 20, cx + R + 250, cy + R + 20), fill=(5, 10, 30, 170))  # Night side
earth.paste(shade, (0, 0), Image.composite(shade, Image.new("RGBA", (400, 400)), mask).split()[3])
earth = earth.resize((int(er_x * 2 * 400 / (2 * R)), int(er_y * 2 * 400 / (2 * R))), Image.LANCZOS)
img.paste(earth, (int(ex - earth.width / 2), int(ey - earth.height / 2)), earth)

# --- Low-poly mountains on the horizon: two layers of flat gray peaks that wrap around seamlessly
horizon = H / 2
def mountain_layer(peaks, height_deg, color, seed):
    r = np.random.default_rng(seed)
    xs = np.linspace(0, W, peaks + 1)
    heights = r.uniform(0.35, 1.0, peaks + 1) * height_deg / 180 * H
    heights[-1] = heights[0]  # Same height at both ends so the panorama wraps without a seam
    pts = [(0, horizon + 6)]
    for i in range(peaks):
        pts.append((xs[i], horizon - heights[i]))
        mid = (xs[i] + xs[i + 1]) / 2 + r.uniform(-0.2, 0.2) * (xs[1] - xs[0])
        pts.append((mid, horizon - min(heights[i], heights[i + 1]) * r.uniform(0.35, 0.7)))
    pts.append((W, horizon - heights[-1]))
    pts.append((W, horizon + 6))
    d.polygon(pts, fill=color)
    # A lighter face on each peak's left side (like light hitting it) for the low-poly look
    for i in range(peaks):
        top = (xs[i], horizon - heights[i])
        d.polygon([top, (xs[i] - (xs[1] - xs[0]) * 0.28, horizon - heights[i] * 0.35), (xs[i], horizon - heights[i] * 0.3)],
                  fill=tuple(min(255, c + 18) for c in color))

mountain_layer(28, 6.0, (58, 58, 64), 3)
mountain_layer(46, 3.2, (84, 84, 90), 5)

# --- Below the horizon: plain moon-gray (mostly hidden by the ground, avoids a black gap at the edge)
d.rectangle((0, horizon + 5, W, H), fill=(128, 128, 130))

img.save(OUTPUT)
print("saved", OUTPUT)
