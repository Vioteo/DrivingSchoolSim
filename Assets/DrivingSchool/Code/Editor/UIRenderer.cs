using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DrivingSchool.Editor
{
    public static class UIRenderer
    {
        [MenuItem("Driving School/Render UI Screenshots")]
        public static void RenderUI()
        {
            string outDir = @"C:\Users\AVSok\Desktop\Unity_Screenshots";
            Directory.CreateDirectory(outDir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            
            var camGo = new GameObject("Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.05f); // Dark gray
            cam.orthographic = true;
            cam.orthographicSize = 540; // 1080 / 2
            camGo.transform.position = new Vector3(960, 540, -100);

            string[] prefabs = { "HUD", "MainMenu", "LessonCatalog", "ConditionsSetup", "G27Calibration", "PauseMenu", "TheoryExam" };

            foreach (var pName in prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/DrivingSchool/Prefabs/UI/{pName}.prefab");
                if (prefab == null) continue;

                var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                var canvas = instance.GetComponent<Canvas>();
                if (canvas != null)
                {
                    canvas.renderMode = RenderMode.WorldSpace;
                    var rt = canvas.GetComponent<RectTransform>();
                    rt.sizeDelta = new Vector2(1920, 1080);
                    rt.position = new Vector3(960, 540, 0);
                    rt.localScale = Vector3.one;
                }
                
                Canvas.ForceUpdateCanvases();

                Capture(cam, Path.Combine(outDir, $"UI_{pName}.png"), 1920, 1080);
                Object.DestroyImmediate(instance);
            }

            Debug.Log("UI_RENDERS_PASS");
        }

        private static void Capture(Camera cam, string path, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24);
            cam.targetTexture = rt;
            
            var oldActive = RenderTexture.active;
            RenderTexture.active = rt;
            
            cam.Render();
            
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            
            cam.targetTexture = null;
            RenderTexture.active = oldActive;
            
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
        }
    }
}
