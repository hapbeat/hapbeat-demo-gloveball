using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Unity.XR.CoreUtils;

namespace GloveBallDemo.Runtime
{
    /// <summary>Handle providers that appear after XROrigin's initial setup (e.g. a late Air Link connection).</summary>
    [DefaultExecutionOrder(100)]
    public sealed class VolleyFloorTracking : MonoBehaviour
    {
        public XROrigin Origin;
        [Tooltip("Fixed scene destination for menu reposition and initial placement. XZ and forward are used; calibrated height is preserved.")]
        public Transform RepositionTarget;
        public bool RecenterOnFirstTracking;
        bool _centered;
        [Tooltip("App-only vertical correction for a miscalibrated runtime floor. Set through the standing-height menu.")]
        public float HeightCorrection;
        public float VirtualLift { get; private set; }
        [Min(1f)] public float StandingEyeHeight=1.60f;
        public float EyeHeight=>Origin.Camera.transform.position.y-Origin.transform.position.y;
        readonly List<XRInputSubsystem> _inputs=new List<XRInputSubsystem>();
        XRInputSubsystem _requested;
        void LateUpdate()
        {
            if(RecenterOnFirstTracking && !_centered && InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.isTracked,out bool tracked) && tracked)
            {Recenter();_centered=true;}
            SubsystemManager.GetSubsystems(_inputs);
            foreach(var input in _inputs)
            {
                if(!input.running) continue;
                if(input!=_requested && (input.GetSupportedTrackingOriginModes()&TrackingOriginModeFlags.Floor)!=0)
                {
                    if(input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor)) _requested=input;
                }
                var mode=input.GetTrackingOriginMode();
                if(mode!=TrackingOriginModeFlags.Floor && mode!=TrackingOriginModeFlags.Device) continue;
                var offset=Origin.CameraFloorOffsetObject.transform;
                var p=offset.localPosition;
                p.y=OffsetForMode(mode,Origin.CameraYOffset)+HeightCorrection+VirtualLift;
                offset.localPosition=p;
                break;
            }
        }
        public static float OffsetForMode(TrackingOriginModeFlags mode,float deviceHeight)
            => mode==TrackingOriginModeFlags.Floor ? 0f : deviceHeight;
        public void SetVirtualLift(float value)
        {
            var offset=Origin.CameraFloorOffsetObject.transform;
            var p=offset.localPosition;p.y+=value-VirtualLift;offset.localPosition=p;
            VirtualLift=value;
        }
        public void CalibrateStandingHeight()
        {
            // Apply equally to camera and both tracked hands, not to the court or target floor frame.
            HeightCorrection+=StandingEyeHeight-EyeHeight;
            var offset=Origin.CameraFloorOffsetObject.transform;
            var p=offset.localPosition;p.y+=StandingEyeHeight-EyeHeight;offset.localPosition=p;
        }
        public void ClearCalibration()
        {
            var offset=Origin.CameraFloorOffsetObject.transform;
            var p=offset.localPosition;p.y-=HeightCorrection;offset.localPosition=p;HeightCorrection=0f;
        }
        public void Recenter()
        {
            // App-local recenter: keep the court fixed, move head and hands together.
            // Preserve the explicit height calibration; runtime floor errors are a separate setting.
            SetVirtualLift(0f);
            var offset=Origin.CameraFloorOffsetObject.transform;
            var head=Origin.Camera.transform;
            var target=RepositionTarget!=null?RepositionTarget:Origin.transform;
            var forward=Vector3.ProjectOnPlane(head.forward,Vector3.up);
            if(forward.sqrMagnitude>.001f)
                offset.RotateAround(head.position,Vector3.up,Vector3.SignedAngle(forward,target.forward,Vector3.up));
            offset.position+=Vector3.ProjectOnPlane(target.position-head.position,Vector3.up);
        }
    }
}
