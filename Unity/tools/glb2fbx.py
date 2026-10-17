# Blender headless: crowd GLB -> FBX (armature + animations + outfit meshes) + extract textures.
# Run: blender -b male.glb -P glb2fbx.py -- out_male.fbx texdir
import bpy, os, sys

argv = sys.argv[sys.argv.index("--") + 1:]
# argv: -- <glb> <out_fbx> <texdir>
out_fbx = argv[1]
texdir = argv[2] if len(argv) > 2 else "."

os.makedirs(texdir, exist_ok=True)

# clear scene + enable the glTF extension if it ships disabled (Blender 5.x)
import addon_utils
addon_utils.enable('io_scene_gltf2', default_set=True, persistent=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
addon_utils.enable('io_scene_gltf2', default_set=True, persistent=True)

glb_path = None
for a in sys.argv:
    if a.lower().endswith(".glb"):
        glb_path = a
        break
assert glb_path, "no .glb argument"
print("IMPORTING", glb_path)
bpy.ops.import_scene.gltf(filepath=glb_path)

# extract embedded images
for img in bpy.data.images:
    if img.size()[0] == 0 or img.size()[1] == 0:
        continue
    try:
        path = os.path.join(texdir, img.name.replace(' ', '_') + ".png")
        import numpy as np
        w, h = img.size
        px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
        px = px[::-1]  # flip vertically
        from bpy_extras.image_utils import load_image  # noqa
        import bpy.data as bd
        new = bd.images.new(name + "_exp", w, h, alpha=True)
        new.pixels = px.ravel().tolist()
        new.filepath_raw = path
        new.file_format = 'PNG'
        new.save()
        print("saved tex", path)
    except Exception as e:
        print("tex save failed", img.name, repr(e))

# stash every action into NLA tracks on the armature so the FBX exporter
# writes them as takes (it otherwise only exports the assigned action)
import mathutils
for ob in bpy.data.objects:
    if ob.type == 'ARMATURE':
        if ob.animation_data is None:
            ob.animation_data_create()
        for act in bpy.data.actions:
            if act.name == 'Armature.001Action' or not act.frame_range:
                continue
            tr = ob.animation_data.nla_tracks.new()
            tr.name = 'TK_' + act.name
            fr = act.frame_range
            tr.strips.new(act.name, int(fr[0]), act)
            tr.mute = True
        if bpy.data.actions:
            act0 = sorted(bpy.data.actions, key=lambda a: a.name)[0]
            ob.animation_data.action = act0
            ob.animation_data.action_slot = ob.animation_data.action.slots[0] if hasattr(ob.animation_data.action, 'slots') and ob.animation_data.action.slots else None

print("PROBE actions:", [(a.name, tuple(a.frame_range)) for a in bpy.data.actions][:12])
for ob in bpy.data.objects:
    if ob.type == 'ARMATURE':
        print("PROBE arm", ob.name, "ad=", ob.animation_data is not None)
# export FBX: armature + meshes + all actions, Y-up (Unity default), apply transform
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(
    filepath=out_fbx,
    use_selection=False,
    object_types={'ARMATURE', 'MESH'},
    use_mesh_modifiers=True,
    add_leaf_bones=False,
    bake_anim=True,
    bake_anim_use_all_bones=True,
    bake_anim_simplify_factor=0.0,
    apply_scale_options='FBX_SCALE_NONE',
    global_scale=1.0,
    axis_forward='-Z',
    axis_up='Y',
    mesh_smooth_type='OFF',
)
print("FBX exported:", out_fbx)
