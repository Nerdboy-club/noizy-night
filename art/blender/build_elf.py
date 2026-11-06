"""
Procedural low-poly "Heist Elf" for Silent Night.

Run headless:
  blender --background --python art/blender/build_elf.py -- <out_dir_unity_elf> <out_glb> <out_blend>

Produces: rigged mesh (Mixamo bone names -> Unity Humanoid works, Mixamo animations retarget),
plus simple procedural clips: Idle, Walk, Run, CrouchIdle, Sneak, CarryIdle, CarryWalk, Jump, Fall.
Units: metres, elf ~1.15 m tall, faces -Y in Blender (= +Z in Unity/glTF).
"""
import bpy
import math
import os
import sys
from mathutils import Vector

argv = sys.argv
args = argv[argv.index("--") + 1:] if "--" in argv else []
try:
    ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
except NameError:  # run from Blender Text editor
    ROOT = "C:/Vs-code/gamedev/noizy-night"
UNITY_ELF = args[0] if len(args) > 0 else os.path.join(ROOT, "unity", "SilentNight", "Assets", "_Project", "Art", "Characters", "Elf")
OUT_GLB = args[1] if len(args) > 1 else os.path.join(ROOT, "public", "models", "elf_lowpoly.glb")
OUT_BLEND = args[2] if len(args) > 2 else os.path.join(ROOT, "art", "blender", "elf.blend")

P = "mixamorig:"

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = 30

# ------------------------------------------------------------------ materials
_mats = {}


def mat(name, color, rough=0.95, emit=0.0):
    if name in _mats:
        return _mats[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1.0)
    b.inputs["Roughness"].default_value = rough
    if emit > 0:
        try:
            b.inputs["Emission Color"].default_value = (*color, 1.0)
            b.inputs["Emission Strength"].default_value = emit
        except KeyError:
            pass
    m.diffuse_color = (*color, 1.0)
    _mats[name] = m
    return m


RED = mat("Felt_Red", (0.80, 0.08, 0.10))
WHITE = mat("Wool_White", (0.95, 0.94, 0.90))
GREEN = mat("Sweater_Green", (0.10, 0.45, 0.20))
DKGREEN = mat("Sweater_DarkGreen", (0.06, 0.30, 0.13))
SKIN = mat("Skin", (1.0, 0.78, 0.64))
DARK = mat("Dark", (0.07, 0.05, 0.06))
PANTS = mat("Pants", (0.16, 0.14, 0.18))
BOOT = mat("Boot_Felt", (0.38, 0.22, 0.12))
GOLD = mat("Brass", (0.95, 0.75, 0.22), rough=0.35)
EYE = mat("Eye_Glow", (1.0, 0.85, 0.15), rough=0.4, emit=3.0)
BELT = mat("Belt", (0.25, 0.14, 0.07))

# ------------------------------------------------------------------ armature layout
# name: (head, tail, parent)   Blender coords: X = character's left, -Y = forward, Z = up
BONES = [
    ("Hips", (0, 0, 0.38), (0, 0, 0.46), None),
    ("Spine", (0, 0, 0.46), (0, 0, 0.54), "Hips"),
    ("Spine1", (0, 0, 0.54), (0, 0, 0.62), "Spine"),
    ("Spine2", (0, 0, 0.62), (0, 0, 0.68), "Spine1"),
    ("Neck", (0, 0, 0.68), (0, 0, 0.74), "Spine2"),
    ("Head", (0, 0, 0.74), (0, 0, 0.98), "Neck"),
    ("HeadTop_End", (0, 0, 0.98), (0, 0, 1.08), "Head"),
]
for side, sx in (("Left", 1), ("Right", -1)):
    BONES += [
        (side + "Shoulder", (0.05 * sx, 0, 0.66), (0.17 * sx, 0, 0.66), "Spine2"),
        (side + "Arm", (0.17 * sx, 0, 0.66), (0.17 * sx, 0, 0.52), side + "Shoulder"),
        (side + "ForeArm", (0.17 * sx, 0, 0.52), (0.17 * sx, 0, 0.40), side + "Arm"),
        (side + "Hand", (0.17 * sx, 0, 0.40), (0.17 * sx, 0, 0.32), side + "ForeArm"),
        (side + "UpLeg", (0.07 * sx, 0, 0.38), (0.07 * sx, 0, 0.22), "Hips"),
        (side + "Leg", (0.07 * sx, 0, 0.22), (0.07 * sx, 0, 0.09), side + "UpLeg"),
        (side + "Foot", (0.07 * sx, 0, 0.09), (0.07 * sx, -0.08, 0.04), side + "Leg"),
        (side + "ToeBase", (0.07 * sx, -0.08, 0.04), (0.07 * sx, -0.14, 0.03), side + "Foot"),
    ]

