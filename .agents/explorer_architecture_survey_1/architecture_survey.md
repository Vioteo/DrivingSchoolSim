# Driving School Simulator (DrivingSchoolSim) — Phase 1 Architecture Survey & Risk Strategy

**Document Status**: Final Architecture Survey  
**Author**: Architecture and Risk Explorer (`explorer_architecture_survey_1`)  
**Target Environment**: Unity 6000.3.10f1 URP, Blender 5.0+, C# (.NET Standard 2.1 / Unity 6), Three.js (WebGL), HTML5/CSS3/Vanilla JS  
**Reference Specification**: `.agents/ORIGINAL_REQUEST.md` (Requirements R1–R5)  
**Date**: 2026-09-19  

---

## 1. Executive Summary & Architectural Scope

The Driving School Simulator (`DrivingSchoolSim`) Phase 1 project establishes the core technical, spatial, visual, and architectural foundation for a professional, highly realistic driving training platform. The system combines an original non-branded training sedan with an articulated cabin, a modular procedural road network, a 10×10 km metropolitan masterplan, a 500×500 m showcase district, a fully parametric autodrome (8 standard regulatory exercises), a responsive 9-screen interactive HTML5/WebGL showcase prototype, and a clean, decoupled Unity 6000.3 URP architecture based on pure C# Plain Old CLR Object (POCO) domain models and explicit engine adapters.

### 1.1 Core Architectural Principles
1. **Strict Core / Engine Decoupling**: Core business and simulation logic (`Contracts`, `Simulation`, `Rules`, `Learning`) consists exclusively of pure C# POCOs and interfaces compiled into assembly definitions with `noEngineReferences: true`. No `UnityEngine` types (`Vector3`, `Transform`, `MonoBehaviour`, `Time`) leak into domain contracts.
2. **Deterministic Double-Precision World**: Canonical spatial truth uses 64-bit floating point (`double`) in SI units (meters, radians, m/s). Unity's 32-bit `float` representation is strictly an ephemeral rendering/collider projection handled across a Floating Origin boundary.
3. **Single Source of Road Truth**: The road network is authored and stored as an immutable compiled graph (`WorldDocument` / `RoadGraph`). Visual meshes, physics colliders, AI navigation paths, and traffic rule monitoring volumes are derived artifacts regenerated from graph revisions, preventing desynchronization.
4. **Hardware Agnostic / Safety First**: Input arbitration cleanly decouples physical hardware (Logitech G27 DirectInput/RawInput) from logical command ingestion (`DriverCommand`). Force Feedback (FFB) incorporates an absolute safety clamp, rate limiter, and idempotent lifecycle teardown on pause, window focus loss, or disconnect.
5. **Auditable Evidence & Immutable Provenance**: Every asset, script, render, and test report is indexed by cryptographic hash (SHA-256) in an automated manifest. Acceptance gates enforce reproducible CLI verification commands.

---

## 2. R1: 3D Asset & Pipeline Architecture

### 2.1 Vehicle Geometry Architecture & Decomposition
The training sedan (`DS_Sedan_A` / `DS-01`) is an original, realistic, non-branded vehicle designed for professional driver education. Its exterior and interior dimensions strictly reflect a modern European C/D-segment sedan:
* **Wheelbase ($L$)**: 2.72 m
* **Track Width ($W_{track}$)**: 1.71 m
* **Overall Length ($L_{total}$)**: 4.50 m
* **Body Width ($W_{body}$)**: 1.80 m (excluding mirrors); **Total Width**: 2.25 m (mirror-to-mirror)
* **Height ($H$)**: 1.48 m
* **Curb Mass**: 1,350 kg (nominal design target)

```
[DS_Sedan_A (Root Empty / Transform)]
  ├── Body_Shell (Main monocoque body, painted, front/rear fascias)
  ├── Underside_Floorpan (Watertight sealed underbody floorpan)
  ├── Glass_Windshield (Front laminated glass)
  ├── Glass_Rear (Heated rear window with defroster lines)
  ├── Glass_Side_L / Glass_Side_R (Side door glazing with rubber seals)
  ├── Wheel_FL_Pivot (X: -0.855, Y: 1.36, Z: 0.327)
  │     └── Wheel_FL_Mesh (Tire 205/55 R16, rim, brake disc)
  ├── Wheel_FR_Pivot (X: 0.855, Y: 1.36, Z: 0.327)
  │     └── Wheel_FR_Mesh
  ├── Wheel_RL_Pivot (X: -0.855, Y: -1.36, Z: 0.327)
  │     └── Wheel_RL_Mesh (Brake caliper/disc)
  ├── Wheel_RR_Pivot (X: 0.855, Y: -1.36, Z: 0.327)
  │     └── Wheel_RR_Mesh
  ├── Cabin_Interior
  │     ├── Dashboard_Main (Instrument cowl, upper dash, glove compartment)
  │     ├── SteeringColumn_Mount
  │     │     └── SteeringWheel_Pivot (Local Z/forward or angled steering shaft)
  │     │           ├── SteeringWheel_Rim (Leather, 3-spoke, center horn cap)
  │     │           └── Stalk_TurnIndicators (Left multifunction stalk)
  │     │           └── Stalk_Wipers (Right wiper stalk)
  │     ├── Cluster_Gauges
  │     │     ├── Cluster_Dial_Speedometer
  │     │     │     └── Needle_Speedometer_Pivot (Local rotation axis: Z/forward)
  │     │     ├── Cluster_Dial_Tachometer
  │     │     │     └── Needle_Tachometer_Pivot (Local rotation axis: Z/forward)
  │     │     └── Cluster_Display_Odometer
  │     ├── Center_Console
  │     │     ├── Climate_Controls (Dials, ventilation louvers)
  │     │     ├── Infotainment_Screen ("DRIVE SCHOOL - READY TO LEARN")
  │     │     ├── Transmission_Manual
  │     │     │     └── GearLever_Pivot (Ball pivot for 6-speed H-pattern)
  │     │     │           └── GearStick + Knob + ShiftBoot
  │     │     ├── Transmission_Automatic (Optional selectable sub-tree: P-R-N-D gate)
  │     │     └── Handbrake_Pivot (Mechanical handbrake lever)
  │     ├── Pedal_Assembly
  │     │     ├── Pedal_Clutch_Pivot (X: -0.57, Y: 0.88, Z: 0.52; top-hinged swing)
  │     │     │     └── Pedal_Clutch_Pad
  │     │     ├── Pedal_Brake_Pivot (X: -0.40, Y: 0.88, Z: 0.52; top-hinged swing)
  │     │     │     └── Pedal_Brake_Pad
  │     │     └── Pedal_Throttle_Pivot (X: -0.23, Y: 0.88, Z: 0.52; floor-hinged/suspended)
  │     │           └── Pedal_Throttle_Pad
  │     ├── Seat_Driver (Adjustable fore/aft, backrest rake, headrest)
  │     ├── Seat_Passenger (Instructor seat)
  │     └── Seat_Rear_Bench
  ├── Exterior_Equipment
  │     ├── Wiper_Pivot_L (Left windshield wiper pivot)
  │     ├── Wiper_Pivot_R (Right windshield wiper pivot)
  │     ├── Mirror_Housing_L (Left side mirror body)
  │     │     └── MirrorSurface_L (Planar reflective surface, normal: inward/rear)
  │     ├── Mirror_Housing_R (Right side mirror body)
  │     │     └── MirrorSurface_R (Planar reflective surface, normal: inward/rear)
  │     └── CentreMirror_Housing
  │           └── MirrorSurface_Centre (Interior rearview mirror surface)
  ├── Lighting_Rig
  │     ├── Light_Headlight_LowBeam_L / R
  │     ├── Light_Headlight_HighBeam_L / R
  │     ├── Light_TurnIndicator_FL / FR / RL / RR
  │     ├── Light_TailBrake_L / R
  │     └── Light_Reverse_L / R
  └── Technical_Sockets
        ├── Socket_DriverEye (Nominal driver eye position: X: -0.38, Y: -0.19, Z: 1.28)
        ├── Socket_InstructorEye (Nominal instructor eye position: X: 0.38, Y: -0.19, Z: 1.28)
        └── Socket_CentreOfMass (Center of Mass: X: 0.0, Y: -0.10, Z: 0.51)
```

