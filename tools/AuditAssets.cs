using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
public static class AuditAssets
{
    // Повторно проверяем зависимости непосредственно перед удалением, не доверяем старому JSON.
    public static string PruneLegacyTrainingMeshes()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Остановите Play Mode");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Есть несохранённая сцена");
        var files = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") && File.Exists(p)).ToArray();
        var used = new HashSet<string>();
        foreach (var p in files)
            foreach (var d in AssetDatabase.GetDependencies(p, false)) if (d != p) used.Add(d);
        var candidates = files.Where(p => Path.GetDirectoryName(p).Replace('\\', '/') == "Assets/DrivingSchool/Art/TrainingKit"
            && p.EndsWith(".asset") && !Path.GetFileName(p).StartsWith("TG_") && !used.Contains(p)
            && AssetDatabase.GetMainAssetTypeAtPath(p) == typeof(Mesh)).ToArray();
        var textExtensions = new HashSet<string>(new[] { ".cs", ".json", ".asset", ".unity", ".prefab", ".mat", ".meta" });
        var texts = files.Concat(Directory.GetFiles("ProjectSettings", "*", SearchOption.AllDirectories))
            .Where(p => textExtensions.Contains(Path.GetExtension(p))).Select(p => new { path=p, text=File.ReadAllText(p) }).ToArray();
        var remove = candidates.Where(p => !texts.Any(t => t.path != p && t.path != p + ".meta"
            && (t.text.Contains(AssetDatabase.AssetPathToGUID(p)) || t.text.Contains(p)))).ToArray();
        // В список никогда не попадают FBX, материалы, действующие TG_* и файлы вне TrainingKit.
        foreach (var p in remove)
            if (!AssetDatabase.DeleteAsset(p)) throw new IOException("Не удалён " + p);
        Directory.CreateDirectory("artifacts/reports");
        File.WriteAllLines("artifacts/reports/asset-cleanup.txt", remove);
        AssetDatabase.SaveAssets();
        Main();
        return "LEGACY_MESHES_REMOVED " + remove.Length;
    }
    [Serializable] public class Row { public string path; public string[] usedBy; public bool inBuild, resources; public long bytes; }
    [Serializable] public class Report { public string[] buildScenes; public Row[] assets; }
    public static string Main()
    {
        var paths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") && File.Exists(p)).ToArray();
        var roots = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var reachable = new HashSet<string>(AssetDatabase.GetDependencies(roots, true));
        var users = new Dictionary<string, List<string>>();
        foreach (var path in paths)
            foreach (var d in AssetDatabase.GetDependencies(path, false))
            {
                if (d == path) continue;
                if (!users.ContainsKey(d)) users[d] = new List<string>();
                users[d].Add(path);
            }
        var extensions = new HashSet<string>(new[]{".fbx", ".prefab", ".mat", ".png", ".jpg", ".asset", ".unity"});
        var report = new Report { buildScenes = roots, assets = paths.Where(p => extensions.Contains(Path.GetExtension(p).ToLowerInvariant())).Select(p => new Row {
            path=p, usedBy=users.TryGetValue(p, out var u) ? u.ToArray() : new string[0], inBuild=reachable.Contains(p), resources=p.Contains("/Resources/"), bytes=new FileInfo(p).Length
        }).ToArray() };
        File.WriteAllText("artifacts/reports/asset-dependencies.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return "DEPENDENCIES_AUDITED " + report.assets.Length;
    }
}

