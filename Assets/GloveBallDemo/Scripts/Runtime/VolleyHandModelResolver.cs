using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// Picks the ghost-hand mesh at runtime. The Unity XR Hands sample meshes are not redistributable as source assets,
    /// so scenes carry no mesh: a private Resources copy wins when it is linked into the project, otherwise the
    /// public placeholder prefab is used. Hit volumes live on <see cref="VolleyTrackedHand"/> and never depend on this mesh.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class VolleyHandModelResolver : MonoBehaviour
    {
        public enum ModelSource { None, Private, Fallback }

        [Tooltip("Resources path of the private hand model, e.g. HapbeatPrivate/UnityHands/LeftHand. Absent in public clones.")]
        public string PrivateResourcePath;
        [Tooltip("Public placeholder used when the private model is not in the project.")]
        public GameObject FallbackPrefab;
        [Tooltip("Applied to the model's skinned mesh (hand pass + depth-only pass).")]
        public Material[] Materials;
        public VolleyGhostHand Ghost;
        public XRHandSkeletonDriver Skeleton;
        public ModelSource Source { get; private set; }
        public GameObject Model { get; private set; }
        public SkinnedMeshRenderer Mesh { get; private set; }

        private void Awake() => Resolve();

        /// <summary>Instantiates the model once and wires the skeleton and ghost visual to it. Safe to call again.</summary>
        public bool Resolve()
        {
            if (Model != null) return true;
            var prefab = LoadPrivateModel(PrivateResourcePath);
            var source = ModelSource.Private;
            if (prefab == null) { prefab = FallbackPrefab; source = ModelSource.Fallback; }
            if (prefab == null)
            {
                Debug.LogError($"[VolleyHands] {name}: no private model at Resources/{PrivateResourcePath} and no fallback prefab.", this);
                return false;
            }
            var model = Instantiate(prefab, transform, false);
            model.name = prefab.name;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            var mesh = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var wrist = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name.EndsWith(XRHandJointID.Wrist.ToString(), StringComparison.OrdinalIgnoreCase));
            if (mesh == null || wrist == null)
            {
                Debug.LogError($"[VolleyHands] {name}: {prefab.name} needs a SkinnedMeshRenderer and a *Wrist joint.", this);
                DestroyModel(model);
                return false;
            }
            if (Materials != null && Materials.Length > 0) mesh.sharedMaterials = Materials;
            mesh.updateWhenOffscreen = true;
            if (Skeleton != null)
            {
                // The driver stays disabled until VolleyGhostHand sees tracked joints, so it never runs without a model.
                Skeleton.rootTransform = wrist;
                Skeleton.jointTransformReferences = new List<JointToTransformReference>();
                var missing = new List<string>();
                Skeleton.FindJointsFromRoot(missing);
                if (missing.Count != 0) Debug.LogError($"[VolleyHands] {name}: {prefab.name} is missing joints: {string.Join(",", missing)}", this);
                Skeleton.InitializeFromSerializedReferences();
            }
            if (Ghost != null) Ghost.Mesh = mesh;
            Model = model; Mesh = mesh; Source = source;
            Debug.Log($"[VolleyHands] {name}: using {source} model {prefab.name}");
            return true;
        }

        public static GameObject LoadPrivateModel(string resourcePath) =>
            string.IsNullOrEmpty(resourcePath) ? null : Resources.Load<GameObject>(resourcePath);

        static void DestroyModel(GameObject model)
        {
            if (Application.isPlaying) Destroy(model); else DestroyImmediate(model);
        }
    }
}
