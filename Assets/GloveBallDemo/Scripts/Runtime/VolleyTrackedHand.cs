using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace GloveBallDemo.Runtime
{
    public enum VolleyInputMode { Automatic, HandsOnly, ControllersOnly }

    /// <summary>One coherent pose source per hand, with no button or vibration API usage.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class VolleyTrackedHand : MonoBehaviour
    {
        public GloveSide Side;
        public VolleyInputMode InputMode;
        [Tooltip("Same tracking-space parent as the headset pose driver (Camera Offset).")]
        public Transform TrackingSpace;
        public Transform Visual;
        [Tooltip("Controller grip pose to paddle rotation; local Y is the receive face normal.")]
        public Vector3 ControllerRotationOffset;
        public Vector3 ControllerPositionOffset = new Vector3(0f, 0f, .07f);
        [Tooltip("Editable thick receive volume. Local Y is palm normal; X includes thumb side.")]
        public BoxCollider ContactVolume;
        [Tooltip("Tracking-space speed above which a pose change is treated as a tracking jump. Virtual jump lift and stance moves are excluded.")]
        [Min(.1f)] public float MaximumTrackedSpeed = 8f;
        [Tooltip("Upper bound for the world-space hand velocity used for ball returns (swing plus virtual jump lift).")]
        [Min(.1f)] public float MaximumHandSpeed = 20f;
        [Min(.05f)] public float ReacquireDelay = .15f;
        [Range(0f,.15f)] public float BriefLossSeconds=.10f;
        [Min(0f)] public float MaximumPredictionDistance=.12f;
        [Min(0f)] public float VisualHoldSeconds=1.5f;
        [Header("Quest standalone Wide Motion Mode")]
        public bool EnableWideMotion = true;
        [Tooltip("EXPERIMENTAL: allow an estimated wrist to receive/block a ball. No inferred swing velocity or jump. Off until tested on your headset.")]
        public bool AllowEstimatedContacts;
        public bool IsEstimated { get; private set; }
        public bool Ready { get; private set; }
        public string Source { get; private set; } = "lost";
        public Vector3 Velocity { get; private set; }
        public Vector3 PreviousPhysicsPosition { get; private set; }
        public Quaternion PreviousPhysicsRotation { get; private set; }
        public Vector3 Normal => transform.up;
        private readonly List<XRHandSubsystem> _subsystems = new List<XRHandSubsystem>();
        private Vector3 _lastPosition;
        private Vector3 _lastLocalPosition;
        private float _lastSampleTime;
        private float _stableSince;
        private string _lastSource = "lost";
        private bool _haveSample;
        private bool _havePhysicsPose;
        private float _estimatedStableSince;
        private Vector3 _lastEstimatedPosition;

        private void Update()
        {
            bool valid = TryPose(out var pose, out var source);
            float now = Time.unscaledTime;
            var wide=GloveBallWideMotionFeature.Active;
            if(EnableWideMotion && InputMode!=VolleyInputMode.ControllersOnly && source!="controller" && wide!=null)
            {
                wide.Prepare();
                if(!valid && (!Ready || now-_lastSampleTime>BriefLossSeconds) && TrackingSpace!=null && wide.TryGetVisual(Side==GloveSide.Left,out var estimated))
                {ApplyWidePose(estimated,now);return;}
            }
            IsEstimated=false;
            if (!valid || TrackingSpace == null)
            {
                if(TrackingSpace!=null && ContinueBriefLoss(now))return;
                Ready = false; Velocity = Vector3.zero; Source = "lost";
                bool hold=_haveSample && now-_lastSampleTime<=VisualHoldSeconds;
                if(hold)Source="held";
                else _haveSample=false;
                _havePhysicsPose = false;
                if (Visual != null) Visual.gameObject.SetActive(hold);
                return;
            }
            Vector3 position = TrackingSpace.TransformPoint(pose.position);
            Quaternion rotation = TrackingSpace.rotation * pose.rotation;
            float dt = now - _lastSampleTime;
            // Continuity is judged in tracking space: a fast swing during a virtual jump (Camera Offset lift)
            // or a blinked stance move must not look like a tracking glitch and disable contacts.
            float moved=Vector3.Distance(pose.position,_lastLocalPosition);
            bool continuous = _haveSample && source == _lastSource && dt > 0f && dt < Mathf.Max(.12f,BriefLossSeconds+.03f)
                && moved <= MaximumTrackedSpeed * dt + .025f;
            if (!continuous)
            {
                bool recovered=_haveSample && source==_lastSource && dt<=VisualHoldSeconds
                    && moved<=MaximumTrackedSpeed*Mathf.Max(dt,.01f)+.025f;
                _stableSince = recovered ? now-ReacquireDelay : now;
                Velocity = recovered && dt>0f ? Vector3.ClampMagnitude((position-_lastPosition)/dt,MaximumHandSpeed) : Vector3.zero;
                _havePhysicsPose = false;
            }
            else Velocity = Vector3.ClampMagnitude((position - _lastPosition) / dt, MaximumHandSpeed);
            transform.SetPositionAndRotation(position, rotation);
            Source = source; _lastSource = source; _lastPosition = position; _lastLocalPosition = pose.position; _lastSampleTime = now;
            _haveSample = true;
            Ready = now - _stableSince >= ReacquireDelay;
            if (Visual != null) Visual.gameObject.SetActive(true);
        }

        public void ApplyWidePose(Pose pose,float now)
        {
            var position=TrackingSpace.TransformPoint(pose.position);
            if(!IsEstimated || Vector3.Distance(position,_lastEstimatedPosition)>.1f)_estimatedStableSince=now;
            IsEstimated=true;Source="wmm-estimated";_lastSource=Source;
            transform.SetPositionAndRotation(position,TrackingSpace.rotation*pose.rotation);
            _lastEstimatedPosition=position;Velocity=Vector3.zero;
            Ready=AllowEstimatedContacts && now-_estimatedStableSince>=ReacquireDelay;
            // Never sweep the moving estimate through a ball or derive force from an inferred jump.
            _haveSample=false;_havePhysicsPose=false;
            if(Visual!=null)Visual.gameObject.SetActive(true);
        }
        public bool ContinueBriefLoss(float now)
        {
            float gap=now-_lastSampleTime;
            if(!_haveSample || !Ready || gap<0f || gap>BriefLossSeconds)return false;
            transform.position=_lastPosition+Vector3.ClampMagnitude(Velocity*gap,MaximumPredictionDistance);
            Source="brief-loss";return true;
        }

        private bool TryPose(out Pose pose, out string source)
        {
            pose = default; source = "lost";
            if (InputMode != VolleyInputMode.ControllersOnly)
            {
                _subsystems.Clear(); SubsystemManager.GetSubsystems(_subsystems);
                foreach (var subsystem in _subsystems)
                {
                    if (!subsystem.running) continue;
                    var hand = Side == GloveSide.Left ? subsystem.leftHand : subsystem.rightHand;
                    if (hand.isTracked && hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out pose))
                    {
                        // The fixed box and ghost skeleton share the wrist's rigid frame.
                        // Finger articulation must never rotate, resize or translate this volume.
                        source = "hands"; return true;
                    }
                }
            }
            if (InputMode == VolleyInputMode.HandsOnly) return false;
            var device = InputDevices.GetDeviceAtXRNode(Side == GloveSide.Left ? XRNode.LeftHand : XRNode.RightHand);
            if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked
                || !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 p)
                || !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion r)) return false;
            // Do not silently substitute an untracked or synthetic hand pointer for a controller.
            if ((device.characteristics & InputDeviceCharacteristics.Controller) == 0) return false;
            pose = new Pose(p + r * ControllerPositionOffset, r * Quaternion.Euler(ControllerRotationOffset));
            source = "controller"; return true;
        }

        public void BeginPhysicsSample()
        {
            if (!_havePhysicsPose) { PreviousPhysicsPosition = transform.position; PreviousPhysicsRotation = transform.rotation; }
        }
        public void EndPhysicsSample()
        {
            PreviousPhysicsPosition = transform.position;
            PreviousPhysicsRotation = transform.rotation;
            _havePhysicsPose = Ready;
        }
        private void OnDisable()
        {
            Ready = false; IsEstimated=false; _haveSample = false; _havePhysicsPose = false; Velocity = Vector3.zero;
        }
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            if (ContactVolume != null && (!Application.isPlaying || ContactVolume.enabled))
            {
                Gizmos.matrix = ContactVolume.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(ContactVolume.center, ContactVolume.size);
                Gizmos.matrix = Matrix4x4.identity;
            }
            Gizmos.DrawRay(transform.position, Normal * .3f);
        }
    }
}
