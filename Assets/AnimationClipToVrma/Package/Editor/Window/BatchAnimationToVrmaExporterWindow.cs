using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniGLTF.Extensions.VRMC_vrm; 
using UnityEditor;
using UnityEngine;
using UniVRM10;

namespace Baxter.Editor
{

    public class BatchAnimationClipToVrmaWindow : EditorWindow
    {
        private Vrm10Instance vrmInstance;
        private string inputFolder = "Assets/Animations/Clip";
        private string outputFolder = "Assets/Animations/Vrma";

        [MenuItem("VRM1/Batch Animations Exporter")]
        public static void OpenWindow()
        {
            var window = GetWindow<BatchAnimationClipToVrmaWindow>("Batch VRM Animation Exporter");
            window.minSize = new Vector2(420, 200);
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Label("Batch VRM Animation Exporter (VRM1)", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Avatar (VRM1 Instance):");
            vrmInstance = (Vrm10Instance)EditorGUILayout.ObjectField(vrmInstance, typeof(Vrm10Instance), true);
            bool validAvatar = ShowAvatarValidityGUI();

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            inputFolder = EditorGUILayout.TextField("Input Clips Folder", inputFolder);
            if (GUILayout.Button("Select", GUILayout.MaxWidth(60)))
                inputFolder = SelectFolder(inputFolder);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            outputFolder = EditorGUILayout.TextField("Output VRMA Folder", outputFolder);
            if (GUILayout.Button("Select", GUILayout.MaxWidth(60)))
                outputFolder = SelectFolder(outputFolder);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            bool canExport = validAvatar && AssetDatabase.IsValidFolder(inputFolder);
            EditorGUI.BeginDisabledGroup(!canExport);
            if (GUILayout.Button("Export All to VRMA", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog(
                    "Confirm Batch Export",
                    $"Export all AnimationClips in:\n'{inputFolder}'\ninto VRMA files under:\n'{outputFolder}'?",
                    "Yes", "No"))
                {
                    ConvertAll();
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        private bool ShowAvatarValidityGUI()
        {
            if (vrmInstance == null)
            {
                EditorGUILayout.HelpBox("Please select a Vrm10Instance avatar.", MessageType.Warning);
                return false;
            }
            var animator = vrmInstance.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
            {
                EditorGUILayout.HelpBox("Selected avatar must have a Humanoid Animator.", MessageType.Error);
                return false;
            }
            return true;
        }

        private string SelectFolder(string current)
        {
            string sys = EditorUtility.OpenFolderPanel("Select Folder", Application.dataPath, "");
            if (string.IsNullOrEmpty(sys)) return current;
            if (sys.StartsWith(Application.dataPath))
            {
                string rel = "Assets" + sys.Substring(Application.dataPath.Length);
                return rel.Replace("\\", "/");
            }
            EditorUtility.DisplayDialog("Invalid Folder", "Please select a folder under Assets.", "OK");
            return current;
        }

        private void ConvertAll()
        {
            var animatorSource = vrmInstance.GetComponent<Animator>();
    
            var expressionMap = new Dictionary<string, ExpressionKey>();
            var exprRoot = vrmInstance.Vrm.Expression;
            if (exprRoot != null)
            {
                foreach (var clipRef in exprRoot.Clips)
                {
                    var clip = clipRef.Clip;
                    if (clip == null) continue;

                    var key = clipRef.Preset != ExpressionPreset.custom
                        ? ExpressionKey.CreateFromPreset(clipRef.Preset)
                        : ExpressionKey.CreateCustom(clip.name);

                    if (clipRef.Clip.MorphTargetBindings.Any())
                    {
                        var bind = clipRef.Clip.MorphTargetBindings[0];
                        var tr = vrmInstance.transform.Find(bind.RelativePath);
                        var smr = tr?.GetComponent<SkinnedMeshRenderer>();
                        if (smr != null && smr.sharedMesh != null && bind.Index < smr.sharedMesh.blendShapeCount)
                        {
                            var name = smr.sharedMesh.GetBlendShapeName(bind.Index);
                            if (!expressionMap.ContainsKey(name))
                                expressionMap[name] = key;
                        }
                    }
                }
            }
            Debug.Log($"[Batch Exporter] Expression map has {expressionMap.Count} entries.");

            var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { inputFolder });
            int exported = 0;
            foreach (var guid in guids)
            {
                string clipPath = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null || !clip.isHumanMotion) continue;


                string rel = clipPath.Substring(inputFolder.Length).TrimStart('/', '\\');
                string outRel = Path.ChangeExtension(rel, "vrma");
                string assetPath = ($"{outputFolder}/{outRel}").Replace("\\", "/");

     
                string sysPath = Path.Combine(
                    Application.dataPath,
                    assetPath.Substring("Assets".Length + 1)
                );
                Directory.CreateDirectory(Path.GetDirectoryName(sysPath));

                GameObject referenceObj = null;
                try
                {
                    referenceObj = GetAnimatorOnlyObject(vrmInstance.gameObject);
                    var animatorClean = referenceObj.GetComponent<Animator>();


                    var bytes = AnimationClipToVrmaCore.Create(
                        animatorClean,
                        clip,
                        expressionMap
                    );

                    File.WriteAllBytes(sysPath, bytes);
                    Debug.Log($"[Batch Exporter] Exported: {assetPath}");
                    exported++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed: {clipPath} → {ex.Message}");
                }
                finally
                {
                    if (referenceObj != null)
                        DestroyImmediate(referenceObj);
                }
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Batch Export Complete",
                $"Exported {exported} clip(s) to '{outputFolder}'.", "OK");
        }

        private static GameObject GetAnimatorOnlyObject(GameObject src)
        {
            var animator = src.GetComponent<Animator>();
            if (animator == null) return null;
            return HumanoidBuilder.CreateHumanoid(animator).gameObject;
        }
    }
}
