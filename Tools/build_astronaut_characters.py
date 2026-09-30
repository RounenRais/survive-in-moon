"""Builds the country astronauts in Blender and exports each one as an FBX with its animations.

Every character is the game's own astronaut (Assets/Sprites/Astronaut/cute-astronaut/source/Astronaut.fbx):
same mesh, same skeleton (German bone names, so AstronautAnimation and the existing clips keep working),
with a recolored suit texture (Tools/recolor_astronaut_skins.py: NASA left, country flag right on the chest)
and a costume on top:
  Classic (Germany)  Pink (Japan)  Nerd (Canada)  Cool (Brazil)  Emo (USA)  - the in-game skins; their accessories
                                                                  are the same shapes as in AstronautSkin.cs
  Captain (USA)  Mexican  Kazakh  Russian (woman)  Turkish        - new country costumes

How the costume is made:
  - Clothes (jacket, poncho, robe, coat, vest) copy the body's own faces in an area, push them outward and give
    them thickness. The copied vertices keep the body's bone weights, so the clothes bend exactly like the body.
  - Hats and decorations are simple shapes glued to one bone (like the accessories in AstronautSkin.cs).
  - Everything is joined into one extra skinned mesh called "Costume".
Animations (24 fps, same as the original): Idle and Walk are the original clips (the game's idle and run),
Grab is new (bend down, reach, close the hands, lift to the chest).

Run from the project root (after make_astronaut_textures.py and recolor_astronaut_skins.py):
  blender --background --factory-startup --python Tools/build_astronaut_characters.py
Writes Assets/Models/Astronauts/Astronaut_<Name>.fbx (textures in Assets/Models/Astronauts/Textures).
"""
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

PROJECT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
SOURCE_FBX = os.path.join(PROJECT, "Assets", "Sprites", "Astronaut", "cute-astronaut", "source", "Astronaut.fbx")
OUT_DIR = os.path.join(PROJECT, "Assets", "Models", "Astronauts")
TEX_DIR = os.path.join(OUT_DIR, "Textures")
FPS = 24

# Directions in Blender (the model looks towards -Y). RIGHT is the CHARACTER's right, like "right" in AstronautSkin.cs.
UP = Vector((0, 0, 1))
FORWARD = Vector((0, -1, 0))
RIGHT = Vector((-1, 0, 0))

# Measured on the model in its rest pose (T-pose), in Blender units
HELMET = Vector((0, -0.06, 2.75))  # Center of the helmet sphere
HELMET_R = 0.85
HIPS = Vector((0, -0.03, 1.30))
HIPS_HALF_WIDTH, HIPS_HALF_DEPTH = 0.67, 0.52
CHEST_BONE = Vector((0, -0.079, 1.611))  # Head of the "brust" bone
BODY_AXIS_Y = -0.05  # The torso is centered around this Y
ARM_AXIS = (-0.08, 1.9)  # (y, z) of the horizontal line through the arms (T-pose)
TORSO_BONES = {"brust", "hüfte"}
ARM_BONES = {"schulter.L", "schulter.R", "unterarm.L", "unterarm.R"}
BACKPACK_Y = 0.5  # Body vertices further back than this belong to the backpack

# Clips cut from the original 251-frame action (the same ranges as idle.anim / run.anim in Unity)
IDLE_FRAMES = (1, 43)
WALK_FRAMES = (76, 136)

# ------------------------------------------------------------------ Materials

_materials = {}


def srgb(h):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((v + 0.055) / 1.055) ** 2.4 if v > 0.04045 else v / 12.92 for v in c) + (1.0,)


def material(name, color="#FFFFFF", smoothness=0.4, metallic=0.0, texture=None):
    if name in _materials:
        return _materials[name]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = srgb(color)
    bsdf.inputs["Roughness"].default_value = 1 - smoothness
    bsdf.inputs["Metallic"].default_value = metallic
    if texture:
        tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(os.path.join(TEX_DIR, texture), check_existing=True)
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    mat.diffuse_color = srgb(color)
    _materials[name] = mat
    return mat


def color_material(color, smoothness, metallic=0.0):
    """Plain colored material, shared by all parts with the same look (like GetMaterial in AstronautSkin.cs)."""
    return material("Color_%s_%d_%d" % (color.lstrip("#"), smoothness * 100, metallic * 100), color, smoothness, metallic)


# ------------------------------------------------------------------ The body (measured once per character)


class Body:
    """The astronaut in its rest pose: world positions, bone weights and a ray-cast structure."""

    def __init__(self, arm, obj):
        arm.data.pose_position = "REST"
        bpy.context.view_layer.update()
        depsgraph = bpy.context.evaluated_depsgraph_get()
        mesh = obj.evaluated_get(depsgraph).to_mesh()
        self.positions = [obj.matrix_world @ v.co for v in mesh.vertices]
        names = {g.index: g.name for g in obj.vertex_groups}
        self.weights = [{names[g.group]: g.weight for g in v.groups if g.weight > 0} for v in obj.data.vertices]
        self.dominant = [max(w, key=w.get) if w else "" for w in self.weights]
        self.faces = [(tuple(p.vertices), mesh.materials[p.material_index].name) for p in mesh.polygons]
        body_faces = [f for f, m in self.faces if m != "visier"]
        visor_faces = [f for f, m in self.faces if m == "visier"]
        self.tree = BVHTree.FromPolygons(self.positions, body_faces)
        self.visor_tree = BVHTree.FromPolygons(self.positions, visor_faces)
        obj.evaluated_get(depsgraph).to_mesh_clear()
        arm.data.pose_position = "POSE"


