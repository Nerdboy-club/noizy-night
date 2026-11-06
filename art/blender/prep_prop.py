"""
Prepare a Tripo/Meshy prop for Unity: decimate, scale to a target height, pivot at floor centre, export FBX.

  blender -b -P art/blender/prep_prop.py -- <in.glb> <Name> <Room> <height_m> [tris=25000] [tex=1024]

Output: unity/SilentNight/Assets/_Project/Art/Environment/<Room>/<Name>.fbx (textures saved next to it in <Name>_Textures)
"""
import bpy, os, sys
from mathutils import Vector, Matrix

a = sys.argv[sys.argv.index("--") + 1:]
SRC, NAME, ROOM, HEIGHT = a[0], a[1], a[2], float(a[3])
TRIS = int(a[4]) if len(a) > 4 else 25000
TEX = int(a[5]) if len(a) > 5 else 1024
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "unity", "SilentNight", "Assets", "_Project", "Art", "Environment", ROOM)
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
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
mesh.name = NAME
mesh.data.name = NAME + "Mesh"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

vs = [v.co.copy() for v in mesh.data.vertices]
mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
k = HEIGHT / (mx.z - mn.z)
centre = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
mesh.data.transform(Matrix.Scale(k, 4) @ Matrix.Translation(-centre))
print("SIZE_M", tuple(round((mx[i] - mn[i]) * k, 3) for i in range(3)))

tris = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
md = mesh.modifiers.new("Decimate", "DECIMATE")
md.decimate_type = "COLLAPSE"
md.ratio = min(1.0, TRIS / max(1, tris))
bpy.ops.object.modifier_apply(modifier="Decimate")
bpy.ops.object.shade_smooth()
print("TRIS", tris, "->", sum(len(p.vertices) - 2 for p in mesh.data.polygons))
TEXDIR = os.path.join(OUT, NAME + "_Textures")
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
    for role, sockname in (("albedo", "Base Color"), ("normal", "Normal"), ("metalrough", "Roughness"), ("emission", "Emission Color")):
        img = find_image(bsdf.inputs[sockname])
        if img and img.name not in roles:
            roles[img.name] = role
print("ROLES", roles)
for n, img in enumerate(bpy.data.images):
    if img.type != "IMAGE" or img.size[0] == 0:
        continue
    if img.size[0] > TEX or img.size[1] > TEX:
        img.scale(TEX, TEX)
    role = roles.get(img.name, "tex%d" % n)
    img.filepath_raw = os.path.join(TEXDIR, "%s_%s.png" % (NAME, role))
    img.file_format = "PNG"
    img.save()

for o in scene.objects:
    o.select_set(False)
mesh.select_set(True)
bpy.context.view_layer.objects.active = mesh
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, NAME + ".fbx"), use_selection=True, object_types={"MESH"},
                         path_mode="RELATIVE", embed_textures=False, bake_space_transform=True)
print("PROP DONE", NAME)
