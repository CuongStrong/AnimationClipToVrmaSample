using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniGLTF;
using UniGLTF.Extensions.VRMC_vrm; // For ExpressionKey
using UnityEditor;
using UnityEngine;
using UniVRM10; // Required for Vrm10Instance and its runtime components

namespace Baxter
{
    public class AnimationClipToVrmaWindow : EditorWindow
    {
        private const string FileExtension = "vrma";

        private Vrm10Instance vrmInstance = null;
        private AnimationClip animationClip = null;

        [MenuItem("VRM1/VRM Animation Exporter")]
        public static void OpenAnimationClipToVrmaWindow()
        {
            var window = (AnimationClipToVrmaWindow)GetWindow(typeof(AnimationClipToVrmaWindow));
            window.titleContent = new GUIContent("VRM Animation Exporter");
            window.Show();
        }

        private void OnGUI()
        {
            minSize = new Vector2(300f, 250f);
            EditorGUIUtility.labelWidth = 150;
            
            EditorGUILayout.Space();
            WrappedLabel("指定したアバターとモーションデータからVRM Animation(.vrma)をエクスポートします。");
            EditorGUILayout.Space();

            if (Application.isPlaying)
            {
                WrappedLabel("This feature is unavailable during play mode.");
            }
            
            EditorGUILayout.LabelField("Avatar (VRM1):");
            vrmInstance = (Vrm10Instance)EditorGUILayout.ObjectField(vrmInstance, typeof(Vrm10Instance), true);
            var avatarIsValid = ShowAvatarValidityGUI();
            
            EditorGUILayout.LabelField("Animation:");
            animationClip = (AnimationClip)EditorGUILayout.ObjectField(animationClip, typeof(AnimationClip));
            var animationIsValid = ShowAnimationClipValidityGUI();

            EditorGUILayout.Space();
            WrappedLabel("The export FPS is fixed at 30.");
            EditorGUILayout.Space();
            
            var canExport = !Application.isPlaying && avatarIsValid && animationIsValid;
            GUI.enabled = canExport;
            if (canExport && GUILayout.Button("Export", GUILayout.MinWidth(100)))
            {
                TrySaveAnimationClip();
            }
            GUI.enabled = true;
        }

        private void TrySaveAnimationClip()
        {
            var saveFilePath = EditorUtility.SaveFilePanel(
                "Save VRM Animation File", "", animationClip.name, FileExtension
            );

            if (string.IsNullOrEmpty(saveFilePath))
            {
                return;
            }

            // --- CORRECTED: Build the expression map automatically from the runtime component ---
            var expressionMap = new Dictionary<string, ExpressionKey>();
            var vrmExpression = vrmInstance.Vrm.Expression;
            if (vrmExpression != null)
            {
                foreach (var expressionClip in vrmExpression.Clips)
                {
                    if (expressionClip.Clip == null) continue;

                    var key = expressionClip.Preset != ExpressionPreset.custom
                        ? ExpressionKey.CreateFromPreset(expressionClip.Preset)
                        : ExpressionKey.CreateCustom(expressionClip.Clip.name);

                    // Use the first blend shape binding as the key for the map
                    if (expressionClip.Clip.MorphTargetBindings.Any())
                    {
                        var firstBind = expressionClip.Clip.MorphTargetBindings[0];
                        
                        // Find the SkinnedMeshRenderer using the relative path
                        var smrTransform = vrmInstance.transform.Find(firstBind.RelativePath);
                        if (smrTransform == null) continue;

                        var smr = smrTransform.GetComponent<SkinnedMeshRenderer>();
                        if (smr != null && smr.sharedMesh != null && firstBind.Index < smr.sharedMesh.blendShapeCount)
                        {
                            var blendShapeName = smr.sharedMesh.GetBlendShapeName(firstBind.Index);
                            if (!expressionMap.ContainsKey(blendShapeName))
                            {
                                expressionMap.Add(blendShapeName, key);
                            }
                        }
                    }
                }
            }
            Debug.Log($"[VRM Animation] Built Expression Map with {expressionMap.Count} entries.");

            GameObject referenceObj = null;
            try
            {
                referenceObj = GetAnimatorOnlyObject(vrmInstance.gameObject);
                
                // Pass the new map to the Create function
                var data = AnimationClipToVrmaCore.Create(
                    referenceObj.GetComponent<Animator>(), 
                    animationClip,
                    expressionMap 
                );
                
                File.WriteAllBytes(saveFilePath, data);
                Debug.Log($"[VRM Animation] File was saved to: {Path.GetFullPath(saveFilePath)}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                if (referenceObj != null)
                {
                    DestroyImmediate(referenceObj);
                }
            }
        }
        
        private bool ShowAvatarValidityGUI()
        {
            if (vrmInstance == null)
            {
                WrappedLabel("*Avatar is not selected.");
                return false;
            }
            var animator = vrmInstance.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
            {
                WrappedLabel("*The selected avatar must have a Humanoid rig.");
                return false;
            }
            return true;
        }

        private bool ShowAnimationClipValidityGUI()
        {
            if (animationClip == null)
            {
                WrappedLabel("*Animation Clip is not selected.");
                return false;
            }
            if (!animationClip.isHumanMotion)
            {
                WrappedLabel("*The Clip is not a Humanoid Animation.");
                return false;
            }
            return true;
        }

        private static GameObject GetAnimatorOnlyObject(GameObject src)
        {
            var animator = src.GetComponent<Animator>();
            if (animator == null) return null;
            var resultAnimator = HumanoidBuilder.CreateHumanoid(animator);
            return resultAnimator.gameObject;
        }

        private static void WrappedLabel(string label)
        {
            var style = new GUIStyle(GUI.skin.label) { wordWrap = true, };
            EditorGUILayout.LabelField(label, style);
        }
    }
}