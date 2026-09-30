"""Creates colored copies of the astronaut texture for each skin (Classic included).

Before recoloring, known painting mistakes in the original texture are fixed (see HOLES_TO_FILL).

The original texture only has three colors: white (suit), orange (details) and black.
Every pixel is treated as a mix of those three, so soft edges stay smooth after recoloring.

Every astronaut belongs to a country: the badge strip on the chest gets the NASA patch on the left and the
country flag on the right (as seen from the front). Where each strip pixel sits on the chest comes from
Tools/data/astronaut_chest_map.npz (Tools/export_astronaut_chest_map.py); the badge images come from
Tools/make_astronaut_textures.py.

Run from the project root (after make_astronaut_textures.py):  python Tools/recolor_astronaut_skins.py
"""
import numpy as np
from PIL import Image, ImageFilter

SOURCE = "Assets/Sprites/Astronaut/cute-astronaut/textures/Atronaut_4096.png"
OUTPUT = "Assets/Resources/AstronautSkins/Astronaut_{}.png"  # The in-game skins (AstronautSkin.cs)
MODEL_OUTPUT = "Assets/Models/Astronauts/Textures/Astronaut_{}.png"  # For the country astronaut FBX files
BADGES = "Assets/Models/Astronauts/Textures/"
CHEST_MAP = "Tools/data/astronaut_chest_map.npz"

WHITE = np.array([255, 255, 255], float)
ORANGE = np.array([255, 166, 57], float)

# name: (suit color, detail color, dark color, country flag)
SKINS = {
    "Classic": ((255, 255, 255), (255, 166, 57), (0, 0, 0), "Germany"),  # Original colors
    "Pink": ((255, 196, 222), (255, 72, 160), (60, 20, 45), "Japan"),
    "Nerd": ((236, 232, 214), (64, 160, 92), (18, 30, 60), "Canada"),
    "Cool": ((46, 46, 52), (242, 190, 60), (10, 10, 12), "Brazil"),
    "Emo":  ((34, 26, 44), (160, 60, 230), (8, 6, 12), "USA"),
    # Country astronauts (only exported as FBX, not skins in the game yet)
    "Captain": ((200, 28, 28), (242, 194, 0), (45, 10, 14), "USA"),
    "Mexican": ((36, 140, 64), (200, 30, 50), (12, 20, 12), "Mexico"),
    "Kazakh": ((70, 186, 214), (242, 194, 0), (20, 25, 60), "Kazakhstan"),
    "Russian": ((72, 112, 190), (213, 43, 30), (25, 20, 24), "Russia"),
    "Turkish": ((255, 255, 255), (206, 20, 40), (0, 0, 0), "Turkey"),
}
IN_GAME_SKINS = ("Classic", "Pink", "Nerd", "Cool", "Emo")

# Badge layout on the chest, in model units seen from the front (X = viewer's right, Z = up).
# The original strip had: USA flag (left), NASA (middle), Swiss flag (right).
NASA_BADGE = (-0.55, 1.99, 0.32)        # Center X, center Z, diameter  (where the USA flag was)
FLAG_BADGE = (0.54, 1.99, 0.36, 0.24)   # Center X, center Z, width, height  (where the Swiss flag was)

# Logo pixels (flags, NASA) and a few pixels around them keep their original colors
LOGO_MARGIN = 6

# The original texture has UV islands that were left half-painted: the white outline is there but parts of the
# inside are black. On the model these show up as black wedges (e.g. on the chest under the helmet).
# For each box below, black pixels that are enclosed by the island's outline are painted white.
HOLES_TO_FILL = [
    (225, 570, 405, 800),  # Chest / collar under the helmet
]


def fill_enclosed_black(image, box):
    """Paints black areas inside `box` white, except the black that is connected to the box border (outside the island)."""
    from PIL import ImageDraw
    x0, y0, x1, y1 = box
    crop = image.crop(box)
    dark = Image.fromarray(((np.asarray(crop).sum(2) < 200) * 255).astype(np.uint8)).copy()  # copy: writable
    # Flood-fill the outside black from every border pixel; whatever dark area stays unreached is a hole
    w, h = dark.size
    for x in range(w):
        for y in (0, h - 1):
            if dark.getpixel((x, y)) == 255: ImageDraw.floodfill(dark, (x, y), 128)
    for y in range(h):
        for x in (0, w - 1):
            if dark.getpixel((x, y)) == 255: ImageDraw.floodfill(dark, (x, y), 128)
    holes = np.asarray(dark) == 255
    pixels = np.asarray(crop).copy()
    pixels[holes] = 255
    image.paste(Image.fromarray(pixels), (x0, y0))
    return int(holes.sum())


# Whole UV islands to repaint. The neck collar is a jagged (star-shaped) ring painted fully black in the original,
# which shows as black wedges hanging under the helmet. It is repainted as a slightly darker shade of the suit.
# Each island is picked by one pixel inside it; the UV layout comes from Tools/data/astronaut_uv.txt
# (exported from Astronaut.fbx: vertex count, UVs, then triangles).
UV_DATA = "Tools/data/astronaut_uv.txt"
ISLANDS_TO_PAINT = [
    ((520, 1785), (215, 215, 215)),  # Neck collar -> suit color, a bit darker
    ((1880, 193), (215, 215, 215)),  # Jagged collar tip under the visor (the black wedge on the chest)
]