def cast(tree, origin, direction):
    """Ray cast returning (point, normal facing the ray) or None. Normals are flipped by the model's mirrored
    scale, so they are turned to face against the ray."""
    hit = tree.ray_cast(origin, direction)
    if hit[0] is None:
        return None
    normal = hit[1] if hit[1].dot(direction) < 0 else -hit[1]
    return hit[0], normal


def front_surface(tree, point):
    """Like FrontSurface in AstronautSkin.cs: the surface in front of a point, found by a ray from the front."""
    return cast(tree, point + FORWARD * 10, -FORWARD)


# ------------------------------------------------------------------ Building parts

class Costume:
    """Collects the parts of one character; each part has vertex groups for the bones that move it."""

    def __init__(self, body):
        self.body = body
        self.parts = []

    def add(self, obj, bone=None, weights=None):
        if bone:
            g = obj.vertex_groups.new(name=bone)
            g.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
        elif weights == "nearest":
            copy_nearest_weights(obj, self.body)
        self.parts.append(obj)
        return obj


def new_object(name, bm, mat, smooth=True):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    mesh.materials.append(mat)
    for p in mesh.polygons:
        p.use_smooth = smooth
    return obj


def look(forward, up):
    """Rotation like Unity's Quaternion.LookRotation(forward, up), as a 3x3 matrix in Blender space.
    Unity is left-handed, Blender right-handed, so the side axis comes from cross(forward, up) here."""
    z = forward.normalized()
    x = z.cross(up).normalized()
    y = x.cross(z).normalized()
    return Matrix((x, y, z)).transposed()


def euler(x, y, z):
    """Unity's Quaternion.Euler(x, y, z) (local rotation, applied Z, then X, then Y)."""
    return (Matrix.Rotation(math.radians(y), 3, "Y") @ Matrix.Rotation(math.radians(x), 3, "X")
            @ Matrix.Rotation(math.radians(z), 3, "Z"))


def angle_axis(degrees, axis):
    """Unity's Quaternion.AngleAxis for a Blender-space axis (the mirror between the two flips the angle)."""
    return Matrix.Rotation(-math.radians(degrees), 3, axis)


def part(costume, name, kind, bone, position, rotation, scale, color, smoothness, metallic=0.0):
    """One simple shape, sized like Unity's primitives: Sphere and Cube are 1 wide, Cylinder is 1 wide and 2 tall."""
    bm = bmesh.new()
    if kind == "sphere":
        bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=16, radius=0.5)
    elif kind == "cube":
        bmesh.ops.create_cube(bm, size=1.0)
    else:
        bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=0.5, radius2=0.5, depth=2.0)
        bmesh.ops.rotate(bm, verts=bm.verts, matrix=Matrix.Rotation(math.radians(90), 3, "X"))  # Axis along Y
    m = Matrix.Translation(position) @ rotation.to_4x4() @ Matrix.Diagonal((*scale, 1.0))
    bm.transform(m)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)  # The mirror in look() turns faces inside out
    obj = new_object(name, bm, color_material(color, smoothness, metallic), smooth=kind != "cube")
    return costume.add(obj, bone=bone)


def lathe(costume, name, profile, mat, bone, center, depth=1.0, arc=None, segments=48, solidify=0.0):
    """Spins a (height, radius) profile around a vertical axis through center (heights are relative to it).
    arc = (start, end) in degrees from the front leaves an opening at the front."""
    closed = arc is None
    a0, a1 = (0.0, 360.0) if closed else arc
    cols = segments if closed else segments + 1
    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap")
    grid = []
    for z, r in profile:
        row = []
        for j in range(cols):
            a = math.radians(a0 + (a1 - a0) * j / segments)
            # Angle 0 = front (-Y); positive angles go towards +X
            row.append(bm.verts.new((center.x + r * math.sin(a), center.y - r * math.cos(a) * depth, center.z + z)))
        grid.append(row)
    n = len(profile)
    for i in range(n - 1):
        for j in range(segments):
            j2 = (j + 1) % cols if closed else j + 1
            f = bm.faces.new((grid[i][j], grid[i][j2], grid[i + 1][j2], grid[i + 1][j]))
            for loop, uv in zip(f.loops, ((j / segments, 1 - i / (n - 1)), ((j + 1) / segments, 1 - i / (n - 1)),
                                          ((j + 1) / segments, 1 - (i + 1) / (n - 1)), (j / segments, 1 - (i + 1) / (n - 1)))):
                loop[uv_layer].uv = uv
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    obj = new_object(name, bm, mat)
    if solidify:
        apply_modifier(obj, "SOLIDIFY", thickness=solidify, offset=0.0)
    return costume.add(obj, bone=bone)


def rounded_box(costume, name, mat, bone, center, size, bevel=0.04, rotation=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bm.transform(Matrix.Diagonal((*size, 1.0)))
    if rotation is not None:
        bm.transform(rotation.to_4x4())
    bm.transform(Matrix.Translation(center))
    obj = new_object(name, bm, mat)
    apply_modifier(obj, "BEVEL", width=bevel, segments=3)
    return costume.add(obj, bone=bone)


def apply_modifier(obj, kind, **settings):
    mod = obj.modifiers.new(kind.lower(), kind)
    for k, v in settings.items():
        setattr(mod, k, v)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)


