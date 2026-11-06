import bpy, math
bpy.ops.wm.open_mainfile(filepath="C:/Vs-code/gamedev/noizy-night/art/blender/elf_tripo.blend")
s=bpy.context.scene
rig=bpy.data.objects["ElfRig"]
rig.animation_data.action=bpy.data.actions["Walk"]
out=[]
for fr,name in ((8,"walk"),):
    s.frame_set(fr)
cam=bpy.data.objects.new("c",bpy.data.cameras.new("c")); s.collection.objects.link(cam)
cam.data.type='ORTHO'; cam.data.ortho_scale=1.7; cam.location=(2.5,-2.5,0.6); cam.rotation_euler=(math.radians(90),0,math.radians(45)); s.camera=cam
l=bpy.data.objects.new("l",bpy.data.lights.new("l","SUN")); s.collection.objects.link(l); l.rotation_euler=(0.9,0.2,0.5)
s.render.engine='BLENDER_WORKBENCH'; s.display.shading.light='FLAT'; s.display.shading.color_type='TEXTURE'
s.render.resolution_x=700; s.render.resolution_y=700
for fr,name in ((8,"walk"),(1,"idle")):
    if name=="idle": rig.animation_data.action=bpy.data.actions["CarryWalk"]
    s.frame_set(fr)
    s.render.filepath="C:/Vs-code/gamedev/noizy-night/art/blender/pose_%s.png"%name
    bpy.ops.render.render(write_still=True)
