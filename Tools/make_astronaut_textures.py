"""Creates the extra textures of the country astronauts: the NASA patch, the country flags and the fabric patterns.

Everything is drawn with simple shapes at 2x size and then shrunk (that smooths the edges, like anti-aliasing).
Used by Tools/recolor_astronaut_skins.py (badges on the chest) and Tools/build_astronaut_characters.py (clothes).
Run from the project root:  python Tools/make_astronaut_textures.py
"""
import math
import os
import random
from PIL import Image, ImageDraw, ImageFont, ImageFilter

OUTPUT = "Assets/Models/Astronauts/Textures"
SS = 2  # Supersampling factor
FONT = "C:/Windows/Fonts/ariblk.ttf"


def canvas(w, h, color):
    img = Image.new("RGB", (w * SS, h * SS), color)
    return img, ImageDraw.Draw(img)


def save(img, name):
    img = img.resize((img.width // SS, img.height // SS), Image.LANCZOS)
    img.save(os.path.join(OUTPUT, name))
    print("saved", name, img.size)


def star_points(cx, cy, r_out, r_in, points=5, rotation=-90):
    pts = []
    for i in range(points * 2):
        r = r_out if i % 2 == 0 else r_in
        a = math.radians(rotation + i * 180 / points)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def patch_border(d, w, h, color, width):
    """A stitched edge, like a sewn badge."""
    d.rectangle([0, 0, w - 1, h - 1], outline=color, width=width)
    step = width * 2
    for x in range(0, w, step):
        d.line([(x, width // 2), (x + width, width // 2)], fill=(255, 255, 255), width=max(1, width // 4))
        d.line([(x, h - width // 2), (x + width, h - width // 2)], fill=(255, 255, 255), width=max(1, width // 4))


# ------------------------------------------------------------------ NASA insignia ("meatball")

def nasa_logo():
    S = 512 * SS
    img = Image.new("RGB", (S, S), (255, 255, 255))
    d = ImageDraw.Draw(img)
    blue, red = (11, 61, 145), (252, 61, 33)
    d.ellipse([0, 0, S - 1, S - 1], fill=blue)

    # Stars
    rnd = random.Random(4)
    for _ in range(22):
        x, y = rnd.uniform(0.2, 0.8) * S, rnd.uniform(0.15, 0.85) * S
        if (x - S / 2) ** 2 + (y - S / 2) ** 2 > (S * 0.42) ** 2: continue
        r = rnd.uniform(0.004, 0.011) * S
        d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 255, 255))

    # Orbit: a thin white ellipse, tilted
    orbit = Image.new("L", (S, S), 0)
    od = ImageDraw.Draw(orbit)
    od.ellipse([S * 0.08, S * 0.33, S * 0.92, S * 0.67], outline=255, width=int(S * 0.014))
    orbit = orbit.rotate(28, resample=Image.BICUBIC)
    img.paste((255, 255, 255), mask=orbit)

    # The red "vector": a forked wing from lower left to upper right
    wing = [(0.10, 0.74), (0.46, 0.54), (0.93, 0.25), (0.86, 0.36), (0.55, 0.57), (0.64, 0.60),
            (0.40, 0.62), (0.16, 0.76)]
    d.polygon([(x * S, y * S) for x, y in wing], fill=red)
    # Keep it inside the circle
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).ellipse([0, 0, S - 1, S - 1], fill=255)
    white = Image.new("RGB", (S, S), (255, 255, 255))
    img = Image.composite(img, white, mask)
    d = ImageDraw.Draw(img)

    # Text
    font = ImageFont.truetype(FONT, int(S * 0.22))
    box = d.textbbox((0, 0), "NASA", font=font)
    tw, th = box[2] - box[0], box[3] - box[1]
    d.text(((S - tw) / 2 - box[0], (S - th) / 2 - box[1]), "NASA", font=font, fill=(255, 255, 255))
    save(img, "Patch_NASA.png")


# ------------------------------------------------------------------ Flags (3:2 patches)

FW, FH = 600, 400


def flag_usa():
    img, d = canvas(FW, FH, (255, 255, 255))
    W, H = FW * SS, FH * SS
    stripe = H / 13
    for i in range(0, 13, 2):
        d.rectangle([0, i * stripe, W, (i + 1) * stripe], fill=(178, 34, 52))
    cw, ch = W * 0.4, stripe * 7
    d.rectangle([0, 0, cw, ch], fill=(60, 59, 110))
    for row in range(9):
        count = 6 if row % 2 == 0 else 5
        for col in range(count):
            x = cw / 12 * (col * 2 + (1 if row % 2 == 0 else 2))
            y = ch / 10 * (row + 1)
            d.polygon(star_points(x, y, ch * 0.045, ch * 0.018), fill=(255, 255, 255))
    patch_border(d, W, H, (200, 160, 40), 14 * SS)
    save(img, "Flag_USA.png")


def flag_mexico():
    img, d = canvas(FW, FH, (255, 255, 255))
    W, H = FW * SS, FH * SS
    d.rectangle([0, 0, W / 3, H], fill=(0, 104, 71))
    d.rectangle([W * 2 / 3, 0, W, H], fill=(206, 17, 38))
    cx, cy = W / 2, H / 2
    s = H * 0.30
    green, brown, dark = (0, 110, 60), (140, 90, 40), (90, 55, 20)
    # Wreath
    d.arc([cx - s, cy - s * 0.6, cx + s, cy + s * 1.1], 20, 160, fill=green, width=int(s * 0.12))
    # Cactus
    d.ellipse([cx - s * 0.18, cy + s * 0.15, cx + s * 0.18, cy + s * 0.65], fill=green)
    d.ellipse([cx - s * 0.45, cy + s * 0.20, cx - s * 0.12, cy + s * 0.48], fill=green)
    d.ellipse([cx + s * 0.12, cy + s * 0.20, cx + s * 0.45, cy + s * 0.48], fill=green)
    # Eagle: body, raised wings, head, snake
    d.ellipse([cx - s * 0.25, cy - s * 0.35, cx + s * 0.25, cy + s * 0.25], fill=brown)
    d.polygon([(cx - s * 0.15, cy - s * 0.2), (cx - s * 0.75, cy - s * 0.75), (cx - s * 0.55, cy - s * 0.25),
               (cx - s * 0.2, cy + s * 0.05)], fill=dark)
    d.polygon([(cx + s * 0.15, cy - s * 0.2), (cx + s * 0.75, cy - s * 0.75), (cx + s * 0.55, cy - s * 0.25),
               (cx + s * 0.2, cy + s * 0.05)], fill=dark)
    d.ellipse([cx - s * 0.42, cy - s * 0.55, cx - s * 0.18, cy - s * 0.30], fill=brown)
    d.polygon([(cx - s * 0.42, cy - s * 0.44), (cx - s * 0.55, cy - s * 0.38), (cx - s * 0.40, cy - s * 0.36)],
              fill=(230, 170, 30))
    d.arc([cx - s * 0.6, cy - s * 0.5, cx - s * 0.1, cy - s * 0.05], 90, 250, fill=(40, 140, 60), width=int(s * 0.06))
    patch_border(d, W, H, (200, 160, 40), 14 * SS)
    save(img, "Flag_Mexico.png")


def flag_kazakhstan():
    img, d = canvas(FW, FH, (0, 175, 202))
    W, H = FW * SS, FH * SS
    gold = (254, 197, 12)
    cx, cy = W * 0.55, H * 0.40
    r = H * 0.14
    # Sun with rays
    for i in range(32):
        a = math.radians(i * 360 / 32)
        a1, a2 = a - math.radians(3.5), a + math.radians(3.5)
        d.polygon([(cx + r * 1.15 * math.cos(a1), cy + r * 1.15 * math.sin(a1)),
                   (cx + r * 1.75 * math.cos(a), cy + r * 1.75 * math.sin(a)),
                   (cx + r * 1.15 * math.cos(a2), cy + r * 1.15 * math.sin(a2))], fill=gold)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=gold)
    # Steppe eagle under the sun: two long swept wings
    ey = cy + r * 2.05
    for side in (-1, 1):
        d.polygon([(cx, ey), (cx + side * r * 3.3, ey - r * 0.55), (cx + side * r * 3.0, ey - r * 0.2),
                   (cx + side * r * 2.2, ey + r * 0.05), (cx + side * r * 0.4, ey + r * 0.35)], fill=gold)
    d.ellipse([cx - r * 0.35, ey - r * 0.25, cx + r * 0.35, ey + r * 0.45], fill=gold)
    # Ornament band near the hoist: repeating ram-horn curls
    bx = W * 0.07
    for i in range(9):
        y = H * (0.06 + i * 0.11)
        s = H * 0.045
        d.arc([bx - s, y - s, bx + s, y + s], 180, 360, fill=gold, width=int(s * 0.35))
        d.arc([bx - s * 2, y - s * 0.2, bx, y + s * 1.8], 270, 90, fill=gold, width=int(s * 0.3))
        d.arc([bx, y - s * 0.2, bx + s * 2, y + s * 1.8], 90, 270, fill=gold, width=int(s * 0.3))
    patch_border(d, W, H, (200, 160, 40), 14 * SS)
    save(img, "Flag_Kazakhstan.png")


def flag_russia():
    img, d = canvas(FW, FH, (255, 255, 255))
    W, H = FW * SS, FH * SS
    d.rectangle([0, H / 3, W, H * 2 / 3], fill=(0, 57, 166))
    d.rectangle([0, H * 2 / 3, W, H], fill=(213, 43, 30))
    patch_border(d, W, H, (200, 160, 40), 14 * SS)
    save(img, "Flag_Russia.png")


def flag_japan():
    img, d = canvas(FW, FH, (255, 255, 255))
    W, H = FW * SS, FH * SS
    r = H * 0.3
    d.ellipse([W / 2 - r, H / 2 - r, W / 2 + r, H / 2 + r], fill=(188, 0, 45))
    patch_border(d, W, H, (200, 160, 40), 14 * SS)
    save(img, "Flag_Japan.png")


def flag_germany():
    img, d = canvas(FW, FH, (0, 0, 0))
    W, H = FW * SS, FH * SS
    d.rectangle([0, H / 3, W, H * 2 / 3], fill=(221, 0, 0))
    d.rectangle([0, H * 2 / 3, W, H], fill=(255, 206, 0))
    patch_border(d, W, H, (200, 160, 40), 14 * SS)
    save(img, "Flag_Germany.png")


def flag_canada():
    img, d = canvas(FW, FH, (255, 255, 255))
    W, H = FW * SS, FH * SS
    red = (216, 6, 33)
    d.rectangle([0, 0, W / 4, H], fill=red)
    d.rectangle([W * 3 / 4, 0, W, H], fill=red)
    # Maple leaf: an 11-point outline (x, y from -1..1, y up) plus a stem
    leaf = [(0, 1), (0.14, 0.72), (0.3, 0.8), (0.24, 0.35), (0.52, 0.62), (0.6, 0.5), (0.9, 0.56), (0.8, 0.28),
            (0.9, 0.2), (0.5, -0.12), (0.56, -0.3), (0.06, -0.24), (0.06, -0.6), (-0.06, -0.6), (-0.06, -0.24),
            (-0.56, -0.3), (-0.5, -0.12), (-0.9, 0.2), (-0.8, 0.28), (-0.9, 0.56), (-0.6, 0.5), (-0.52, 0.62),
            (-0.24, 0.35), (-0.3, 0.8), (-0.14, 0.72)]
    s = H * 0.36
    d.polygon([(W / 2 + x * s, H * 0.5 - y * s) for x, y in leaf], fill=red)
    patch_border(d, W, H, (200, 160, 40), 14 * SS)
    save(img, "Flag_Canada.png")


def flag_brazil():
    img, d = canvas(FW, FH, (0, 151, 57))
    W, H = FW * SS, FH * SS
    m = H * 0.1
    d.polygon([(m, H / 2), (W / 2, m), (W - m, H / 2), (W / 2, H - m)], fill=(254, 221, 0))
    r = H * 0.25
    d.ellipse([W / 2 - r, H / 2 - r, W / 2 + r, H / 2 + r], fill=(1, 33, 105))
    d.arc([W / 2 - r * 2.2, H / 2 - r * 0.7, W / 2 + r * 1.4, H / 2 + r * 2.6], 215, 300, fill=(255, 255, 255), width=int(r * 0.16))
    rnd = random.Random(8)
    for _ in range(14):
        a, rr = rnd.uniform(0, 2 * math.pi), rnd.uniform(0.2, 0.85) * r
        x, y = W / 2 + rr * math.cos(a), H / 2 + rr * math.sin(a)
        d.ellipse([x - 4 * SS, y - 4 * SS, x + 4 * SS, y + 4 * SS], fill=(255, 255, 255))
    patch_border(d, W, H, (200, 160, 40), 14 * SS)
    save(img, "Flag_Brazil.png")


def flag_turkey():
    img, d = canvas(FW, FH, (227, 10, 23))
    W, H = FW * SS, FH * SS
    white = (255, 255, 255)
    # Crescent: a white disc with a slightly smaller red disc shifted to the right
    cx, cy, r = W * 0.36, H / 2, H * 0.25
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=white)
    r2, cx2 = r * 0.8, cx + r * 0.25
    d.ellipse([cx2 - r2, cy - r2, cx2 + r2, cy + r2], fill=(227, 10, 23))
    d.polygon(star_points(W * 0.54, cy, H * 0.125, H * 0.05, rotation=180), fill=white)
    patch_border(d, W, H, (200, 160, 40), 14 * SS)
    save(img, "Flag_Turkey.png")


# ------------------------------------------------------------------ Fabric patterns
# These wrap around the body: x = around the body, y = up/down (top of the image = top of the garment)

def serape():
    """Mexican poncho: bright horizontal stripes."""
    W, H = 512, 512
    img, d = canvas(W, H, (0, 0, 0))
    colors = [(200, 30, 50), (240, 120, 20), (250, 200, 30), (30, 140, 70), (250, 245, 230),
              (220, 60, 140), (20, 20, 20), (40, 90, 200)]
    y, i = 0, 0
    widths = [34, 10, 22, 8, 40, 12, 6, 26]
    while y < H * SS:
        w = widths[i % len(widths)] * SS
        d.rectangle([0, y, W * SS, y + w], fill=colors[(i * 3) % len(colors)])
        y += w
        i += 1
    # A little woven noise
    rnd = random.Random(2)
    for _ in range(9000):
        x, yy = rnd.randrange(W * SS), rnd.randrange(H * SS)
        c = img.getpixel((x, yy))
        f = rnd.uniform(0.8, 1.1)
        d.point((x, yy), fill=tuple(min(255, int(v * f)) for v in c))
    save(img, "Fabric_Serape.png")


def vest():
    """Turkish vest (yelek): red felt with gold cord embroidery - swirls along the edges and a curl in the middle."""
    W, H = 1024, 512
    img, d = canvas(W, H, (178, 18, 32))
    rnd = random.Random(9)
    for _ in range(16000):  # Felt texture
        x, y = rnd.randrange(W * SS), rnd.randrange(H * SS)
        v = rnd.randint(-14, 14)
        d.point((x, y), fill=(178 + v, 18 + v // 3, 32 + v // 3))
    gold = (232, 186, 70)
    w = 5 * SS
    # Edge cords: bottom and both front edges (the ends of the image meet at the open front)
    d.rectangle([0, H * SS - 16 * SS, W * SS, H * SS - 8 * SS], fill=gold)
    for x0 in (8 * SS, W * SS - 16 * SS):
        d.rectangle([x0, 0, x0 + 8 * SS, H * SS], fill=gold)
    # Swirls near the front edges and along the bottom
    for x in (60 * SS, W * SS - 60 * SS):
        for y in range(40 * SS, H * SS - 60 * SS, 110 * SS):
            s = 34 * SS
            d.arc([x - s, y, x + s, y + 2 * s], 90, 360, fill=gold, width=w)
            d.arc([x - s / 2, y + s / 2, x + s / 2, y + s * 1.5], 180, 450, fill=gold, width=w)
    for x in range(120 * SS, W * SS - 120 * SS, 130 * SS):
        y = H * SS - 90 * SS
        d.arc([x - 40 * SS, y, x + 40 * SS, y + 60 * SS], 180, 360, fill=gold, width=w)
        d.arc([x - 20 * SS, y + 10 * SS, x + 20 * SS, y + 40 * SS], 0, 270, fill=gold, width=w)
    save(img, "Fabric_Vest.png")


def ornament_curl(d, x, y, s, color, width):
    """A Kazakh "ram horn" (koshkar muyiz) motif: a stem with two curls."""
    d.line([(x, y + s), (x, y - s * 0.2)], fill=color, width=width)
    d.arc([x - s, y - s * 0.9, x, y + s * 0.1], 0, 270, fill=color, width=width)
    d.arc([x, y - s * 0.9, x + s, y + s * 0.1], 270, 180, fill=color, width=width)


def chapan():
    """Kazakh robe: dark velvet with a gold ornament border at the bottom and on both front edges."""
    W, H = 1024, 512
    img, d = canvas(W, H, (107, 21, 48))
    rnd = random.Random(5)
    for _ in range(20000):  # Velvet sheen
        x, y = rnd.randrange(W * SS), rnd.randrange(H * SS)
        v = rnd.randint(-12, 12)
        d.point((x, y), fill=(107 + v, 21 + v // 2, 48 + v // 2))
    gold = (226, 176, 60)
    band = 70 * SS
    # Bottom border
    d.rectangle([0, H * SS - band, W * SS, H * SS], fill=(70, 12, 30))
    d.line([(0, H * SS - band), (W * SS, H * SS - band)], fill=gold, width=5 * SS)
    for x in range(0, W * SS, 64 * SS):
        ornament_curl(d, x + 32 * SS, H * SS - band / 2, 22 * SS, gold, 4 * SS)
    # Front edges (left and right end of the image meet at the front opening)
    edge = 46 * SS
    for x0 in (0, W * SS - edge):
        d.rectangle([x0, 0, x0 + edge, H * SS], fill=(70, 12, 30))
        d.line([(x0 + (edge if x0 == 0 else 0), 0), (x0 + (edge if x0 == 0 else 0), H * SS)], fill=gold, width=5 * SS)
        for y in range(0, H * SS, 56 * SS):
            ornament_curl(d, x0 + edge / 2, y + 28 * SS, 15 * SS, gold, 3 * SS)
    # Top edge (collar)
    d.rectangle([0, 0, W * SS, 20 * SS], fill=gold)
    save(img, "Fabric_Chapan.png")


def takiya():
    """Kazakh skullcap: dark with white/gold embroidered ornaments around it."""
    W, H = 512, 256
    img, d = canvas(W, H, (25, 25, 60))
    gold, white = (226, 176, 60), (240, 235, 220)
    for i in range(8):
        x = (i + 0.5) * W * SS / 8
        ornament_curl(d, x, H * SS * 0.55, 20 * SS, white if i % 2 else gold, 4 * SS)
    d.rectangle([0, H * SS - 26 * SS, W * SS, H * SS], fill=gold)
    for x in range(0, W * SS, 24 * SS):
        d.polygon([(x, H * SS - 26 * SS), (x + 12 * SS, H * SS - 40 * SS), (x + 24 * SS, H * SS - 26 * SS)], fill=gold)
    save(img, "Fabric_Takiya.png")


def fur(name, base, seed):
    """Soft fur: noise, blurred a little, with short strands."""
    W = H = 512
    img = Image.new("RGB", (W, H), base)
    d = ImageDraw.Draw(img)
    rnd = random.Random(seed)
    for _ in range(14000):
        x, y = rnd.randrange(W), rnd.randrange(H)
        f = rnd.uniform(0.7, 1.25)
        c = tuple(max(0, min(255, int(v * f))) for v in base)
        a = rnd.uniform(-0.5, 0.5)
        l = rnd.uniform(3, 9)
        d.line([(x, y), (x + l * math.sin(a), y + l * math.cos(a))], fill=c, width=1)
    img = img.filter(ImageFilter.GaussianBlur(0.6))
    img.save(os.path.join(OUTPUT, name))
    print("saved", name, img.size)


def straw():
    """Sombrero straw: woven criss-cross lines."""
    W = H = 512
    img = Image.new("RGB", (W, H), (222, 188, 112))
    d = ImageDraw.Draw(img)
    for i in range(-H, W, 12):
        d.line([(i, 0), (i + H, H)], fill=(196, 160, 86), width=3)
        d.line([(i + H, 0), (i, H)], fill=(236, 206, 136), width=2)
    img.save(os.path.join(OUTPUT, "Fabric_Straw.png"))
    print("saved Fabric_Straw.png", img.size)


if __name__ == "__main__":
    os.makedirs(OUTPUT, exist_ok=True)
    nasa_logo()
    flag_usa()
    flag_mexico()
    flag_kazakhstan()
    flag_russia()
    flag_japan()
    flag_germany()
    flag_canada()
    flag_brazil()
    flag_turkey()
    vest()
    serape()
    chapan()
    takiya()
    fur("Fur_Brown.png", (92, 62, 40), 1)
    fur("Fur_Cream.png", (226, 212, 186), 2)
    straw()