def copy_nearest_weights(obj, source):
    """Gives every vertex the bone weights of the closest vertex of `source` (a Body or a list of objects)."""
    if isinstance(source, Body):
        positions, weights = source.positions, source.weights
    else:
        positions, weights = [], []
        for o in source:
            names = {g.index: g.name for g in o.vertex_groups}
            for v in o.data.vertices:
                positions.append(o.matrix_world @ v.co)
                weights.append({names[g.group]: g.weight for g in v.groups})
    tree = KDTree(len(positions))
    for i, p in enumerate(positions):
        tree.insert(p, i)
    tree.balance()
    groups = {}
    for v in obj.data.vertices:
        _, index, _ = tree.find(obj.matrix_world @ v.co)
        for bone, w in weights[index].items():
            if bone not in groups:
                groups[bone] = obj.vertex_groups.new(name=bone)
            groups[bone].add([v.index], w, "REPLACE")


# ------------------------------------------------------------------ Clothes made from the body's own faces


def angle_from_front(p):
    """0 at the front, growing towards +X, 0..360."""
    return math.degrees(math.atan2(p.x, -(p.y - BODY_AXIS_Y))) % 360


def garment(costume, name, mat, z0, z1, offset, thickness=0.03, open_angle=0.0, sleeves_to=0.0, flare=0.0,
            max_x=0.99, include_torso=True):
    """Copies the body faces between heights z0..z1 (torso, and the arms out to |x| = sleeves_to), pushes them
    outward by `offset` (+ `flare` more at the bottom) and gives them thickness.
    open_angle leaves the front open (a robe or a vest). include_torso=False takes only the arms from |x| = max_x on. UV: u around the body (0 and 1 at the front opening),
    v from bottom (0) to top (1), so fabric patterns wrap around."""
    body = costume.body
    span = 360 - 2 * open_angle
    chosen = []
    for verts, mat_name in body.faces:
        if mat_name == "visier":
            continue
        pts = [body.positions[i] for i in verts]
        doms = [body.dominant[i] for i in verts]
        center = sum(pts, Vector()) / len(pts)
        torso = include_torso and all(d in TORSO_BONES for d in doms) and all(p.y < BACKPACK_Y and abs(p.x) <= max_x for p in pts)
        arm = sleeves_to > 0 and all(d in ARM_BONES or d in TORSO_BONES for d in doms) and \
            all(max_x - 0.05 <= abs(p.x) <= sleeves_to for p in pts)
        if not (torso or arm) or not all(z0 <= p.z <= z1 for p in pts):
            continue
        if torso and open_angle and (angle_from_front(center) < open_angle or angle_from_front(center) > 360 - open_angle):
            continue
        chosen.append((verts, arm))

    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap")
    new_index = {}
    weights = []
    for verts, arm in chosen:
        for i in verts:
            if i in new_index:
                continue
            p = body.positions[i]
            if abs(p.x) > max_x - 0.05 and arm:  # Sleeve: push out from the arm's own axis
                d = Vector((0, p.y - ARM_AXIS[0], p.z - ARM_AXIS[1]))
                push = offset
            else:
                d = Vector((p.x, p.y - BODY_AXIS_Y, 0))
                push = offset + flare * ((z1 - p.z) / (z1 - z0)) ** 2
            new_index[i] = len(weights)
            bm.verts.new(p + d.normalized() * push)
            weights.append(body.weights[i])
    bm.verts.ensure_lookup_table()
    for verts, arm in chosen:
        try:
            f = bm.faces.new([bm.verts[new_index[i]] for i in verts])
        except ValueError:
            continue
        for loop in f.loops:
            p = loop.vert.co
            a = angle_from_front(p)
            u = (a - open_angle) / span if open_angle else a / 360
            loop[uv_layer].uv = (min(max(u, 0.0), 1.0), (p.z - z0) / (z1 - z0))
    bmesh.ops.reverse_faces(bm, faces=bm.faces)  # The copied faces come from a mirrored object
    obj = new_object(name, bm, mat, smooth=False)
    for bone in {b for w in weights for b in w}:
        obj.vertex_groups.new(name=bone)
    for index, w in enumerate(weights):
        for bone, value in w.items():
            obj.vertex_groups[bone].add([index], value, "REPLACE")
    apply_modifier(obj, "SOLIDIFY", thickness=thickness, offset=-1.0)
    return costume.add(obj)


def skirt(costume, name, mat, z_top, z_bottom, extra, flare, open_angle=0.0, texture_rows=1.0):
    """A flared ring below the torso (the lower part of a coat, robe or poncho), moving with the hips."""
    steps = 6
    profile = []
    for i in range(steps + 1):
        t = i / steps
        z = z_top + (z_bottom - z_top) * t - HIPS.z
        profile.append((z, HIPS_HALF_WIDTH + extra + flare * t ** 1.3))
    arc = (open_angle, 360 - open_angle) if open_angle else None
    center = Vector((0, BODY_AXIS_Y + 0.06, HIPS.z))
    obj = lathe(costume, name, profile, mat, "hüfte", center, depth=HIPS_HALF_DEPTH / HIPS_HALF_WIDTH + 0.05,
                arc=arc, segments=40, solidify=0.03)
    return obj


def surface_patch(costume, name, mat, targets, cx, cz, width, height, round_patch=False):
    """A badge laid onto the front of the clothes: a small grid pushed onto the surface with rays from the front."""
    bm_all = bmesh.new()
    for o in targets:
        bm_all.from_mesh(o.data)
    tree = BVHTree.FromBMesh(bm_all)
    res = 10
    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap")
    grid = []
    for i in range(res + 1):
        row = []
        for j in range(res + 1):
            u, v = j / res, i / res
            if round_patch:
                x, y = u * 2 - 1, v * 2 - 1
                x, y = x * math.sqrt(max(0.0, 1 - y * y / 2)), y * math.sqrt(max(0.0, 1 - x * x / 2))
                u, v = (x + 1) / 2, (y + 1) / 2
            p = Vector((cx + (u - 0.5) * width, 0, cz + (v - 0.5) * height))
            hit = cast(tree, p + FORWARD * 10, -FORWARD)
            loc = hit[0] + FORWARD * 0.008 if hit else p
            row.append((bm.verts.new(loc), (u, v)))
        grid.append(row)
    for i in range(res):
        for j in range(res):
            quad = (grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j])
            f = bm.faces.new([q[0] for q in quad])
            for loop, q in zip(f.loops, quad):
                loop[uv_layer].uv = q[1]
    bm.normal_update()
    bm.faces.ensure_lookup_table()
    if bm.faces and bm.faces[0].normal.dot(FORWARD) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm_all.free()
    obj = new_object(name, bm, mat)
    copy_nearest_weights(obj, targets)
    costume.parts.append(obj)
    return obj