### 2.2 Material System & PBR Consistency
To ensure consistent visual rendering across Blender Cycles/EEVEE, Unity URP, and Three.js WebGL:
* **Material Palette**: Standardized PBR material definitions (`palette.json`) using Metallic-Roughness workflow.
  - `Paint_Atlantic`: Metallic 0.70, Roughness 0.25, Deep clearcoat blue (`#163B4A`).
  - `Rubber`: Metallic 0.0, Roughness 0.82, Matte tire/seal black (`#050607`).
  - `Interior_Graphite`: Metallic 0.0, Roughness 0.70, Neutral graphite dashboard (`#0A0C0D`).
  - `Satin_Aluminium`: Metallic 0.85, Roughness 0.28, Machined accents (`#667578`).
  - `Glass`: Alpha 0.16, Roughness 0.12, Index of Refraction (IOR) 1.52.
  - `Mirror`: Metallic 1.0, Roughness 0.035, Highly specular chrome/silver.
  - `Lamp_White` / `Lamp_Red` / `Lamp_Amber`: Emission strength 2.0–3.0 with corresponding coloration for active signals.
* **Blender → FBX → Unity Import Mapping**:
  - Coordinate convention: Blender ($+X$ right, $+Y$ forward, $+Z$ up) converted during FBX export to Unity ($+X$ right, $+Y$ up, $+Z$ forward) with `axis_forward='Y', axis_up='Z', apply_unit_scale=True`.
  - Transform baking: Root transform has scale 1.0. All child articulators retain non-zero translation relative to parent, with rotation zeroed in resting position.

### 2.3 Mirror Reflection Cameras & URP Render Targets
Driver education requires functionally accurate rear vision through 3 mirrors: Left Wing Mirror, Right Wing Mirror, and Interior Center Mirror.
* **Optical Geometry**: A mirror surface is a planar reflector defined by point $\mathbf{P}_0$ and outward normal $\mathbf{n}$. Given driver eye position $\mathbf{E}$:
  $$\mathbf{E}_{virtual} = \mathbf{E} - 2 ((\mathbf{E} - \mathbf{P}_0) \cdot \mathbf{n}) \mathbf{n}$$
  The reflection camera is placed at $\mathbf{E}_{virtual}$ looking at $\mathbf{P}_0$, with an oblique near clipping plane matching the mirror surface plane to prevent rendering geometry between the camera and mirror surface.
* **Unity URP Implementation**:
  - `PlanarMirror.cs` utilizes `RenderTexture` (512×256 pixels, 16-bit depth).
  - ScriptableRenderContext hook: `RenderPipelineManager.beginCameraRendering`.
  - Oblique near-plane calculation: `sourceCamera.CalculateObliqueMatrix(clipPlane)`.
  - Layer culling mask: Excludes vehicle interior shell and the mirror mesh itself (`~LayerMask.GetMask("MirrorSurfaces")`) to prevent infinite recursion and clipping artifacts.
  - **Performance & XR Adaptation**: Rendering 3 separate reflection cameras incurs 3 additional scene render passes (4 passes total per frame). In desktop mode, frame rate is maintained by updating wing mirrors at 30 Hz or alternating frames (time-slicing). In OpenXR stereo mode, planar mirrors require independent left-eye and right-eye reflections (6 render passes), which can bottleneck the GPU; hence, a configurable fallback (Screen Space Planar Reflection or dynamic cubemap with parallax correction) is architected for lower-tier VR hardware.

### 2.4 Interactive 3D Web Viewer (Three.js / WebGL)
The browser showcase (`artifacts/visual-review/viewer.html` and `shell-repair/index.html`) implements a zero-install interactive inspection tool:
* **Engine**: Three.js r160+ (ES Module via importmap), `GLTFLoader`, `OrbitControls`.
* **Rendering Features**: ACESFilmic tone mapping, sRGB color space, soft hemisphere and directional studio lights, ground shadow grid helper.
* **Kinematic Articulation**:
  - Exposes interactive sliders for Wheel Steer ($\pm 32^\circ$), Steering Wheel ($\pm 450^\circ$), Pedals ($0–100\%$, $0–20^\circ$ swing), Speedometer Needle ($0–260^\circ$), and Wiper sweep ($0–70^\circ$).
  - Explicit UI watermark: *"Visual Kinematic Verification Stand — Vehicle Dynamics Solved Separately in Unity Engine"*.
  - Wireframe inspection toggle (`o.material.wireframe = true`) and GLB direct asset download link.

---

## 3. R2: Spatial, Road Network & Environment Architecture

### 3.1 Seamless Road Mesh Generation
Procedural road generation without cracks, seams, normal breaks, or Z-fighting is critical for physics stability and visual realism.

```
+-------------------------------------------------------------------------+
|                         ROAD NETWORK ARCHITECTURE                       |
+-------------------------------------------------------------------------+
|                                                                         |
|  [RoadNode] (Double x, y, z; Tangent vectors; Intersection junction ID) |
|      |                                                                  |
|      v                                                                  |
|  [RoadSegment] (FromNode -> ToNode; Width; SpeedLimit; Profile Type)    |
|      |                                                                  |
|      +---> Cross-Section Profile (Pavement, Curbs, Sidewalks, Verges)  |
|      |                                                                  |
|      +---> Spline Generator (Hermite / Cubic Catmull-Rom in double)     |
|      |                                                                  |
|      v                                                                  |
|  [Tessellation Engine] (Adaptive longitudinal step ds based on kappa)   |
|      |                                                                  |
|      +---> Mesh Extrusion (Vertices, UVs, Bi-tangents, Smooth Normals)  |
|      |                                                                  |
|      +---> Junction Meshing (Trimmed boundary polygon, Cap triangulation|
|      |     without overlapping road segment ends)                       |
|      |                                                                  |
|      v                                                                  |
|  [Physics & Visual Outputs] (RoadSurface Collider, Lane Centerlines)    |
+-------------------------------------------------------------------------+
```

