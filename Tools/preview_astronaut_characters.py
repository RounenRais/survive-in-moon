"""Checks the exported astronaut FBX files: imports them back into an empty Blender scene, prints what is inside
(bones, clips and their lengths, meshes and materials) and renders preview images.

Run from the project root:
  blender --background --factory-startup --python Tools/preview_astronaut_characters.py -- <output folder> [names...]
Writes lineup_<skins|countries>_<front|back>.png (first Idle frame) and, for the first character given
(or Turkish), one image per frame of Walk and Grab.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

PROJECT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
MODEL_DIR = os.path.join(PROJECT, "Assets", "Models", "Astronauts")
ALL = ["Classic", "Pink", "Nerd", "Cool", "Emo", "Captain", "Mexican", "Kazakh", "Russian", "Turkish"]
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_dir = args[0] if args else os.path.join(PROJECT, "Temp")
focus = args[1] if len(args) > 1 else "Turkish"
os.makedirs(out_dir, exist_ok=True)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete()
scene = bpy.context.scene
scene.render.fps = 24

armatures = {}
for i, name in enumerate(ALL):
    before = set(bpy.data.objects)
    actions_before = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=os.path.join(MODEL_DIR, "Astronaut_%s.fbx" % name))
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == "ARMATURE")
    arm.name = "Armature_" + name
    meshes = [o for o in new if o.type == "MESH"]
    armatures[name] = (arm, meshes, [a for a in bpy.data.actions if a not in actions_before])
    arm.location = ((i % 5 - 2) * 2.7, 0, 0)  # Two groups of five, rendered one after the other
    print("==", name, "| bones:", len(arm.data.bones), "| meshes:",
          ", ".join("%s (%d verts, %s)" % (m.name, len(m.data.vertices), "/".join(s.material.name for s in m.material_slots))
                    for m in meshes))
    for a in armatures[name][2]:
        print("   clip", a.name, "frames", tuple(round(f) for f in a.frame_range))
        # The exporter also keys the armature OBJECT itself (always the same). Drop those curves, so the
        # characters can stand side by side here.
        for layer in a.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for fc in [fc for fc in bag.fcurves if not fc.data_path.startswith("pose.")]:
                        bag.fcurves.remove(fc)


def set_clip(name, clip):
    arm, _, actions = armatures[name]
    action = next(a for a in actions if a.name.split("|")[-1].split(".")[0] == clip)  # e.g. "Armature Astronaut|Walk.003"
    arm.animation_data_create()
    arm.animation_data.action = action
    if len(action.slots):
        arm.animation_data.action_slot = action.slots[0]


scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x, scene.render.resolution_y = 1800, 1100
world = bpy.data.worlds.new("World")
scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.05, 0.07, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
bpy.ops.mesh.primitive_plane_add(size=80)
ground = bpy.context.active_object
gm = bpy.data.materials.new("Ground")
gm.use_nodes = True
gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.3, 0.3, 0.3, 1)
ground.data.materials.append(gm)
for angle, energy in ((-30, 3.5), (150, 1.2)):
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = energy
    sun.rotation_euler = (math.radians(50), 0, math.radians(angle))
    scene.collection.objects.link(sun)
cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
scene.collection.objects.link(cam)
scene.camera = cam


def shoot(position, target, path, lens=50):
    cam.location = position
    cam.rotation_euler = (Vector(target) - Vector(position)).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lens
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("rendered", path)


def show_only(names):
    for name, (arm, meshes, _) in armatures.items():
        for o in [arm] + meshes:
            o.hide_render = name not in names


for name in ALL:
    set_clip(name, "Idle")
scene.frame_set(1)
for group, names in (("skins", ALL[:5]), ("countries", ALL[5:])):
    show_only(names)
    shoot((0, -17, 4.5), (0, 0, 1.9), os.path.join(out_dir, "lineup_%s_front.png" % group), 45)
    shoot((10, 13, 4.5), (0, 0, 1.9), os.path.join(out_dir, "lineup_%s_back.png" % group), 45)

# Frames of Walk and Grab for one character, alone, from the front-right
show_only([focus])
arm = armatures[focus][0]
arm.location = (0, 0, 0)
scene.render.resolution_x, scene.render.resolution_y = 600, 800
for clip, frames in (("Walk", [1, 9, 17, 25, 33, 41, 49, 57]), ("Grab", [1, 8, 12, 18, 24, 30, 38, 46])):
    set_clip(focus, clip)
    for f in frames:
        scene.frame_set(f)
        shoot((7.5, -8.5, 3.2), (0, 0, 1.9), os.path.join(out_dir, "%s_%s_%02d.png" % (focus, clip, f)), 50)
