using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UniGLTF;
using UniGLTF.Extensions.VRMC_vrm_animation;
using UnityEditor;
using UnityEngine;
using UniVRM10;

namespace app.c0dt
{
    public class VrmAnimationExporter : gltfExporter
    {
        // (Constructor and other humanoid properties remain the same)
        public VrmAnimationExporter(
                ExportingGltfData data,
                GltfExportSettings settings)
        : base(data, settings)
        {
            settings.InverseAxis = Axes.X;
        }

        readonly List<float> m_times = new();

        class PositionExporter
        {
            public List<Vector3> Values = new();
            public Transform Node;
            readonly Transform m_root;

            public PositionExporter(Transform bone, Transform root)
            {
                Node = bone;
                m_root = root;
            }

            public void Add()
            {
                var p = m_root.worldToLocalMatrix.MultiplyPoint(Node.position);
                Values.Add(new Vector3(-p.x, p.y, p.z));
            }
        }
        PositionExporter m_position;
        public void SetPositionBoneAndParent(Transform bone, Transform parent)
        {
            m_position = new PositionExporter(bone, parent);
        }

        class RotationExporter
        {
            public List<Quaternion> Values = new();
            public readonly Transform Node;
            public Transform m_parent;

            public RotationExporter(Transform bone, Transform parent)
            {
                Node = bone;
                m_parent = parent;
            }

            public void Add()
            {
                var q = Quaternion.Inverse(m_parent.rotation) * Node.rotation;
                Values.Add(new Quaternion(q.x, -q.y, -q.z, q.w));
            }
        }
        readonly Dictionary<HumanBodyBones, RotationExporter> m_rotations = new();
        public void AddRotationBoneAndParent(HumanBodyBones bone, Transform transform, Transform parent)
        {
            m_rotations.Add(bone, new RotationExporter(transform, parent));
        }
        
        // --- MODIFICATION START ---

        private class ExpressionCurve
        {
            public ExpressionKey Key;
            public AnimationCurve Curve;
            public int NodeIndex;
        }
        private readonly List<ExpressionCurve> m_expressions = new();

        /// <summary>
        /// Adds an expression animation curve to the export list.
        /// The curve should be normalized to a 0.0 - 1.0 value range.
        /// </summary>
        /// <param name="key">The expression key (preset or custom).</param>
        /// <param name="normalizedCurve">The normalized animation curve.</param>
        public void AddExpression(ExpressionKey key, AnimationCurve normalizedCurve)
        {
            m_expressions.Add(new ExpressionCurve
            {
                Key = key,
                Curve = normalizedCurve,
            });
        }
        
        // --- MODIFICATION END ---

        public void AddFrame(TimeSpan time)
        {
            m_times.Add((float)time.TotalSeconds);
            m_position?.Add();
            foreach (var kv in m_rotations)
            {
                kv.Value.Add();
            }
        }
        
        // The Export method no longer needs changes. It will process
        // whatever is in m_expressions when it runs.
        public void Export(Action<VrmAnimationExporter> addFrames)
        {
            base.Export();

            addFrames(this);
            
            // Sort expressions to ensure a consistent export order.
            m_expressions.Sort((a, b) => String.Compare(a.Key.Name, b.Key.Name, StringComparison.Ordinal));

            var gltfAnimation = new glTFAnimation { name = "vrm_animation" };
            _data.Gltf.animations.Add(gltfAnimation);
            
            var names = Nodes.Select(x => x.name).ToList();

            var input = _data.ExtendBufferAndGetAccessorIndex(m_times.ToArray());

            // (Humanoid export logic is unchanged)
            if (m_position != null)
            {
                var output = _data.ExtendBufferAndGetAccessorIndex(m_position.Values.ToArray());
                var sampler = gltfAnimation.samplers.Count;
                gltfAnimation.samplers.Add(new glTFAnimationSampler { input = input, output = output, interpolation = "LINEAR", });
                gltfAnimation.channels.Add(new glTFAnimationChannel { sampler = sampler, target = new glTFAnimationTarget { node = names.IndexOf(m_position.Node.name), path = "translation", }, });
            }
            foreach (var kv in m_rotations)
            {
                var output = _data.ExtendBufferAndGetAccessorIndex(kv.Value.Values.ToArray());
                var sampler = gltfAnimation.samplers.Count;
                gltfAnimation.samplers.Add(new glTFAnimationSampler { input = input, output = output, interpolation = "LINEAR", });
                gltfAnimation.channels.Add(new glTFAnimationChannel { sampler = sampler, target = new glTFAnimationTarget { node = names.IndexOf(kv.Value.Node.name), path = "rotation", }, });
            }

            // --- Expression Export Logic (Now runs on data provided by the callback) ---
            foreach (var expression in m_expressions)
            {
                var node = new glTFNode { name = $"__expression_{expression.Key.Name}" };
                expression.NodeIndex = _data.Gltf.nodes.Count;
                _data.Gltf.nodes.Add(node);
            }

            foreach (var expression in m_expressions)
            {
                var keyframes = expression.Curve.keys;
                var times = keyframes.Select(k => k.time).ToArray();
                var values = keyframes.Select(k => new Vector3(k.value, 0, 0)).ToArray();

                var expressionInput = _data.ExtendBufferAndGetAccessorIndex(times);
                var expressionOutput = _data.ExtendBufferAndGetAccessorIndex(values);
                var sampler = gltfAnimation.samplers.Count;
                gltfAnimation.samplers.Add(new glTFAnimationSampler { input = expressionInput, output = expressionOutput, interpolation = "LINEAR", });
                gltfAnimation.channels.Add(new glTFAnimationChannel { sampler = sampler, target = new glTFAnimationTarget { node = expression.NodeIndex, path = "translation", }, });
            }

            var vrmAnimation = VrmAnimationUtil.Create(m_rotations.ToDictionary(kv => kv.Key, kv => kv.Value.Node), names);
            vrmAnimation.Expressions = new Expressions();
            var presetExpressions = new Preset();
            var customExpressions = new Dictionary<string, Expression>();

            foreach(var expression in m_expressions)
            {
                var exp = new Expression { Node = expression.NodeIndex };
                if (expression.Key.Preset != ExpressionPreset.custom)
                {
                    var fieldName = expression.Key.Preset.ToString();

                    var field = typeof(Preset).GetField(
                        fieldName, 
                        BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance
                    );

                    if (field != null)
                    {
                        // Use field.SetValue to assign the value
                        field.SetValue(presetExpressions, exp);
                    }
                }
                else
                {
                    customExpressions.Add(expression.Key.Name, exp);
                }
            }
            vrmAnimation.Expressions.Preset = presetExpressions;
            if(customExpressions.Any()) vrmAnimation.Expressions.Custom = customExpressions;
            
            UniGLTF.Extensions.VRMC_vrm_animation.GltfSerializer.SerializeTo(ref _data.Gltf.extensions, vrmAnimation);
        }
    }
}