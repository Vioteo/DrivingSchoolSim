# Sedan shell repair — 2026-09-19

Source: `ArtSource/DS_Sedan_A_reviewed.blend`. Saved separately as `ArtSource/DS_Sedan_A_closed_shell.blend`; the reviewed source was not overwritten. Reproduction: run `tools/repair_shell.py` in background Blender with the reviewed source loaded. Add `-- --diagnose` for the before diagnosis/renders; omit it for repair, save and after renders. This is a targeted shell repair, not a production-quality vehicle rebuild.

## Diagnosis and changes

The front bumper ended at Z 0.620 m while the bonnet edge was approximately Z 0.77–0.807 m. Lamps covered the outer portions, leaving the cabin visible through the centre. The original cabin floor was only 1.52 × 2.43 m; the side, engine-bay and rear underside regions had no closing surface.

- Added `Shell_FrontUpperFacia` and `Shell_RearUpperFacia`, closed painted meshes following the existing bonnet/boot end profiles. They overlap the bumper vertically and meet the existing upper panels.
- Added `Shell_Undertray`, a 35 mm thick closed panel from Y −2.25 to +2.22 m, Z 0.250–0.285 m, following the existing lower side profile. Four notches preserve the wheel openings.
- Added `Shell_Wheelhouse_L_Front`, `Shell_Wheelhouse_L_Rear`, `Shell_Wheelhouse_R_Front`, `Shell_Wheelhouse_R_Rear`: closed inboard walls, at absolute X 0.633–0.650 m, meeting the existing curved liners. No wall was placed across an outer wheel opening.
- Corrected the two right-hand arch-liner normal directions and added 8 mm outward sheet thickness to all four `WheelArchLiner*` objects.
- Relieved the existing `Cabin_Floor` corners around the wheels while retaining its object, hierarchy, transform and floor height (Z 0.285–0.325 m).
- Added the shaped `Shell_Firewall`, Y 1.025–1.045 m, ahead of the pedals, with raised lower corners around the wheelhouses.

New meshes have recalculated outward normals. No glass, pedal, wheel, body side or seat geometry was removed. Backface culling was not changed. FBX/GLB export is outside this repair script.

## Visual self-review

Genuine Blender Cycles CPU renders, 640 × 480, 16 samples, matching camera positions and lighting. Only the studio ground was excluded so it could not obscure the underside; no car component was hidden for the comparison. Temporary inspection lights/cameras were created after saving and are not part of the output blend.

Evidence directory: `artifacts/visual-review/shell-repair/`.

- **PASS — front-low:** `before-front-low.png` shows the open strip below the bonnet; `after-front-low.png` shows a continuous painted front panel with lamps and bumper retained.
- **PASS — underside:** `before-underside.png` shows exposed floor edges and broad openings; `after-underside.png` shows an opaque continuous lower panel with wheel cutouts.
- **PASS — front3quarter:** `before-front3quarter.png` / `after-front3quarter.png` confirm closure from an oblique angle and retention of the wheel apertures.
- **PASS — cockpit-floor:** `after-cockpit-floor.png` shows all three pedals above the floor and the bulkhead behind them; no new panel blocks the pedal faces.

All seven images were opened and visually reviewed. Mesh diagnosis reports zero boundary edges and zero non-manifold edges on all eight added meshes and the modified cabin floor. See `diagnosis-before.json` and `repair-geometry.json` for object-level bounds, topology and modifier records.

## Camera definitions

Coordinates are Blender world metres; each entry is position → target, lens in mm:

- front-low: `(0, 5.3, 0.39)` → `(0, 1.45, 0.55)`, 52.
- underside: `(-3.1, 4.5, -3.2)` → `(0, 0, 0.35)`, 48.
- front3quarter: `(-4.7, 6.1, 2.45)` → `(0, 0.15, 0.75)`, 52.
- cockpit-floor: `(-0.4, -0.12, 0.83)` → `(-0.4, 0.78, 0.38)`, 25.

## Limits and next validation

The undertray is a simple closing panel, without suspension, exhaust or underbody stamping detail. The existing broad body reflections, pillar junctions and overall vehicle styling remain unchanged. Individual new panels are closed volumes; the complete car remains a collection of overlapping component meshes rather than one unified watertight mesh. Wheel clearance was arranged for the current straight-ahead pose; full steering and suspension travel require separate testing. Door, window and animated pedal clearance were not exhaustively tested.

Recommended independent checks: ray-test former front/underside openings; confirm export includes all eight `Shell_*` meshes; compare wheel and pedal pivots with the reviewed source; verify front/rear wheel clearance through the intended animation range; inspect reimported FBX/GLB for winding, material and triangulation differences.