* **Mathematical Profile Definition**:
  A cross-section $S(u)$ is defined as a series of lateral offsets $u \in [-W/2, +W/2]$ with height offsets $h(u)$, material IDs, and UV coordinates:
  - Driving Lanes: $u \in [-W_{road}/2, +W_{road}/2]$, $h = 0$, slope $-1.5\%$ (crown for drainage).
  - Curbs: $u \in [\pm W_{road}/2, \pm (W_{road}/2 + 0.15\text{m})]$, vertical rise $+0.15\text{m}$, beveled top.
  - Sidewalk: $u \in [\pm (W_{road}/2 + 0.15\text{m}), \pm (W_{road}/2 + 2.50\text{m})]$, $h = +0.15\text{m}$.
* **Junction Transition & Anti-Seam Rules**:
  1. Road segments terminate strictly at the convex hull boundary of the intersection node (e.g., $d = W_{intersect}/2$ from center).
  2. The intersection is tessellated as an independent planar polygon mesh (`IntersectionSurface`) sharing the exact boundary edge vertices of incoming segments.
  3. Vertex snapping: Boundary vertices are clamped to identical double-precision coordinates before converting to mesh floats, eliminating floating-point gaps.
  4. Marking geometry: Road paint lines are extruded with a strict vertical offset $+0.005$ m to $+0.010$ m above asphalt to guarantee no Z-fighting across all view distances.

### 3.2 Graph Representation & Canonical Data Structure
The road network is represented as an immutable directed topological multigraph:
* **Nodes (`RoadNode`)**: Unique string ID, 64-bit coordinate $(x, y, z)$.
* **Segments (`RoadSegment`)**: Connection from `fromNode` to `toNode`, total road width, speed limit, and lane count.
* **Lanes (`Lane`)**: Individual driving lanes inside a segment. Properties: `index` (0-indexed from rightmost curb), `widthM`, `fromNode`, `toNode`, and explicit `successors` array linking to lane IDs in continuing or intersecting segments.
* **Attachments**: Stop lines, zebra crossings, traffic signal groups, speed limit zones, and parking stalls maintain explicit foreign keys referencing target `Lane` IDs and longitudinal station offset $s \in [0, \text{length}]$.

### 3.3 10×10 km Metropolitan Masterplan
The masterplan establishes the macro-spatial zoning and highway topology of the driving simulation region:
* **Spatial Dimensions**: 10,000 m × 10,000 m (100 km²), origin at center $(0, 0)$ with coordinates spanning $[-5000, +5000]$ m along X and Z.
* **Zoning Hierarchy**:
  1. **Historical & Business Downtown (Center)**: Dense grid, one-way streets, pedestrian zones, signalized multi-lane avenues, speed limit 40–50 km/h.
  2. **Residential Districts (East / North-East)**: Calm neighborhoods, 2-lane streets, courtyard driveways, uncontrolled T-junctions, speed limit 20–30 km/h.
  3. **Industrial & Logistics Zone (South-East)**: Wide radius turns, heavy vehicle docks, loading zones, unpaved shoulders, speed limit 60 km/h.
  4. **Suburban Outskirts (South / South-West)**: Low-density housing, rural roads, ditch verges, speed limit 70–90 km/h.
  5. **Beltway / Expressway (Ring & North Arterial)**: 4-to-6 lane divided highway, grade-separated cloverleaf and diamond interchanges, slip roads, speed limit 110 km/h.
  6. **Educational Autodrome Complex (West)**: Dedicated closed training ground (220×180 m) with full regulatory exercise modules.
* **Closed Educational Routes**: The masterplan embeds 4 closed continuous driving loops spanning diverse traffic complexities (e.g., Downtown Examination Loop: 4.8 km; Outskirt Rural Loop: 8.2 km; Mixed Traffic Circuit: 12.5 km).

### 3.4 500×500 m Showcase District Composition
The detailed demo block (`DS_District_500m`, centered at origin) serves as the primary visual, navigational, and performance validation scene:
* **Infrastructure Composition**:
  - Major 4-way signalized intersection with dedicated left-turn lanes and pedestrian zebra crossings (`Crosswalk`).
  - Standard street lighting network: Poles at 40 m intervals, 7 m height, luminaire arm $1.5$ m, warm light spectrum (3000 K).
  - Active traffic signals (`TrafficPole` / `SignalHousing`) with three-aspect vertical signals (Red, Amber, Green).
  - Open surface parking lot (10 stalls, $2.5\text{m} \times 5.0\text{m}$ each, with standard concrete wheel stops).
  - Courtyard residential access lane ($3.5$ m single shared driveway with traffic calmed entrance).
  - Transit bus stop shelter ($5.0\text{m} \times 2.1\text{m}$ with steel posts, glass windbreaks, bench, and waste bin).
* **Architecture & Scenery**:
  - 16 modular building masses representing 3 distinct styles: Residential Plaster (3–5 floors), Commercial Retail Brick (2–4 floors), Modern Dark Facade (5–6 floors).
  - 45+ procedural deciduous trees (`Tree_Trunk` and `Tree_Canopy` icosperes) and shrubbery placed along pedestrian boulevards.
  - Day/Night lighting setup: Cycles/URP real-time directional sun light paired with night street luminaire emission cones ($350$ lumens, $120^\circ$ spot).

### 3.5 Parametric Autodrome Layout & Regulatory Formulas
The autodrome layout is strictly parameterized around the geometric envelope and steering kinematics of the training sedan (`DS_Sedan_A`):
* **Vehicle Kinematic Parameters**:
  - Wheelbase $L = 2.72$ m
  - Track Width $W_{track} = 1.71$ m, Body Width $W = 1.80$ m, Length $L_{car} = 4.50$ m
  - Maximum steering wheel lock: $450^\circ$ (ratio 14.0:1 $\implies \delta_{max} \approx 32.1^\circ$)
  - Minimum turning radius (outer wheel centerline):
    $$R_{min} = \frac{L}{\sin(\delta_{max})} = \frac{2.72}{\sin(32.1^\circ)} \approx 5.12\text{ m}$$
    $$R_{outer\_body} = \sqrt{(R_{min} + W_{track}/2)^2 + L_{front\_overhang}^2} \approx 5.60\text{ m}$$

* **Formulas for the 8 Standard Exercises**:

