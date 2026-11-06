"""
Rig the Tripo fat cat (standing on 4 legs) for Unity (Generic rig) + simple clips: Idle, Walk, Run, Sleep, Alert.

  blender -b -P art/blender/rig_cat.py -- <cat.glb> [target_tris=20000]

Outputs: Art/Characters/Cat/Cat.fbx (+ Cat_Textures), Art/Characters/Cat/Animations/Cat_Clips.fbx, art/blender/cat.blend
Cat height 0.6 m, faces -Y in Blender (= +Z in Unity).
"""
import bpy, math, os, sys
from mathutils import Vector, Matrix

a = sys.argv[sys.argv.index("--") + 1:]
SRC = a[0]
TRIS = int(a[1]) if len(a) > 1 else 20000
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CAT_DIR = os.path.join(ROOT, "unity", "SilentNight", "Assets", "_Project", "Art", "Characters", "Cat")
os.makedirs(os.path.join(CAT_DIR, "Animations"), exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = 30
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
mesh.name = "Cat"
mesh.data.name = "CatMesh"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# ---- remove the flat display plate under the cat (loose, very thin, wide)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.separate(type="LOOSE")
bpy.ops.object.mode_set(mode="OBJECT")
parts = [o for o in scene.objects if o.type == "MESH"]
removed = 0
allz = [v.co.z for p in parts for v in p.data.vertices]
zlo, zhi = min(allz), max(allz)
Ht = zhi - zlo
for p in list(parts):
    zs = [v.co.z for v in p.data.vertices]
    # flat debris lying on the floor (display plate / contact shadow): thin and entirely near ground level
    if (max(zs) - zlo) < 0.10 * Ht and len(p.data.vertices) < 50000 and len(parts) > 1:
        bpy.data.objects.remove(p, do_unlink=True)
        removed += 1
print("PLATE PARTS REMOVED", removed)
parts = [o for o in scene.objects if o.type == "MESH"]
for o in scene.objects:
    o.select_set(False)
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
if len(parts) > 1:
    bpy.ops.object.join()
mesh = bpy.context.view_layer.objects.active
mesh.name = "Cat"
mesh.data.name = "CatMesh"

# ---- face -Y: the model looks to +X, rotate -90 deg about Z; feet on the floor, centred
mesh.data.transform(Matrix.Rotation(-math.pi / 2, 4, "Z"))
vs = [v.co.copy() for v in mesh.data.vertices]
mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
H0 = mx.z - mn.z
k = 0.6 / H0
mesh.data.transform(Matrix.Scale(k, 4) @ Matrix.Translation(-Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))))
vs = [v.co.copy() for v in mesh.data.vertices]
mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
L = mx.y - mn.y
print("CAT size", tuple(round(x, 3) for x in (mx - mn)), "front y", round(mn.y, 3), "back y", round(mx.y, 3))

tris = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
md = mesh.modifiers.new("D", "DECIMATE")
md.decimate_type = "COLLAPSE"
md.ratio = min(1.0, TRIS / max(1, tris))
bpy.ops.object.modifier_apply(modifier="D")
bpy.ops.object.shade_smooth()

# ---- armature (y fractions measured from the render: 0 = nose, 1 = tail tip)
yf = lambda f: mn.y + f * L          # f=0 nose -> y = min (front is -Y)
Z = 0.6 / 0.743 / 0.6 * 0.6          # = k  (heights below are in original metres * k)
K = k
def P(f, x, z):
    return (x, yf(f), z * K)

