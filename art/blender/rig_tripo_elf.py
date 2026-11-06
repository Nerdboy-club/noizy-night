"""
Take a high-poly Tripo/Meshy elf GLB, decimate, rig (Mixamo bone names), add procedural clips, export for Unity + web.

  blender --background --python art/blender/rig_tripo_elf.py -- <input.glb> [target_tris]

Outputs:
  unity/.../Art/Characters/Elf/Elf_Tripo.fbx            (mesh + rig, textures embedded)
  unity/.../Art/Characters/Elf/Animations/Elf_Clips.fbx (rig + clips)
  public/models/elf_tripo.glb
  art/blender/elf_tripo.blend
"""
import bpy
import math
import os
import sys
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
SRC = args[0]
TARGET_TRIS = int(args[1]) if len(args) > 1 else 30000
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ELF_DIR = os.path.join(ROOT, "unity", "SilentNight", "Assets", "_Project", "Art", "Characters", "Elf")
OUT_GLB = os.path.join(ROOT, "public", "models", "elf_tripo.glb")
OUT_BLEND = os.path.join(ROOT, "art", "blender", "elf_tripo.blend")
P = "mixamorig:"

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = 30

# ------------------------------------------------------------------ import + clean
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in scene.objects if o.type == "MESH"]
for o in scene.objects:
    o.select_set(False)
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
mesh = bpy.context.view_layer.objects.active
mesh.parent = None
for o in list(scene.objects):
    if o is not mesh:
        bpy.data.objects.remove(o, do_unlink=True)
mesh.name = "Elf"
mesh.data.name = "ElfMesh"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# feet on the floor (z=0), centred in x/y
vs = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
shift = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
mesh.data.transform(__import__("mathutils").Matrix.Translation(-shift))
H = mx.z - mn.z
print("HEIGHT", H, "verts", len(mesh.data.vertices))

# ------------------------------------------------------------------ decimate
tris = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
ratio = min(1.0, TARGET_TRIS / max(1, tris))
md = mesh.modifiers.new("Decimate", "DECIMATE")
md.decimate_type = "COLLAPSE"
md.ratio = ratio
bpy.ops.object.modifier_apply(modifier="Decimate")
bpy.ops.object.shade_smooth()
print("TRIS", tris, "->", sum(len(p.vertices) - 2 for p in mesh.data.polygons))

# downscale textures to 2048
for img in bpy.data.images:
    if img.size[0] > 2048 or img.size[1] > 2048:
        img.scale(2048, 2048)

# ------------------------------------------------------------------ armature (proportions measured from the model)
s = H / 1.139
V = lambda x, y, z: (x * s, y * s, z * s)
BONES = [
    ("Hips", V(0, 0, 0.34), V(0, 0, 0.43), None),
    ("Spine", V(0, 0, 0.43), V(0, 0, 0.51), "Hips"),
    ("Spine1", V(0, 0, 0.51), V(0, 0, 0.58), "Spine"),
    ("Spine2", V(0, 0, 0.58), V(0, 0, 0.64), "Spine1"),
    ("Neck", V(0, 0, 0.64), V(0, 0, 0.70), "Spine2"),
    ("Head", V(0, 0, 0.70), V(0, 0, 1.0), "Neck"),
    ("HeadTop_End", V(0, 0, 1.0), V(0, 0, 1.139), "Head"),
]
for side, sx in (("Left", 1), ("Right", -1)):
    BONES += [
        (side + "Shoulder", V(0.04 * sx, 0, 0.62), V(0.13 * sx, 0, 0.62), "Spine2"),
        (side + "Arm", V(0.13 * sx, 0, 0.62), V(0.25 * sx, 0, 0.49), side + "Shoulder"),
        (side + "ForeArm", V(0.25 * sx, 0, 0.49), V(0.33 * sx, 0, 0.40), side + "Arm"),
        (side + "Hand", V(0.33 * sx, 0, 0.40), V(0.38 * sx, 0, 0.35), side + "ForeArm"),
        (side + "UpLeg", V(0.09 * sx, 0, 0.34), V(0.095 * sx, 0, 0.19), "Hips"),
        (side + "Leg", V(0.095 * sx, 0, 0.19), V(0.095 * sx, 0, 0.075), side + "UpLeg"),
        (side + "Foot", V(0.095 * sx, 0, 0.075), V(0.095 * sx, -0.09, 0.03), side + "Leg"),
        (side + "ToeBase", V(0.095 * sx, -0.09, 0.03), V(0.095 * sx, -0.15, 0.025), side + "Foot"),
    ]

arm_data = bpy.data.armatures.new("ElfArmature")
rig = bpy.data.objects.new("ElfRig", arm_data)
scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
for o in scene.objects:
    o.select_set(False)
