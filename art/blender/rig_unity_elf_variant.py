"""
Rig a custom Tripo/Meshy Elf GLB variant for Unity ONLY (never touches web public/ files).
Normalizes height to 1.139m (matching Elf_Tripo.fbx) and rigs with Mixamo bone names expected by ElfRagdoll.cs.

  blender --background --python art/blender/rig_unity_elf_variant.py -- <input.glb> <VariantName> [target_tris]

Outputs:
  unity/SilentNight/Assets/_Project/Art/Characters/Elf/<VariantName>.fbx
  unity/SilentNight/Assets/_Project/Art/Characters/Elf/<VariantName>_Textures/<VariantName>_albedo|normal|metalrough.png
"""
import bpy
import math
import os
import sys
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
SRC = args[0]
VARIANT_NAME = args[1] if len(args) > 1 else "Elf_P2"
TARGET_TRIS = int(args[2]) if len(args) > 2 else 28000
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ELF_DIR = os.path.join(ROOT, "unity", "SilentNight", "Assets", "_Project", "Art", "Characters", "Elf")
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
mesh.name = VARIANT_NAME
mesh.data.name = VARIANT_NAME + "Mesh"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# Normalize height to 1.139m (exact height of Elf_Tripo.fbx) with feet on floor (z=0), centred in x/y
vs = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
raw_h = max(1e-4, mx.z - mn.z)
target_h = 1.139
scale_k = target_h / raw_h
shift = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
mesh.data.transform(Matrix.Scale(scale_k, 4) @ Matrix.Translation(-shift))
H = target_h
print("VARIANT", VARIANT_NAME, "RAW_H", round(raw_h, 3), "-> NORMALIZED_H", H)

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

# ------------------------------------------------------------------ armature
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

arm_data = bpy.data.armatures.new(VARIANT_NAME + "Armature")
rig = bpy.data.objects.new(VARIANT_NAME + "Rig", arm_data)
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

# ------------------------------------------------------------------ textures as separate files
TEXDIR = os.path.join(ELF_DIR, VARIANT_NAME + "_Textures")
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
    img.filepath_raw = os.path.join(TEXDIR, "%s_%s.png" % (VARIANT_NAME, roles.get(img.name, "tex%d" % n)))
    img.file_format = "PNG"
    img.save()

os.makedirs(ELF_DIR, exist_ok=True)
for o in scene.objects:
    o.select_set(False)
mesh.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=os.path.join(ELF_DIR, VARIANT_NAME + ".fbx"),
    use_selection=True,
    object_types={"ARMATURE", "MESH"},
    add_leaf_bones=False,
    bake_anim=False,
    path_mode="RELATIVE",
    embed_textures=False,
    bake_space_transform=True,
)
print("VARIANT RIG DONE:", VARIANT_NAME)
