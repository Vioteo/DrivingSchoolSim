"""Material palette shared by all vehicles. Names already used by the project
(Paint_Atlantic, Rubber, Glass, Lamp_*, Mirror, ...) keep their meaning so the
existing Unity material remaps still apply."""

# name: (base colour RGB linear, metallic, roughness, emission strength, alpha)
PALETTE = {
    'Paint_Atlantic': ((0.085, 0.23, 0.29), 0.7, 0.25, 0, 1),
    'Paint_Silver': ((0.42, 0.44, 0.45), 0.85, 0.3, 0, 1),
    'Paint_Graphite': ((0.06, 0.065, 0.07), 0.75, 0.28, 0, 1),
    'Paint_Terracotta': ((0.36, 0.09, 0.04), 0.6, 0.3, 0, 1),
    'Paint_White': ((0.8, 0.83, 0.8), 0.15, 0.28, 0, 1),
    'Paint_PoliceBlue': ((0.02, 0.09, 0.42), 0.2, 0.35, 0, 1),
    'Paint_AmbulanceRed': ((0.62, 0.02, 0.02), 0.1, 0.35, 0, 1),
    'Trim_Black': ((0.012, 0.013, 0.014), 0.0, 0.25, 0, 1),
    'Plastic_Black': ((0.02, 0.022, 0.024), 0.0, 0.75, 0, 1),
    'Plastic_Grey': ((0.16, 0.17, 0.18), 0.1, 0.55, 0, 1),
    'Rubber': ((0.018, 0.024, 0.026), 0.0, 0.82, 0, 1),
    'Chrome': ((0.7, 0.76, 0.79), 0.95, 0.13, 0, 1),
    'Satin_Aluminium': ((0.4, 0.46, 0.47), 0.85, 0.28, 0, 1),
    'Glass': ((0.24, 0.44, 0.48), 0.05, 0.12, 0, 0.16),
    'Glass_Frosted': ((0.75, 0.78, 0.78), 0.0, 0.5, 0, 0.85),
    'Mirror': ((0.53, 0.65, 0.7), 1.0, 0.035, 0, 1),
    'Lamp_White': ((0.83, 0.94, 1.0), 0.1, 0.18, 3, 1),
    'Lamp_Red': ((0.8, 0.025, 0.018), 0.1, 0.23, 2, 1),
    'Lamp_Amber': ((1.0, 0.3, 0.025), 0.1, 0.25, 2, 1),
    'Lamp_Blue': ((0.02, 0.12, 1.0), 0.1, 0.2, 3, 1),
    'Display': ((0.018, 0.06, 0.08), 0.1, 0.3, 0, 1),
    'Ink': ((0.72, 0.89, 0.91), 0.0, 0.6, 1.2, 1),
    'Ink_Dark': ((0.01, 0.01, 0.012), 0.0, 0.6, 0, 1),
    'Interior_Graphite': ((0.038, 0.048, 0.052), 0.0, 0.7, 0, 1),
    'Interior_Graphite_Light': ((0.07, 0.08, 0.085), 0.0, 0.7, 0, 1),
    'Interior_Stone': ((0.32, 0.35, 0.32), 0.0, 0.83, 0, 1),
    'Interior_Light': ((0.62, 0.64, 0.63), 0.0, 0.7, 0, 1),
    'Carpet': ((0.03, 0.033, 0.035), 0.0, 0.95, 0, 1),
    'Leather': ((0.07, 0.085, 0.08), 0.0, 0.68, 0, 1),
    'Seat_Fabric': ((0.06, 0.065, 0.07), 0.0, 0.9, 0, 1),
    'Seat_Insert': ((0.16, 0.17, 0.17), 0.0, 0.9, 0, 1),
    'Stretcher_Orange': ((0.8, 0.28, 0.03), 0.0, 0.6, 0, 1),
}

# sRGB-ish preview colours (for offline renders only)
PREVIEW = {k: tuple(min(1.0, c ** (1 / 2.2)) for c in v[0]) for k, v in PALETTE.items()}