| ID | Exercise Name | Geometric Specification & Parametric Formulas | Regulatory Clearance Envelope |
|---|---|---|---|
| **01** | **Start & Stop / Straight Line** | Length $S \ge 30$ m; lane width $W_{lane} = W + 1.0\text{m} = 2.80$ m. Starting box and terminal stop line within $\pm 0.30$ m tolerance. | $22\text{m} \times 38\text{m}$ overall zone |
| **02** | **Slalom ("Змейка")** | Series of 5 cones along centerline. Distance between cones: $D_{cone} = 2.5 \cdot L_{car} \approx 11.25$ m (standard: 11.0 m). Corridor width $W_{corridor} = 2 \cdot W = 3.60$ m. | $16\text{m} \times 48\text{m}$ overall zone |
| **03** | **Reverse Box Stall ("Въезд в бокс задним ходом")** | Stall depth $D_{stall} = L_{car} + 1.0\text{m} = 5.50$ m; stall width $W_{stall} = W + 1.0\text{m} = 2.80$ m. Access lane width $W_{lane} = L_{car} + 1.5\text{m} = 6.00$ m. $90^\circ$ backward entry. | $28\text{m} \times 36\text{m}$ overall zone |
| **04** | **Parallel Parking ("Параллельная парковка")** | Bay length $L_{bay} = 1.5 \cdot L_{car} + 0.5\text{m} \approx 7.25$ m; bay width $W_{bay} = W + 0.8\text{m} = 2.60$ m. Drive-by lane width $W_{lane} = 3.50$ m. | $27\text{m} \times 30\text{m}$ overall zone |
| **05** | **Hill Start Ramp ("Эстакада / Горка")** | Ramp slope $\alpha = 10\%$ (approx. $5.71^\circ$). Ascent length $12.0$ m; flat top plateau $8.0$ m (elevation $+1.20$ m); descent length $12.0$ m. Width $6.0$ m (two lanes with curb barriers). Intermediate stop bar on slope. | $16\text{m} \times 44\text{m}$ overall zone |
| **06** | **Limited Space U-Turn ("Разворот в ограниченном пространстве")** | Enclosed corridor width $W_{corridor} = 2 \cdot L_{car} = 9.00$ m; corridor length $L_{corridor} = 3 \cdot L_{car} = 13.50$ m. Requires single reverse maneuver without crossing boundary markers. | $28\text{m} \times 28\text{m}$ overall zone |
| **07** | **$90^\circ$ Turns ("Повороты на 90 градусов")** | S-shaped chicane with two consecutive $90^\circ$ turns (left then right). Corridor width $W_{corridor} = W + 1.5\text{m} = 3.30$ m. Corner inner radius $r_{in} = 3.0$ m, outer radius $r_{out} = 6.5$ m. | $18\text{m} \times 38\text{m}$ overall zone |
| **08** | **Gear Shift & Emergency Braking** | Acceleration strip $50.0$ m (gear 1 $\to$ 2 shift); deceleration zone $20.0$ m; target stopping line with ABS verification. Lane width $3.0$ m. | $16\text{m} \times 50\text{m}$ overall zone |

---

## 4. R3: UI/UX & Showcase Viewer Architecture

### 4.1 Design System & Component Tokens
The UI follows a functional, distraction-free Scandinavian aesthetic engineered for dark driving cockpit environments:
* **Color Tokens**:
  - `Surface Base`: `#14191E` (Dark graphite matte)
  - `Surface Elevated / Cards`: `#202A32` (Semi-transparent `#14212BEE` in overlays)
  - `Border / Dividers`: `#36434D` (Subtle boundary line)
  - `Text Primary`: `#F0F4F7` (Crisp high-contrast off-white)
  - `Text Muted / Eyebrows`: `#A4B2BD` (Neutral slate gray)
  - `Accent Blue Primary`: `#8EBFE1` / `#8DBDE0` (Cool instructional blue)
  - `Accent Blue Dark`: `#112738` (Text on blue button)
  - `Warning Amber`: `#DFBF8A` / `#E9BD75` (Cautionary driving remark)
  - `Error / Disconnected`: `#E06D6D` (Violation or missing hardware)
  - `Success / Pass`: `#A6D2B8` (Passed exercise or correct rule answer)
* **Interactive Component States**:
  - `Hover`: Background lightens by 8%, border shifts toward accent blue.
  - `Active / Pressed`: Inset scale transform $0.98$, background darkens.
  - `Focus-Visible`: 3px solid `#8EBFE1`, outline-offset 4px (accessible via keyboard navigation / game controller).
  - `Disabled`: Opacity $0.40$, cursor `not-allowed`, event absorption.
  - `Missing Device`: Highlighted badge with warning banner ("Device not detected — Fallback enabled").
  - `Unsaved Changes`: Yellow status indicator dot ("● Unsaved changes in progress").

### 4.2 Interactive 9-Screen HTML5/CSS/JS Prototype Architecture
The prototype is implemented in vanilla ECMAScript 2022 and CSS Grid/Flexbox without heavy framework dependencies, ensuring immediate local execution via any HTTP server or `file://`:

```
+--------------------------------------------------------------------------+
|                     INTERACTIVE UI PROTOTYPE (9 SCREENS)                 |
+--------------------------------------------------------------------------+
|                                                                          |
|  [01: Home / Course Progression] ───+───> [02: Lesson Selection & Brief] |
|                                     │        │                           |
|  [03: Vehicle & Environment Setup] <+        v                           |
|        │                              [05: Driving Cockpit & HUD]        |
|        v                                     │       ▲                   |
|  [04: G27 Calibration & Wizard]              v       │ (Resume)          |
|                                       [06: Pause & Settings]             |
|  [07: Theory Exam Block (PDD)]               │                           |
|                                              v                           |
|  [09: World & City Map Editor]        [08: Trip Debrief & Telemetry]     |
|                                                                          |
|  [VR Overlay Preview (World-Space Panels: Pause & Exercise Selection)]   |
+--------------------------------------------------------------------------+
```

