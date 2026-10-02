using UnityEngine;
using UnityEngine.XR.Hands;

namespace EnergyDuel.Runtime
{
    /// <summary>Drives the ghost-hand mesh from XR Hands joints, or poses it rigidly on a controller. Adapted from GloveBall's VolleyGhostHand.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class EnergyGhostHand : MonoBehaviour
    {
        public EnergyTrackedHand Hand;
        public XRHandSkeletonDriver Skeleton;
        public SkinnedMeshRenderer Mesh;
        private void LateUpdate() => UpdateVisual();
        public void UpdateVisual()
        {
            if (Hand == null || Skeleton == null || Mesh == null) return;
            bool joints = Hand.Source == EnergyTrackedHand.HandsSource;
            Skeleton.enabled = joints;
            Mesh.enabled = Hand.Source != EnergyTrackedHand.LostSource;
            if (Hand.Source == EnergyTrackedHand.ControllerSource)
                Skeleton.rootTransform.SetPositionAndRotation(Hand.transform.position - Hand.transform.forward * .06f, Hand.transform.rotation);
            else if (Hand.Source == EnergyTrackedHand.HeldSource || Hand.Source == EnergyTrackedHand.BriefLossSource)
                Skeleton.rootTransform.SetPositionAndRotation(Hand.transform.position, Hand.transform.rotation);
        }
    }
}
