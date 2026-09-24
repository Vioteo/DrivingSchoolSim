import bpy
import bmesh
import json
import math
import os
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ART_SOURCE = ROOT / "ArtSource"
ART_OUT = ROOT / "Assets/DrivingSchool/Art"
WEB_MODELS = ROOT / "artifacts/visual-review/models"
REPORTS = ROOT / "artifacts/reports"

REPORTS.mkdir(parents=True, exist_ok=True)
WEB_MODELS.mkdir(parents=True, exist_ok=True)

def purge_orphans():
    for _ in range(3):
        bpy.ops.outliner.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)

def measure_scene():
    depsgraph = bpy.context.evaluated_depsgraph_get()
    total_tris = 0
    total_verts = 0
    for obj in bpy.data.objects:
        if obj.type == 'MESH' and obj.data:
            eval_obj = obj.evaluated_get(depsgraph)
            m = eval_obj.to_mesh()
            m.calc_loop_triangles()
            total_tris += len(m.loop_triangles)
            total_verts += len(m.vertices)
            eval_obj.to_mesh_clear()
    return total_tris, total_verts

def optimize_sedan(filepath, export_fbx_name=None):
    bpy.ops.wm.open_mainfile(filepath=str(filepath))
    before_tris, before_verts = measure_scene()
    
    # 1. Bevel and Subsurf adjustments
    for obj in bpy.data.objects:
        if obj.type != 'MESH' or not obj.data:
            continue
        for mod in obj.modifiers:
            if mod.type == 'BEVEL':
                if mod.segments > 1:
                    mod.segments = 1
            elif mod.type == 'SUBSURF':
                if mod.levels > 1:
                    mod.levels = 1
        
        # 2. Dense torus and cylindrical small details
        if any(k in obj.name for k in ["TreadChannel", "GaugeBezel", "RimLip"]):
            has_dec = any(m.type == 'DECIMATE' for m in obj.modifiers)
            if not has_dec:
                dec = obj.modifiers.new('Opt_Ratio', 'DECIMATE')
                dec.ratio = 0.35
        elif any(k in obj.name for k in ["Cabin_Floor", "Shell_Undertray"]):
            has_dec = any(m.type == 'DECIMATE' for m in obj.modifiers)
            if not has_dec:
                dec = obj.modifiers.new('Opt_Planar', 'DECIMATE')
                dec.decimate_type = 'DISSOLVE'
                dec.angle_limit = 0.035
                dec.delimit = {'MATERIAL'}

    after_tris, after_verts = measure_scene()
    purge_orphans()
    bpy.ops.wm.save_as_mainfile(filepath=str(filepath))
    print(f"Optimized {filepath.name}: {before_tris} -> {after_tris} tris")
    
    # Export FBX and GLB if requested
    if export_fbx_name and 'DS_Sedan_A' in bpy.data.objects:
        root = bpy.data.objects['DS_Sedan_A']
        bpy.ops.object.select_all(action='DESELECT')
        objs = [root] + list(root.children_recursive)
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = root
        
        # Export GLB
        glb_path = WEB_MODELS / f"{export_fbx_name}.glb"
        bpy.ops.export_scene.gltf(filepath=str(glb_path), export_format='GLB', use_selection=True, export_apply=True)
        
        # FBX export
        originals = list(objs)
        copies = []
        for o in originals:
            if o.type in {'FONT', 'CURVE'}:
                deps = bpy.context.evaluated_depsgraph_get()
                me = bpy.data.meshes.new_from_object(o.evaluated_get(deps))
                copy = bpy.data.objects.new(o.name + '_Mesh', me)
                bpy.context.collection.objects.link(copy)
                copy.parent = o.parent
                copy.matrix_world = o.matrix_world
                o.select_set(False)
                copy.select_set(True)
                copies.append(copy)
        fbx_path = ART_OUT / f"{export_fbx_name}.fbx"
        bpy.ops.export_scene.fbx(filepath=str(fbx_path), use_selection=True, object_types={'MESH', 'EMPTY'},
                                 axis_forward='Y', axis_up='Z', apply_unit_scale=True, add_leaf_bones=False, bake_anim=False)
        for o in copies:
            bpy.data.objects.remove(o, do_unlink=True)
        print(f"Exported {export_fbx_name} to FBX and GLB")
        
    return before_tris, after_tris, before_verts, after_verts

