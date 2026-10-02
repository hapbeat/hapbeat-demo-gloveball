using System;
using UnityEngine;

namespace EnergyDuel.Core
{
    public enum EnergyHandShape { Unknown, Open, Fist }

    /// <summary>Every threshold of the hand gesture, editable in the Inspector and tuned on the headset.</summary>
    [Serializable]
    public sealed class EnergyHandGestureSettings
    {
        [Header("Fist / open (mean FullCurl of index..little, thumb excluded)")]
        [Range(0f, 1f)] public float FistCurl = .70f;
        [Range(0f, 1f)] public float OpenCurl = .40f;

        [Header("Charge")]
        [Min(.05f)] public float ChargeSeconds = 1.2f;

        [Header("Push (hand velocity along the horizontal head forward)")]
        [Min(0f)] public float PushSpeed = 1.2f;
        [Min(0f)] public float PushHoldSeconds = .06f;

        [Header("Fire while pushing: (a) open, (b) reach, (c) timeout")]
        [Tooltip("(b) Distance in front of the head, along the horizontal head forward, that fires the shot.")]
        [Min(0f)] public float FireReach = .50f;
        [Tooltip("(c) Seconds after the push is recognised.")]
        [Min(0f)] public float PushTimeoutSeconds = .25f;
        [Tooltip("0 = head forward only, 1 = push direction only.")]
        [Range(0f, 1f)] public float AimBlend = .5f;

        [Header("Guard (open hand, fingers up, palm forward, in front of face/chest)")]
        [Range(0f, 90f)] public float GuardFingerUpDegrees = 35f;
        [Range(-1f, 1f)] public float GuardPalmForwardDot = .5f;
        public float GuardMinForward = .10f;
        public float GuardMaxForward = .60f;
        [Tooltip("Hand height relative to the head.")]
        public float GuardMinHeight = -.55f;
        public float GuardMaxHeight = .20f;
        [Min(0f)] public float GuardOnSeconds = .12f;
        [Min(0f)] public float GuardOffSeconds = .15f;
    }

    /// <summary>One sample of one hand, in world space.</summary>
    public struct EnergyHandFrame
    {
        public float Dt;
        public bool Tracked;
        /// <summary>Mean FullCurl of index..little. NaN when fewer than three fingers were measurable.</summary>
        public float Curl;
        public Vector3 HandPosition;
        public Vector3 PalmNormal;
        /// <summary>Wrist to middle finger tip.</summary>
        public Vector3 FingerDirection;
        public Vector3 HeadPosition;
        public Vector3 HeadForward;
    }

    /// <summary>
    /// Fist charges, a forward push fires, an upright open hand guards. One instance per hand;
    /// fed one <see cref="EnergyHandFrame"/> per rendered frame. Pure logic, no MonoBehaviour.
    ///
    /// There is no minimum charge and no recast: making a fist again starts the next charge.
    /// </summary>
    public sealed class EnergyHandGesture
    {
        private readonly EnergyHandGestureSettings _settings;

        private bool _charging;
        private bool _armed = true;
        private float _pushCandidateSeconds;
        private float _pushSeconds;
        private Vector3 _pushStart;
        private bool _havePrevious;
        private Vector3 _previousHand;
        private float _previousReach;
        private float _guardCandidateSeconds;
        private float _guardReleaseSeconds;

        public EnergyHandGesture(EnergyHandGestureSettings settings) =>
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        public EnergyHandGestureSettings Settings => _settings;

        public EnergyHandShape Shape { get; private set; } = EnergyHandShape.Unknown;
        public bool Charging => _charging;
        public float Charge01 { get; private set; }
        public bool Pushing { get; private set; }
        public bool Guarding { get; private set; }

        /// <summary>Forward hand speed (m/s) along the horizontal head forward, last tracked frame.</summary>
        public float ForwardSpeed { get; private set; }
        /// <summary>Hand distance in front of the head (m) along the horizontal head forward, last tracked frame.</summary>
        public float Reach { get; private set; }

