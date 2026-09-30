"""Creates a seamless (tileable), stylized moon ground texture: a few flat gray tones and simple cartoon craters.
It matches the game's low-poly look instead of trying to be photo-realistic.
Run from the project root:  python Tools/make_moon_ground_texture.py
"""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SIZE = 1024          # Final texture size
SS = 2               # Drawn at 2x and scaled down: smooth (anti-aliased) edges
OUTPUT = "Assets/Materials/MoonGround.png"
rng = np.random.default_rng(21)
N = SIZE * SS

BASE = (132, 131, 128)      # Main moon gray
PATCH_DARK = (125, 124, 121)
PATCH_LIGHT = (138, 137, 134)
BOWL = (116, 115, 113)      # Inside of a crater
SHADOW = (100, 99, 98)      # Shadowed inner wall
RIM = (156, 155, 151)       # Lit rim

# --- Big soft patches: very smooth wrapping noise cut into 3 flat tones (posterized = cartoon look)
f = np.fft.fftfreq(N)
fx, fy = np.meshgrid(f, f)
radius = np.sqrt(fx ** 2 + fy ** 2)
radius[0, 0] = 1
spectrum = (rng.normal(size=(N, N)) + 1j * rng.normal(size=(N, N))) * np.exp(-(radius * N / 6) ** 2)
noise = np.real(np.fft.ifft2(spectrum))
noise = (noise - noise.mean()) / noise.std()
tones = np.zeros((N, N, 3))
tones[:] = BASE
tones[noise < -0.7] = PATCH_DARK
tones[noise > 0.8] = PATCH_LIGHT
img = Image.fromarray(tones.astype(np.uint8))
d = ImageDraw.Draw(img)


def wrapped(draw_fn, x, y, r):
    """Draws a shape again on the other side when it crosses an edge, so the texture tiles without seams."""
    for ox in (-N, 0, N):
        for oy in (-N, 0, N):
            if -r <= x + ox <= N + r and -r <= y + oy <= N + r:
                draw_fn(x + ox, y + oy)


# --- Craters: flat bowl, a crescent of shadow on the upper-left inner wall, a lit crescent on the lower-right rim
craters = []
for _ in range(400):
    if len(craters) >= 12: break
    r = rng.uniform(28, 120) * SS if rng.random() < 0.8 else rng.uniform(140, 200) * SS
    x, y = rng.uniform(0, N, 2)
    # Keep craters from overlapping (cleaner, more cartoon-like)
    if any(min(abs(x - cx), N - abs(x - cx)) ** 2 + min(abs(y - cy), N - abs(y - cy)) ** 2 < (r + cr + 12 * SS) ** 2
           for cx, cy, cr in craters):
        continue
    craters.append((x, y, r))

for x, y, r in craters:
    squash = 0.82  # Slightly oval, as if seen at an angle
    def rim(cx, cy):
        d.ellipse((cx - r * 1.12, cy - r * 1.12 * squash, cx + r * 1.12, cy + r * 1.12 * squash), fill=RIM)
    def bowl(cx, cy):
        d.ellipse((cx - r, cy - r * squash, cx + r, cy + r * squash), fill=BOWL)
    def shadow(cx, cy):
        # Shadow = bowl minus a shifted circle: draw the shadow, then cover part of it again with the bowl color
        d.ellipse((cx - r, cy - r * squash, cx + r, cy + r * squash), fill=SHADOW)
        s = r * 0.28
        d.ellipse((cx - r + s, cy - (r - s * 0.9) * squash, cx + r + s * 0.4, cy + (r + s * 0.2) * squash), fill=BOWL)
    wrapped(rim, x + r * 0.08, y + r * 0.08, r * 1.2)  # Rim shifted toward the light side
    wrapped(bowl, x, y, r * 1.2)
    wrapped(shadow, x, y, r * 1.2)

# --- A few small flat pebbles
for _ in range(60):
    x, y = rng.uniform(0, N, 2)
    r = rng.uniform(3, 7) * SS
    col = PATCH_DARK if rng.random() < 0.6 else RIM
    wrapped(lambda cx, cy: d.ellipse((cx - r, cy - r * 0.8, cx + r, cy + r * 0.8), fill=col), x, y, r)

img = img.resize((SIZE, SIZE), Image.LANCZOS)
img.save(OUTPUT)
print("saved", OUTPUT, np.asarray(img).mean())
