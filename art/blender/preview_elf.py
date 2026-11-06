import bpy, math
bpy.ops.wm.open_mainfile(filepath="C:/Vs-code/gamedev/noizy-night/art/blender/elf.blend")
s=bpy.context.scene
for i,(loc,rot,name) in enumerate([((0,-2.6,0.62),(math.radians(88),0,0),"front"),((2.6,0,0.62),(math.radians(88),0,math.radians(90)),"side"),((0,2.6,0.62),(math.radians(88),0,math.radians(180)),"back")]):
    cam=bpy.data.objects.new("c%d"%i,bpy.data.cameras.new("c%d"%i)); s.collection.objects.link(cam)
    cam.data.type='ORTHO'; cam.data.ortho_scale=1.5; cam.location=loc; cam.rotation_euler=rot
    s.camera=cam
    l=bpy.data.objects.new("l%d"%i,bpy.data.lights.new("l%d"%i,"SUN")); s.collection.objects.link(l); l.rotation_euler=(0.9,0.2,0.5)
    s.render.engine='BLENDER_WORKBENCH'; s.display.shading.light='STUDIO'; s.display.shading.color_type='MATERIAL'
    s.render.film_transparent=False
    s.render.resolution_x=560; s.render.resolution_y=700
    s.render.filepath="C:/Vs-code/gamedev/noizy-night/art/blender/elf_%s.png"%name
    bpy.ops.render.render(write_still=True)