def badges(costume, targets, flag):
    """NASA on the left, the country flag on the right, at the same places as on the suit texture."""
    nasa = material("Badge_NASA", "#0B3D91", 0.2, texture="Patch_NASA.png")
    country = material("Badge_" + flag, "#FFFFFF", 0.2, texture="Flag_%s.png" % flag)
    surface_patch(costume, "Badge_NASA", nasa, targets, -0.55, 1.99, 0.30, 0.30, round_patch=True)
    surface_patch(costume, "Badge_Flag", country, targets, 0.54, 1.99, 0.34, 0.23)


def hat_base(height):
    """A point on the helmet at `height` (fraction of the radius above the center): its z and the helmet's radius there."""
    return HELMET.z + height * HELMET_R, HELMET_R * math.sqrt(1 - height * height)


# ------------------------------------------------------------------ The in-game skins (same shapes as AstronautSkin.cs)


def helmet_direction(yaw, pitch):
    return angle_axis(yaw, UP) @ angle_axis(-pitch, RIGHT) @ FORWARD


def build_pink(c):
    r = HELMET_R
    bow_color = "#FF4FA3"
    d = (UP + RIGHT * 0.55 + FORWARD * 0.15).normalized()
    center = HELMET + d * r * 0.98
    base = look(FORWARD, d)
    bow_right, bow_up = base @ Vector((1, 0, 0)), base @ Vector((0, 1, 0))
    for side in (-1, 1):
        part(c, "BowLoop", "sphere", "kopf", center + bow_right * side * 0.32 * r + bow_up * 0.06 * r,
             base @ euler(0, 0, -side * 18), Vector((0.55, 0.36, 0.2)) * r, bow_color, 0.6)
        part(c, "BowTail", "cube", "kopf", center - bow_up * 0.2 * r + bow_right * side * 0.12 * r,
             base @ euler(0, 0, side * 20), Vector((0.1, 0.35, 0.04)) * r, bow_color, 0.6)
    part(c, "BowKnot", "sphere", "kopf", center + bow_up * 0.04 * r, base, Vector((0.2, 0.2, 0.18)) * r, "#E0307F", 0.6)
    flat = look(FORWARD, UP)
    part(c, "TutuTop", "cylinder", "hüfte", HIPS + UP * 0.04 * r, flat,
         Vector((HIPS_HALF_WIDTH * 2.7, 0.05 * r, HIPS_HALF_DEPTH * 2.7)), "#FFB3D1", 0.3)
    part(c, "TutuBottom", "cylinder", "hüfte", HIPS - UP * 0.07 * r, flat,
         Vector((HIPS_HALF_WIDTH * 2.45, 0.05 * r, HIPS_HALF_DEPTH * 2.45)), "#FF7AB8", 0.3)


def build_nerd(c):
    r = HELMET_R
    upright = look(FORWARD, UP)
    part(c, "Beanie", "sphere", "kopf", HELMET + UP * r * 0.3, upright, Vector((2.06, 1.56, 2.06)) * r, "#3B6FE0", 0.2)
    top = HELMET + UP * r * 1.07
    part(c, "Stem", "cylinder", "kopf", top + UP * 0.1 * r, upright, Vector((0.07, 0.1, 0.07)) * r, "#FFD23F", 0.5)
    hub = top + UP * 0.21 * r
    part(c, "Hub", "sphere", "kopf", hub, upright, Vector((0.14, 0.14, 0.14)) * r, "#FFD23F", 0.5)
    for side in (-1, 1):
        part(c, "Blade", "cube", "kopf", hub + RIGHT * side * 0.42 * r, upright @ euler(side * 15, 0, 0),
             Vector((0.75, 0.03, 0.18)) * r, "#E63946" if side < 0 else "#2A9D8F", 0.5)
    hit = front_surface(c.body.tree, CHEST_BONE - RIGHT * r * 0.3)
    if hit:
        point, normal = hit
        facing = look(normal, UP)
        part(c, "Pocket", "cube", "brust", point + normal * 0.02 * r, facing, Vector((0.3, 0.34, 0.05)) * r, "#F4F4F4", 0.3)
        for i, pen in enumerate(("#1D4ED8", "#DC2626", "#111111")):
            side = (facing @ Vector((1, 0, 0))) * (i - 1) * 0.09 * r
            part(c, "Pen", "cylinder", "brust", point + normal * 0.05 * r + UP * 0.2 * r + side, facing,
                 Vector((0.05, 0.13, 0.05)) * r, pen, 0.7)


