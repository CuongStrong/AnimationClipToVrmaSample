using System;
using System.Collections.Generic;
using System.Linq;
using UniGLTF;
using UnityEditor;
using UnityEngine;
using UniVRM10;

namespace Baxter
{
    /// <summary>
    /// AnimatorとAnimationClipからVRM Animationファイルのデータを出力する主機能。
    /// ウィンドウ / アセット右クリックのいずれの導線を用いる場合も、本クラスを用いて .vrma のバイナリを生成する。
    /// </summary>
    public static class AnimationClipToVrmaCore
    {
        private const float Frequency = 30f;
        
        /// Creates a VRM Animation (.vrma) file.
        /// </summary>
        /// <param name="humanoid">The Animator component of the humanoid model.</param>
        /// <param name="clip">The AnimationClip containing the humanoid and expression animations.</param>
        /// <param name="expressionMap">Optional. A dictionary that maps blend shape names to VRM ExpressionKeys. If not provided, a direct name mapping will be attempted.</param>
        /// <returns>The exported animation as a byte array.</returns>
        public static byte[] Create(Animator humanoid, AnimationClip clip, IReadOnlyDictionary<string, ExpressionKey> expressionMap = null)
        {
            var data = new ExportingGltfData();
            // Use a compatible exporter, e.g., app.c0dt.VrmAnimationExporter
            using var exporter = new app.c0dt.VrmAnimationExporter(data, new GltfExportSettings());
            
            exporter.Prepare(humanoid.gameObject);
            var go = humanoid.gameObject;

            exporter.Export(anim =>
            {
                // --- Expression Logic ---
                var bindings = AnimationUtility.GetCurveBindings(clip);
                foreach (var binding in bindings)
                {
                    if (binding.type != typeof(SkinnedMeshRenderer) || !binding.propertyName.StartsWith("blendShape."))
                    {
                        continue;
                    }

                    var blendShapeName = binding.propertyName.Substring("blendShape.".Length);
                    ExpressionKey key;

                    // **MODIFICATION**: Check if the optional map was provided.
                    if (expressionMap != null)
                    {
                        // If map is provided, use it for lookup.
                        if (!expressionMap.TryGetValue(blendShapeName, out key))
                        {
                            // If a map is used, we only export what's in the map.
                            continue;
                        }
                    }
                    else
                    {
                        // If no map, fall back to direct mapping.
                        if (Enum.TryParse<ExpressionPreset>(blendShapeName, true, out var preset))
                        {
                            key = ExpressionKey.CreateFromPreset(preset);
                        }
                        else
                        {
                            key = ExpressionKey.CreateCustom(blendShapeName);
                        }
                    }
                    
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    var normalizedKeys = curve.keys.Select(k => new Keyframe(k.time, k.value / 100.0f)).ToArray();
                    var normalizedCurve = new AnimationCurve(normalizedKeys);

                    anim.AddExpression(key, normalizedCurve);
                }
                
                // --- Humanoid Logic (Unchanged) ---
                var map = new Dictionary<HumanBodyBones, Transform>();
                foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (bone == HumanBodyBones.LastBone) continue;
                    var t = humanoid.GetBoneTransform(bone);
                    if (t != null) map.Add(bone, t);
                }

                if (!map.ContainsKey(HumanBodyBones.Hips))
                {
                    // Log error if required Hips bone is missing
                    return;
                }

                var rootTransform = humanoid.avatarRoot != null ? humanoid.avatarRoot : humanoid.transform;
                anim.SetPositionBoneAndParent(map[HumanBodyBones.Hips], rootTransform);
                
                foreach (var kv in map)
                {
                    var vrmBone = Vrm10HumanoidBoneSpecification.ConvertFromUnityBone(kv.Key);
                    var parent = GetParentBone(map, vrmBone) ?? rootTransform;
                    anim.AddRotationBoneAndParent(kv.Key, kv.Value, parent);
                }
                
                var frameCount = Mathf.FloorToInt(clip.length * Frequency);
                for (var i = 0; i <= frameCount; i++)
                {
                    var time = i / Frequency;
                    clip.SampleAnimation(go, Mathf.Min(time, clip.length));
                    anim.AddFrame(TimeSpan.FromSeconds(time));
                }
            });

            return data.ToGlbBytes();
        }
        
        private static Transform GetParentBone(Dictionary<HumanBodyBones, Transform> map, Vrm10HumanoidBones bone)
        {
            while (true)
            {
                if (bone == Vrm10HumanoidBones.Hips)
                {
                    break;
                }
                var parentBone = Vrm10HumanoidBoneSpecification.GetDefine(bone).ParentBone.Value;
                var unityParentBone = Vrm10HumanoidBoneSpecification.ConvertToUnityBone(parentBone);
                if (map.TryGetValue(unityParentBone, out var found))
                {
                    return found;
                }
                bone = parentBone;
            }

            // hips has no parent
            return null;
        }
    }
}