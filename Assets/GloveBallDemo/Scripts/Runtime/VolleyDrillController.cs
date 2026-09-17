using System.Collections.Generic;
using GloveBallDemo.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace GloveBallDemo.Runtime
{
    public enum VolleyDrill { Receive, Spike }

    /// <summary>One-ball, automatic, button-free drill. Only this component serves balls in volley scenes.</summary>
    public sealed class VolleyDrillController : MonoBehaviour
    {
        public VolleyDrill Drill;
        public Transform Head;
        public Transform CourtFrame;
        public VolleyTrackedHand Left;
        public VolleyTrackedHand Right;
        public BallPool Pool;
        public TargetLayoutField Targets;
        public Text StatusText;
        public Text ScoreText;
        public TargetPanel[] Panels;
        [Header("Feed, relative to current head height; metres / seconds")]
        [Min(.3f)] public float FeedDistance = 5f;
        public float FeedHeightAboveHead = .4f;
        public float ContactHeightFromHead = -.4f;
        [Min(.25f)] public float FlightSeconds = .85f;
        [Min(.15f)] public float ContactForwardDistance = .65f;
        [Min(0f)] public float LateralSpread = .3f;
        [Min(.3f)] public float ServeInterval = 2.5f;
        [Min(0f)] public float ReadySeconds = 2f;
        [Header("Hand response (no auto aim)")]
        [Range(0f, 1f)] public float Restitution = .9f;
        [Range(0f, 3f)] public float SwingGain = 1.25f;
        [Min(1f)] public float MaximumReturnSpeed = 14f;
        [Min(.05f)] public float RehitCooldown = .2f;
        [Min(.1f)] public float MaximumBallAge = 5f;
        public int Returns { get; private set; }
        public int TargetHits { get; private set; }
        public int BodyHits { get; private set; }
        public int Serves { get; private set; }
        public bool TrackingReady => (Left.Ready || Right.Ready) && HeadIsTracked();
        private Ball _ball;
        private float _ballAge;
        private float _nextServe;
        private float _lastContact = -100f;
        private float _trackingStable;
        private Vector3 _previousBallPosition;
        private bool _haveBallSample;
        private bool _subscribed;

        private void Start()
        {
            Targets.BeginWave(0, 3, 3);
            foreach (var panel in Panels) panel.HitFlashCompleted += OnTarget;
            _subscribed = true;
            _nextServe = ServeInterval;
        }
        private void OnDestroy()
        {
            if (_subscribed) foreach (var panel in Panels) if (panel != null) panel.HitFlashCompleted -= OnTarget;
        }

        private bool HeadIsTracked()
        {
            var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return hmd.isValid && hmd.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        }

        private void Update()
        {
            // Temporary hand occlusion must not erase a ball already in flight.
            // Lifetime still advances, even while waiting for tracking to recover.
            if (_ball != null)
            {
                _ballAge += Time.deltaTime;
                if (_ball.State == BallState.Idle || _ball.State == BallState.Dead) _ball = null;
                else if (_ballAge > MaximumBallAge) { _ball.Kill("volley timeout"); _ball = null; }
            }
            if (!TrackingReady)
            {
                _trackingStable = 0f;
                _nextServe = 0f;
            }
            else
            {
                _trackingStable += Time.deltaTime;
                _nextServe -= Time.deltaTime;
                if (_trackingStable >= ReadySeconds && _ball == null && _nextServe <= 0f) Serve();
            }
            if (StatusText != null)
            {
                string action = Drill == VolleyDrill.Receive ? "RECEIVE: angle your hands toward a target" : "SPIKE: strike the dropping ball toward a target";
                string state = !TrackingReady ? "Show hands / pick up controllers" : _trackingStable < ReadySeconds ? "READY " + Mathf.CeilToInt(ReadySeconds - _trackingStable) : action;
                StatusText.text = state + "\nL: " + Left.Source + "   R: " + Right.Source + "   (no buttons)";
            }
            if (ScoreText != null) ScoreText.text = $"{Drill.ToString().ToUpperInvariant()}   TARGET {TargetHits}   RETURNS {Returns}   BODY {BodyHits}";
        }

        private void Serve()
        {
            _ball = Pool.Take();
            if (_ball == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(CourtFrame.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 start = Head.position + forward * FeedDistance + Vector3.up * FeedHeightAboveHead;
            Vector3 destination = Head.position + forward * ContactForwardDistance
                + Vector3.up * ContactHeightFromHead + right * Random.Range(-LateralSpread, LateralSpread);
            // Fixed flight time gives a readable feed independent of the former high-speed launcher rules.
            _ball.Body.linearDamping = 0f;
            _ball.LaunchIncoming(start, VolleyMath.ServeVelocity(start, destination, FlightSeconds, Physics.gravity));
            _previousBallPosition = start; _haveBallSample = true;
            _lastContact = -100f; _ballAge = 0f; _nextServe = ServeInterval;
            Serves++;
        }

        private void FixedUpdate()
        {
            Left.BeginPhysicsSample(); Right.BeginPhysicsSample();
            if (_ball != null && _ball.gameObject.activeInHierarchy && TrackingReady)
            {
                Vector3 current = _ball.Body.position;
                Vector3 previous = _haveBallSample ? _previousBallPosition : current;
                if (Time.time - _lastContact >= RehitCooldown)
                {
                    var sphere = _ball.GetComponent<SphereCollider>();
                    float radius = sphere != null ? sphere.radius * Mathf.Max(_ball.transform.lossyScale.x, _ball.transform.lossyScale.y, _ball.transform.lossyScale.z) : .12f;
                    bool l = Contact(Left, previous, current, radius, out float lt, out var ln);
                    bool r = Contact(Right, previous, current, radius, out float rt, out var rn);
                    if (l || r)
                    {
                        var hand = l && (!r || lt <= rt) ? Left : Right;
                        var normal = hand == Left ? ln : rn;
                        var incoming = _ball.Body.linearVelocity;
                        var velocity = VolleyMath.ReturnVelocity(incoming, hand.Velocity, normal, Restitution, SwingGain, MaximumReturnSpeed);
                        if (_ball.Deflect(velocity))
                        {
                            // Place just clear of the contact volume on the outgoing side, not at an anchor.
                            var volume = hand.ContactVolume;
                            var localHit = volume.transform.InverseTransformPoint(Vector3.Lerp(previous, current, hand == Left ? lt : rt)) - volume.center;
                            var half = volume.size * .5f;
                            for (int axis = 0; axis < 3; axis++) localHit[axis] = Mathf.Clamp(localHit[axis], -half[axis], half[axis]);
                            var localNormal = volume.transform.InverseTransformDirection(normal);
                            for (int axis = 0; axis < 3; axis++)
                                if (Mathf.Abs(localNormal[axis]) > .5f) localHit[axis] = Mathf.Sign(localNormal[axis]) * half[axis];
                            _ball.Body.position = volume.transform.TransformPoint(volume.center + localHit) + normal * (radius + .01f);
                            HapticEventRelay.ReportBallImpact(_ball, hand.Side == GloveSide.Left ? DemoHapticEvent.LeftArmCollide : DemoHapticEvent.RightArmCollide, hand.transform.position);
                            _lastContact = Time.time; Returns++;
                        }
                    }
                }
                _previousBallPosition = _ball.Body.position; _haveBallSample = true;
            }
            else _haveBallSample = false; // Never sweep across an unobserved tracking gap.
            Left.EndPhysicsSample(); Right.EndPhysicsSample();
        }

        private static bool Contact(VolleyTrackedHand hand, Vector3 previous, Vector3 current, float radius, out float fraction, out Vector3 normal)
        {
            fraction = 0f; normal = Vector3.up;
            var box = hand.ContactVolume;
            if (!hand.Ready || box == null || !box.enabled) return false;
            var start = Quaternion.Inverse(hand.PreviousPhysicsRotation) * (previous - hand.PreviousPhysicsPosition) - box.center;
            var end = hand.transform.InverseTransformPoint(current) - box.center;
            if (!VolleyMath.SweptBoxContact(start, end, box.size * .5f, radius, out fraction, out var localNormal)) return false;
            normal = hand.transform.TransformDirection(localNormal);
            return true;
        }

        public void RegisterBodyHit(Ball ball, Vector3 point)
        {
            BodyHits++;
            HapticEventRelay.ReportBallImpact(ball, DemoHapticEvent.BodyCollide, point);
        }
        private void OnTarget(TargetPanel panel)
        {
            TargetHits++;
            HapticEventRelay.Report(DemoHapticEvent.TargetHit, panel.transform.position);
            if (_ball != null) { _ball.Kill("volley target"); _ball = null; }
        }
    }
}
