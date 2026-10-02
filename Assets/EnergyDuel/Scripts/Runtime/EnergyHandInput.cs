using System;
using EnergyDuel.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;

namespace EnergyDuel.Runtime
{
    /// <summary>
    /// One hand's charge / fire / guard intent. Tracked hands go through <see cref="EnergyHandGesture"/>
    /// (fist charges, push fires, upright open hand guards); a controller falls back to
    /// Trigger = charge, release = fire, Grip held = guard. Gameplay listens to the events.
    /// </summary>
    public sealed class EnergyHandInput : MonoBehaviour
    {
        public EnergyTrackedHand Hand;
        public Transform Head;
        public EnergyHandGestureSettings Gesture = new EnergyHandGestureSettings();

        [Header("Controller fallback")]
        public InputActionProperty Trigger;
        public InputActionProperty Grip;

        [Header("Aim assist (yaw only, toward the current target)")]
        [Tooltip("Set by the duel controller: the opponent, or the tutorial target.")]
        public Transform AssistTarget;
        [Range(0f, 1f)] public float AssistStrength = .3f;
        [Range(0f, 45f)] public float AssistMaxDegrees = 15f;

        [Header("Shot origin")]
        [Tooltip("Shots and the charge orb start this far in front of the palm.")]
        [Min(0f)] public float PalmForwardOffset = .08f;

        [Header("Debug")]
        public bool ShowDebugText;
        [Tooltip("Height of the debug readout above the hand.")]
        public float DebugTextHeight = .14f;

        private EnergyHandGesture _gesture;
        private bool _injected;
        private EnergyHandFrame _injectedFrame;
        private bool _controllerCharging;
        private float _controllerCharge;
        private bool _wasGuarding;
        private TextMesh _debugText;

        public EnergyHandSide Side => Hand != null ? Hand.Side : EnergyHandSide.Left;
        public bool IsLeft => Side == EnergyHandSide.Left;
        public bool Present { get; private set; }
        public bool UsingController { get; private set; }
        public Vector3 PalmPosition { get; private set; }
        /// <summary>Out of the palm (tracked hands) or the horizontal head forward (controller).</summary>
        public Vector3 PalmNormal { get; private set; } = Vector3.forward;
        public float Curl { get; private set; } = float.NaN;
        public bool Charging => UsingController ? _controllerCharging : _gesture != null && _gesture.Charging;
        public float Charge01 => UsingController ? _controllerCharge : _gesture != null ? _gesture.Charge01 : 0f;
        public bool Guarding { get; private set; }
        public EnergyHandShape Shape => _gesture != null ? _gesture.Shape : EnergyHandShape.Unknown;
        public Vector3 ChargePoint => PalmPosition + (PalmNormal * PalmForwardOffset);

        public event Action<EnergyHandInput> ChargeStarted;
        public event Action<EnergyHandInput> ChargeEnded;
        public event Action<EnergyHandInput> ChargeFull;
        /// <summary>Origin, direction (unit), charge 0..1.</summary>
        public event Action<EnergyHandInput, Vector3, Vector3, float> Fired;
        public event Action<EnergyHandInput, bool> GuardChanged;

        /// <summary>Automated play: the next Update uses this frame instead of the tracked hand.</summary>
        public void Inject(EnergyHandFrame frame)
        {
            _injected = true;
            _injectedFrame = frame;
        }

        private void Awake() => _gesture = new EnergyHandGesture(Gesture);

        private void OnEnable()
        {
            EnableAction(Trigger.action);
            EnableAction(Grip.action);
        }

        private static void EnableAction(InputAction action)
        {
            if (action != null && !action.enabled) action.Enable();
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (_injected)
            {
                _injected = false;
                var frame = _injectedFrame;
                frame.Dt = dt;
                UseGesture(frame);
            }
            else if (Hand != null && Hand.Source == EnergyTrackedHand.ControllerSource)
            {
                UseController(dt);
            }
            else if (Hand != null && Hand.TryGetTrackedHand(out var xrHand))
            {
                UseGesture(Measure(xrHand, dt));
            }
            else if (Hand != null && Hand.Source == EnergyTrackedHand.BriefLossSource)
            {
                // The pose source is bridging a dropout: hold state, never fire on a guess.
                PalmPosition = Hand.transform.position;
                UseGesture(new EnergyHandFrame { Dt = dt, Tracked = false });
            }
            else
            {
                Lose();
            }

            UpdateDebugText();
        }

