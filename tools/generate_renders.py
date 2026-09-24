import bpy
from mathutils import Vector
from pathlib import Path
import sys

def setup_scene():
    s = bpy.context.scene
    s.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in bpy.types.RenderEngine.__subclasses__() else 'CYCLES'
    if s.render.engine == 'CYCLES':
        s.cycles.samples = 16
        s.cycles.device = 'CPU'
    s.render.resolution_x = 720
    s.render.resolution_y = 540
    s.render.image_settings.file_format = 'PNG'
    
    # World background
    if not s.world:
        s.world = bpy.data.worlds.new("OptWorld")
    s.world.use_nodes = True
    bg = s.world.node_tree.nodes.get('Background')
    if bg:
        bg.inputs[0].default_value = (0.28, 0.33, 0.38, 1)
        bg.inputs[1].default_value = 0.85

    # Remove existing cameras and lights
    for o in list(bpy.data.objects):
        if o.type in {'CAMERA', 'LIGHT'}:
            bpy.data.objects.remove(o, do_unlink=True)

    # Lighting setup
    sun_data = bpy.data.lights.new('OptSun', 'SUN')
    sun_data.energy = 3.5
    sun_obj = bpy.data.objects.new('OptSun', sun_data)
    bpy.context.collection.objects.link(sun_obj)
    sun_obj.rotation_euler = (0.6, 0.3, 0.8)

    fill_data = bpy.data.lights.new('OptFill', 'SUN')
    fill_data.energy = 1.2
    fill_obj = bpy.data.objects.new('OptFill', fill_data)
    bpy.context.collection.objects.link(fill_obj)
    fill_obj.rotation_euler = (-0.5, -0.4, 2.5)

def render_view(cam_name, loc, target, lens, out_path):
    s = bpy.context.scene
    cam_data = bpy.data.cameras.new(cam_name)
    cam_data.lens = lens
    cam_data.clip_start = 0.05
    cam_obj = bpy.data.objects.new(cam_name, cam_data)
    bpy.context.collection.objects.link(cam_obj)
    cam_obj.location = loc
    cam_obj.rotation_euler = (Vector(target) - cam_obj.location).to_track_quat('-Z', 'Y').to_euler()
    s.camera = cam_obj
    
    s.render.filepath = str(out_path)
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam_obj, do_unlink=True)
    print(f"Rendered {cam_name} -> {out_path}")

def main():
    stage = "before"
    if "--after" in sys.argv:
        stage = "after"
    elif "--before" in sys.argv:
        stage = "before"

    root = Path(__file__).resolve().parents[1]
    src_dir = root / "ArtSource_Backup" if stage == "before" else root / "ArtSource"
    out_dir = Path(r"C:\Users\AVSok\.gemini\antigravity\brain\6c4757ef-c738-475e-8940-cb8989676c37")

    # 1. Sedan Exterior
    sedan_file = src_dir / "DS_Sedan_A.blend"
    if sedan_file.exists():
        bpy.ops.wm.open_mainfile(filepath=str(sedan_file))
        setup_scene()
        render_view("Sedan_Ext", (-4.5, 5.5, 2.2), (0, 0, 0.7), 45, out_dir / f"sedan_ext_{stage}.png")
        
        # Cabin interior light
        interior_light = bpy.data.lights.new('CabinFill', 'POINT')
        interior_light.energy = 25
        il_obj = bpy.data.objects.new('CabinFill', interior_light)
        bpy.context.collection.objects.link(il_obj)
        il_obj.location = (-0.3, 0.1, 1.2)
        render_view("Sedan_Int", (-0.38, 0.05, 1.20), (-0.22, 0.95, 0.95), 26, out_dir / f"sedan_int_{stage}.png")

    # 2. Traffic Cone
    cone_file = src_dir / "DS_TrafficCone.blend"
    if cone_file.exists():
        bpy.ops.wm.open_mainfile(filepath=str(cone_file))
        setup_scene()
        render_view("Cone_View", (-1.1, -1.2, 0.85), (0, 0, 0.35), 45, out_dir / f"cone_{stage}.png")

    # 3. Speed Bump
    sb_file = src_dir / "DS_SpeedBumps.blend"
    if sb_file.exists():
        bpy.ops.wm.open_mainfile(filepath=str(sb_file))
        setup_scene()
        render_view("SpeedBump_View", (1.2, -2.4, 1.1), (0, 0, 0.05), 42, out_dir / f"speedbump_{stage}.png")

if __name__ == "__main__":
    main()