        // One-frame outputs.
        public bool ChargeStarted { get; private set; }
        public bool ChargeCanceled { get; private set; }
        public bool ChargeFull { get; private set; }
        public bool FireNow { get; private set; }
        public float FireCharge01 { get; private set; }
        public Vector3 FireDirection { get; private set; }

        public void Tick(in EnergyHandFrame frame)
        {
            ChargeStarted = ChargeCanceled = ChargeFull = FireNow = false;

            // Hold everything through a tracking gap; never fire on a guess.
            if (!frame.Tracked)
            {
                _havePrevious = false;
                return;
            }

            // A zero-length frame carries no motion; letting it through would read as a stopped
            // hand and reset the push timer.
            if (frame.Dt <= 0f)
            {
                return;
            }

            UpdateShape(frame.Curl);

            var forward = Horizontal(frame.HeadForward);
            var reach = Vector3.Dot(frame.HandPosition - frame.HeadPosition, forward);
            var dt = Mathf.Max(0f, frame.Dt);
            var forwardSpeed = 0f;
            var reachGrowing = false;
            if (_havePrevious && dt > 0f)
            {
                forwardSpeed = Vector3.Dot(frame.HandPosition - _previousHand, forward) / dt;
                reachGrowing = reach > _previousReach;
            }

            ForwardSpeed = forwardSpeed;
            Reach = reach;

            if (_charging)
            {
                TickCharge(frame, dt, forward, reach, forwardSpeed, reachGrowing);
            }
            else if (Shape == EnergyHandShape.Fist && _armed)
            {
                StartCharge(frame.HandPosition);
            }

            if (Shape != EnergyHandShape.Fist)
            {
                _armed = true;
            }

            TickGuard(frame, dt, forward, reach);

            _previousHand = frame.HandPosition;
            _previousReach = reach;
            _havePrevious = true;
        }

        /// <summary>The hand is gone for longer than the pose source bridges: drop charge and guard.</summary>
        public void Release()
        {
            ChargeStarted = ChargeFull = FireNow = false;
            ChargeCanceled = _charging;
            ResetCharge();
            Guarding = false;
            _guardCandidateSeconds = 0f;
            _guardReleaseSeconds = 0f;
            _havePrevious = false;
        }

        private void UpdateShape(float curl)
        {
            if (float.IsNaN(curl))
            {
                return;
            }

            if (curl >= _settings.FistCurl)
            {
                Shape = EnergyHandShape.Fist;
            }
            else if (curl <= _settings.OpenCurl)
            {
                Shape = EnergyHandShape.Open;
            }
        }

        private void StartCharge(Vector3 hand)
        {
            _charging = true;
            Charge01 = 0f;
            Pushing = false;
            _pushCandidateSeconds = 0f;
            _pushSeconds = 0f;
            _pushStart = hand;
            ChargeStarted = true;
            Guarding = false;
            _guardCandidateSeconds = 0f;
            _guardReleaseSeconds = 0f;
        }

        private void TickCharge(in EnergyHandFrame frame, float dt, Vector3 forward, float reach, float forwardSpeed, bool reachGrowing)
        {
            if (Charge01 < 1f)
            {
                Charge01 = Mathf.Min(1f, Charge01 + (dt / _settings.ChargeSeconds));
                ChargeFull = Charge01 >= 1f;
            }

            if (!Pushing)
            {
                if (forwardSpeed >= _settings.PushSpeed && reachGrowing)
                {
                    if (_pushCandidateSeconds <= 0f)
                    {
                        _pushStart = _previousHand;
                    }

                    _pushCandidateSeconds += dt;
                    // Tolerance absorbs float accumulation of fixed-step frames.
                    if (_pushCandidateSeconds >= _settings.PushHoldSeconds - 1e-4f)
                    {
                        Pushing = true;
                        _pushSeconds = 0f;
                    }
                }
                else
                {
                    _pushCandidateSeconds = 0f;
                }

                if (!Pushing)
                {
                    // Opening the hand without a push cancels the charge.
                    if (Shape == EnergyHandShape.Open)
                    {
                        ChargeCanceled = true;
                        ResetCharge();
                    }

                    return;
                }
            }
            else
            {
                _pushSeconds += dt;
            }

            var open = Shape == EnergyHandShape.Open;
            var reached = reach >= _settings.FireReach;
            var timedOut = _pushSeconds >= _settings.PushTimeoutSeconds - 1e-4f;
            if (!open && !reached && !timedOut)
            {
                return;
            }

            var push = frame.HandPosition - _pushStart;
            var pushDirection = push.sqrMagnitude > 1e-8f ? push.normalized : forward;
            FireNow = true;
            FireCharge01 = Charge01;
            FireDirection = Aim(frame.HeadForward, pushDirection, _settings.AimBlend);
            ResetCharge();
            // A shot fired by reach or timeout leaves a fist; the next charge needs the hand opened first.
            _armed = Shape != EnergyHandShape.Fist;
        }

