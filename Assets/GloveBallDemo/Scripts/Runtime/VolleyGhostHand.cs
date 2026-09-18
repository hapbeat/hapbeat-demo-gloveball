using UnityEngine;
using UnityEngine.XR.Hands;

namespace GloveBallDemo.Runtime
{
    /// <summary>Uses the same XR Hands skeleton driver as Hands Interaction Demo, without its interactors.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class VolleyGhostHand : MonoBehaviour
    {
        public VolleyTrackedHand Hand;
        public XRHandSkeletonDriver Skeleton;
        public SkinnedMeshRenderer Mesh;
        private void LateUpdate()=>UpdateVisual();
        public void UpdateVisual()
        {
            if (Hand == null || Skeleton == null || Mesh == null) return;
            bool joints = Hand.Source == "hands";
            Skeleton.enabled = joints;
            Mesh.enabled = Hand.Source != "lost";
            if (Hand.Source == "controller")
                Skeleton.rootTransform.SetPositionAndRotation(Hand.transform.position - Hand.transform.forward * .06f, Hand.transform.rotation);
            else if(Hand.IsEstimated || Hand.Source=="held" || Hand.Source=="brief-loss")
                Skeleton.rootTransform.SetPositionAndRotation(Hand.transform.position,Hand.transform.rotation);
        }
    }
}
