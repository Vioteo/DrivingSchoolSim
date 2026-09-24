import bpy
import json

root = bpy.data.objects.get('DS_Sedan_A')
print("=== ROOT OBJECT ===")
if root:
    print(f"Name: {root.name}")
    print(f"Location: {list(root.location)}")
    print(f"Rotation: {list(root.rotation_euler)}")
    print(f"Scale: {list(root.scale)}")
else:
    print("DS_Sedan_A not found!")

print(f"\nTotal objects in blend: {len(bpy.data.objects)}")

# Check required pivots and critical objects
required_keywords = [
    'Wheel_FL', 'Wheel_FR', 'Wheel_RL', 'Wheel_RR',
    'SteeringWheel_Pivot', 'SteeringColumn', 'Steering_Rim',
    'Pedal_Clutch', 'Pedal_Brake', 'Pedal_Throttle',
    'Needle_Speed', 'Needle_RPM',
    'Wiper_Pivot_-0.44', 'Wiper_Pivot_0.31',
    'MirrorSurface_L', 'MirrorSurface_R', 'MirrorSurface_Centre',
    'Socket_DriverEye', 'Socket_CentreOfMass',
    'GearLever_Pivot', 'Handbrake_Pivot',
    'Transmission_Manual', 'Transmission_Automatic'
]

print("\n=== REQUIRED PIVOTS & KEY OBJECTS ===")
for name in required_keywords:
    obj = bpy.data.objects.get(name)
    if obj:
        parent = obj.parent.name if obj.parent else "None"
        print(f"OBJECT: {obj.name:25} | Loc: {[round(v, 4) for v in obj.location]} | Rot: {[round(v, 4) for v in obj.rotation_euler]} | Scale: {[round(v, 4) for v in obj.scale]} | Parent: {parent}")
    else:
        print(f"MISSING: {name}")

# Check materials
print("\n=== MATERIALS USED ===")
materials = {m.name for m in bpy.data.materials}
print(f"Total materials: {len(materials)}")
for m in sorted(materials):
    print(f"Material: {m}")

# Check dimensions of all evaluated geometry
print("\n=== BOUNDS & DIMENSIONS ===")
deps = bpy.context.evaluated_depsgraph_get()
points = []
for obj in bpy.data.objects:
    if obj.type in {'MESH', 'FONT', 'CURVE'}:
        ev = obj.evaluated_get(deps)
        me = ev.to_mesh()
        if me:
            points.extend([list(ev.matrix_world @ v.co) for v in me.vertices])
        ev.to_mesh_clear()

print("\n=== LIGHTS & LAMPS ===")
for o in bpy.data.objects:
    if any(k in o.name.lower() for k in ['lamp', 'light', 'drl', 'indicator', 'beam', 'projector']):
        mats = [m.name for m in o.data.materials] if hasattr(o.data, 'materials') else []
        print(f"LIGHT OBJ: {o.name:25} | Type: {o.type:6} | Materials: {mats}")