1. **Screen 01: Home & Course Progression (`#home`)**: Course progress hero, completion metrics (4/8 lessons), direct "Continue Practicing" CTA, card showing the next scheduled autodrome skill.
2. **Screen 02: Lesson Selection (`#lessons`)**: Master-detail catalog of all 8 lessons, syllabus details, estimated completion time, passing criteria, and preview render of the exercise ground.
3. **Screen 03: Vehicle & Condition Setup (`#vehicle`)**: Configuration of transmission (Manual 6-speed / Automatic), weather conditions (Clear, Rain, Fog), and time of day (Day, Night) with live render preview updates.
4. **Screen 04: Logitech G27 Calibration (`#calibration`)**: Interactive hardware wizard. Real-time SVG steering wheel rotating from $-450^\circ$ to $+450^\circ$, three independent pedal progress bars (Clutch, Brake, Throttle), calibration step guide, and profile save mechanism.
5. **Screen 05: Driving Cockpit HUD (`#drive`)**: Photorealistic cockpit backdrop with integrated minimalist HUD: digital/analog speed readout, gear indicator, speed limit badge, instruction banner from virtual tutor ("Mirrors $\to$ Turn Signal $\to$ Smooth Brake").
6. **Screen 06: Pause & Settings (`#pause`)**: Non-modal pause overlay, tutor voice volume slider, FOV slider ($50^\circ–100^\circ$), graphics quality preset, display mode toggle (Monitor / VR), and quick navigation to calibration or lesson catalog.
7. **Screen 07: Traffic Rules Theory Exam (`#theory`)**: Interactive Russian Traffic Regulations (ПДД) question module. Scalable vector diagram of an intersection, question text, 3 selectable answers, immediate validation feedback, timer, and detailed educational rationale.
8. **Screen 08: Trip Debrief & Telemetry (`#result`)**: Score card ($0–100$), completion time, chronological event timeline with color-coded markers (green for positive actions like mirror check; amber for penalties like late indicator signal), and actionable recommendations.
9. **Screen 09: World & City Map Editor (`#editor`)**: 10×10 km masterplan canvas with SVG panning, district overlays, interactive object placement palette (Road, Building, Sign, Vegetation), undo stack, and JSON export/download tool.

### 4.3 Viewport Responsiveness & VR Spatial Ergonomics
* **Responsive Scaling (1080p vs. 720p)**:
  - Base target: 1920×1080 (Full HD, 100% UI scale).
  - Constrained target: 1280×720 (HD). CSS media queries adjust padding, font clamps (`clamp(14px, 1.2vw, 18px)`), and switch split grids from two-column to single-column flex layouts to prevent element clipping or overlap.
* **VR Overlay Mode (`#vr`)**:
  - Ergonomic layout designed for a virtual curved canvas floating at a distance of $1.8$ m at driver eye level.
  - Generous button hit targets ($>64$ px height, high angular separation) suited for ray-casting or gaze dwell.
  - High-contrast visual cards with minimal peripheral clutter to eliminate motion sickness and eye strain.

### 4.4 Unified Showcase Viewer Architecture (`index.html`)
The root showcase viewer acts as the comprehensive verification portal for stakeholders and developers:
* Integrates links to the 9-screen prototype, 3D WebGL viewer, shell repair verification gallery, and full render gallery (24 high-resolution Cycles renders).
* Dynamic QA checklist loaded from `qa-results.json` displaying test statuses (`PASS` / `WARN` / `PENDING`) and explicit boundary disclaimers.

---

## 5. R4: Unity Core Architecture & Engineering Contracts

### 5.1 Assembly Definition Architecture & Dependency Graph
To enforce strict modular boundaries and maintain fast compilation, the codebase is partitioned into distinct assemblies (`.asmdef`):

```
+------------------------------------------------------------------------+
|                      ASSEMBLY DEFINITION GRAPH                         |
+------------------------------------------------------------------------+
|                                                                        |
|                     +----------------------+                           |
|                     |     DS.Contracts     | (Pure C# POCOs/Interfaces)|
|                     | (noEngineReferences) |                           |
|                     +----------+-----------+                           |
|                                |                                       |
|        +-----------------------+-----------------------+               |
|        |                       |                       |               |
|        v                       v                       v               |
|  +--------------+       +--------------+       +---------------+       |
|  |DS.Simulation |       |   DS.Rules   |       |  DS.Learning  |       |
|  |  (Pure C#)   |       |  (Pure C#)   |       |   (Pure C#)   |       |
|  +-------+------+       +-------+------+       +-------+-------+       |
|          |                      |                      |               |
|          |       +--------------+                      |               |
|          |       |                                     |               |
|          v       v                                     |               |
|    +-------------------+                               |               |
|    | DS.PhysicsAdapter | (Unity Rigidbody/WheelColl.)  |               |
|    +---------+---------+                               |               |
|              |                                         |               |
|              |      +----------------------------------+               |
|              |      |                                                  |
|              v      v                                                  |
|      +---------------------+       +----------------+                  |
|      |   DS.Presentation   |<------+    DS.Input    |                  |
|      |  (URP / MonoBeh)    |       | (InputSystem)  |                  |
|      +----------+----------+       +----------------+                  |
|                 |                                                      |
|                 v                                                      |
|          +--------------+       +--------------+                       |
|          |  DS.Editor   |       |   DS.Tests   |                       |
|          | (Build/Tools)|       | (NUnit Suite)|                       |
|          +--------------+       +--------------+                       |
|                                                                        |
+------------------------------------------------------------------------+
```

* **Pure Domain Layer (`noEngineReferences: true`)**:
  - `DS.Contracts`: Universal DTOs, structs, and ports. Zero references.
  - `DS.Simulation`: Powertrain physics, clutch friction, gearbox math, steering kinematics. Depends only on `DS.Contracts`.
  - `DS.Rules`: Traffic rule evaluator state machines (speed limits, signal compliance, stop lines). Depends only on `DS.Contracts`.
  - `DS.Learning`: Exercise criteria, lesson session lifecycle, scoring calculators. Depends only on `DS.Contracts`.
* **Engine Integration Layer**:
  - `DS.World`: World serialization, JSON persistence, validation, chunk spatial hashing. Depends on `DS.Contracts` and Unity engine serialization.
  - `DS.Input`: DirectInput/RawInput hardware adapter, Logitech G27 axis mapping, and InputSystem bindings. Depends on `DS.Contracts` and `Unity.InputSystem`.
  - `DS.PhysicsAdapter` (Recommended Architectural Addition): Bridges pure `DS.Simulation` calculations to Unity PhysX `Rigidbody` and raycast/`WheelCollider` actors in `FixedUpdate`. Prevents contaminating pure solver code with Unity engine physics types.
  - `DS.Presentation`: MonoBehaviour coordinators, URP render target mirror controllers, cockpit instrument animations, audio management. Depends on all runtime modules.
  - `DS.Editor`: Batch project builder, asset importer, scene setup routines.
  - `DS.Tests`: NUnit test fixtures (EditMode and PlayMode).

### 5.2 Pure C# POCO Interfaces & DTO Specifications
The contracts defined in `Assets/DrivingSchool/Code/Contracts/Contracts.cs` establish the decoupled communication spine:

