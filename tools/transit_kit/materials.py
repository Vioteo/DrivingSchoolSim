"""Transit kit palette: name -> (linear RGB, metallic, roughness, emission, alpha).

Written next to the FBX as PT_Materials.json; TransitKitBuilder creates the
URP materials in Assets/DrivingSchool/Materials/Transit from that file.
"""

PALETTE = {
    # vehicles
    'PT_Paint_BusWhite': ((0.80, 0.82, 0.80), 0.1, 0.3, 0, 1),
    'PT_Paint_BusBlue': ((0.02, 0.16, 0.45), 0.2, 0.3, 0, 1),
    'PT_Paint_MinibusYellow': ((0.85, 0.52, 0.02), 0.15, 0.3, 0, 1),
    'PT_Paint_MinibusBlue': ((0.017, 0.19, 0.58), 0.15, 0.3, 0, 1),
    'PT_Paint_TramCream': ((0.78, 0.72, 0.55), 0.1, 0.35, 0, 1),
    'PT_Paint_TramRed': ((0.50, 0.03, 0.03), 0.15, 0.35, 0, 1),
    'PT_Glass_Tinted': ((0.015, 0.03, 0.035), 0.3, 0.06, 0, 1),
    'PT_Trim_Black': ((0.012, 0.013, 0.014), 0.0, 0.4, 0, 1),
    'PT_Plastic_Black': ((0.02, 0.022, 0.024), 0.0, 0.75, 0, 1),
    'PT_Plastic_Grey': ((0.20, 0.21, 0.22), 0.0, 0.6, 0, 1),
    'PT_Rubber': ((0.018, 0.022, 0.024), 0.0, 0.85, 0, 1),
    'PT_Steel_Wheel': ((0.35, 0.37, 0.38), 0.8, 0.4, 0, 1),
    'PT_Steel_Dark': ((0.05, 0.055, 0.06), 0.6, 0.55, 0, 1),
    'PT_Steel_Bright': ((0.6, 0.62, 0.63), 0.9, 0.25, 0, 1),
    'PT_Mirror': ((0.53, 0.65, 0.70), 1.0, 0.03, 0, 1),
    'PT_Lamp_White': ((0.83, 0.94, 1.0), 0.1, 0.18, 1.5, 1),
    'PT_Lamp_Red': ((0.8, 0.025, 0.018), 0.1, 0.23, 1.0, 1),
    'PT_Lamp_Amber': ((1.0, 0.3, 0.025), 0.1, 0.25, 1.0, 1),
    'PT_Display': ((0.01, 0.01, 0.012), 0.0, 0.3, 0, 1),
    'PT_Display_Amber': ((1.0, 0.42, 0.02), 0.0, 0.5, 2.0, 1),
    'PT_Plate_White': ((0.85, 0.86, 0.84), 0.0, 0.4, 0, 1),
    'PT_Ink': ((0.01, 0.01, 0.012), 0.0, 0.6, 0, 1),
    # infrastructure
    'PT_Asphalt': ((0.115, 0.13, 0.145), 0.0, 0.9, 0, 1),
    'PT_TrackSlab': ((0.30, 0.31, 0.30), 0.0, 0.85, 0, 1),
    'PT_Grass': ((0.10, 0.22, 0.05), 0.0, 0.95, 0, 1),
    'PT_Concrete': ((0.63, 0.65, 0.62), 0.0, 0.85, 0, 1),
    'PT_Paving': ((0.51, 0.53, 0.51), 0.0, 0.85, 0, 1),
    'PT_Rail_Steel': ((0.55, 0.56, 0.57), 0.9, 0.35, 0, 1),
    'PT_Groove': ((0.03, 0.03, 0.03), 0.3, 0.8, 0, 1),
    'PT_Marking_White': ((0.87, 0.88, 0.83), 0.0, 0.78, 0, 1),
    'PT_Marking_Yellow': ((0.95, 0.61, 0.035), 0.0, 0.8, 0, 1),
    'PT_Shelter_Frame': ((0.16, 0.18, 0.2), 0.7, 0.45, 0, 1),
    'PT_Glass_Clear': ((0.55, 0.68, 0.72), 0.0, 0.05, 0, 0.25),
    'PT_Wood': ((0.35, 0.18, 0.07), 0.0, 0.7, 0, 1),
    'PT_Steel': ((0.39, 0.46, 0.5), 0.78, 0.32, 0, 1),
    'PT_Sign_Blue': ((0.012, 0.12, 0.57), 0.0, 0.4, 0, 1),
    'PT_Sign_White': ((0.92, 0.95, 0.92), 0.0, 0.4, 0, 1),
    'PT_Collision': ((1.0, 0.0, 1.0), 0.0, 1.0, 0, 1),
}

# materials whose UVs are world-planar with a 2 m repeat (ground textures later)
GROUND = {'PT_Asphalt', 'PT_TrackSlab', 'PT_Grass', 'PT_Concrete', 'PT_Paving'}
