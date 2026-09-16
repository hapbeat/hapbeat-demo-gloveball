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
            if (!TrackingReady)
            {
                _trackingStable = 0f;
                if (_ball != null) { _ball.Kill("tracking lost"); _ball = null; }
                _nextServe = 0f;
            }
            else
            {
                _trackingStable += Time.deltaTime;
                _nextServe -= Time.deltaTime;
                if (_ball != null)
                {
                    _ballAge += Time.deltaTime;
                    if (_ball.State == BallState.Idle || _ball.State == BallState.Dead) _ball = null;
                    else if (_ballAge > MaximumBallAge) { _ball.Kill("volley timeout"); _ball = null; }
                }
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
                    bool l = Contact(Left, previous, current, radius, out float lt);
                    bool r = Contact(Right, previous, current, radius, out float rt);
                    if (l || r)
                    {
                        var hand = l && (!r || lt <= rt) ? Left : Right;
                        var incoming = _ball.Body.linearVelocity;
                        var velocity = VolleyMath.ReturnVelocity(incoming, hand.Velocity, hand.Normal, Restitution, SwingGain, MaximumReturnSpeed);
                        if (_ball.Deflect(velocity))
                        {
                            // Place just clear of the contact volume on the outgoing side, not at an anchor.
                            Vector3 exit = velocity.sqrMagnitude > .01f ? velocity.normalized : hand.Normal;
                            _ball.Body.position = hand.transform.position + exit * (radius + hand.ContactRadius + .01f);
                            HapticEventRelay.ReportBallImpact(_ball, hand.Side == GloveSide.Left ? DemoHapticEvent.LeftArmCollide : DemoHapticEvent.RightArmCollide, hand.transform.position);
                            _lastContact = Time.time; Returns++;
                        }
                    }
                }
                _previousBallPosition = _ball.Body.position; _haveBallSample = true;
            }
            Left.EndPhysicsSample(); Right.EndPhysicsSample();
        }

        private static bool Contact(VolleyTrackedHand hand, Vector3 previous, Vector3 current, float radius, out float fraction)
        {
            fraction = 0f;
            return hand.Ready && VolleyMath.SweptContact(previous, current, hand.PreviousPhysicsPosition,
                hand.transform.position, radius + hand.ContactRadius, out fraction);
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