BONES = [
    ("Root", P(0.50, 0, 0.45), P(0.50, 0, 0.52), None),
    ("Hips", P(0.70, 0, 0.50), P(0.58, 0, 0.52), "Root"),
    ("Spine", P(0.58, 0, 0.52), P(0.42, 0, 0.52), "Hips"),
    ("Chest", P(0.42, 0, 0.52), P(0.28, 0, 0.54), "Spine"),
    ("Neck", P(0.28, 0, 0.54), P(0.20, 0, 0.58), "Chest"),
    ("Head", P(0.20, 0, 0.58), P(0.06, 0, 0.60), "Neck"),
    ("HeadTip", P(0.06, 0, 0.60), P(0.0, 0, 0.60), "Head"),
    ("Tail1", P(0.76, 0, 0.50), P(0.88, 0, 0.42), "Hips"),
    ("Tail2", P(0.88, 0, 0.42), P(0.95, 0, 0.30), "Tail1"),
    ("Tail3", P(0.95, 0, 0.30), P(0.99, 0, 0.16), "Tail2"),
]
for side, sx in (("L", 1), ("R", -1)):
    BONES += [
        ("HindUpper" + side, P(0.68, 0.12 * sx, 0.46), P(0.68, 0.12 * sx, 0.26), "Hips"),
        ("HindLower" + side, P(0.68, 0.12 * sx, 0.26), P(0.67, 0.12 * sx, 0.07), "HindUpper" + side),
        ("HindFoot" + side, P(0.67, 0.12 * sx, 0.07), P(0.62, 0.12 * sx, 0.02), "HindLower" + side),
        ("FrontUpper" + side, P(0.30, 0.12 * sx, 0.46), P(0.30, 0.12 * sx, 0.26), "Chest"),
        ("FrontLower" + side, P(0.30, 0.12 * sx, 0.26), P(0.30, 0.12 * sx, 0.07), "FrontUpper" + side),
        ("FrontFoot" + side, P(0.30, 0.12 * sx, 0.07), P(0.25, 0.12 * sx, 0.02), "FrontLower" + side),
    ]

arm = bpy.data.armatures.new("CatArmature")
rig = bpy.data.objects.new("CatRig", arm)
scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
for o in scene.objects:
    o.select_set(False)
rig.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
ebs = {}
for name, h, t, parent in BONES:
    eb = arm.edit_bones.new(name)
    eb.head, eb.tail = h, t
    if parent:
        eb.parent = ebs[parent]
        eb.use_connect = False
    # keep local X = world X for all bones: pitch swings about the left-right axis
    eb.align_roll(Vector((0, 0, 1)) if abs(Vector(t)[2] - Vector(h)[2]) < 1e-4 else Vector((1, 0, 0)))
    ebs[name] = eb
bpy.ops.object.mode_set(mode="OBJECT")

for o in scene.objects:
    o.select_set(False)
mesh.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type="ARMATURE_AUTO")

def seg_dist(p, a_, b_):
    ab = b_ - a_
    t = max(0.0, min(1.0, (p - a_).dot(ab) / max(ab.length_squared, 1e-9)))
    return (p - (a_ + ab * t)).length

segs = [(n, Vector(h), Vector(t)) for n, h, t, _ in BONES if n not in ("Root", "HeadTip")]
fixed = 0
for v in mesh.data.vertices:
    if any(g.weight > 0 for g in v.groups):
        continue
    ds = sorted(((seg_dist(v.co, a_, b_), n) for n, a_, b_ in segs))[:3]
    ws = [math.exp(-d / 0.03) for d, _ in ds]
    tot = sum(ws) or 1.0
    for w, (_, n) in zip(ws, ds):
        mesh.vertex_groups[n].add([v.index], w / tot, "REPLACE")
    fixed += 1
print("FALLBACK WEIGHTED", fixed)

# ---- animations
rig.animation_data_create()
ANIM = ["Hips", "Spine", "Chest", "Neck", "Head", "Tail1", "Tail2", "Tail3",
        "HindUpperL", "HindLowerL", "HindUpperR", "HindLowerR", "FrontUpperL", "FrontLowerL", "FrontUpperR", "FrontLowerR"]
for n in ANIM:
    rig.pose.bones[n].rotation_mode = "XYZ"
TAU = 2 * math.pi


def make_clip(name, frames, fn):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data.action = act
    for i in range(frames + 1):
        t = (i / frames) if i < frames else 0.0
        pose = fn(t)
        for n in ANIM:
            rx, rz, dz = pose.get(n, (0.0, 0.0, 0.0))
            pb = rig.pose.bones[n]
            pb.rotation_euler = (rx, 0.0, rz)
            pb.location = (0.0, 0.0, 0.0)
            pb.keyframe_insert("rotation_euler", frame=i + 1)
    return act


def idle(t):
    b = math.sin(TAU * t)
    return {"Spine": (0.02 * b, 0, 0), "Chest": (0.02 * b, 0, 0), "Head": (0.03 * b, 0, 0),
            "Tail1": (0, 0.15 * math.sin(TAU * t), 0), "Tail2": (0, 0.2 * math.sin(TAU * t + 0.8), 0), "Tail3": (0, 0.25 * math.sin(TAU * t + 1.6), 0)}