def load_uv_islands(path, size):
    """Returns the triangles (in pixel coordinates) grouped into islands (pieces connected by shared UV corners)."""
    lines = open(path).read().splitlines()
    vertex_count = int(lines[0].split()[0])
    uv = np.array([list(map(float, l.split())) for l in lines[1:1 + vertex_count]])
    tri_start = 1 + vertex_count
    tri_count = int(lines[tri_start].split()[1]) // 3
    tris = np.array([list(map(int, l.split())) for l in lines[tri_start + 1:tri_start + 1 + tri_count]])
    pixels = np.stack([uv[:, 0] * size[0], (1 - uv[:, 1]) * size[1]], 1)

    parent = list(range(tri_count))
    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    owner = {}
    for t, corners in enumerate(tris):
        for v in corners:
            key = tuple(np.round(pixels[v], 1))
            if key in owner: parent[root(t)] = root(owner[key])
            else: owner[key] = t
    islands = {}
    for t in range(tri_count):
        islands.setdefault(root(t), []).append([tuple(pixels[v]) for v in tris[t]])
    return list(islands.values())


def paint_island(image, islands, seed, color):
    """Paints the dark pixels of the island that contains `seed` with `color` (bright pixels are kept)."""
    from PIL import ImageDraw
    for triangles in islands:
        mask = Image.new("L", image.size, 0)
        draw = ImageDraw.Draw(mask)
        for tri in triangles: draw.polygon(tri, fill=255)
        mask = np.asarray(mask) > 0
        if not mask[seed[1], seed[0]]: continue
        pixels = np.asarray(image).copy()
        target = mask & (pixels.sum(2) < 150)
        pixels[target] = color
        image.paste(Image.fromarray(pixels))
        return int(target.sum())
    raise ValueError(f"No UV island contains pixel {seed}")


source = Image.open(SOURCE).convert("RGB")
for box in HOLES_TO_FILL:
    print("filled", fill_enclosed_black(source, box), "pixels in", box)
uv_islands = load_uv_islands(UV_DATA, source.size)
for seed, color in ISLANDS_TO_PAINT:
    print("painted", paint_island(source, uv_islands, seed, color), "pixels of the island at", seed)
img = np.asarray(source, float)

# Solve pixel = a*WHITE + b*ORANGE for each pixel (least squares), rest is black
basis = np.stack([WHITE, ORANGE], axis=1)  # 3x2
weights = np.linalg.lstsq(basis, img.reshape(-1, 3).T, rcond=None)[0].T.clip(0, 1)
a, b = weights[:, :1], weights[:, 1:]
a, b = a / np.maximum(1, a + b), b / np.maximum(1, a + b)
residual = np.abs(weights @ basis.T - img.reshape(-1, 3)).sum(1)

# Logos contain colors that aren't white/orange/black; grow that area a little to include their white parts
logo = Image.fromarray(((residual > 60).reshape(img.shape[:2]) * 255).astype(np.uint8))
keep = np.asarray(logo.filter(ImageFilter.MaxFilter(LOGO_MARGIN * 2 + 1))).reshape(-1) > 0

# Chest strip: which texture pixels it has and where each one is on the chest
chest = np.load(CHEST_MAP)
chest_px = chest["pixels"]
chest_xz = chest["front"]
chest_index = chest_px[:, 1] * img.shape[1] + chest_px[:, 0]


def sample(image, u, v):
    """Colors of an RGB image at u, v (0..1, v down); nearest pixel."""
    h, w = image.shape[:2]
    return image[np.clip((v * h).astype(int), 0, h - 1), np.clip((u * w).astype(int), 0, w - 1)]


def paint_badges(out, suit, flag_name):
    """Clears the old logos of the strip and draws NASA (left) and the country flag (right)."""
    old_logo = keep[chest_index]
    out[chest_index[old_logo]] = suit
    x, z = chest_xz[:, 0], chest_xz[:, 1]

    nasa = np.asarray(Image.open(BADGES + "Patch_NASA.png").convert("RGB"), float)
    cx, cz, d = NASA_BADGE
    u, v = (x - cx) / d + 0.5, 0.5 - (z - cz) / d
    inside = (u - 0.5) ** 2 + (v - 0.5) ** 2 <= 0.25
    out[chest_index[inside]] = sample(nasa, u[inside], v[inside])

    flag = np.asarray(Image.open(BADGES + "Flag_%s.png" % flag_name).convert("RGB"), float)
    cx, cz, w, h = FLAG_BADGE
    u, v = (x - cx) / w + 0.5, 0.5 - (z - cz) / h
    inside = (u >= 0) & (u < 1) & (v >= 0) & (v < 1)
    out[chest_index[inside]] = sample(flag, u[inside], v[inside])


for name, (suit, detail, dark, flag_name) in SKINS.items():
    suit, detail, dark = (np.array(c, float) for c in (suit, detail, dark))
    out = a * suit + b * detail + (1 - a - b) * dark
    # Pixels that aren't a white/orange/black mix (other logo colors) stay unchanged
    out[keep] = img.reshape(-1, 3)[keep]
    paint_badges(out, suit, flag_name)
    out = Image.fromarray(out.reshape(img.shape).clip(0, 255).astype(np.uint8))
    paths = [MODEL_OUTPUT.format(name)] + ([OUTPUT.format(name)] if name in IN_GAME_SKINS else [])
    for path in paths:
        out.save(path)
        print("saved", path)