def optimize_traffic_cone(filepath):
    bpy.ops.wm.open_mainfile(filepath=str(filepath))
    before_tris, before_verts = measure_scene()
    
    # Rebuild cone shell with 24 segments to preserve material stripes perfectly
    profile = [(.187, .057), (.196, .064), (.196, .077), (.188, .087),
               (.185, .098), (.153, .23), (.1288, .33), (.0998, .45),
               (.0756, .55), (.032, .73), (.0305, .744), (.026, .75),
               (.019, .75), (.016, .744), (.0175, .73), (.171, .098),
               (.174, .077), (.174, .057)]
    segments = 24
    vertices = [(r*math.cos(2*math.pi*i/segments), r*math.sin(2*math.pi*i/segments), z)
                for r,z in profile for i in range(segments)]
    faces = []
    for j in range(len(profile)):
        for i in range(segments):
            k = (i+1) % segments
            jj = (j+1) % len(profile)
            faces.append((j*segments+i, j*segments+k, jj*segments+k, jj*segments+i))
            
    # Find materials from existing objects
    mats = {m.name: m for m in bpy.data.materials}
    orange = mats.get('Cone_Orange')
    white = mats.get('Cone_White')
    rubber = mats.get('Cone_Rubber')
    
    # Remove old objects
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
        
    mesh = bpy.data.meshes.new('ConeShellMesh')
    mesh.from_pydata(vertices, [], faces)
    if orange: mesh.materials.append(orange)
    if white: mesh.materials.append(white)
    for poly in mesh.polygons:
        ring = poly.index // segments
        poly.material_index = 1 if ring in (5, 7) else 0
        poly.use_smooth = ring not in (11, 17)
    shell = bpy.data.objects.new('Cone_Shell', mesh)
    bpy.context.collection.objects.link(shell)
    
    # Base with 1 segment bevel
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0,0,.031))
    base = bpy.context.object
    base.name = 'Cone_WeightedBase'
    base.scale = (.46,.46,.062)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if rubber: base.data.materials.append(rubber)
    bevel = base.modifiers.new('Bevel', 'BEVEL')
    bevel.width = .016
    bevel.segments = 1
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    normal = base.modifiers.new('WeightedNormal','WEIGHTED_NORMAL')
    bpy.ops.object.modifier_apply(modifier=normal.name)
    
    # Join into single DS_TrafficCone
    bpy.ops.object.select_all(action='DESELECT')
    shell.select_set(True)
    base.select_set(True)
    bpy.context.view_layer.objects.active = shell
    bpy.ops.object.join()
    cone = bpy.context.active_object
    cone.name = 'DS_TrafficCone'
    
    after_tris, after_verts = measure_scene()
    purge_orphans()
    bpy.ops.wm.save_as_mainfile(filepath=str(filepath))
    print(f"Optimized TrafficCone: {before_tris} -> {after_tris} tris")
    
    # Export FBX & GLB
    fbx_path = ART_OUT / "Props/DS_TrafficCone.fbx"
    fbx_path.parent.mkdir(parents=True, exist_ok=True)
    glb_path = ROOT / "artifacts/visual-review/traffic-cone/DS_TrafficCone.glb"
    glb_path.parent.mkdir(parents=True, exist_ok=True)
    
    bpy.ops.object.select_all(action='SELECT')
    for o in bpy.data.objects:
        if o.type not in {'MESH', 'EMPTY'}:
            o.select_set(False)
    bpy.ops.export_scene.fbx(filepath=str(fbx_path), use_selection=True, object_types={'MESH'},
                             axis_forward='-Z', axis_up='Y', apply_unit_scale=True)
    bpy.ops.export_scene.gltf(filepath=str(glb_path), export_format='GLB', use_selection=True, export_yup=True)
    print("Exported DS_TrafficCone.fbx and GLB")
    return before_tris, after_tris, before_verts, after_verts

