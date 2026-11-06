import bpy, math, sys, mathutils
args=sys.argv[sys.argv.index("--")+1:]
out=args[0]; files=args[1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
s=bpy.context.scene
x=0
for f in files:
    bpy.ops.import_scene.fbx(filepath=f)
    new=[o for o in s.objects if o.type=='MESH' and o.get("placed") is None]
    for o in new:
        o["placed"]=1
        o.location.x+=x
    mx=max((o.dimensions.x for o in new),default=0.3)
    x+=mx*1.4
meshes=[o for o in s.objects if o.type=='MESH']
cam=bpy.data.objects.new("c",bpy.data.cameras.new("c")); s.collection.objects.link(cam)
cam.data.type='ORTHO'; cam.data.ortho_scale=max(x,0.5)*1.1
cam.location=(x/2-0.2,-5,0.2); cam.rotation_euler=(math.radians(90),0,0); s.camera=cam
l=bpy.data.objects.new("l",bpy.data.lights.new("l","SUN")); s.collection.objects.link(l); l.rotation_euler=(0.9,0.2,0.5)
s.render.engine='BLENDER_WORKBENCH'; s.display.shading.light='FLAT'; s.display.shading.color_type='MATERIAL'
s.render.resolution_x=1200; s.render.resolution_y=500
s.render.filepath=out
bpy.ops.render.render(write_still=True)
