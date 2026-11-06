import bpy, math
bpy.ops.wm.open_mainfile(filepath="C:/Vs-code/gamedev/noizy-night/art/blender/cat.blend")
s=bpy.context.scene
rig=bpy.data.objects["CatRig"]
cam=bpy.data.objects.new("c",bpy.data.cameras.new("c")); s.collection.objects.link(cam)
cam.data.type='ORTHO'; cam.data.ortho_scale=1.3; cam.location=(3,0,0.3); cam.rotation_euler=(math.radians(90),0,math.radians(90)); s.camera=cam
l=bpy.data.objects.new("l",bpy.data.lights.new("l","SUN")); s.collection.objects.link(l); l.rotation_euler=(0.9,0.2,0.5)
s.render.engine='BLENDER_WORKBENCH'; s.display.shading.light='FLAT'; s.display.shading.color_type='TEXTURE'
s.render.resolution_x=700; s.render.resolution_y=500
for act,fr in (("Walk",10),("Sleep",20)):
    rig.animation_data.action=bpy.data.actions[act]; s.frame_set(fr)
    s.render.filepath="C:/Vs-code/gamedev/noizy-night/art/blender/cat_%s.png"%act
    bpy.ops.render.render(write_still=True)
