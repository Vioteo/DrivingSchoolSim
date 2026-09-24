import bpy
import os
import sys

blend_files = [
    "ArtSource/DS_Sedan_A.blend",
    "ArtSource/DS_Sedan_A_closed_shell.blend",
    "ArtSource/DS_District.blend",
    "ArtSource/DS_Autodrome.blend"
]

root = r"c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim"

print("=" * 80)
print("FORENSIC 3D ASSET AUDIT VIA BLENDER HEADLESS ENGINE")
print("=" * 80)

for rel in blend_files:
    full_path = os.path.join(root, rel)
    print(f"\nAUDITING: {rel}")
    bpy.ops.wm.open_mainfile(filepath=full_path)
    
    total_objects = len(bpy.data.objects)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    total_verts = sum(len(m.data.vertices) for m in meshes)
    total_polys = sum(len(m.data.polygons) for m in meshes)
    materials = list(bpy.data.materials)
    
    print(f"  Total Objects: {total_objects}")
    print(f"  Mesh Objects: {len(meshes)}")
    print(f"  Total Vertices: {total_verts}")
    print(f"  Total Polygons: {total_polys}")
    print(f"  Materials ({len(materials)}): {[m.name for m in materials[:8]]}")
    
    # Check scale
    scales = [(o.name, tuple(round(x, 4) for x in o.scale)) for o in meshes if any(abs(s - 1.0) > 0.001 for s in o.scale)]
    if scales:
        print(f"  Unbaked Scales ({len(scales)}): {scales[:5]}")
    else:
        print("  Scale check: ALL MESH TRANSFORMS BAKED (1.0, 1.0, 1.0)")
        
    # Check specific required pivots if sedan
    if "Sedan" in rel:
        key_pivots = ["SteeringWheel_Pivot", "Pedal_Throttle", "Pedal_Brake", "Pedal_Clutch", "Wheel_FL", "Wheel_FR"]
        found = [p for p in key_pivots if p in bpy.data.objects]
        print(f"  Key Kinematic Pivots found: {len(found)}/{len(key_pivots)} -> {found}")

print("\n" + "=" * 80)
print("BLENDER 3D AUDIT COMPLETE")
print("=" * 80)
