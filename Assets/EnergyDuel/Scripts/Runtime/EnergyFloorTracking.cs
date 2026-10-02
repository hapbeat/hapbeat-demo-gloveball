using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace EnergyDuel.Runtime
{
    /// <summary>
    /// Requests floor tracking from providers that appear after XROrigin's own setup (a late Air Link
    /// connection, for instance) and optionally recentres the player on the start anchor once the
    /// headset tracks. The floor / recentre part of GloveBall's VolleyFloorTracking.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class EnergyFloorTracking : MonoBehaviour
    {
        public XROrigin Origin;
        [Tooltip("Fixed scene destination for the first recentre. XZ and forward are used; calibrated height is preserved.")]
        public Transform RepositionTarget;
        public bool RecenterOnFirstTracking;
        [Tooltip("App-only vertical correction for a miscalibrated runtime floor.")]
        public float HeightCorrection;

        private readonly List<XRInputSubsystem> _inputs = new List<XRInputSubsystem>();
        private XRInputSubsystem _requested;
        private bool _centered;

        private void LateUpdate()
        {
            if (RecenterOnFirstTracking && !_centered && InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked)
            {
                Recenter();
                _centered = true;
            }

            SubsystemManager.GetSubsystems(_inputs);
            foreach (var input in _inputs)
            {
                if (!input.running) continue;
                if (input != _requested && (input.GetSupportedTrackingOriginModes() & TrackingOriginModeFlags.Floor) != 0)
                {
                    if (input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor)) _requested = input;
                }

                var mode = input.GetTrackingOriginMode();
                if (mode != TrackingOriginModeFlags.Floor && mode != TrackingOriginModeFlags.Device) continue;
                var offset = Origin.CameraFloorOffsetObject.transform;
                var p = offset.localPosition;
                p.y = (mode == TrackingOriginModeFlags.Floor ? 0f : Origin.CameraYOffset) + HeightCorrection;
                offset.localPosition = p;
                break;
            }
        }

        /// <summary>App-local recentre: keep the court fixed, move head and hands together.</summary>
        public void Recenter()
        {
            var offset = Origin.CameraFloorOffsetObject.transform;
            var head = Origin.Camera.transform;
            var target = RepositionTarget != null ? RepositionTarget : Origin.transform;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude > .001f)
                offset.RotateAround(head.position, Vector3.up, Vector3.SignedAngle(forward, target.forward, Vector3.up));
            offset.position += Vector3.ProjectOnPlane(target.position - head.position, Vector3.up);
        }
    }
}
