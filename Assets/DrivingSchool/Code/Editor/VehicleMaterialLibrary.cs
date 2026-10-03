using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// Surface recipes of the shared car materials in Materials/ (T72). Before, every car material got the same
    /// smoothness (0.25, paint 0.65): chrome looked like grey plastic, glass was matte, fabric shone like vinyl. And the
    /// colours were the Blender (linear) values used as sRGB, so cars were darker than modelled — the other kits convert
    /// with Color.gamma (TransitKitBuilder, TrainKitBuilder, PedestrianAssetBuilder), the cars did not.
    ///  • Lin(…) — the colour of tools/vehicle_kit/materials.py (sedan values where its .blend differs), converted to sRGB;
    ///  • Srgb(…) — chosen in Unity: lamp lenses, screens, dial faces (they must stay near black), print, the dark cabin
    ///    plastics and leather (the palette values looked light grey in sunlight);
    ///  • paints use URP Complex Lit with a clear coat (glossy lacquer over the metallic or solid base).
    /// Applied when VehicleAssembler creates a material, by Driving School → Vehicles → Import, audit and build traffic
    /// prefabs and by its own menu item. Idempotent: values are set, not scaled.
    /// </summary>
    public static class VehicleMaterialLibrary
    {
        const string MaterialDir = "Assets/DrivingSchool/Materials";
        const string LitShader = "Universal Render Pipeline/Lit", ComplexLitShader = "Universal Render Pipeline/Complex Lit";

        enum Kind { Opaque, Paint, Glass, Print }

        sealed class Recipe
        {
            public Color colour; public float metallic, smoothness, emission; public Kind kind; public bool keepColour;
        }

        static Recipe Lin(float r, float g, float b, float metallic, float smoothness, Kind kind = Kind.Opaque) =>
            new Recipe { colour = new Color(r, g, b).gamma, metallic = metallic, smoothness = smoothness, kind = kind };

        static Recipe Srgb(float r, float g, float b, float metallic, float smoothness, Kind kind = Kind.Opaque, float emission = 0f) =>
            new Recipe { colour = new Color(r, g, b), metallic = metallic, smoothness = smoothness, kind = kind, emission = emission };

        static Recipe Keep(float metallic, float smoothness, Kind kind) =>
            new Recipe { metallic = metallic, smoothness = smoothness, kind = kind, keepColour = true };

        static readonly Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>
        {
            // body paint: metallic base under a clear coat (Complex Lit)
            { "Paint_Atlantic", Srgb(.1f, .24f, .36f, .45f, .62f, Kind.Paint) },   // deep blue-teal; the palette value read pastel under lacquer
            { "Paint_Terracotta", Srgb(.52f, .2f, .1f, .4f, .62f, Kind.Paint) },   // the palette value read bright orange
            { "Paint_Silver", Lin(.42f, .44f, .45f, .55f, .62f, Kind.Paint) },
            { "Paint_Graphite", Lin(.06f, .065f, .07f, .5f, .62f, Kind.Paint) },
            { "Paint_White", Lin(.8f, .83f, .8f, .05f, .7f, Kind.Paint) },
            { "Paint_PoliceBlue", Lin(.02f, .09f, .42f, .1f, .7f, Kind.Paint) },
            { "Paint_AmbulanceRed", Lin(.62f, .02f, .02f, .05f, .7f, Kind.Paint) },
            // exterior trim
            { "Trim_Black", Lin(.012f, .013f, .014f, 0f, .75f) },        // gloss black (window frames, pillars)
            { "Plastic_Black", Lin(.02f, .022f, .024f, 0f, .3f) },        // textured bumper plastic
            { "Plastic_Grey", Lin(.16f, .17f, .18f, 0f, .45f) },
            { "Rubber", Lin(.018f, .024f, .026f, 0f, .2f) },
            { "Chrome", Lin(.7f, .76f, .79f, 1f, .9f) },
            { "Satin_Aluminium", Lin(.4f, .46f, .47f, .9f, .6f) },
            { "Mirror", Lin(.53f, .65f, .7f, 1f, .95f) },
            { "Glass", Keep(0f, .95f, Kind.Glass) },
            { "Glass_Frosted", Keep(0f, .55f, Kind.Glass) },
            // lamp lenses: the colour of an unlit lens; VehicleLightsView makes them glow
            { "Lamp_White", Srgb(.83f, .94f, 1f, 0f, .9f) },
            { "Lamp_Red", Srgb(.8f, .025f, .018f, 0f, .9f) },
            { "Lamp_Amber", Srgb(1f, .3f, .025f, 0f, .9f) },
            { "Lamp_Blue", Srgb(.02f, .12f, 1f, 0f, .9f) },
            // cabin
            { "Interior_Graphite", Srgb(.12f, .13f, .14f, 0f, .3f) },      // checked in Play: the palette value (sRGB .22) read as light grey in the sun
            { "Interior_Graphite_Light", Srgb(.2f, .21f, .22f, 0f, .3f) },
            { "Interior_Stone", Lin(.32f, .35f, .32f, 0f, .18f) },
            { "Interior_Light", Lin(.62f, .64f, .63f, 0f, .3f) },
            { "Carpet", Lin(.03f, .033f, .035f, 0f, .05f) },
            { "Leather", Srgb(.1f, .11f, .11f, 0f, .38f) },              // black leather: steering wheel, seats
            { "Seat_Fabric", Srgb(.17f, .19f, .2f, 0f, .08f) },
            { "Seat_Insert", Srgb(.27f, .3f, .31f, 0f, .08f) },              // the palette value made the inserts look almost white
            { "Stitch", Lin(.55f, .58f, .48f, 0f, .1f) },
            { "Stretcher_Orange", Lin(.8f, .28f, .03f, 0f, .4f) },
            // instruments: dark glossy screens, black dial faces, light print that glows a little
            // number plates: own materials, so the profile colour (it repaints Paint_*) does not reach them
            { "Plate_White", Lin(.82f, .83f, .8f, 0f, .45f) },
            { "Plate_Blue", Lin(.02f, .09f, .42f, 0f, .5f) },
            { "Display", Srgb(.018f, .06f, .08f, 0f, .88f) },
            { "Gauge_Face", Srgb(.02f, .022f, .025f, 0f, .5f) },
            { "Ink", Srgb(.72f, .89f, .91f, 0f, .3f, Kind.Print, 1.2f) },
            { "Ink_Dark", Lin(.01f, .01f, .012f, 0f, .4f) },
        };

        public static bool Has(string materialName) => Recipes.ContainsKey(materialName);

        [MenuItem("Driving School/Vehicles/Update car materials")]
        public static int UpdateAll()
        {
            int updated = 0;
            foreach (var name in Recipes.Keys)
                if (Apply(AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/" + name + ".mat"))) updated++;
            AssetDatabase.SaveAssets();
            Debug.Log("VEHICLE_MATERIALS_PASS " + updated + " materials");
            return updated;
        }

        /// <summary>Sets shader, colour and surface of a shared car material by its name; false when there is no recipe.</summary>
        public static bool Apply(Material m)
        {
            if (m == null || !Recipes.TryGetValue(m.name, out var r)) return false;
            if (r.kind == Kind.Paint)
            {
                var shader = Shader.Find(ComplexLitShader) ?? Shader.Find(LitShader);
                if (shader != null && m.shader != shader) m.shader = shader;
            }
            else if (r.kind != Kind.Glass)
            {
                var shader = Shader.Find(LitShader);
                if (shader != null && m.shader != shader) m.shader = shader;
            }
            if (!r.keepColour && m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", r.colour);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", r.metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", r.smoothness);
            if (r.kind == Kind.Paint && m.HasProperty("_ClearCoatMask"))
            {
                // Same keywords LitGUI sets for a clear coat without a map.
                if (m.HasProperty("_ClearCoat")) m.SetFloat("_ClearCoat", 1f);
                m.SetFloat("_ClearCoatMask", 1f);
                if (m.HasProperty("_ClearCoatSmoothness")) m.SetFloat("_ClearCoatSmoothness", .92f);
                m.DisableKeyword("_CLEARCOATMAP");
                m.EnableKeyword("_CLEARCOAT");
            }
            if (r.kind == Kind.Print)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", r.colour * r.emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            EditorUtility.SetDirty(m);
            return true;
        }
    }
}