rig.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
eb_map = {}
for name, head, tail, parent in BONES:
    eb = arm_data.edit_bones.new(P + name)
    eb.head, eb.tail = head, tail
    if parent:
        eb.parent = eb_map[parent]
        eb.use_connect = (Vector(eb.parent.tail) - Vector(head)).length < 1e-5
    # Arms are angled (A-pose): make local X the forward/back swing axis, same convention as vertical bones.
    if name in ("LeftArm", "RightArm", "LeftForeArm", "RightForeArm", "LeftHand", "RightHand"):
        eb.align_roll(Vector((0, 1, 0)))
    eb_map[name] = eb
bpy.ops.object.mode_set(mode="OBJECT")

# ------------------------------------------------------------------ skinning
for o in scene.objects:
    o.select_set(False)
mesh.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type="ARMATURE_AUTO")
used = {vg.name for vg in mesh.vertex_groups}
print("WEIGHT GROUPS", len(used), "of", len(BONES))
unweighted = sum(1 for v in mesh.data.vertices if not any(g.weight > 0 for g in v.groups))
print("UNWEIGHTED VERTS", unweighted)

# Fallback: Blender heat weighting fails on messy AI meshes -> weight leftover vertices by distance to bone segments.
def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (p - (a + ab * t)).length


segs = [(P + n, Vector(h), Vector(t)) for n, h, t, _ in BONES if n not in ("HeadTop_End", "LeftShoulder", "RightShoulder")]
fixed = 0
for v in mesh.data.vertices:
    if any(g.weight > 0 for g in v.groups):
        continue
    ds = sorted(((seg_dist(v.co, a, b), name) for name, a, b in segs))[:3]
    ws = [math.exp(-d / 0.035) for d, _ in ds]
    tot = sum(ws) or 1.0
    for w, (_, name) in zip(ws, ds):
        mesh.vertex_groups[name].add([v.index], w / tot, "REPLACE")
    fixed += 1
print("FALLBACK WEIGHTED", fixed)

# ------------------------------------------------------------------ animation (same procedural clips as build_elf.py)
rig.animation_data_create()
ANIMATED = ["Hips", "Spine", "Spine1", "Head", "LeftArm", "LeftForeArm", "RightArm", "RightForeArm",
            "LeftUpLeg", "LeftLeg", "RightUpLeg", "RightLeg", "LeftFoot", "RightFoot"]
for n in ANIMATED:
    rig.pose.bones[P + n].rotation_mode = "XYZ"
TAU = 2 * math.pi


def make_clip(name, frames, fn):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data.action = act
    for i in range(frames + 1):
        t = (i / frames) if i < frames else 0.0
        pose = fn(t)
        for n in ANIMATED:
            rx, hy = pose.get(n, (0.0, 0.0))
            pb = rig.pose.bones[P + n]
            pb.rotation_euler = (rx, 0.0, 0.0)
            pb.location = (0.0, hy * s if n == "Hips" else 0.0, 0.0)
            pb.keyframe_insert("rotation_euler", frame=i + 1)
            if n == "Hips":
                pb.keyframe_insert("location", frame=i + 1)
    return act


def idle(t):
    b = math.sin(TAU * t)
    return {"Hips": (0, 0.004 * b), "Spine1": (0.02 * b, 0), "Head": (-0.03 * b, 0),
            "LeftArm": (0.04 * b, 0), "RightArm": (0.04 * b, 0),
            "LeftForeArm": (-0.06, 0), "RightForeArm": (-0.06, 0)}


def gait(t, amp, bob, arm_amp, lean, drop=0.0, thigh_bias=0.0, knee_bias=0.0, arms_fwd=0.0, fore=-0.3):
    sn, cs = math.sin(TAU * t), math.cos(TAU * t)
    return {
        "Hips": (0, -drop + bob * abs(sn)),
        "Spine": (lean * 0.5, 0), "Spine1": (lean * 0.5, 0), "Head": (-lean * 0.8, 0),
        "LeftUpLeg": (-amp * sn - thigh_bias, 0), "RightUpLeg": (amp * sn - thigh_bias, 0),
        "LeftLeg": (knee_bias + amp * 1.1 * max(0.0, -cs), 0), "RightLeg": (knee_bias + amp * 1.1 * max(0.0, cs), 0),
        "LeftArm": (arm_amp * sn - arms_fwd, 0), "RightArm": (-arm_amp * sn - arms_fwd, 0),
        "LeftForeArm": (fore, 0), "RightForeArm": (fore, 0),
    }


make_clip("Idle", 60, idle)
make_clip("Walk", 30, lambda t: gait(t, 0.55, 0.012, 0.45, 0.06))
make_clip("Run", 20, lambda t: gait(t, 0.95, 0.03, 0.85, 0.22, fore=-1.1))
make_clip("CrouchIdle", 60, lambda t: dict(gait(0.0, 0.0, 0.0, 0.0, 0.35, drop=0.12, thigh_bias=0.9, knee_bias=1.5),
                                           Spine1=(0.35 + 0.02 * math.sin(TAU * t), 0)))
