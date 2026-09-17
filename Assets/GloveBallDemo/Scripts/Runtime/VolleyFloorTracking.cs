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
                p.y=OffsetForMode(mode,Origin.CameraYOffset);
                offset.localPosition=p;
                break;
            }
        }
        public static float OffsetForMode(TrackingOriginModeFlags mode,float deviceHeight)
            => mode==TrackingOriginModeFlags.Floor ? 0f : deviceHeight;
    }
}
