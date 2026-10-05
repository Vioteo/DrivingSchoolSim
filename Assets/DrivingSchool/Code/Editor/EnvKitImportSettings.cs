using UnityEditor;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// Import settings for the environment kit EN_* (T73, docs/env-kit.md), as required by art-pipeline.md §5.
    /// The FBX come from tools/build_env_kit.py; this keeps every (re)import of Art/EnvKit correct without
    /// manual Inspector changes. Prefabs, URP materials from EN_Materials.json, LOD groups and colliders
    /// are the next step (kit builder), not done here.
    /// </summary>
    internal sealed class EnvKitImportSettings : AssetPostprocessor
    {
        const string Folder = "Assets/DrivingSchool/Art/EnvKit/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.isReadable = false;
            var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            // trees and bushes are instanced in large numbers and lit by probes, not lightmaps
            importer.generateSecondaryUV = !(name.StartsWith("EN_Tree") || name.StartsWith("EN_Bush"));
        }
    }
}
