import bpy, math, sys
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=sys.argv[sys.argv.index("--")+1])
s=bpy.context.scene
objs=[o for o in s.objects if o.type=='MESH']
import mathutils
mn=mathutils.Vector((1e9,)*3); mx=mathutils.Vector((-1e9,)*3)
for o in objs:
    for c in o.bound_box:
        w=o.matrix_world@mathutils.Vector(c)
        mn=mathutils.Vector(map(min,mn,w)); mx=mathutils.Vector(map(max,mx,w))
size=mx-mn; ctr=(mx+mn)/2
print("DIM",tuple(round(x,3) for x in size))
d=max(size)*1.2
cam=bpy.data.objects.new("c",bpy.data.cameras.new("c")); s.collection.objects.link(cam)
cam.data.type='ORTHO'; cam.data.ortho_scale=d*1.3
cam.location=(ctr.x,ctr.y-5*d,ctr.z); cam.rotation_euler=(math.radians(90),0,0); s.camera=cam
l=bpy.data.objects.new("l",bpy.data.lights.new("l","SUN")); s.collection.objects.link(l); l.rotation_euler=(0.9,0.2,0.5)
s.render.engine='BLENDER_WORKBENCH'; s.display.shading.light='FLAT'; s.display.shading.color_type='TEXTURE'
s.render.resolution_x=700; s.render.resolution_y=700
s.render.filepath=sys.argv[sys.argv.index("--")+2]
bpy.ops.render.render(write_still=True)
