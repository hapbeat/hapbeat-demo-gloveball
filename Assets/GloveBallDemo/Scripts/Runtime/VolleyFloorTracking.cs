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
        [Tooltip("App-only vertical correction for a miscalibrated runtime floor. Set through the standing-height menu.")]
        public float HeightCorrection;
        public float VirtualLift { get; private set; }
        [Min(1f)] public float StandingEyeHeight=1.60f;
        public float EyeHeight=>Origin.Camera.transform.position.y-Origin.transform.position.y;
        readonly List<XRInputSubsystem> _inputs=new List<XRInputSubsystem>();
        XRInputSubsystem _requested;
        void LateUpdate()
        {
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
    }
}