def build_cool(c):
    r = HELMET_R
    upright = look(FORWARD, UP)
    cap = "#D7263D"
    part(c, "CapDome", "sphere", "kopf", HELMET + UP * r * 0.35, upright, Vector((2.06, 1.4, 2.06)) * r, cap, 0.3)
    part(c, "CapButton", "sphere", "kopf", HELMET + UP * r * 1.04, upright, Vector((0.12, 0.12, 0.12)) * r, "#9E1B2C", 0.3)
    brim = angle_axis(12, RIGHT) @ look(-FORWARD, UP)
    part(c, "CapBrim", "cube", "kopf", HELMET - FORWARD * r * 1.3 + UP * r * 0.18, brim, Vector((1.1, 0.05, 0.8)) * r, cap, 0.3)
    gold = "#F2C14E"
    axis = Vector((CHEST_BONE.x, CHEST_BONE.y, HELMET.z - r))
    beads = 13
    lowest = None
    for i in range(beads):
        t = -1 + 2 * i / (beads - 1)
        point = axis + RIGHT * t * r * 0.5 - UP * ((1 - t * t) * 0.55 + 0.08) * r
        hit = front_surface(c.body.tree, point)
        if not hit:
            continue
        part(c, "Bead", "sphere", "brust", hit[0] + hit[1] * 0.03 * r, Matrix.Identity(3), Vector((0.09, 0.09, 0.09)) * r,
             gold, 0.85, 0.9)
        if i == beads // 2:
            lowest = hit
    if lowest:
        part(c, "Medallion", "cylinder", "brust", lowest[0] + lowest[1] * 0.06 * r - UP * 0.12 * r,
             look(lowest[1], UP) @ euler(90, 0, 0), Vector((0.28, 0.025, 0.28)) * r, gold, 0.85, 0.9)


def build_emo(c):
    r = HELMET_R
    black = "#141418"
    part(c, "HairCap", "sphere", "kopf", HELMET + UP * r * 0.25, look(FORWARD, UP), Vector((2.08, 1.64, 2.08)) * r, black, 0.35)
    start = helmet_direction(35, 60)
    for k in range(4):
        end = helmet_direction(-10 - 12 * k, 12 - 7 * k)
        middle = start.slerp(end, 0.55).normalized()
        tangent = (end - start).normalized()
        length = start.angle(end) * r * 1.05
        part(c, "Bang", "sphere", "kopf", HELMET + middle * r * 1.05, look(middle, tangent),
             Vector((0.34 * r, length, 0.14 * r)), "#B45CFF" if k == 1 else black, 0.4)
    part(c, "Belt", "cylinder", "hüfte", HIPS, look(FORWARD, UP),
         Vector((HIPS_HALF_WIDTH * 2.12, 0.07 * r, HIPS_HALF_DEPTH * 2.12)), "#1A1A1F", 0.5)
    studs = 14
    for i in range(studs):
        a = i * math.pi * 2 / studs
        p = HIPS + RIGHT * math.cos(a) * HIPS_HALF_WIDTH * 1.08 + FORWARD * math.sin(a) * HIPS_HALF_DEPTH * 1.08
        part(c, "Stud", "sphere", "hüfte", p, Matrix.Identity(3), Vector((0.08, 0.08, 0.08)) * r, "#C9CED6", 0.9, 1.0)


# ------------------------------------------------------------------ The country costumes


def build_captain(c):
    jacket = material("Jacket_Maroon", "#5A1418", 0.3)
    gold = color_material("#F2C200", 0.7, 0.8)
    coat = garment(c, "Jacket", jacket, 1.30, 2.32, 0.04, sleeves_to=1.38)
    for i, z in enumerate((1.45, 1.62, 1.79)):
        hit = front_surface(BVHTree.FromObject(coat, bpy.context.evaluated_depsgraph_get()), Vector((0, 0, z)))
        if hit:
            obj = part(c, "Button", "sphere", None, hit[0] + FORWARD * 0.02, Matrix.Identity(3), Vector((0.07, 0.04, 0.07)),
                       "#F2C200", 0.7, 0.8)
            copy_nearest_weights(obj, c.body)
    for side in (-1, 1):
        x = 0.8 * side
        board = rounded_box(c, "ShoulderBoard", jacket, "brust", Vector((x, -0.08, 2.3)), (0.36, 0.5, 0.07), bevel=0.025)
        stripe = rounded_box(c, "ShoulderStripe", gold, "brust", Vector((x, -0.08, 2.34)), (0.34, 0.08, 0.03), bevel=0.01)
    z, rb = hat_base(0.68)
    r0 = rb + 0.07
    center = Vector((0, HELMET.y, z))
    lathe(c, "HatBand", [(0.24, r0 + 0.02), (0.0, r0)], jacket, "kopf", center)
    lathe(c, "HatTop", [(0.22, r0 + 0.02), (0.30, r0 + 0.14), (0.38, r0 + 0.18), (0.44, r0 + 0.1), (0.48, 0.45), (0.49, 0.0)],
          jacket, "kopf", center)
    lathe(c, "HatGoldBand", [(0.15, r0 + 0.035), (0.06, r0 + 0.035)], gold, "kopf", center)
    lathe(c, "HatPeak", [(0.04, r0 - 0.04), (-0.02, r0 + 0.2), (-0.08, r0 + 0.34)], color_material("#141414", 0.6), "kopf",
          center, arc=(-70, 70), solidify=0.03)
    part(c, "HatBadge", "cylinder", "kopf", Vector((0, HELMET.y - r0 - 0.04, z + 0.33)), look(FORWARD, UP) @ euler(90, 0, 0),
         Vector((0.14, 0.02, 0.14)), "#F2C200", 0.7, 0.8)
    badges(c, [coat], "USA")