make_clip("Sneak", 36, lambda t: gait(t, 0.4, 0.006, 0.25, 0.35, drop=0.12, thigh_bias=0.9, knee_bias=1.4))
make_clip("CarryIdle", 60, lambda t: dict(idle(t), LeftArm=(-1.25, 0), RightArm=(-1.25, 0),
                                          LeftForeArm=(-0.5, 0), RightForeArm=(-0.5, 0)))
make_clip("CarryWalk", 30, lambda t: gait(t, 0.5, 0.012, 0.0, -0.05, arms_fwd=1.25, fore=-0.5))


def jump(t):
    if t < 0.25:
        k = t / 0.25
        return {"Hips": (0, -0.08 * k), "Spine1": (0.25 * k, 0), "LeftUpLeg": (-0.6 * k, 0), "RightUpLeg": (-0.6 * k, 0),
                "LeftLeg": (1.0 * k, 0), "RightLeg": (1.0 * k, 0), "LeftArm": (0.6 * k, 0), "RightArm": (0.6 * k, 0)}
    k = min(1.0, (t - 0.25) / 0.25)
    return {"Spine1": (0.05, 0), "LeftUpLeg": (-0.5 * k, 0), "RightUpLeg": (-0.3 * k, 0),
            "LeftLeg": (0.9 * k, 0), "RightLeg": (0.6 * k, 0), "LeftArm": (-2.4 * k, 0), "RightArm": (-2.4 * k, 0),
            "LeftForeArm": (-0.3, 0), "RightForeArm": (-0.3, 0)}


make_clip("Jump", 24, jump)
make_clip("Fall", 24, lambda t: {"Spine1": (0.05, 0), "LeftUpLeg": (-0.35 + 0.1 * math.sin(TAU * t), 0),
                                 "RightUpLeg": (-0.35 - 0.1 * math.sin(TAU * t), 0), "LeftLeg": (0.6, 0), "RightLeg": (0.6, 0),
                                 "LeftArm": (-2.0 + 0.2 * math.sin(TAU * t), 0), "RightArm": (-2.0 - 0.2 * math.sin(TAU * t), 0),
                                 "LeftForeArm": (-0.3, 0), "RightForeArm": (-0.3, 0)})

rig.animation_data.action = bpy.data.actions["Idle"]
for pb in rig.pose.bones:
    pb.rotation_euler = (0, 0, 0)
    pb.location = (0, 0, 0)

# ------------------------------------------------------------------ textures as separate files (Unity builds a URP material from them)
TEXDIR = os.path.join(ELF_DIR, "Elf_Tripo_Textures")
os.makedirs(TEXDIR, exist_ok=True)


def find_image(sock, seen=None):
    seen = seen or set()
    for l in sock.links:
        n = l.from_node
        if n.name in seen:
            continue
        seen.add(n.name)
        if n.type == "TEX_IMAGE" and n.image:
            return n.image
        for i in n.inputs:
            r = find_image(i, seen)
            if r:
                return r
    return None


roles = {}
for m in bpy.data.materials:
    if not m.use_nodes:
        continue
    bsdf = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if not bsdf:
        continue
    for role, sock in (("albedo", "Base Color"), ("normal", "Normal"), ("metalrough", "Roughness")):
        img = find_image(bsdf.inputs[sock])
        if img and img.name not in roles:
            roles[img.name] = role
print("ROLES", roles)
for n, img in enumerate(bpy.data.images):
    if img.type != "IMAGE" or img.size[0] == 0:
        continue
    img.filepath_raw = os.path.join(TEXDIR, "Elf_Tripo_%s.png" % roles.get(img.name, "tex%d" % n))
    img.file_format = "PNG"
    img.save()

# ------------------------------------------------------------------ export
os.makedirs(os.path.join(ELF_DIR, "Animations"), exist_ok=True)
os.makedirs(os.path.dirname(OUT_GLB), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
for o in scene.objects:
    o.select_set(False)
mesh.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=os.path.join(ELF_DIR, "Elf_Tripo.fbx"), use_selection=True, object_types={"ARMATURE", "MESH"},
                         add_leaf_bones=False, bake_anim=False, path_mode="RELATIVE", embed_textures=False, bake_space_transform=True)
bpy.ops.export_scene.fbx(filepath=os.path.join(ELF_DIR, "Animations", "Elf_Clips.fbx"), use_selection=True,
                         object_types={"ARMATURE", "MESH"}, add_leaf_bones=False, bake_anim=True,
                         bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0.0,
                         bake_space_transform=True)
bpy.ops.export_scene.gltf(filepath=OUT_GLB, export_format="GLB", use_selection=True, export_animations=True,
                          export_animation_mode="ACTIONS", export_yup=True)
print("RIG DONE")
