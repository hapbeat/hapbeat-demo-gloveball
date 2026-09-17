"""Original low-poly ball feeder. Blender Z is up, -Y is the launch direction.
The Unity launcher origin is 1.2m above ground; mouth is local +Z 0.6m.
Protects existing authored outputs; no third-party assets.
"""
from pathlib import Path
import math
import sys
import bpy
from mathutils import Vector

here=Path(__file__).resolve().parent
articulated='--articulated' in sys.argv
blend=here/('ball_feeder_articulated.blend' if articulated else 'ball_feeder.blend')
fbx=here.parents[2]/('unity/gloveball/Assets/GloveBallDemo/Art/BallFeeder/'+('BallFeederArticulated.fbx' if articulated else 'BallFeeder.fbx'))
if blend.exists() or fbx.exists(): raise RuntimeError('Existing authored output; choose a new output rather than overwrite.')
fbx.parent.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
def mat(name,color):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); return m
blue=mat('FeederBlue',(.06,.23,.38)); dark=mat('FeederRubber',(.025,.03,.04))
metal=mat('FeederMetal',(.65,.7,.74)); yellow=mat('FeederAccent',(.98,.62,.06))
def finish(o,name,m):
    o.name=name; o.data.materials.append(m); return o
def box(name,p,size,m):
    bpy.ops.mesh.primitive_cube_add(size=1,location=p); o=bpy.context.object; o.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True); return finish(o,name,m)
def cylinder(name,p,r,depth,m,rot=(0,0,0)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=16,radius=r,depth=depth,location=p,rotation=rot)
    return finish(bpy.context.object,name,m)
def rod(a,b,r,m):
    d=Vector(b)-Vector(a); o=cylinder('Basket bar',(Vector(a)+Vector(b))/2,r,d.length,m)
    o.rotation_euler=d.to_track_quat('Z','Y').to_euler()
box('Housing',(0,.05,-.30),(.62,.65,.55),blue)
box('Front safety stripe',(0,-.284,-.34),(.58,.03,.12),yellow)
box('Stand',(0,.10,-.83),(.15,.16,.52),metal)
box('Foot',(0,.08,-1.11),(.7,.55,.10),blue)
for x in [-.32,.32]:
    cylinder('Transport wheel',(x,.18,-1.08),.12,.08,dark,(0,math.pi/2,0))
for x in [-.23,.23]:
    cylinder('Feed roller',(x,-.28,.015),.18,.17,dark)
    cylinder('Roller cap',(x,-.28,.11),.11,.025,yellow)
# Open circular mouth: ball centre exits at the existing muzzle (0,-0.6,0).
bpy.ops.mesh.primitive_torus_add(major_segments=24,minor_segments=8,location=(0,-.57,0),rotation=(math.pi/2,0,0),major_radius=.175,minor_radius=.03)
finish(bpy.context.object,'Ball outlet',yellow)
box('Outlet lower lip',(0,-.45,-.19),(.4,.30,.035),metal)
for x in [-.28,.28]:
    for y in [0,.52]: rod((x*.6,y*.8,.03),(x,y,.67),.014,metal)
for z in [.28,.48,.67]:
    for y in [0,.52]: rod((-.28,y,z),(.28,y,z),.012,metal)
    for x in [-.28,.28]: rod((x,0,z),(x,.52,z),.012,metal)
for p in [(-.12,.16,.32),(.12,.34,.34),(0,.25,.53)]:
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=8,radius=.11,location=p)
    finish(bpy.context.object,'Stored ball',metal)
bpy.context.scene.cursor.location=(0,0,0)
parts=list(bpy.context.scene.objects)
groups={'FeederBase':[o for o in parts if o.name.startswith(('Stand','Foot','Transport wheel'))]}
groups['FeederHead']=[o for o in parts if o not in groups['FeederBase']]
if not articulated: groups={'BallFeeder':parts}
for name,objects in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects: o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.object.join(); bpy.context.object.name=name
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.wm.save_as_mainfile(filepath=str(blend))
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False)
print('FEEDER_CREATED',fbx)