def build_mexican(c):
    serape = material("Serape", "#C81E32", 0.1, texture="Fabric_Serape.png")
    straw = material("Straw", "#DEBC70", 0.1, texture="Fabric_Straw.png")
    poncho = garment(c, "Serape", serape, 1.12, 2.32, 0.05, thickness=0.035, sleeves_to=1.2, flare=0.14)
    # Fringe hanging from the bottom edge of the poncho
    bottom = [poncho.matrix_world @ v.co for v in poncho.data.vertices if v.co.z < 1.14]
    bottom.sort(key=angle_from_front)
    for i, p in enumerate(bottom[::3]):
        obj = part(c, "Fringe", "cylinder", None, p - UP * 0.08, look(FORWARD, UP), Vector((0.035, 0.08, 0.035)),
                   "#F0E6D2", 0.1)
        copy_nearest_weights(obj, [poncho])
    z, rb = hat_base(0.6)
    r0 = rb + 0.06
    center = Vector((0, HELMET.y, z))
    lathe(c, "SombreroCrown", [(0.0, r0), (0.2, r0 - 0.04), (0.42, r0 - 0.16), (0.56, r0 - 0.36), (0.6, 0.0)], straw, "kopf", center)
    lathe(c, "SombreroBrim", [(0.04, r0 - 0.04), (0.0, r0 + 0.3), (0.02, r0 + 0.55), (0.12, r0 + 0.72), (0.2, r0 + 0.74)],
          straw, "kopf", center, solidify=0.03)
    lathe(c, "SombreroBand", [(0.14, r0 - 0.01), (0.04, r0 + 0.0)], color_material("#C81E32", 0.4), "kopf", center,
          solidify=0.02)
    badges(c, [poncho], "Mexico")


def build_kazakh(c):
    chapan = material("Chapan", "#6B1530", 0.25, texture="Fabric_Chapan.png")
    takiya = material("Takiya", "#19193C", 0.3, texture="Fabric_Takiya.png")
    gold = color_material("#F2C200", 0.7, 0.8)
    robe = garment(c, "Chapan", chapan, 1.25, 2.32, 0.045, open_angle=20, sleeves_to=1.42)
    skirt(c, "ChapanSkirt", chapan, 1.32, 0.88, 0.07, 0.18, open_angle=20)
    garment(c, "Sash", gold, 1.48, 1.64, 0.09, thickness=0.02, open_angle=20)
    z, rb = hat_base(0.62)
    r0 = rb + 0.03
    center = Vector((0, HELMET.y, z))
    lathe(c, "Takiya", [(0.0, r0), (0.1, r0 - 0.01), (0.2, r0 - 0.1), (0.3, r0 - 0.3), (0.35, 0.0)], takiya, "kopf", center)
    top = center + UP * 0.35
    for i, (x, tilt) in enumerate(((-0.05, -18), (0.0, 0), (0.05, 18))):
        part(c, "OwlFeather", "sphere", "kopf", top + Vector((x, 0, 0.13)), look(FORWARD, UP) @ euler(0, 0, -tilt),
             Vector((0.05, 0.2, 0.05)), "#F5F2EA", 0.1)
    part(c, "FeatherBead", "sphere", "kopf", top + UP * 0.02, Matrix.Identity(3), Vector((0.08, 0.08, 0.08)), "#F2C200", 0.7, 0.8)
    badges(c, [robe], "Kazakhstan")


def build_russian(c):
    """A woman in a sheepskin coat (tulup) with woolly trims, a fur ushanka and two blonde braids."""
    coat = material("Coat_Sheepskin", "#6B4A2E", 0.15)
    fur = material("Fur_Cream", "#E2D4BA", 0.0, texture="Fur_Cream.png")
    fur_brown = material("Fur_Brown", "#5C3E28", 0.0, texture="Fur_Brown.png")
    tulup = garment(c, "Coat", coat, 1.25, 2.30, 0.05, sleeves_to=1.36)
    skirt(c, "CoatSkirt", coat, 1.32, 0.72, 0.08, 0.32)
    skirt(c, "FurHem", fur, 0.80, 0.68, 0.43, 0.04)
    garment(c, "FurCollar", fur, 2.14, 2.32, 0.12, thickness=0.08)
    garment(c, "FurCuffs", fur, 1.6, 2.2, 0.06, thickness=0.08, sleeves_to=1.42, max_x=1.30, include_torso=False)
    tree = BVHTree.FromObject(tulup, bpy.context.evaluated_depsgraph_get())
    for i in range(8):
        z = 1.3 + i * 0.12
        hit = front_surface(tree, Vector((0, 0, z)))
        if hit:
            obj = part(c, "FurFront", "sphere", None, hit[0] + FORWARD * 0.03, look(FORWARD, UP), Vector((0.16, 0.14, 0.08)),
                       "#E2D4BA", 0.0)
            copy_nearest_weights(obj, c.body)
    # Ushanka
    z, rb = hat_base(0.5)
    r0 = rb + 0.06
    center = Vector((0, HELMET.y, z))
    lathe(c, "UshankaCrown", [(0.0, r0), (0.25, r0 - 0.01), (0.4, r0 - 0.1), (0.47, r0 - 0.35), (0.49, 0.0)], fur_brown, "kopf", center)
    lathe(c, "UshankaFront", [(0.3, r0 + 0.1), (0.05, r0 + 0.11), (-0.02, r0 + 0.05)], fur_brown, "kopf", center,
          arc=(-65, 65), solidify=0.08)
    for side in (-1, 1):
        x = (HELMET_R + 0.06) * side
        rounded_box(c, "EarFlap", fur_brown, "kopf", Vector((x, HELMET.y + 0.05, 2.95)), (0.14, 0.44, 0.46), bevel=0.06)
        # Braid: overlapping beads that lean left and right a little, with a red ribbon at the end
        for k in range(5):
            p = Vector((x * 1.02, HELMET.y + 0.12, 2.70 - k * 0.085))
            part(c, "Braid", "sphere", "kopf", p, look(FORWARD, UP) @ euler(0, 0, 22 if k % 2 else -22),
                 Vector((0.16, 0.13, 0.13)), "#E8C35A", 0.3)
        ribbon = Vector((x * 1.02, HELMET.y + 0.12, 2.29))
        for s in (-1, 1):
            part(c, "Ribbon", "sphere", "kopf", ribbon + Vector((0.07 * s, 0, 0)), look(FORWARD, UP) @ euler(0, 0, 25 * s),
                 Vector((0.14, 0.08, 0.05)), "#D52B1E", 0.5)
    badges(c, [tulup], "Russia")


