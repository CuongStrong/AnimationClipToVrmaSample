using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Baxter
{
    public class BatchAnimationToVrmaExporterWindow : EditorWindow
    {
        private string inputFolder = "Assets/Animations/Clip";
        private string outputFolder = "Assets/Animations/Vrma";

        [MenuItem("VRM/Batch Animation To VRMA")]
        public static void OpenWindow()
        {
            var window = GetWindow<BatchAnimationToVrmaExporterWindow>("Batch VRMA Exporter");
            window.minSize = new Vector2(400, 180);
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Label("Batch VRM Animation Exporter", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            inputFolder = EditorGUILayout.TextField("Input Folder", inputFolder);
            if (GUILayout.Button("Select", GUILayout.MaxWidth(60)))
                inputFolder = SelectFolder(inputFolder);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            if (GUILayout.Button("Select", GUILayout.MaxWidth(60)))
                outputFolder = SelectFolder(outputFolder);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);
            bool canExport = AssetDatabase.IsValidFolder(inputFolder);
            EditorGUI.BeginDisabledGroup(!canExport);
            if (GUILayout.Button("Convert All Clips to VRMA", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog(
                    "Confirm Batch Export",
                    $"Convert all AnimationClips in:\n'{inputFolder}'\ninto VRMA under:\n'{outputFolder}'?",
                    "Yes", "No"))
                {
                    ConvertAll();
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        private string SelectFolder(string current)
        {
            string systemPath = EditorUtility.OpenFolderPanel("Select Folder", Application.dataPath, "");
            if (string.IsNullOrEmpty(systemPath)) return current;
            if (systemPath.StartsWith(Application.dataPath))
            {
                string rel = "Assets" + systemPath.Substring(Application.dataPath.Length);
                return rel.Replace("\\", "/");
            }
            EditorUtility.DisplayDialog("Invalid Folder",
                "Please select a folder inside the project's Assets directory.", "OK");
            return current;
        }

        private void ConvertAll()
        {
            int count = 0;

            var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { inputFolder });
            foreach (var guid in guids)
            {
                string clipPath = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null || !clip.isHumanMotion) continue;

                string relative = clipPath.Substring(inputFolder.Length).TrimStart('/', '\\');
                string outRel = Path.ChangeExtension(relative, "vrma");
                string assetPath = ($"{outputFolder}/{outRel}").Replace("\\", "/");

                string systemPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets".Length + 1));
                string dir = Path.GetDirectoryName(systemPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                
                GameObject animatorObject = null;
                try
                {
                    var animator = HumanoidBuilder.CreateHumanoid(ReferenceHumanoid.BoneLocalPoseMap);
                    animatorObject = animator.gameObject;
                    var bytes = AnimationClipToVrmaCore.Create(animator, clip);
                    File.WriteAllBytes(systemPath, bytes);
                    count++;
                    Debug.Log("VRM Animation saved to: " + systemPath);
                }
                finally
                {
                    if (animatorObject != null)
                    {
                        Object.DestroyImmediate(animatorObject);
                    }
                }
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Batch Export Complete",
                $"Successfully exported {count} clip(s) to '{outputFolder}'.", "OK");
        }
    }
}
