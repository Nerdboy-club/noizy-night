"""Split the 3-ornament GLB into Ornament_Red/Blue/Green FBX files (height 0.16 m each)."""
import bpy, os, sys
from mathutils import Vector, Matrix
SRC = sys.argv[sys.argv.index("--") + 1]
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "unity", "SilentNight", "Assets", "_Project", "Art", "Environment", "Decor")
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in scene.objects if o.type == "MESH"]
for o in scene.objects: o.select_set(False)
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
m = bpy.context.view_layer.objects.active
m.parent = None
for o in list(scene.objects):
    if o is not m: bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.separate(type="LOOSE")
bpy.ops.object.mode_set(mode="OBJECT")
parts = [o for o in scene.objects if o.type == "MESH"]
def cx(o): return sum((o.matrix_world @ v.co).x for v in o.data.vertices) / len(o.data.vertices)
def size(o):
    zs = [v.co.z for v in o.data.vertices]; return max(zs) - min(zs)
# split the loose pieces into 3 ornaments: thirds of the overall x range (balls are evenly spaced)
xs = [cx(p) for p in parts]
lo, hi = min(xs), max(xs)
t1, t2 = lo + (hi - lo) / 3, lo + 2 * (hi - lo) / 3
groups = [[p for p, x in zip(parts, xs) if x < t1], [p for p, x in zip(parts, xs) if t1 <= x < t2], [p for p, x in zip(parts, xs) if x >= t2]]
print("GROUPS", [len(g) for g in groups])
merged = []
for g in groups:
    for o in scene.objects: o.select_set(False)
    for o in g: o.select_set(True)
    bpy.context.view_layer.objects.active = g[0]
    if len(g) > 1: bpy.ops.object.join()
    merged.append(bpy.context.view_layer.objects.active)
parts = merged
names = ["Ornament_A", "Ornament_B", "Ornament_C"]
for p, name in zip(parts, names):
    vs = [v.co.copy() for v in p.data.vertices]
    mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
    mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    k = 0.16 / (mx.z - mn.z)
    c = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
    p.data.transform(Matrix.Scale(k, 4) @ Matrix.Translation(-c))
    p.name = name; p.data.name = name + "Mesh"
    tris = sum(len(q.vertices) - 2 for q in p.data.polygons)
    md = p.modifiers.new("D", "DECIMATE"); md.decimate_type = "COLLAPSE"; md.ratio = min(1.0, 6000 / max(1, tris))
    for o in scene.objects: o.select_set(False)
    p.select_set(True); bpy.context.view_layer.objects.active = p
    bpy.ops.object.modifier_apply(modifier="D"); bpy.ops.object.shade_smooth()
# textures (shared atlas): one copy per ornament so each FBX has its own _Textures folder
imgs = [i for i in bpy.data.images if i.type == "IMAGE" and i.size[0] > 0]
roles = {}
for mt in bpy.data.materials:
    if not mt.use_nodes: continue
    b = next((n for n in mt.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if not b: continue
    def find(sock, seen=None):
        seen = seen or set()
        for l in sock.links:
            n = l.from_node
            if n.name in seen: continue
            seen.add(n.name)
            if n.type == "TEX_IMAGE" and n.image: return n.image
            for i in n.inputs:
                r = find(i, seen)
                if r: return r
    for role, sn in (("albedo", "Base Color"), ("normal", "Normal"), ("metalrough", "Roughness")):
        im = find(b.inputs[sn])
        if im and im.name not in roles: roles[im.name] = role
for im in imgs:
    if im.size[0] > 1024: im.scale(1024, 1024)
for p, name in zip(parts, names):
    td = os.path.join(OUT, name + "_Textures"); os.makedirs(td, exist_ok=True)
    for im in imgs:
        im.filepath_raw = os.path.join(td, "%s_%s.png" % (name, roles.get(im.name, im.name))); im.file_format = "PNG"; im.save()
    for o in scene.objects: o.select_set(False)
    p.select_set(True); bpy.context.view_layer.objects.active = p
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={"MESH"},
                             path_mode="RELATIVE", embed_textures=False, bake_space_transform=True)
print("ORNAMENTS DONE")