def walk(t, amp=0.45, speed_tail=0.2):
    s, c = math.sin(TAU * t), math.cos(TAU * t)
    # diagonal gait: front-left with hind-right
    return {
        "FrontUpperL": (-amp * s, 0, 0), "HindUpperR": (-amp * s, 0, 0),
        "FrontUpperR": (amp * s, 0, 0), "HindUpperL": (amp * s, 0, 0),
        "FrontLowerL": (amp * 0.8 * max(0, c), 0, 0), "FrontLowerR": (amp * 0.8 * max(0, -c), 0, 0),
        "HindLowerL": (amp * 0.8 * max(0, -c), 0, 0), "HindLowerR": (amp * 0.8 * max(0, c), 0, 0),
        "Spine": (0.02 * c, 0.05 * s, 0), "Head": (-0.02 * c, 0, 0),
        "Tail1": (0, speed_tail * s, 0), "Tail2": (0, speed_tail * 1.3 * math.sin(TAU * t + 0.8), 0),
    }


def sleep(t):
    b = math.sin(TAU * t)
    # lowered, head down; breathing
    return {"Chest": (0.05 + 0.03 * b, 0, 0), "Neck": (0.35, 0, 0), "Head": (0.25 + 0.02 * b, 0, 0),
            "Spine": (0.03 * b, 0, 0),
            "FrontUpperL": (-0.35, 0, 0), "FrontUpperR": (-0.35, 0, 0), "FrontLowerL": (0.9, 0, 0), "FrontLowerR": (0.9, 0, 0),
            "HindUpperL": (-0.45, 0, 0), "HindUpperR": (-0.45, 0, 0), "HindLowerL": (1.0, 0, 0), "HindLowerR": (1.0, 0, 0),
            "Tail1": (0, 0.5, 0), "Tail2": (0, 0.7, 0), "Tail3": (0, 0.6, 0)}


def alert(t):
    b = math.sin(TAU * t)
    return {"Neck": (-0.25, 0, 0), "Head": (-0.2, 0.15 * math.sin(TAU * t * 0.5), 0), "Chest": (-0.05, 0, 0),
            "Tail1": (-0.3, 0.1 * b, 0), "Tail2": (-0.3, 0.2 * b, 0)}


make_clip("Idle", 60, idle)
make_clip("Walk", 36, lambda t: walk(t, 0.4))
make_clip("Run", 20, lambda t: walk(t, 0.75, 0.3))
make_clip("Sleep", 90, sleep)
make_clip("Alert", 48, alert)
rig.animation_data.action = bpy.data.actions["Idle"]
for pb in rig.pose.bones:
    pb.rotation_euler = (0, 0, 0)

# ---- textures as files (Unity fixer builds the material)
TEXDIR = os.path.join(CAT_DIR, "Cat_Textures")
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
    b = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if not b:
        continue
    for role, sock in (("albedo", "Base Color"), ("normal", "Normal"), ("metalrough", "Roughness")):
        im = find_image(b.inputs[sock])
        if im and im.name not in roles:
            roles[im.name] = role
for n, img in enumerate(bpy.data.images):
    if img.type != "IMAGE" or img.size[0] == 0:
        continue
    if img.size[0] > 2048:
        img.scale(2048, 2048)
    img.filepath_raw = os.path.join(TEXDIR, "Cat_%s.png" % roles.get(img.name, "tex%d" % n))
    img.file_format = "PNG"
    img.save()

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "art", "blender", "cat.blend"))
for o in scene.objects:
    o.select_set(False)
mesh.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=os.path.join(CAT_DIR, "Cat.fbx"), use_selection=True, object_types={"ARMATURE", "MESH"},
                         add_leaf_bones=False, bake_anim=False, path_mode="RELATIVE", embed_textures=False, bake_space_transform=True)
bpy.ops.export_scene.fbx(filepath=os.path.join(CAT_DIR, "Animations", "Cat_Clips.fbx"), use_selection=True,
                         object_types={"ARMATURE", "MESH"}, add_leaf_bones=False, bake_anim=True,
                         bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0.0,
                         bake_space_transform=True)
print("CAT DONE")