```csharp
namespace DrivingSchool.Contracts
{
    // High-frequency driver command payload (fixed simulation tick)
    [Serializable] 
    public struct DriverCommand
    {
        public long sequence;
        public float steering;      // [-1.0, +1.0]: Full left to full right
        public float throttle;      // [0.0, 1.0]: Idle to full depression
        public float brake;         // [0.0, 1.0]: Released to full braking
        public float clutch;        // [0.0, 1.0]: 0.0 = fully engaged, 1.0 = fully disengaged
        public bool handbrake;
        public bool ignition;
        public bool starter;
        public int requestedGear;   // -1 = Reverse, 0 = Neutral, 1..6 = Forward gears

        public void Validate()
        {
            if (!Finite(steering) || !Unit(throttle) || !Unit(brake) || !Unit(clutch) ||
                steering < -1f || steering > 1f || requestedGear < -1 || requestedGear > 6)
                throw new ArgumentOutOfRangeException(nameof(DriverCommand), "Command values out of physical bounds.");
        }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Unit(float v) => Finite(v) && v >= 0f && v <= 1f;
    }

    public enum EnginePhase { Off, Ignition, Cranking, Running, Stalled }

    // Observable vehicle snapshot emitted by simulation solver
    [Serializable] 
    public struct VehicleState
    {
        public long tick;
        public double simulationSeconds;
        public float signedSpeedMps;    // Positive forward, negative reverse
        public float engineRpm;
        public float steeringRadians;
        public float clutchTorqueNm;
        public int gear;
        public EnginePhase engine;
        public bool leftIndicator, rightIndicator, lowBeam, highBeam, brakeLight;
    }

    public interface IInputSource
    {
        DriverCommand Read(long tick);
        bool IsConnected { get; }
    }

    public interface IForceFeedbackOutput : IDisposable
    {
        bool IsAvailable { get; }
        void SetNormalizedTorque(float torque); // [-1.0, 1.0]; backend clamps
        void Stop(); // Idempotent shutdown on pause, focus loss, disconnect
    }

    // World graph and content documents
    [Serializable] 
    public sealed class WorldDocument
    {
        public int schemaVersion = 1;
        public string id, name;
        public int chunkSizeM = 256;
        public RoadNode[] nodes = Array.Empty<RoadNode>();
        public RoadSegment[] segments = Array.Empty<RoadSegment>();
        public Lane[] lanes = Array.Empty<Lane>();
        public WorldObject[] objects = Array.Empty<WorldObject>();
        public District[] districts = Array.Empty<District>();
    }

    [Serializable] public sealed class RoadNode { public string id; public double x, y, z; }
    [Serializable] public sealed class RoadSegment { public string id, fromNode, toNode; public float widthM, speedLimitKph; public int laneCount; }
    [Serializable] public sealed class Lane { public string id, segmentId, fromNode, toNode; public int index; public float widthM; public string[] successors = Array.Empty<string>(); }
    [Serializable] public sealed class WorldObject { public string id, catalogId; public double x, y, z; public float yawDeg; }
    [Serializable] public sealed class District { public string id; public double minX, minZ; public float sizeM; }

    [Serializable]
    public sealed class RuleEvent
    {
        public string id, ruleId, ruleRevision, participantId, evidenceId, explanationKey;
        public double simulationSeconds, x, y, z;
        public int penalty;
    }

    [Serializable]
    public sealed class LessonDefinition
    {
        public int schemaVersion = 1;
        public string id, title, worldId, contentStatus;
        public float timeLimitSeconds, targetDistanceM, stopSpeedMps, requiredStopSeconds;
    }

    public enum SessionPhase { Briefing, Ready, Running, Passed, Failed, Cancelled }

    [Serializable]
    public sealed class SessionResult
    {
        public string lessonId, reason;
        public SessionPhase phase;
        public float elapsedSeconds;
        public string[] enabledAssists = Array.Empty<string>();
        public RuleEvent[] events = Array.Empty<RuleEvent>();
    }

    [Serializable]
    public sealed class TheoryContentPack
    {
        public int schemaVersion = 1;
        public string id, revision, source;
        public bool isOfficial;
        public TheoryQuestion[] questions = Array.Empty<TheoryQuestion>();
    }

    [Serializable]
    public sealed class TheoryQuestion
    {
        public string id, text, explanation, ruleReference, topic;
        public string[] answers = Array.Empty<string>();
        public int correctIndex;
    }
}
```

### 5.3 Floating Origin Architecture
Driving across a 10×10 km territory exceeds single-precision floating-point ($32$-bit `float`) precision, leading to vertex jitter, depth buffer z-fighting, and physics instabilities when distance from $(0,0,0)$ exceeds $\sim 1,000$ m:
* **Mathematical Coordinates**: Canonical world coordinates are stored as 64-bit IEEE 754 doubles:
  $$\mathbf{P}_{canonical} = (X, Y, Z) \in \mathbb{R}^3 \quad (\text{double})$$
* **Engine Transformation**: Given active origin $\mathbf{O}_{origin} \in \mathbb{R}^3$ (double):
  $$\mathbf{P}_{unity} = (\text{float})(X - O_X), (\text{float})(Y - O_Y), (\text{float})(Z - O_Z)$$
* **Atomic Shift Protocol**:
  1. Trigger: Active player car distance from current origin exceeds threshold $R_{threshold} = 500.0$ m.
  2. Quantum: The origin shifts to align with the nearest 256 m chunk grid boundary:
     $$\Delta \mathbf{O} = \left(\text{round}\left(\frac{P_X}{256}\right) \cdot 256, 0, \text{round}\left(\frac{P_Z}{256}\right) \cdot 256\right)$$
  3. Execution boundary: Atomic update at `FixedUpdate` boundary before physics evaluation.
  4. Displaced entities: All active `Rigidbody` components, active cameras, audio listeners, and particle systems have their local positions offset by $-\Delta \mathbf{O}$. Rigidbody velocities, angular velocities, and internal physics solver impulses remain completely untouched, preventing momentum discontinuities.
  5. Telemetry invariance: All `RuleEvent` records and session telemetry persistently log canonical double coordinates, guaranteeing identical coordinates regardless of origin re-centering.

### 5.4 Logitech G27 Force Feedback (FFB) Calculation
The steering wheel physics model implements realistic force feedback based on tire aligning torque and steering rack forces:

```
+--------------------------------------------------------------------------+
|                     LOGITECH G27 FFB PIPELINE                            |
+--------------------------------------------------------------------------+
|                                                                          |
|  [Vehicle State (Speed, Lateral Slip Angle alpha, Steering Angle delta)]  |
|      │                                                                   |
|      v                                                                   |
|  [Pacejka Aligning Torque (Mz)]: Mz = Fz * trail * sin(C * arctan(B * a))|
|      │                                                                   |
|      +---> Mechanical Centering Spring: T_spring = -k_s(v) * delta       |
|      +---> Steering Rack Friction:     T_fric   = -mu * sgn(omega)       |
|      +---> Fluid / Gyro Damping:       T_damp   = -c_damp * omega        |
|      +---> Engine & Kerb Rumble:       T_rumble = A_vib * sin(omega_e*t) |
|      │                                                                   |
|      v                                                                   |
|  [Raw FFB Torque Calculation]: T_raw = Mz + T_spring + T_fric + T_damp   |
|      │                                                                   |
|      +---> Hard Physical End-Stops (|delta| > 450 deg): Heavy counter     |
|      │                                                                   |
|      v                                                                   |
|  [Safety Rate Limiter & Normalization]: Clamp to [-1.0, +1.0]            |
|      │                                                                   |
|      v                                                                   |
|  [IForceFeedbackOutput.SetNormalizedTorque(torque)]                      |
|      │                                                                   |
|      +---> Fail-Safe Watchdog: Idempotent Stop() on Pause/FocusLoss      |
+--------------------------------------------------------------------------+
```

