"""Finds where the chest badge strip of the astronaut texture sits on the model (run inside Blender).

The strip under the helmet (USA flag, NASA, Swiss flag in the original texture) is stored skewed and rotated in the
texture, so a flag can't simply be pasted into the image. Instead, for every texture pixel of the strip this script
finds the point on the model it colors and writes its position as seen from the front (X = viewer's right, Z = up).
Tools/recolor_astronaut_skins.py then draws the badges in that flat front view and copies them into the texture.

Run from the project root:
  blender --background --factory-startup --python Tools/export_astronaut_chest_map.py
Writes Tools/data/astronaut_chest_map.npz
"""
import os

import bpy
import numpy as np

PROJECT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
FBX = os.path.join(PROJECT, "Assets", "Sprites", "Astronaut", "cute-astronaut", "source", "Astronaut.fbx")
OUTPUT = os.path.join(PROJECT, "Tools", "data", "astronaut_chest_map.npz")
SIZE = 2048  # Texture size of the skins
STRIP_BOX = (0, 1560, 460, 2048)  # Pixel area of the strip in the texture (x0, y0, x1, y1)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete()
bpy.ops.import_scene.fbx(filepath=FBX)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
arm.data.pose_position = "REST"
obj = next(o for o in bpy.data.objects if o.type == "MESH")
depsgraph = bpy.context.evaluated_depsgraph_get()
mesh = obj.evaluated_get(depsgraph).to_mesh()
mesh.calc_loop_triangles()
uv = mesh.uv_layers.active.data
world = obj.matrix_world

pixels, fronts = [], []
for tri in mesh.loop_triangles:
    if mesh.materials[tri.material_index].name != "Astronaut":
        continue
    uvs = np.array([uv[l].uv[:] for l in tri.loops]) * [SIZE, -SIZE] + [0, SIZE]  # To pixel coordinates (y down)
    center = uvs.mean(0)
    if not (STRIP_BOX[0] <= center[0] < STRIP_BOX[2] and STRIP_BOX[1] <= center[1] < STRIP_BOX[3]):
        continue
    pts = np.array([(world @ mesh.vertices[v].co)[:] for v in tri.vertices])
    # Rasterize the triangle in the texture: every pixel center inside it gets a barycentric mix of the 3D corners
    x0, y0 = np.floor(uvs.min(0)).astype(int)
    x1, y1 = np.ceil(uvs.max(0)).astype(int)
    xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
    p = np.stack([xs.ravel() + 0.5, ys.ravel() + 0.5], 1)
    a, b, c = uvs
    m = np.array([b - a, c - a]).T
    if abs(np.linalg.det(m)) < 1e-9:
        continue
    w = np.linalg.solve(m, (p - a).T).T
    inside = (w[:, 0] >= -0.02) & (w[:, 1] >= -0.02) & (w.sum(1) <= 1.02)
    w = w[inside]
    pos = pts[0] + w[:, :1] * (pts[1] - pts[0]) + w[:, 1:] * (pts[2] - pts[0])
    pixels.append(np.floor(p[inside]).astype(int))
    fronts.append(np.stack([pos[:, 0], pos[:, 2], pos[:, 1]], 1))

pixels = np.concatenate(pixels)
fronts = np.concatenate(fronts)
# Front of the chest only (the model looks towards -Y). Not by face normal: the model's mirrored scale flips them.
keep = fronts[:, 2] < -0.3
np.savez(OUTPUT, pixels=pixels[keep], front=fronts[keep, :2])
print("chest map:", keep.sum(), "pixels, X range", fronts[keep, 0].min(), fronts[keep, 0].max(),
      "Z range", fronts[keep, 1].min(), fronts[keep, 1].max())
