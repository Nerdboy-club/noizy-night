"""
Export Unity-side FBX props to web-ready GLB (embedded textures, small).
  blender -b -P art/blender/export_web_glb.py -- <fbx> <out.glb> [<fbx> <out.glb> ...]
"""
import bpy, sys, os

a = sys.argv[sys.argv.index("--") + 1:]
for i in range(0, len(a), 2):
    src, dst = a[i], a[i + 1]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)
    for img in bpy.data.images:
        if img.size[0] > 1024:
            img.scale(1024, 1024)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", export_apply=True, export_image_format="JPEG", export_jpeg_quality=80)
    print("EXPORTED", dst, os.path.getsize(dst))