        private EnergyHandFrame Measure(XRHand hand, float dt)
        {
            var space = Hand.TrackingSpace;
            var curl = MeanCurl(hand);
            var palmPosition = Hand.transform.position;
            var palmNormal = -Hand.transform.up;
            if (hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm))
            {
                palmPosition = space.TransformPoint(palm.position);
                palmNormal = PalmDirection(space.rotation * palm.rotation);
            }

            var fingers = Hand.transform.forward;
            if (hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist) && hand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out var tip))
            {
                var direction = space.TransformPoint(tip.position) - space.TransformPoint(wrist.position);
                if (direction.sqrMagnitude > 1e-6f) fingers = direction.normalized;
            }

            return new EnergyHandFrame
            {
                Dt = dt,
                Tracked = true,
                Curl = curl,
                HandPosition = palmPosition,
                PalmNormal = palmNormal,
                FingerDirection = fingers,
                HeadPosition = Head.position,
                HeadForward = Head.forward,
            };
        }

        /// <summary>
        /// Out of the palm, for either hand: XR Hands defines PalmDirection as the joint's -Y for both
        /// handednesses (XRHandOrientationUtility.GetHandAxisDirection); only the thumb axis mirrors.
        /// </summary>
        public static Vector3 PalmDirection(Quaternion palmRotation) => palmRotation * Vector3.down;

        /// <summary>Mean FullCurl of index..little; NaN when fewer than three were measurable. The thumb is hidden behind the fist when punching.</summary>
        public static float MeanCurl(XRHand hand)
        {
            var sum = 0f;
            var count = 0;
            for (var finger = XRHandFingerID.Index; finger <= XRHandFingerID.Little; finger++)
            {
                var shape = XRFingerShapeMath.CalculateFingerShape(hand, finger, XRFingerShapeTypes.FullCurl);
                if (shape.TryGetFullCurl(out var curl))
                {
                    sum += curl;
                    count++;
                }
            }

            return count >= 3 ? sum / count : float.NaN;
        }

        private void UseGesture(in EnergyHandFrame frame)
        {
            if (UsingController) LeaveController();
            if (frame.Tracked)
            {
                Present = true;
                PalmPosition = frame.HandPosition;
                if (frame.PalmNormal.sqrMagnitude > 1e-6f) PalmNormal = frame.PalmNormal.normalized;
                Curl = frame.Curl;
            }

            _gesture.Tick(frame);
            if (_gesture.ChargeStarted) ChargeStarted?.Invoke(this);
            if (_gesture.ChargeFull) ChargeFull?.Invoke(this);
            if (_gesture.ChargeCanceled) ChargeEnded?.Invoke(this);
            if (_gesture.FireNow)
            {
                ChargeEnded?.Invoke(this);
                Fire(_gesture.FireDirection, _gesture.FireCharge01);
            }

            SetGuarding(_gesture.Guarding);
        }

        private void UseController(float dt)
        {
            if (!UsingController)
            {
                _gesture.Release();
                if (_gesture.ChargeCanceled) ChargeEnded?.Invoke(this);
                UsingController = true;
            }

            Present = Hand.Ready;
            PalmPosition = Hand.transform.position;
            PalmNormal = EnergyHandGesture.Horizontal(Head.forward);
            Curl = float.NaN;
            var trigger = IsPressed(Trigger.action);
            var grip = IsPressed(Grip.action);

            if (trigger && !_controllerCharging && Present)
            {
                _controllerCharging = true;
                _controllerCharge = 0f;
                SetGuarding(false);
                ChargeStarted?.Invoke(this);
            }
            else if (_controllerCharging && trigger)
            {
                if (_controllerCharge < 1f)
                {
                    _controllerCharge = Mathf.Min(1f, _controllerCharge + (dt / Gesture.ChargeSeconds));
                    if (_controllerCharge >= 1f) ChargeFull?.Invoke(this);
                }
            }
            else if (_controllerCharging && !trigger)
            {
                var charge = _controllerCharge;
                _controllerCharging = false;
                _controllerCharge = 0f;
                ChargeEnded?.Invoke(this);
                Fire(EnergyHandGesture.Aim(Head.forward, Hand.transform.forward, Gesture.AimBlend), charge);
            }

            SetGuarding(grip && !_controllerCharging && Present);
        }

        private void LeaveController()
        {
            UsingController = false;
            if (_controllerCharging) ChargeEnded?.Invoke(this);
            _controllerCharging = false;
            _controllerCharge = 0f;
        }

        private void Lose()
        {
            if (UsingController) LeaveController();
            if (_gesture.Charging || _gesture.Guarding)
            {
                _gesture.Release();
                if (_gesture.ChargeCanceled) ChargeEnded?.Invoke(this);
            }

            Present = false;
            SetGuarding(false);
        }

        private static bool IsPressed(InputAction action) => action != null && action.enabled && action.IsPressed();

        private void SetGuarding(bool guarding)
        {
            Guarding = guarding;
            if (guarding == _wasGuarding) return;
            _wasGuarding = guarding;
            GuardChanged?.Invoke(this, guarding);
        }

        private void Fire(Vector3 direction, float charge)
        {
            var origin = ChargePoint;
            Fired?.Invoke(this, origin, Assist(origin, direction), charge);
        }

        /// <summary>Rotates the shot about world up toward the assist target, by a fraction of the yaw error, within a cone.</summary>
        public Vector3 Assist(Vector3 origin, Vector3 direction)
        {
            if (AssistTarget == null || AssistStrength <= 0f) return direction;
            var toTarget = AssistTarget.position - origin;
            var flatShot = new Vector3(direction.x, 0f, direction.z);
            var flatTarget = new Vector3(toTarget.x, 0f, toTarget.z);
            if (flatShot.sqrMagnitude < 1e-6f || flatTarget.sqrMagnitude < 1e-6f) return direction;
            var yaw = Vector3.SignedAngle(flatShot, flatTarget, Vector3.up);
            if (Mathf.Abs(yaw) > AssistMaxDegrees) return direction;
            return Quaternion.AngleAxis(yaw * AssistStrength, Vector3.up) * direction;
        }

        private void UpdateDebugText()
        {
            if (!ShowDebugText)
            {
                if (_debugText != null) _debugText.gameObject.SetActive(false);
                return;
            }

            if (_debugText == null)
            {
                var go = new GameObject("Energy Debug Text");
                go.transform.SetParent(transform, false);
                _debugText = go.AddComponent<TextMesh>();
                _debugText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                go.GetComponent<MeshRenderer>().sharedMaterial = _debugText.font.material;
                _debugText.characterSize = .01f;
                _debugText.fontSize = 48;
                _debugText.anchor = TextAnchor.LowerCenter;
            }

            _debugText.gameObject.SetActive(true);
            _debugText.transform.position = PalmPosition + (Vector3.up * DebugTextHeight);
            if (Head != null) _debugText.transform.rotation = Quaternion.LookRotation(_debugText.transform.position - Head.position, Vector3.up);
            var source = Hand != null ? Hand.Source : "-";
            _debugText.text = UsingController
                ? $"{source}\ncharge {_controllerCharge:0.00}\nguard {(Guarding ? "ON " : "off")}"
                : $"{source} {Shape,-7}\ncurl {(float.IsNaN(Curl) ? " -- " : Curl.ToString("0.00"))}  fwd {(_gesture != null ? _gesture.ForwardSpeed : 0f),5:0.0}\nreach {(_gesture != null ? _gesture.Reach : 0f):0.00}  charge {Charge01:0.00}\nguard {(Guarding ? "ON " : "off")}";
        }
    }
}