def build_turkish(c):
    """Red vest with gold embroidery over the white suit, a sash, a fez, a big moustache and prayer beads (tespih)."""
    vest = material("Vest", "#B2121F", 0.2, texture="Fabric_Vest.png")
    yelek = garment(c, "Vest", vest, 1.42, 2.30, 0.045, thickness=0.03, open_angle=26)
    garment(c, "Sash", color_material("#1C1C1C", 0.3), 1.32, 1.52, 0.07, thickness=0.03)
    # Fez: a red cone with a flat top and a black tassel falling to the back
    z, rb = hat_base(0.78)
    r0 = rb + 0.04
    center = Vector((0, HELMET.y, z))
    lathe(c, "Fez", [(0.0, r0), (0.42, r0 - 0.1), (0.43, r0 - 0.12), (0.435, 0.0)], color_material("#B3121F", 0.25), "kopf",
          center)
    top = center + UP * 0.44
    part(c, "TasselCord", "cylinder", "kopf", top + Vector((0.12, 0.1, 0.03)), look(FORWARD, UP) @ euler(0, 0, 90),
         Vector((0.025, 0.14, 0.025)), "#111111", 0.3)
    part(c, "TasselCord", "cylinder", "kopf", top + Vector((0.27, 0.1, -0.18)), look(FORWARD, UP), Vector((0.025, 0.18, 0.025)),
         "#111111", 0.3)
    part(c, "Tassel", "cylinder", "kopf", top + Vector((0.27, 0.1, -0.42)), look(FORWARD, UP), Vector((0.08, 0.08, 0.08)),
         "#111111", 0.3)
    # Moustache on the visor
    for side in (-1, 1):
        hit = front_surface(c.body.visor_tree, Vector((0.14 * side, 0, 2.43)))
        if hit:
            part(c, "Moustache", "sphere", "kopf", hit[0] + FORWARD * 0.03, look(FORWARD, UP) @ euler(0, 0, -18 * side),
                 Vector((0.34, 0.12, 0.08)), "#7A4B2A", 0.3)  # Lighter than black, or it vanishes on the dark visor
    # Tespih in the right hand: a loop of amber beads with a tassel
    hand = Vector((-1.68, -0.1, 1.9))
    count = 18
    for i in range(count):
        a = 2 * math.pi * i / count
        p = hand + Vector((0.12 * math.sin(a), -0.05, -0.2 - 0.2 * math.cos(a)))
        part(c, "Bead", "sphere", "handfläche.R", p, Matrix.Identity(3), Vector((0.07, 0.07, 0.07)), "#F08A24", 0.8)
    part(c, "BeadTassel", "cylinder", "handfläche.R", hand + Vector((0, -0.05, -0.5)), look(FORWARD, UP),
         Vector((0.05, 0.08, 0.05)), "#F08A24", 0.5)
    badges(c, [yelek], "Turkey")


# name: (texture, costume builder)
CHARACTERS = [
    ("Classic", None),
    ("Pink", build_pink),
    ("Nerd", build_nerd),
    ("Cool", build_cool),
    ("Emo", build_emo),
    ("Captain", build_captain),
    ("Mexican", build_mexican),
    ("Kazakh", build_kazakh),
    ("Russian", build_russian),
    ("Turkish", build_turkish),
]

# ------------------------------------------------------------------ Grab animation (new, on the original skeleton)


def knee_bend(drop):
    """Hip and knee angles that lower the hips by `drop` while the feet stay under them (two-bone leg)."""
    thigh, shin = 0.345, 0.731
    best = (0.0, 0.0)
    for step in range(1, 900):
        a = math.radians(step * 0.1)
        s = thigh * math.sin(a) / shin
        if s >= 1:
            break
        b = math.asin(s)
        if thigh + shin - (thigh * math.cos(a) + shin * math.cos(b)) >= drop:
            return math.degrees(a), math.degrees(b)
        best = (math.degrees(a), math.degrees(b))
    return best


def world_rotation(pb, axis, degrees):
    """A rotation around an armature axis, turned into the bone's own pose space."""
    rest = pb.bone.matrix_local.to_3x3()
    q = Quaternion(Vector(axis), math.radians(degrees)).to_matrix()
    return (rest.inverted() @ q @ rest).to_quaternion()


