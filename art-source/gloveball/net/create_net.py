"""Original low-poly regulation-size volleyball net. No third-party model input.
Run Blender --background --factory-startup --python create_net.py.
Existing authored outputs are protected.
"""
from pathlib import Path
import bpy

here=Path(__file__).resolve().parent
blend=here/'volleyball_net.blend'
fbx=here.parents[2]/'unity/gloveball/Assets/GloveBallDemo/Art/VolleyballNet/VolleyballNet.fbx'
if blend.exists() or fbx.exists():
    raise RuntimeError('Output exists; preserve manual edits instead of regenerating.')
fbx.parent.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
def mat(name,color):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); return m
black=mat('NetCord',(.025,.035,.05)); white=mat('NetTape',(.94,.95,.96)); blue=mat('NetPost',(.04,.22,.48))
def box(name,loc,size,material):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
    o=bpy.context.object; o.name=name; o.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.data.materials.append(material)
# Blender Z is height; Unity FBX export maps it to Y. Top tape is exactly 2.43 m.
for i in range(96): box('VerticalCord',(-4.75+i*.1,0,1.93),(.006,.008,1),black)
for i in range(11): box('HorizontalCord',(0,0,1.43+i*.1),(9.5,.008,.006),black)
box('TopTape',(0,0,2.395),(9.5,.028,.07),white)
box('BottomTape',(0,0,1.455),(9.5,.025,.05),white)
for x in [-4.5,4.5]: box('SideTape',(x,0,1.93),(.05,.03,1),white)
for x in [-5.25,5.25]:
    box('PaddedPost',(x,0,1.275),(.16,.16,2.55),blue)
    box('PostFoot',(x,0,.04),(.38,.38,.08),blue)
    box('TopCable',(x/abs(x)*5,0,2.40),(.5,.012,.012),black)
    box('LowerCable',(x/abs(x)*5,0,1.46),(.5,.012,.012),black)
# One mesh, three material slots; no hundreds of runtime draw calls.
bpy.ops.object.select_all(action='SELECT'); bpy.context.view_layer.objects.active=bpy.context.selected_objects[0]
bpy.ops.object.join(); bpy.context.object.name='VolleyballNet'
bpy.context.scene.cursor.location=(0,0,0); bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
bpy.ops.wm.save_as_mainfile(filepath=str(blend))
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False)
print('NET_CREATED',fbx)