def optimize_speed_bumps(filepath):
    bpy.ops.wm.open_mainfile(filepath=str(filepath))
    before_tris, before_verts = measure_scene()
    
    models = []
    for obj in bpy.data.objects:
        if obj.type == 'MESH' and 'SpeedBump' in obj.name:
            models.append(obj)
            has_dec = any(m.type == 'DECIMATE' for m in obj.modifiers)
            if not has_dec:
                dec = obj.modifiers.new('Opt_Planar', 'DECIMATE')
                dec.decimate_type = 'DISSOLVE'
                dec.angle_limit = 0.035
                dec.delimit = {'MATERIAL'}
                
    after_tris, after_verts = measure_scene()
    purge_orphans()
    bpy.ops.wm.save_as_mainfile(filepath=str(filepath))
    print(f"Optimized SpeedBumps: {before_tris} -> {after_tris} tris")
    
    # Export FBX for each bump
    out_dir = ART_OUT / "SpeedBumps"
    out_dir.mkdir(parents=True, exist_ok=True)
    rev_dir = ROOT / "artifacts/visual-review/speed-bumps"
    rev_dir.mkdir(parents=True, exist_ok=True)
    
    for obj in models:
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        paths = [out_dir / (obj.name + '.fbx'), rev_dir / (obj.name + '.glb')]
        bpy.ops.export_scene.fbx(filepath=str(paths[0]), use_selection=True, object_types={'MESH'},
                                 add_leaf_bones=False, bake_anim=False, axis_forward='-Z', axis_up='Y',
                                 apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS')
        bpy.ops.export_scene.gltf(filepath=str(paths[1]), export_format='GLB', use_selection=True, export_yup=True)
    print("Exported SpeedBumps FBX and GLB")
    return before_tris, after_tris, before_verts, after_verts

def optimize_generic(filepath, is_roadkit=False, is_district=False, is_autodrome=False):
    bpy.ops.wm.open_mainfile(filepath=str(filepath))
    before_tris, before_verts = measure_scene()
    
    for obj in bpy.data.objects:
        if obj.type != 'MESH' or not obj.data:
            continue
        for mod in obj.modifiers:
            if mod.type == 'BEVEL' and mod.segments > 1:
                mod.segments = 1
        if is_roadkit and len(obj.data.polygons) > 30:
            has_dec = any(m.type == 'DECIMATE' for m in obj.modifiers)
            if not has_dec:
                dec = obj.modifiers.new('Opt_Planar', 'DECIMATE')
                dec.decimate_type = 'DISSOLVE'
                dec.angle_limit = 0.035
                dec.delimit = {'MATERIAL'}
                
    after_tris, after_verts = measure_scene()
    purge_orphans()
    bpy.ops.wm.save_as_mainfile(filepath=str(filepath))
    print(f"Optimized {filepath.name}: {before_tris} -> {after_tris} tris")
    
    if is_district and 'DS_District' in bpy.data.objects:
        root = bpy.data.objects['DS_District']
        bpy.ops.object.select_all(action='DESELECT')
        for o in [root] + list(root.children_recursive):
            o.select_set(True)
        bpy.context.view_layer.objects.active = root
        bpy.ops.export_scene.fbx(filepath=str(ART_OUT / "DS_District.fbx"), use_selection=True, object_types={'MESH', 'EMPTY'},
                                 axis_forward='Y', axis_up='Z', apply_unit_scale=True, add_leaf_bones=False, bake_anim=False)
        bpy.ops.export_scene.gltf(filepath=str(WEB_MODELS / "DS_District.glb"), export_format='GLB', use_selection=True, export_apply=True)
        print("Exported DS_District FBX and GLB")
    elif is_autodrome and 'DS_Autodrome' in bpy.data.objects:
        root = bpy.data.objects['DS_Autodrome']
        bpy.ops.object.select_all(action='DESELECT')
        for o in [root] + list(root.children_recursive):
            o.select_set(True)
        bpy.context.view_layer.objects.active = root
        bpy.ops.export_scene.fbx(filepath=str(ART_OUT / "DS_Autodrome.fbx"), use_selection=True, object_types={'MESH', 'EMPTY'},
                                 axis_forward='Y', axis_up='Z', apply_unit_scale=True, add_leaf_bones=False, bake_anim=False)
        bpy.ops.export_scene.gltf(filepath=str(WEB_MODELS / "DS_Autodrome.glb"), export_format='GLB', use_selection=True, export_apply=True)
        print("Exported DS_Autodrome FBX and GLB")
    elif is_roadkit:
        # Export individual road pieces
        rk_dir = ART_OUT / "RoadKit"
        rk_dir.mkdir(parents=True, exist_ok=True)
        roots = [o for o in bpy.data.objects if o.parent is None and o.name.startswith('RK_')]
        for r in roots:
            bpy.ops.object.select_all(action='DESELECT')
            for o in [r] + list(r.children_recursive):
                o.select_set(True)
            bpy.context.view_layer.objects.active = r
            bpy.ops.export_scene.fbx(filepath=str(rk_dir / f"{r.name}.fbx"), use_selection=True, object_types={'MESH', 'EMPTY'},
                                     axis_forward='-Z', axis_up='Y', apply_unit_scale=True, add_leaf_bones=False, bake_anim=False)
        print("Exported RoadKit FBXs")
        
    return before_tris, after_tris, before_verts, after_verts

def optimize_pedestrian(filepath):
    bpy.ops.wm.open_mainfile(filepath=str(filepath))
    before_tris, before_verts = measure_scene()
    name = filepath.stem
    
    body = next((o for o in bpy.data.objects if o.type == 'MESH' and 'Body' in o.name), None)
    rig = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
    if body:
        has_dec = any(m.type == 'DECIMATE' for m in body.modifiers)
        if not has_dec:
            dec = body.modifiers.new('Opt_Ped', 'DECIMATE')
            dec.ratio = 0.75
            
    after_tris, after_verts = measure_scene()
    purge_orphans()
    bpy.ops.wm.save_as_mainfile(filepath=str(filepath))
    print(f"Optimized {name}: {before_tris} -> {after_tris} tris")
    
    if body and rig:
        ped_dir = ART_OUT / "Pedestrians"
        ped_dir.mkdir(parents=True, exist_ok=True)
        bpy.ops.object.select_all(action='DESELECT')
        rig.select_set(True)
        body.select_set(True)
        bpy.context.view_layer.objects.active = rig
        for track in rig.animation_data.nla_tracks:
            track.mute = False
        bpy.ops.export_scene.fbx(filepath=str(ped_dir / f"{name}.fbx"), use_selection=True,
                                 object_types={'ARMATURE', 'MESH'}, axis_forward='-Z', axis_up='Y', add_leaf_bones=False,
                                 bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
                                 bake_anim_simplify_factor=0, apply_scale_options='FBX_SCALE_UNITS')
        bpy.ops.export_scene.gltf(filepath=str(WEB_MODELS / f"{name}.glb"), export_format='GLB',
                                 use_selection=True, export_animations=True, export_animation_mode='NLA_TRACKS',
                                 export_force_sampling=True, export_frame_range=False)
        for track in rig.animation_data.nla_tracks:
            track.mute = True
        print(f"Exported {name} FBX and GLB")
        
    return before_tris, after_tris, before_verts, after_verts

def main():
    report = {}
    
    # 1. Sedan files
    report["DS_Sedan_A.blend"] = optimize_sedan(ART_SOURCE / "DS_Sedan_A.blend", "DS_Sedan_A")
    report["DS_Sedan_A_cabin_v3.blend"] = optimize_sedan(ART_SOURCE / "DS_Sedan_A_cabin_v3.blend", "DS_Sedan_A_cabin_v3")
    report["DS_Sedan_A_closed_shell.blend"] = optimize_sedan(ART_SOURCE / "DS_Sedan_A_closed_shell.blend", "DS_Sedan_A_closed_shell")
    report["DS_Sedan_A_reviewed.blend"] = optimize_sedan(ART_SOURCE / "DS_Sedan_A_reviewed.blend", None)
    report["DS_Sedan_A_v1.blend"] = optimize_sedan(ART_SOURCE / "DS_Sedan_A_v1.blend", None)
    
    # 2. Props & Environment
    report["DS_TrafficCone.blend"] = optimize_traffic_cone(ART_SOURCE / "DS_TrafficCone.blend")
    report["DS_SpeedBumps.blend"] = optimize_speed_bumps(ART_SOURCE / "DS_SpeedBumps.blend")
    report["DS_RoadKit_v1.blend"] = optimize_generic(ART_SOURCE / "DS_RoadKit_v1.blend", is_roadkit=True)
    report["DS_Autodrome.blend"] = optimize_generic(ART_SOURCE / "DS_Autodrome.blend", is_autodrome=True)
    report["DS_District.blend"] = optimize_generic(ART_SOURCE / "DS_District.blend", is_district=True)
    
    # 3. Pedestrians
    for p in ["DS_Pedestrian_A.blend", "DS_Pedestrian_B.blend", "DS_Pedestrian_C.blend"]:
        p_path = ART_SOURCE / "Pedestrians" / p
        if p_path.exists():
            report[p] = optimize_pedestrian(p_path)
            
    # Summary JSON
    summary = []
    total_before = 0
    total_after = 0
    for name, (b_tris, a_tris, b_v, a_v) in report.items():
        total_before += b_tris
        total_after += a_tris
        saved = b_tris - a_tris
        pct = round(saved / b_tris * 100, 1) if b_tris > 0 else 0
        summary.append({
            "model": name,
            "before_triangles": b_tris,
            "after_triangles": a_tris,
            "saved_triangles": saved,
            "reduction_percent": pct,
            "before_vertices": b_v,
            "after_vertices": a_v
        })
        
    out_json = REPORTS / "optimization_summary.json"
    with open(out_json, "w", encoding="utf-8") as f:
        json.dump({
            "total_before_triangles": total_before,
            "total_after_triangles": total_after,
            "total_saved_triangles": total_before - total_after,
            "total_reduction_percent": round((total_before - total_after) / total_before * 100, 1),
            "models": summary
        }, f, indent=2)
        
    print(f"\nOptimization complete! Total: {total_before} -> {total_after} tris (-{round((total_before - total_after) / total_before * 100, 1)}%)")

if __name__ == "__main__":
    main()