def make_grab(arm, base):
    """Base = the pose at the first idle frame. Keys: stand, reach down, hands close (contact), hold,
    lift to the chest, hold, stand. Frame 18 is the contact: 0.38 of the clip (frames 1..46, 1.9 s at 24 fps)."""
    X, Y = (1, 0, 0), (0, 1, 0)

    def pose(drop=0.0, hips=0.0, chest=0.0, head=0.0, reach=0.0, inward=0.0, elbow=0.0, curl=0.0):
        a, b = knee_bend(drop)
        spec = {
            "hüfte": {"rot": [(X, hips)], "loc": (0, 0, -drop)},
            "brust": {"rot": [(X, chest)]},
            "kopf": {"rot": [(X, head)]},
        }
        for side, s in (("L", 1), ("R", -1)):
            # Thighs forward (knees out front), shins back, feet flat; minus the hip tilt so the legs stay put
            spec["oberschenkel." + side] = {"rot": [(X, -a - hips)]}
            spec["unterschenkel." + side] = {"rot": [(X, a + b)]}
            spec["fuss." + side] = {"rot": [(X, -b)]}
            spec["schulter." + side] = {"rot": [(X, -reach), (Y, inward * s)]}
            spec["unterarm." + side] = {"rot": [(X, -elbow)]}
            for finger in ("finger.", "daumen.", "daumenSpitze."):
                spec[finger + side] = {"curl": curl}
        return spec

    stand = pose()
    reach = pose(drop=0.22, hips=14, chest=24, head=-10, reach=38, inward=14, elbow=10)
    grab = pose(drop=0.24, hips=15, chest=27, head=-10, reach=40, inward=16, elbow=10, curl=65)
    lift = pose(drop=0.02, hips=0, chest=-4, head=-4, reach=40, inward=26, elbow=70, curl=60)
    keys = [(1, stand), (12, reach), (18, grab), (21, grab), (30, lift), (36, lift), (46, stand)]

    action = bpy.data.actions.new("Grab")
    arm.animation_data.action = action
    for frame, spec in keys:
        for pb in arm.pose.bones:
            loc, rot, scale = base[pb.name]
            s = spec.get(pb.name, {})
            q = rot.copy()
            for axis, deg in s.get("rot", []):
                q = world_rotation(pb, axis, deg) @ q
            if s.get("curl"):
                q = q @ Quaternion((1, 0, 0), math.radians(s["curl"]))  # Around the finger's own side axis
            offset = Vector(s.get("loc", (0, 0, 0)))
            # Key the same rotation channels as the original clips use, so Idle/Walk/Grab all drive the same curves
            if pb.rotation_mode == "QUATERNION":
                pb.rotation_quaternion = q
                pb.keyframe_insert("rotation_quaternion", frame=frame)
            else:
                pb.rotation_euler = q.to_euler(pb.rotation_mode)
                pb.keyframe_insert("rotation_euler", frame=frame)
            pb.location = loc + pb.bone.matrix_local.to_3x3().inverted() @ offset
            pb.scale = scale
            pb.keyframe_insert("location", frame=frame)
            pb.keyframe_insert("scale", frame=frame)
    arm.animation_data.action = None
    return action


def setup_animations(arm):
    original = arm.animation_data.action
    bpy.context.scene.frame_set(IDLE_FRAMES[0])
    base = {pb.name: pb.matrix_basis.decompose() for pb in arm.pose.bones}
    grab = make_grab(arm, base)
    arm.animation_data.action = None
    for name, action, (start, end) in (("Idle", original, IDLE_FRAMES), ("Walk", original, WALK_FRAMES),
                                       ("Grab", grab, (1, 46))):
        track = arm.animation_data.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, 1, action)
        strip.action_frame_start, strip.action_frame_end = start, end
        strip.name = name
        print("  clip", name, "frames", strip.frame_start, "-", strip.frame_end, "scale", strip.scale,
              "action frames", strip.action_frame_start, "-", strip.action_frame_end)
        if hasattr(strip, "action_slot") and strip.action_slot is None and len(action.slots):
            strip.action_slot = action.slots[0]
        action.use_fake_user = True


# ------------------------------------------------------------------ Putting one character together


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions, bpy.data.materials, bpy.data.lights):
        for item in list(block):
            block.remove(item)
    _materials.clear()


def build(name, builder):
    clear_scene()
    bpy.ops.import_scene.fbx(filepath=SOURCE_FBX)
    for o in [o for o in bpy.data.objects if o.type not in ("ARMATURE", "MESH")]:
        bpy.data.objects.remove(o)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body_obj = next(o for o in bpy.data.objects if o.type == "MESH")

    body = Body(arm, body_obj)  # Measure first: it tells the visor apart by its original material name
    # Suit texture and a dark glossy visor (in the game the visor shows the dark part of the suit texture)
    for i, slot in enumerate(body_obj.material_slots):
        if slot.material.name.startswith("visier"):
            body_obj.material_slots[i].material = material("Visor", "#0B0D12", 0.85, 0.3)
        else:
            body_obj.material_slots[i].material = material("Astronaut_" + name, "#FFFFFF", 0.2,
                                                           texture="Astronaut_%s.png" % name)

    costume = Costume(body)
    if builder:
        builder(costume)
        bpy.ops.object.select_all(action="DESELECT")
        for o in costume.parts:
            o.select_set(True)
        bpy.context.view_layer.objects.active = costume.parts[0]
        bpy.ops.object.join()
        clothes = bpy.context.active_object
        clothes.name = clothes.data.name = "Costume"
        clothes.parent = arm
        clothes.matrix_parent_inverse = arm.matrix_world.inverted()
        mod = clothes.modifiers.new("Armature", "ARMATURE")
        mod.object = arm
    setup_animations(arm)
    return arm


def export(name, arm):
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    for child in arm.children:
        child.select_set(True)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(OUT_DIR, "Astronaut_%s.fbx" % name)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
        add_leaf_bones=False, mesh_smooth_type="FACE",
        bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
        bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
        path_mode="RELATIVE", embed_textures=False,
    )
    print("exported", path)


def main():
    bpy.context.scene.render.fps = FPS
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, builder in CHARACTERS:
        arm = build(name, builder)
        export(name, arm)


if __name__ == "__main__":
    main()
