import bpy

print(f"=== SCENE: {bpy.data.filepath} ===")
print(f"Total objects: {len(bpy.data.objects)}")
root = bpy.context.scene.objects[0] if bpy.context.scene.objects else None
print(f"Root/First obj: {root.name if root else 'None'}")

for obj in bpy.data.objects:
    if obj.parent is None:
        print(f"Top-level: {obj.name:30} | Loc: {[round(v,2) for v in obj.location]} | Scale: {[round(v,2) for v in obj.scale]}")

print("\n--- Exercise bounds / key items ---")
for obj in bpy.data.objects:
    if any(k in obj.name.lower() for k in ['exercise', 'hill', 'slalom', 'parallel', 'parking', 'turn', 'intersection', 'terrain']):
        print(f"Obj: {obj.name:30} | Type: {obj.type:6} | Loc: {[round(v,3) for v in obj.location]} | Scale: {[round(v,3) for v in obj.scale]}")
