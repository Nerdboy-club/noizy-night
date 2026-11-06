import bpy, math
bpy.ops.wm.open_mainfile(filepath="C:/Vs-code/gamedev/noizy-night/art/blender/elf.blend")
s=bpy.context.scene
cam=bpy.data.objects.new("c",bpy.data.cameras.new("c")); s.collection.objects.link(cam); s.camera=cam
cam.location=(1.3,-2.2,0.9); cam.rotation_euler=(math.radians(84),0,math.radians(30))
l=bpy.data.objects.new("l",bpy.data.lights.new("l","SUN")); l.rotation_euler=(0.8,0.3,0.6); l.data.energy=4; s.collection.objects.link(l)
s.render.engine='BLENDER_WORKBENCH'; s.display.shading.light='STUDIO'; s.display.shading.color_type='MATERIAL'
s.render.resolution_x=700; s.render.resolution_y=800; s.render.filepath="C:/Vs-code/gamedev/noizy-night/art/blender/preview.png"
bpy.ops.render.render(write_still=True)