        private void ResetCharge()
        {
            _charging = false;
            Charge01 = 0f;
            Pushing = false;
            _pushCandidateSeconds = 0f;
            _pushSeconds = 0f;
        }

        private void TickGuard(in EnergyHandFrame frame, float dt, Vector3 forward, float reach)
        {
            // Guard and fist are exclusive.
            if (Shape == EnergyHandShape.Fist || _charging)
            {
                Guarding = false;
                _guardCandidateSeconds = 0f;
                _guardReleaseSeconds = 0f;
                return;
            }

            var candidate = IsGuardPose(frame, forward, reach);
            if (Guarding)
            {
                _guardReleaseSeconds = candidate ? 0f : _guardReleaseSeconds + dt;
                if (_guardReleaseSeconds >= _settings.GuardOffSeconds - 1e-4f)
                {
                    Guarding = false;
                    _guardReleaseSeconds = 0f;
                    _guardCandidateSeconds = 0f;
                }

                return;
            }

            _guardCandidateSeconds = candidate ? _guardCandidateSeconds + dt : 0f;
            if (candidate && _guardCandidateSeconds >= _settings.GuardOnSeconds - 1e-4f)
            {
                Guarding = true;
                _guardReleaseSeconds = 0f;
            }
        }

        private bool IsGuardPose(in EnergyHandFrame frame, Vector3 forward, float reach)
        {
            if (Shape != EnergyHandShape.Open)
            {
                return false;
            }

            if (frame.FingerDirection.sqrMagnitude < 1e-8f || frame.PalmNormal.sqrMagnitude < 1e-8f)
            {
                return false;
            }

            if (Vector3.Angle(frame.FingerDirection, Vector3.up) > _settings.GuardFingerUpDegrees)
            {
                return false;
            }

            if (Vector3.Dot(frame.PalmNormal.normalized, forward) < _settings.GuardPalmForwardDot)
            {
                return false;
            }

            var height = frame.HandPosition.y - frame.HeadPosition.y;
            return reach >= _settings.GuardMinForward && reach <= _settings.GuardMaxForward
                && height >= _settings.GuardMinHeight && height <= _settings.GuardMaxHeight;
        }

        /// <summary>Head forward blended toward the push direction. 0 = head only, 1 = push only.</summary>
        public static Vector3 Aim(Vector3 headForward, Vector3 pushDirection, float blend)
        {
            var head = headForward.sqrMagnitude > 1e-8f ? headForward.normalized : Vector3.forward;
            var push = pushDirection.sqrMagnitude > 1e-8f ? pushDirection.normalized : head;
            var mixed = Vector3.Lerp(head, push, Mathf.Clamp01(blend));
            return mixed.sqrMagnitude > 1e-8f ? mixed.normalized : head;
        }

        /// <summary>Head forward with the pitch removed; falls back to world forward when looking straight up/down.</summary>
        public static Vector3 Horizontal(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude > 1e-8f ? forward.normalized : Vector3.forward;
        }
    }
}