* **Pacejka Aligning Torque ($M_z$)**:
  Front tire lateral force $F_y$ and pneumatic trail $t_p$ produce the primary aligning torque:
  $$M_z = -F_y \cdot t_p = -D_y \sin\left(C_y \arctan(B_y \alpha_f)\right) \cdot t_p$$
  where $\alpha_f$ is the front slip angle:
  $$\alpha_f = \delta - \arctan\left(\frac{v_y + L_f \omega_z}{v_x}\right)$$
* **Synthetic Restoring Components**:
  - Low-speed auto-center: $T_{center} = -k_s(v) \cdot \delta$, where $k_s(v)$ scales smoothly with vehicle forward velocity $v$.
  - End-stop barrier: When $|\delta| \ge 450^\circ$, a stiff repulsive spring force $T_{stop} = -k_{stop} (|\delta| - 450^\circ) \cdot \text{sgn}(\delta)$ simulates physical lock.
* **Safety & Hardware Watchdog**:
  - DirectInput hardware torque is strictly normalized to $[-1.0, +1.0]$.
  - Slew rate limiting: Maximum torque delta per second is constrained ($\Delta T / \Delta t \le 10.0\text{ s}^{-1}$) to avoid violent wheel oscillation.
  - Fail-safe isolation: If application focus is lost, simulation paused, or device communication interrupted, `IForceFeedbackOutput.Stop()` is immediately invoked, zeroing all motor currents.

### 5.5 Serialization & Persistence Architecture
* **JSON Serialization Strategy**:
  - Current format: Unity's built-in `JsonUtility` provides zero-allocation serialization for simple POCOs with public fields.
  - Safe Write Cycle: `WorldRepository.Save()` writes to temporary file `<path>.tmp`, flushes to disk, and executes atomic file replacement (`File.Replace`) creating an automatic backup `<path>.bak`.
  - Version Verification: `WorldValidator` verifies `schemaVersion == 1` before deserialization. Forward unknown schemas are rejected, preventing silent field truncation.
* **Binary Caching Specification (Derived Chunks)**:
  - Canonical graph remains JSON for transparency and human authoring.
  - Pre-tessellated road chunk meshes and collision trees are cached as binary blobs (`.chunkbin`) keyed by a cryptographic hash (SHA-256) of the segment data and tessellation parameters, eliminating runtime startup lag.

### 5.6 Unit Test Harness
* **Contract & Logic Test Suite (`DS.Tests`)**:
  - 12 baseline NUnit tests verifying drivetrain torque equations (Neutral gear transmits 0 torque; reverse gear inverts sign; clutch disengagement decouples engine torque).
  - Validation tests verifying rejection of `float.NaN` and out-of-range commands in `DriverCommand.Validate()`.
  - Session lifecycle tests: `LessonSession` fails on timeout, rejects illegal phase transitions (e.g. `Start()` before `Ready()`), and enforces minimum hold time on target stops.
  - Repository tests: Atomic file creation, `.bak` creation on overwrite, path traversal rejection (`../`), and invalid successor graph validation.

---

## 6. R5: Task Package Structure & Verification Framework

### 6.1 Worker Task Package Specification Structure
To enable junior AI workers or focused subagents to work without risk of project regression, each task is packaged as an isolated, self-contained specification card:

```markdown
# [Task ID] — [Descriptive Title]

## 1. Context & Objective
[Concise description of the single feature or bugfix to be implemented]

## 2. Preconditions & Checked Revisions
- Target Git Branch / Commit: `main` @ [commit_hash]
- Required Tools: Unity 6000.3.10f1 / Blender 5.0 / Python 3.11+
- Prerequisites: [Completed predecessor tasks]

## 3. Scope of Allowed Files
- MODIFIABLE:
  - `Assets/DrivingSchool/Code/Subsystem/SpecificFile.cs`
  - `Assets/DrivingSchool/Code/Tests/SpecificTest.cs`
- STRICTLY FORBIDDEN:
  - Any file in `Assets/DrivingSchool/Code/Contracts/`
  - Any `.asmdef` configuration
  - Any file in `ArtSource/` or `tools/`

## 4. Input / Output Contracts
- Input: [Method signature, DTO payload, or file format]
- Output: [Expected return values, state changes, or file outputs]
- Error Modes: [Explicit exception types thrown on invalid inputs]

## 5. Verification Commands (CLI)
- Automated Unit Test:
  `& $dsUnity -batchmode -nographics -projectPath $dsProject -runTests -testPlatform EditMode -testResults artifacts/reports/test-result.xml -logFile artifacts/reports/test.log`
- Exit Code Requirement: `0`
- Success Criteria: Total tests increased, failed == 0.

## 6. Definition of Done (DoD)
- [ ] Code compiles with zero warnings and zero errors.
- [ ] Unit tests cover nominal execution, boundary conditions, and invalid inputs.
- [ ] No contract or architecture boundaries violated.
- [ ] Evidence manifest updated with new test logs.
```

### 6.2 Architecture Decision Record (ADR) Template
All significant technical choices must be documented in `docs/adr.md` following this standardized format:

```markdown
## ADR-[Number] — [Short Title]

* **Status**: [Proposed | Accepted for Design | Implemented | Superseded by ADR-xxx]
* **Date**: YYYY-MM-DD
* **Deciders**: [Architect / Roles involved]

### Context & Problem Statement
[What technical or product constraint required a decision?]

### Decision Outcome
[The chosen architectural path or pattern]

### Rationale & Alternatives Considered
1. *Option A (Chosen)*: [Pros and why selected]
2. *Option B (Rejected)*: [Cons and trade-offs]
3. *Option C (Rejected)*: [Cons and trade-offs]

### Consequences & Trade-offs
* Positive: [Benefits gained]
* Negative / Cost: [Architectural debt, additional adapter layers, or performance cost]

### Verification & Invalidation Conditions
* Verification Command: [Specific CLI command or benchmark]
* Invalidation: [What benchmark failure or requirement change would force a redesign?]
```

### 6.3 End-to-End (E2E) Verification Test Suite Strategy
Verification is structured into reproducible, automated test gates (C00 through C05):

