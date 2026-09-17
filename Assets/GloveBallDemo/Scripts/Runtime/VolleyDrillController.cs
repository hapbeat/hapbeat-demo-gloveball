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
        [Tooltip("Visible emitters. The drill owns timing; their BallLauncher components stay disabled.")]
        public BallLauncher[] FeedLaunchers;
        public VolleyJoinedHands JoinedHands;
        [Header("Feed, relative to current head height; metres / seconds")]
        [Min(.3f)] public float FeedDistance = 5f;
        public float FeedHeightAboveHead = .4f;
        public float ContactHeightFromHead = -.4f;
        [Header("Receive only: floor-relative contact and net clearance")]
        public float ReceiveContactHeight = .65f;
        [Min(0f)] public float ReceiveHeightSpread = .2f;
        public BoxCollider ReceiveNet;
        [Min(.25f)] public float ReceiveMinimumFlightSeconds = .95f;
        [Min(.15f)] public float ReceiveNetClearance = .22f;
        [Min(.25f)] public float FlightSeconds = .85f;
        [Min(.15f)] public float ContactForwardDistance = .65f;
        [Min(0f)] public float LateralSpread = .3f;
        [Min(0f)] public float VerticalSpread = .2f;
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
            Targets.BeginWave(0, Drill == VolleyDrill.Receive ? 1 : 3, Drill == VolleyDrill.Receive ? 1 : 3);
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
            if(GameInputGate.IsBlocked) return;
            if (FeedLaunchers != null && Head != null && CourtFrame != null)
            {
                var target = GetContactCentre();
                foreach (var launcher in FeedLaunchers)
                    if (launcher != null && launcher.TryGetComponent<VolleyFeederAim>(out var aim))
                        aim.Track(target, GetFlightSeconds(launcher.MuzzlePosition,target), Time.deltaTime);
            }
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
            Vector3 start = Head.position + forward * FeedDistance + Vector3.up * FeedHeightAboveHead;
            Vector3 destination = GetServeDestination();
            Vector3 velocity = VolleyMath.ServeVelocity(start, destination, FlightSeconds, Physics.gravity);
            if (FeedLaunchers != null && FeedLaunchers.Length > 0)
            {
                var launcher = FeedLaunchers[Random.Range(0, FeedLaunchers.Length)];
                if (launcher.TryGetComponent<VolleyFeederAim>(out var aim))
                {
                    // Pitch changes the outlet height, which changes the minimum net-clearance flight time.
                    for(int i=0;i<8;i++) velocity=aim.AimForShot(destination,GetFlightSeconds(launcher.MuzzlePosition,destination));
                }
                else velocity = VolleyMath.ServeVelocity(launcher.MuzzlePosition, destination, GetFlightSeconds(launcher.MuzzlePosition,destination), Physics.gravity);
                start = launcher.MuzzlePosition;
            }
            // Fixed flight time gives a readable feed independent of the former high-speed launcher rules.
            _ball.Body.linearDamping = 0f;
            _ball.LaunchIncoming(start, velocity);
            _previousBallPosition = start; _haveBallSample = true;
            _lastContact = -100f; _ballAge = 0f; _nextServe = ServeInterval;
            Serves++;
        }

        public Vector3 GetServeDestination()
        {
            var forward=Vector3.ProjectOnPlane(CourtFrame.forward,Vector3.up).normalized;
            var right=Vector3.Cross(Vector3.up,forward);
            var spread=Drill==VolleyDrill.Receive ? ReceiveHeightSpread : VerticalSpread;
            return GetContactCentre()
                + Vector3.up*Random.Range(-spread,spread)
                + right*Random.Range(-LateralSpread,LateralSpread);
        }

        public Vector3 GetContactCentre()
        {
            var result=Head.position+Vector3.ProjectOnPlane(CourtFrame.forward,Vector3.up).normalized*ContactForwardDistance;
            result.y=Drill==VolleyDrill.Receive ? CourtFrame.position.y+ReceiveContactHeight : Head.position.y+ContactHeightFromHead;
            return result;
        }

        public float GetFlightSeconds(Vector3 start,Vector3 destination)
        {
            if(Drill!=VolleyDrill.Receive || ReceiveNet==null) return FlightSeconds;
            var normal=ReceiveNet.transform.forward;
            var distance=Vector3.Dot(destination-start,normal);
            if(Mathf.Abs(distance)<.001f) return ReceiveMinimumFlightSeconds;
            var fraction=Vector3.Dot(ReceiveNet.bounds.center-start,normal)/distance;
            if(fraction<=0f || fraction>=1f) return ReceiveMinimumFlightSeconds;
            var linearHeight=Mathf.Lerp(start.y,destination.y,fraction);
            var lift=ReceiveNet.bounds.max.y+ReceiveNetClearance-linearHeight;
            var required=Mathf.Sqrt(Mathf.Max(0f,2f*lift/(Mathf.Abs(Physics.gravity.y)*fraction*(1f-fraction))));
            return Mathf.Max(ReceiveMinimumFlightSeconds,required);
        }

        private void FixedUpdate()
        {
            Left.BeginPhysicsSample(); Right.BeginPhysicsSample();
            if(JoinedHands!=null) JoinedHands.Sample(Time.fixedDeltaTime);
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
                    bool joined=JoinedHands!=null && JoinedHands.Joined;
                    if(joined)
                    {
                        l=ContactBox(JoinedHands.Volume,JoinedHands.PreviousPosition,JoinedHands.PreviousRotation,previous,current,radius,out lt,out ln);
                        r=false; // A joined hit is solved once; never also bounce off the individual hands.
                    }
                    if (l || r)
                    {
                        var hand = l && (!r || lt <= rt) ? Left : Right;
                        var normal = hand == Left ? ln : rn;
                        var incoming = _ball.Body.linearVelocity;
                        var velocity = VolleyMath.ReturnVelocity(incoming, joined ? JoinedHands.Velocity : hand.Velocity, normal, Restitution, SwingGain, MaximumReturnSpeed);
                        if (_ball.Deflect(velocity))
                        {
                            // Place just clear of the contact volume on the outgoing side, not at an anchor.
                            var volume = joined ? JoinedHands.Volume : hand.ContactVolume;
                            var localHit = volume.transform.InverseTransformPoint(Vector3.Lerp(previous, current, hand == Left ? lt : rt)) - volume.center;
                            var half = volume.size * .5f;
                            for (int axis = 0; axis < 3; axis++) localHit[axis] = Mathf.Clamp(localHit[axis], -half[axis], half[axis]);
                            var localNormal = volume.transform.InverseTransformDirection(normal);
                            for (int axis = 0; axis < 3; axis++)
                                if (Mathf.Abs(localNormal[axis]) > .5f) localHit[axis] = Mathf.Sign(localNormal[axis]) * half[axis];
                            _ball.Body.position = volume.transform.TransformPoint(volume.center + localHit) + normal * (radius + .01f);
                            HapticEventRelay.ReportBallImpact(_ball, hand.Side == GloveSide.Left ? DemoHapticEvent.LeftArmCollide : DemoHapticEvent.RightArmCollide, hand.transform.position);
                            if(joined) HapticEventRelay.ReportHapticOnly(_ball.ImpactEvent(DemoHapticEvent.RightArmCollide),Right.transform.position);
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
            return ContactBox(box,hand.PreviousPhysicsPosition,hand.PreviousPhysicsRotation,previous,current,radius,out fraction,out normal);
        }

        private static bool ContactBox(BoxCollider box,Vector3 previousPosition,Quaternion previousRotation,Vector3 previous,Vector3 current,float radius,out float fraction,out Vector3 normal)
        {
            normal=Vector3.up;
            var start = Quaternion.Inverse(previousRotation) * (previous - previousPosition) - box.center;
            var end = box.transform.InverseTransformPoint(current) - box.center;
            if (!VolleyMath.SweptBoxContact(start, end, box.size * .5f, radius, out fraction, out var localNormal)) return false;
            normal = box.transform.TransformDirection(localNormal);
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
