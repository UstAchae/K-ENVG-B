import bpy, sys
from mathutils import Vector
src, out = sys.argv[-2:]
bpy.ops.wm.read_factory_settings(use_empty=True)
for n in ('Eyecup_R','Eyecup_L'):
    bpy.ops.wm.obj_import(filepath=f'{src}/{n}.obj', forward_axis='Y', up_axis='Z')
objs=[o for o in bpy.context.scene.objects if o.type=='MESH']
for o in objs:
    m=bpy.data.materials.new('m'); m.diffuse_color=(0.25,0.25,0.25,1); o.data.materials.append(m)
    for p in o.data.polygons: p.use_smooth=True
sc=bpy.context.scene; sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.color_type='MATERIAL'; sc.display.shading.light='STUDIO'
sc.render.film_transparent=True; sc.render.resolution_x=280; sc.render.resolution_y=240
c=sum((o.matrix_world@v.co for o in objs for v in o.data.vertices),Vector())/sum(len(o.data.vertices) for o in objs)
cam=bpy.data.cameras.new('c'); co=bpy.data.objects.new('c',cam); sc.collection.objects.link(co); sc.camera=co; cam.type='ORTHO'; cam.ortho_scale=0.15
# device frame: eye looks along -Y (eyecups face +Y), up +Z, right +X. A 3/4 view of the cups from the eye side, above.
d=Vector((-0.55,0.75,0.45)).normalized(); co.location=c+d*0.5; co.rotation_euler=(-d).to_track_quat('-Z','Z').to_euler()
sc.render.filepath=out; bpy.ops.render.render(write_still=True)