# ------------------------------------------------------------------ mesh helpers
parts = []


def _finish(o, name, material, bone, smooth=True):
    o.name = name
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if smooth:
        bpy.ops.object.shade_smooth()
    o.data.materials.append(material)
    vg = o.vertex_groups.new(name=P + bone)
    vg.add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
    o.select_set(False)
    parts.append(o)
    return o


def sphere(name, loc, scale, material, bone, seg=14, rings=9):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=1.0, location=loc)
    o = bpy.context.view_layer.objects.active
    o.scale = scale
    return _finish(o, name, material, bone)


def limb(name, p1, p2, r1, r2, material, bone, verts=12, smooth=True):
    a, b = Vector(p1), Vector(p2)
    d = b - a
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=d.length, location=(a + b) / 2)
    o = bpy.context.view_layer.objects.active
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = d.to_track_quat("Z", "Y")
    return _finish(o, name, material, bone, smooth)


def ring(name, loc, major, minor, material, bone, squash=1.0):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=18, minor_segments=8, location=loc)
    o = bpy.context.view_layer.objects.active
    o.scale = (1.0, squash, 1.0)
    return _finish(o, name, material, bone)


def box(name, loc, size, material, bone, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    o = bpy.context.view_layer.objects.active
    o.scale = size
    o.rotation_euler = rot
    return _finish(o, name, material, bone, smooth=False)


# ------------------------------------------------------------------ build the elf
# Torso (green knitted sweater) + candy stripe + cable-knit band
sphere("Torso", (0, 0, 0.55), (0.16, 0.12, 0.17), GREEN, "Spine1")
sphere("Belly", (0, 0, 0.46), (0.165, 0.13, 0.12), GREEN, "Hips")
ring("SweaterHem", (0, 0, 0.40), 0.15, 0.022, DKGREEN, "Hips", squash=0.8)
ring("SweaterStripe", (0, 0, 0.52), 0.168, 0.016, WHITE, "Spine1", squash=0.78)
ring("Belt", (0, 0, 0.42), 0.158, 0.018, BELT, "Hips", squash=0.8)
box("BeltBuckle", (0, -0.125, 0.42), (0.04, 0.012, 0.035), GOLD, "Hips")

# Scarf: red/white striped, chunky, with a tail hanging down the front-left
for i in range(6):
    ring("Scarf%d" % i, (0, -0.005, 0.655 + i * 0.022), 0.115 + (0.006 if i % 2 else 0), 0.021,
         RED if i % 2 == 0 else WHITE, "Neck", squash=0.92)
for k in range(5):
    z0 = 0.66 - k * 0.055
    limb("ScarfTail%d" % k, (0.075, -0.115, z0), (0.08, -0.12, z0 - 0.05), 0.04, 0.04, RED if k % 2 == 0 else WHITE, "Spine2", verts=10)
for k in range(4):
    sphere("Fringe%d" % k, (0.06 + k * 0.016, -0.12, 0.40), (0.008, 0.008, 0.03), WHITE, "Spine2", seg=6, rings=4)

# Head: big round red knitted balaclava (face covered up to the eyes), slouchy hat tail bending back
sphere("Head", (0, 0, 0.90), (0.205, 0.195, 0.205), RED, "Head", seg=24, rings=16)
sphere("MaskBulge", (0, -0.075, 0.855), (0.17, 0.13, 0.12), RED, "Head", seg=16, rings=10)
box("EyeSlit", (0, -0.175, 0.925), (0.2, 0.045, 0.07), DARK, "Head", )
sphere("EyeL", (0.068, -0.19, 0.925), (0.05, 0.022, 0.052), EYE, "Head", seg=12, rings=8)
sphere("EyeR", (-0.068, -0.19, 0.925), (0.05, 0.022, 0.052), EYE, "Head", seg=12, rings=8)
sphere("PupilL", (0.068, -0.208, 0.925), (0.02, 0.012, 0.032), DARK, "Head", seg=8, rings=6)
sphere("PupilR", (-0.068, -0.208, 0.925), (0.02, 0.012, 0.032), DARK, "Head", seg=8, rings=6)
for sx, side in ((1, "L"), (-1, "R")):
    box("Brow" + side, (0.07 * sx, -0.185, 0.972), (0.1, 0.03, 0.03), RED, "Head", rot=(0, -0.5 * sx, 0))
ring("HatBand", (0, -0.005, 0.975), 0.195, 0.035, RED, "Head", squash=0.97)
limb("HatTop", (0, 0.02, 1.0), (0, 0.14, 1.16), 0.175, 0.07, RED, "HeadTop_End", verts=20)
for i, (y, z, r) in enumerate(((0.17, 1.15, 0.085), (0.24, 1.10, 0.07), (0.29, 1.03, 0.055), (0.31, 0.96, 0.045))):
    sphere("HatTail%d" % i, (0, y, z), (r, r, r), RED, "HeadTop_End", seg=12, rings=8)

# Big pointy pink ears
for sx, side in ((1, "L"), (-1, "R")):
    limb("Ear" + side, (0.17 * sx, 0.0, 0.90), (0.44 * sx, 0.01, 1.03), 0.075, 0.004, SKIN, "Head", verts=12)

# Arms (sweater sleeve + big red mitten)
for sx, side in ((1, "Left"), (-1, "Right")):
    limb(side + "UpperArm", (0.17 * sx, 0, 0.665), (0.17 * sx, 0, 0.52), 0.065, 0.058, GREEN, side + "Arm")
    sphere(side + "Shoulder", (0.17 * sx, 0, 0.655), (0.075, 0.075, 0.075), GREEN, side + "Arm", seg=12, rings=8)
    sphere(side + "Elbow", (0.17 * sx, 0, 0.52), (0.058, 0.058, 0.058), GREEN, side + "ForeArm", seg=10, rings=7)
    limb(side + "Forearm", (0.17 * sx, 0, 0.52), (0.17 * sx, 0, 0.40), 0.058, 0.05, GREEN, side + "ForeArm")
    ring(side + "Cuff", (0.17 * sx, 0, 0.395), 0.052, 0.014, WHITE, side + "Hand")
    sphere(side + "Mitten", (0.17 * sx, 0, 0.345), (0.062, 0.058, 0.07), RED, side + "Hand", seg=12, rings=8)
    sphere(side + "Thumb", (0.215 * sx, -0.02, 0.36), (0.026, 0.026, 0.034), RED, side + "Hand", seg=8, rings=6)

# Legs (baggy dark pants) + felt boots with curled toes and a bell
for sx, side in ((1, "Left"), (-1, "Right")):
    limb(side + "Thigh", (0.07 * sx, 0, 0.40), (0.07 * sx, 0, 0.22), 0.085, 0.07, PANTS, side + "UpLeg")
    sphere(side + "PantsPuff", (0.075 * sx, 0, 0.27), (0.105, 0.1, 0.11), PANTS, side + "UpLeg", seg=12, rings=8)
    limb(side + "Shin", (0.07 * sx, 0, 0.22), (0.07 * sx, 0, 0.11), 0.07, 0.065, PANTS, side + "Leg")
    sphere(side + "Boot", (0.07 * sx, -0.03, 0.06), (0.075, 0.115, 0.06), BOOT, side + "Foot", seg=12, rings=8)
    limb(side + "BootCurl", (0.07 * sx, -0.10, 0.06), (0.07 * sx, -0.19, 0.13), 0.055, 0.02, BOOT, side + "ToeBase", verts=10)
    sphere(side + "Bell", (0.07 * sx, -0.20, 0.14), (0.022, 0.022, 0.022), GOLD, side + "ToeBase", seg=8, rings=6)
    ring(side + "BootCuff", (0.07 * sx, 0, 0.115), 0.068, 0.012, WHITE, side + "Leg")

# join into one mesh
bpy.ops.object.select_all(action="DESELECT")
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
mesh = bpy.context.view_layer.objects.active
mesh.name = "Elf"
mesh.data.name = "ElfMesh"

# ------------------------------------------------------------------ armature
arm_data = bpy.data.armatures.new("ElfArmature")
rig = bpy.data.objects.new("ElfRig", arm_data)
scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
eb_map = {}
for name, head, tail, parent in BONES:
    eb = arm_data.edit_bones.new(P + name)
    eb.head = head
    eb.tail = tail
    if parent:
        eb.parent = eb_map[parent]
        eb.use_connect = (Vector(eb.parent.tail) - Vector(head)).length < 1e-5
    eb_map[name] = eb
bpy.ops.object.mode_set(mode="OBJECT")

mesh.parent = rig
mod = mesh.modifiers.new("Armature", "ARMATURE")
mod.object = rig

# ------------------------------------------------------------------ animation
# For vertical bones local X == world X. Down-pointing bones (arms, legs): +angle swings BACK, -angle FORWARD.
# Up-pointing bones (spine/head): +angle leans FORWARD.
rig.animation_data_create()
ANIMATED = ["Hips", "Spine", "Spine1", "Head", "LeftArm", "LeftForeArm", "RightArm", "RightForeArm",
            "LeftUpLeg", "LeftLeg", "RightUpLeg", "RightLeg", "LeftFoot", "RightFoot"]
for n in ANIMATED:
    pb = rig.pose.bones[P + n]
    pb.rotation_mode = "XYZ"

TAU = 2 * math.pi


def make_clip(name, frames, fn):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data.action = act
    for i in range(frames + 1):
        t = (i / frames) if i < frames else 0.0  # last key == first key -> seamless loop
        pose = fn(t)
        for n in ANIMATED:
            rx, hy = pose.get(n, (0.0, 0.0))
            pb = rig.pose.bones[P + n]
            pb.rotation_euler = (rx, 0.0, 0.0)
            pb.location = (0.0, hy if n == "Hips" else 0.0, 0.0)
            pb.keyframe_insert("rotation_euler", frame=i + 1)
            if n == "Hips":
                pb.keyframe_insert("location", frame=i + 1)
    try:  # Blender 5 moved fcurves to layered actions; default interpolation is already Bezier
        for fc in act.fcurves:
            for k in fc.keyframe_points:
                k.interpolation = "BEZIER"
    except AttributeError:
        pass
    return act


def idle(t):
    b = math.sin(TAU * t)
    return {"Hips": (0, 0.004 * b), "Spine1": (0.02 * b, 0), "Head": (-0.03 * b, 0),
            "LeftArm": (0.04 * b, 0), "RightArm": (0.04 * b, 0),
            "LeftForeArm": (-0.06, 0), "RightForeArm": (-0.06, 0)}


def gait(t, amp, bob, arm_amp, lean, drop=0.0, thigh_bias=0.0, knee_bias=0.0, arms_fwd=0.0, fore=-0.3):
    s = math.sin(TAU * t)
    c = math.cos(TAU * t)
    lknee = knee_bias + amp * 1.1 * max(0.0, -c)
    rknee = knee_bias + amp * 1.1 * max(0.0, c)
    return {
        "Hips": (0, -drop + bob * abs(s)),
        "Spine": (lean * 0.5, 0), "Spine1": (lean * 0.5, 0), "Head": (-lean * 0.8, 0),
        "LeftUpLeg": (-amp * s - thigh_bias, 0), "RightUpLeg": (amp * s - thigh_bias, 0),
        "LeftLeg": (lknee, 0), "RightLeg": (rknee, 0),
        "LeftArm": (arm_amp * s - arms_fwd, 0), "RightArm": (-arm_amp * s - arms_fwd, 0),
        "LeftForeArm": (fore, 0), "RightForeArm": (fore, 0),
    }


make_clip("Idle", 60, idle)
make_clip("Walk", 30, lambda t: gait(t, 0.55, 0.012, 0.45, 0.06))
make_clip("Run", 20, lambda t: gait(t, 0.95, 0.03, 0.85, 0.22, fore=-1.1))
make_clip("CrouchIdle", 60, lambda t: gait(0.0, 0.0, 0.0, 0.0, 0.35, drop=0.12, thigh_bias=0.9, knee_bias=1.5)
          | {"Spine1": (0.35 + 0.02 * math.sin(TAU * t), 0)})
make_clip("Sneak", 36, lambda t: gait(t, 0.4, 0.006, 0.25, 0.35, drop=0.12, thigh_bias=0.9, knee_bias=1.4))
make_clip("CarryIdle", 60, lambda t: idle(t) | {"LeftArm": (-1.25, 0), "RightArm": (-1.25, 0),
                                                "LeftForeArm": (-0.5, 0), "RightForeArm": (-0.5, 0)})
make_clip("CarryWalk", 30, lambda t: gait(t, 0.5, 0.012, 0.0, -0.05, arms_fwd=1.25, fore=-0.5))


def jump(t):  # one-shot: crouch -> launch -> tuck -> reach for landing
    if t < 0.25:
        k = t / 0.25
        return {"Hips": (0, -0.08 * k), "Spine1": (0.25 * k, 0), "LeftUpLeg": (-0.6 * k, 0), "RightUpLeg": (-0.6 * k, 0),
                "LeftLeg": (1.0 * k, 0), "RightLeg": (1.0 * k, 0), "LeftArm": (0.6 * k, 0), "RightArm": (0.6 * k, 0)}
    k = min(1.0, (t - 0.25) / 0.25)
    return {"Hips": (0, 0.0), "Spine1": (0.05, 0), "LeftUpLeg": (-0.5 * k, 0), "RightUpLeg": (-0.3 * k, 0),
            "LeftLeg": (0.9 * k, 0), "RightLeg": (0.6 * k, 0), "LeftArm": (-2.4 * k, 0), "RightArm": (-2.4 * k, 0),
            "LeftForeArm": (-0.3, 0), "RightForeArm": (-0.3, 0)}


make_clip("Jump", 24, jump)
make_clip("Fall", 24, lambda t: {"Hips": (0, 0), "Spine1": (0.05, 0), "LeftUpLeg": (-0.35 + 0.1 * math.sin(TAU * t), 0),
                                 "RightUpLeg": (-0.35 - 0.1 * math.sin(TAU * t), 0), "LeftLeg": (0.6, 0), "RightLeg": (0.6, 0),
                                 "LeftArm": (-2.0 + 0.2 * math.sin(TAU * t), 0), "RightArm": (-2.0 - 0.2 * math.sin(TAU * t), 0),
                                 "LeftForeArm": (-0.3, 0), "RightForeArm": (-0.3, 0)})

rig.animation_data.action = bpy.data.actions["Idle"]
for pb in rig.pose.bones:
    pb.rotation_euler = (0, 0, 0)
    pb.location = (0, 0, 0)

# ------------------------------------------------------------------ export
os.makedirs(os.path.dirname(OUT_BLEND), exist_ok=True)
os.makedirs(os.path.dirname(OUT_GLB), exist_ok=True)
os.makedirs(UNITY_ELF, exist_ok=True)
os.makedirs(os.path.join(UNITY_ELF, "Animations"), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)

bpy.ops.object.select_all(action="DESELECT")
mesh.select_set(True)
rig.select_set(True)

# Unity: one FBX with mesh+rig (Elf_LowPoly.fbx) and one with the clips (Animations/Elf_Clips.fbx).
bpy.ops.export_scene.fbx(filepath=os.path.join(UNITY_ELF, "Elf_LowPoly.fbx"), use_selection=True,
                         object_types={"ARMATURE", "MESH"}, add_leaf_bones=False, bake_anim=False,
                         path_mode="COPY", embed_textures=False, mesh_smooth_type="FACE",
                         bake_space_transform=True)
bpy.ops.export_scene.fbx(filepath=os.path.join(UNITY_ELF, "Animations", "Elf_Clips.fbx"), use_selection=True,
                         object_types={"ARMATURE", "MESH"}, add_leaf_bones=False, bake_anim=True,
                         bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_simplify_factor=0.0, bake_space_transform=True)

# Web (three.js): GLB with all clips
bpy.ops.export_scene.gltf(filepath=OUT_GLB, export_format="GLB", use_selection=True,
                          export_animations=True, export_animation_mode="ACTIONS",
                          export_apply=False, export_yup=True)

print("ELF DONE verts=%d tris~=%d" % (len(mesh.data.vertices), sum(len(p.vertices) - 2 for p in mesh.data.polygons)))
