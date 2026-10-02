using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace EnergyDuel.Runtime
{
    public enum EnergyHandSide { Left, Right }

    public enum EnergyInputMode { Automatic, HandsOnly, ControllersOnly }

    /// <summary>
    /// One coherent pose source per hand: XR Hands first, the controller grip pose as fallback,
    /// with a short dead-reckoned bridge over tracking dropouts. Adapted from GloveBall's
    /// VolleyTrackedHand without its ball-contact and Wide Motion parts, and exposing the
    /// tracked <see cref="XRHand"/> so finger shapes can be read.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class EnergyTrackedHand : MonoBehaviour
    {
        public const string HandsSource = "hands";
        public const string ControllerSource = "controller";
        public const string BriefLossSource = "brief-loss";
        public const string HeldSource = "held";
        public const string LostSource = "lost";

        public EnergyHandSide Side;
        public EnergyInputMode InputMode;
        [Tooltip("Same tracking-space parent as the headset pose driver (Camera Offset).")]
        public Transform TrackingSpace;
        [Tooltip("Optional proxy shown while this hand has a pose.")]
        public Transform Visual;
        [Tooltip("Controller grip pose to hand rotation.")]
        public Vector3 ControllerRotationOffset;
        public Vector3 ControllerPositionOffset = new Vector3(0f, 0f, .07f);
        [Tooltip("Tracking-space speed above which a pose change is treated as a tracking jump.")]
        [Min(.1f)] public float MaximumTrackedSpeed = 8f;
        [Min(.1f)] public float MaximumHandSpeed = 20f;
        [Min(.05f)] public float ReacquireDelay = .15f;
        [Range(0f, .15f)] public float BriefLossSeconds = .10f;
        [Min(0f)] public float MaximumPredictionDistance = .12f;
        [Header("Fast swing: bridge tracking loss")]
        [Tooltip("When the hand was moving at least this fast, a dropout is bridged along the last velocity. 0 s = off.")]
        [Min(0f)] public float FastSwingSpeed = 3f;
        [Range(0f, .4f)] public float FastSwingLossSeconds;
        [Min(0f)] public float FastSwingPredictionDistance;
        [Min(0f)] public float VisualHoldSeconds = 1.5f;

        public bool Ready { get; private set; }
        public string Source { get; private set; } = LostSource;
        public Vector3 Velocity { get; private set; }

        private readonly List<XRHandSubsystem> _subsystems = new List<XRHandSubsystem>();
        private XRHand _trackedHand;
        private bool FastSwing => Velocity.magnitude >= FastSwingSpeed;
        private float LossBridgeSeconds => FastSwing ? Mathf.Max(BriefLossSeconds, FastSwingLossSeconds) : BriefLossSeconds;
        private float LossBridgeDistance => FastSwing ? Mathf.Max(MaximumPredictionDistance, FastSwingPredictionDistance) : MaximumPredictionDistance;
        private Vector3 _lastPosition;
        private Vector3 _lastLocalPosition;
        private float _lastSampleTime;
        private float _stableSince;
        private string _lastSource = LostSource;
        private bool _haveSample;

        /// <summary>The XR Hands hand behind this pose; only while the pose comes from tracked joints this frame.</summary>
        public bool TryGetTrackedHand(out XRHand hand)
        {
            hand = _trackedHand;
            return Source == HandsSource && _trackedHand.isTracked;
        }

        private void Update()
        {
            bool valid = TryPose(out var pose, out var source);
            float now = Time.unscaledTime;
            if (!valid || TrackingSpace == null)
            {
                if (TrackingSpace != null && ContinueBriefLoss(now)) return;
                Ready = false; Velocity = Vector3.zero; Source = LostSource;
                bool hold = _haveSample && now - _lastSampleTime <= VisualHoldSeconds;
                if (hold) Source = HeldSource;
                else _haveSample = false;
                if (Visual != null) Visual.gameObject.SetActive(hold);
                return;
            }

            Vector3 position = TrackingSpace.TransformPoint(pose.position);
            Quaternion rotation = TrackingSpace.rotation * pose.rotation;
            float dt = now - _lastSampleTime;
            float moved = Vector3.Distance(pose.position, _lastLocalPosition);
            bool continuous = _haveSample && source == _lastSource && dt > 0f && dt < Mathf.Max(.12f, BriefLossSeconds + .03f)
                && moved <= MaximumTrackedSpeed * dt + .025f;
            if (!continuous)
            {
                bool recovered = _haveSample && source == _lastSource && dt <= VisualHoldSeconds
                    && moved <= MaximumTrackedSpeed * Mathf.Max(dt, .01f) + .025f;
                _stableSince = recovered ? now - ReacquireDelay : now;
                Velocity = recovered && dt > 0f ? Vector3.ClampMagnitude((position - _lastPosition) / dt, MaximumHandSpeed) : Vector3.zero;
            }
            else Velocity = Vector3.ClampMagnitude((position - _lastPosition) / dt, MaximumHandSpeed);

            transform.SetPositionAndRotation(position, rotation);
            Source = source; _lastSource = source; _lastPosition = position; _lastLocalPosition = pose.position; _lastSampleTime = now;
            _haveSample = true;
            Ready = now - _stableSince >= ReacquireDelay;
            if (Visual != null) Visual.gameObject.SetActive(true);
        }

        private bool ContinueBriefLoss(float now)
        {
            float gap = now - _lastSampleTime;
            if (!_haveSample || !Ready || gap < 0f || gap > LossBridgeSeconds) return false;
            transform.position = _lastPosition + Vector3.ClampMagnitude(Velocity * gap, LossBridgeDistance);
            Source = BriefLossSource; return true;
        }

        private bool TryPose(out Pose pose, out string source)
        {
            pose = default; source = LostSource;
            if (InputMode != EnergyInputMode.ControllersOnly)
            {
                _subsystems.Clear(); SubsystemManager.GetSubsystems(_subsystems);
                foreach (var subsystem in _subsystems)
                {
                    if (!subsystem.running) continue;
                    var hand = Side == EnergyHandSide.Left ? subsystem.leftHand : subsystem.rightHand;
                    if (hand.isTracked && hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out pose))
                    {
                        _trackedHand = hand;
                        source = HandsSource; return true;
                    }
                }
            }

            _trackedHand = default;
            if (InputMode == EnergyInputMode.HandsOnly) return false;
            var device = InputDevices.GetDeviceAtXRNode(Side == EnergyHandSide.Left ? XRNode.LeftHand : XRNode.RightHand);
            if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked
                || !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 p)
                || !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion r)) return false;
            // Do not silently substitute an untracked or synthetic hand pointer for a controller.
            if ((device.characteristics & InputDeviceCharacteristics.Controller) == 0) return false;
            pose = new Pose(p + r * ControllerPositionOffset, r * Quaternion.Euler(ControllerRotationOffset));
            source = ControllerSource; return true;
        }

        private void OnDisable()
        {
            Ready = false; _haveSample = false; Velocity = Vector3.zero; Source = LostSource;
        }
    }
}