| Gate ID | Scope | Execution Command | Acceptance Threshold |
|---|---|---|---|
| **C00** | **Environment & Hash Integrity** | `powershell -File tools/verify_evidence.py` | Unity 6000.3.10f1 detected; all 73 audited files exist with matching SHA-256 hashes. |
| **C01** | **Unit & Contract Suite (EditMode)** | Unity `-runTests -testPlatform EditMode` | 100% tests pass; zero failures; execution time $< 10$ s. |
| **C02** | **Project Asset Import & Scene Setup** | Unity `-executeMethod DrivingSchool.Editor.ProjectBuilder.Prepare` | Clean import of all 3 FBXs; URP renderer assets configured; idempotency verified (second run produces zero diff). |
| **C03** | **Windows Player Batch Build** | Unity `-executeMethod DrivingSchool.Editor.ProjectBuilder.Build` | Output `Builds/Windows/DrivingSchoolSim.exe` generated; exit code 0; zero compilation errors. |
| **C04** | **Automated Smoke Test** | `DrivingSchoolSim.exe --smoke --capture artifacts/reports/smoke.png` | Headless execution completes; world saved and loaded; lesson simulated to completion; screenshot captured. |
| **C05** | **Hardware & Interactive Protocol** | Manual execution with G27 & OpenXR | Physical axis mapping confirmed; FFB zero torque on focus loss; VR head tracking 90 FPS without jitter. |

---

## 7. Technical Risk Analysis & Mitigation Matrix

| # | Risk Description | Severity | Likelihood | Impact Area | Mitigation & Architectural Strategy |
|---|---|---|---|---|---|
| **R-01** | **Blender $\to$ Unity Coordinate & Scale Desynchronization** | High | Medium | R1 (3D Assets) | Enforce explicit FBX export settings (`axis_forward='Y', axis_up='Z', apply_unit_scale=True`); verify scale 1.0 in `DS_Sedan_A-geometry.json`; automate import validation in `ProjectBuilder.Prepare()`. |
| **R-02** | **URP Multi-Mirror Performance Bottleneck** | High | High | R1 & R4 | Three planar mirrors require 4 scene rendering passes. Mitigate by rendering wing mirrors at half-rate (30 Hz) or alternating odd/even frames; budget render texture size to 512×256; disable shadows and terrain trees in reflection camera culling masks. |
| **R-03** | **OpenXR Stereo Mirror Parallax Inversion** | High | Medium | R1 & R4 | Planar reflection matrices are viewer-eye dependent. In VR, planar mirrors must render per-eye or fall back to an interior parallax-corrected cubemap. Mono mirrors in VR cause severe binocular disparity and nausea. |
| **R-04** | **T-Junction Seams and Z-Fighting in Procedural Roads** | Medium | High | R2 (Roads) | Truncate road segments strictly at intersection boundary polygon edges; use shared double-precision snapping; elevate road marking meshes by $+0.008$ m above asphalt. |
| **R-05** | **Floating Origin Discontinuities on Active Physics Actors** | Critical | Low | R2 & R4 | Execute origin shifts strictly at `FixedUpdate` boundaries; apply $-\Delta \mathbf{O}$ to `transform.position` while preserving `Rigidbody.velocity` and `angularVelocity`; store all canonical telemetry in `double`. |
| **R-06** | **Logitech G27 Missing Hardware in Automated CI** | Medium | High | R4 & R5 | Strict interface boundary (`IInputSource`, `IForceFeedbackOutput`). CI runs synthetic mock adapters (`SyntheticInputSource`, `MockForceFeedbackOutput`); physical hardware testing isolated to Gate C05. |
| **R-07** | **JsonUtility Data Truncation on Format Upgrades** | High | Medium | R4 (Persistence) | Enforce `schemaVersion` check in `WorldValidator`; reject unknown schemas before overwriting; use `.tmp` $\to$ `File.Replace` $\to$ `.bak` atomic write cycle. |
| **R-08** | **C# Core Domain Contamination by UnityEngine Types** | High | Low | R4 (Unity Core) | Pure assemblies (`DS.Contracts`, `DS.Simulation`, `DS.Rules`, `DS.Learning`) have `noEngineReferences: true`. Any attempt to import `UnityEngine` triggers a compilation error in Unity and CI. |

---

## 8. Milestone & Team Dispatch Recommendations

To execute Phase 1 efficiently across specialized subagents, the project is structured into 5 logical milestones:

```
+-------------------------------------------------------------------------+
|                  PHASE 1 MILESTONE EXECUTION ROADMAP                    |
+-------------------------------------------------------------------------+
|                                                                         |
|  [Milestone 1: 3D Asset & Visual Geometry Pipeline]                     |
|  (Finalize watertight body shell, kinematic hierarchy, Three.js viewer) |
|                                │                                        |
|                                v                                        |
|  [Milestone 2: Spatial & Road Framework]                                |
|  (Seamless road mesh generation, 10x10 km masterplan, autodrome 8 ex.)  |
|                                │                                        |
|                                v                                        |
|  [Milestone 3: UI Design System & 9-Screen Prototype]                   |
|  (CSS/JS state machine, G27 calibration wizard, VR overlay preview)     |
|                                │                                        |
|                                v                                        |
|  [Milestone 4: Unity Engine Core, Floating Origin & FFB]                |
|  (Decoupled asmdefs, C# POCOs, G27 input/FFB, Floating Origin, tests)   |
|                                │                                        |
|                                v                                        |
|  [Milestone 5: Verification Suite, Worker Task Packages & Handoff]      |
|  (26 task packages T01-T26, ADR documentation, Gates C00-C05 E2E run)   |
|                                                                         |
+-------------------------------------------------------------------------+
```

### Milestone Breakdown
1. **Milestone M1 — 3D Asset & Visual Geometry Pipeline**:
   - Deliverable: Finalized `DS_Sedan_A_closed_shell.blend`, exported FBX and GLB, 24 Cycles renders, Three.js interactive inspector with kinematic sliders.
2. **Milestone M2 — Spatial, Road Network & Autodrome**:
   - Deliverable: Clean road profile generator, 10×10 km masterplan SVG/JSON, 500×500 m demo district scene, 8 parametric autodrome exercise layouts tailored to sedan geometry.
3. **Milestone M3 — UI Design System & 9-Screen Prototype**:
   - Deliverable: Interactive HTML5 prototype of all 9 screens with responsive 1080p/720p layouts, component state tokens, G27 calibration wizard, and VR overlay preview.
4. **Milestone M4 — Unity Engine Foundation & Contracts**:
   - Deliverable: Compiling Unity 6000.3 project with isolated `.asmdef` modules, pure C# POCO contracts, Floating Origin coordinator, G27 FFB physics model, and NUnit test harness.
5. **Milestone M5 — Verification Suite & Task Package Handoff**:
   - Deliverable: 26 structured worker task packages (`docs/tasks/T01.md`–`T26.md`), complete ADR records (`docs/adr.md`), and automated verification runs (C00–C04).
