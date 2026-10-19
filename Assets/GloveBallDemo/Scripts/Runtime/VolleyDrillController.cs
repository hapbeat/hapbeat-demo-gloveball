using System.Collections.Generic;
using GloveBallDemo.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace GloveBallDemo.Runtime
{
    public enum VolleyDrill { Receive, Spike, Block }

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
        public VolleyAerialSequence Aerial;
        [Header("Feed, relative to current head height; metres / seconds")]
        [Min(.3f)] public float FeedDistance = 5f;
        public float FeedHeightAboveHead = .4f;
        public float ContactHeightFromHead = -.4f;
        [Header("Receive only: floor-relative contact and net clearance")]
        public float ReceiveContactHeight = .9f;
        [Min(0f)] public float ReceiveHeightSpread = .1f;
        public BoxCollider ReceiveNet;
        [Min(.25f)] public float ReceiveMinimumFlightSeconds = .95f;
        [Min(.15f)] public float ReceiveNetClearance = .22f;
        [Range(0f,1f)] public float ReceiveLobChance=.35f;
        [Min(0f)] public float ReceiveLobExtraSeconds=.3f;
        [Min(.25f)] public float FlightSeconds = .85f;
        [Min(0f)] public float ContactForwardDistance = .65f;
        [Min(0f)] public float LateralSpread = .3f;
        [Min(0f)] public float VerticalSpread = .2f;
        [Min(.3f)] public float ServeInterval = 2.5f;
        [Min(0f)] public float ReadySeconds = 2f;
        [Header("Hand response (no auto aim)")]
        [Range(0f, 1f)] public float Restitution = .9f;
        [System.Serializable]
        public sealed class HandBallResponse
        {
            public BallKind Kind;
            [Range(0f,1.2f)] public float Bounce=1f;
            [Range(0f,2f)] public float ReturnDrag=.1f;
        }
        [Tooltip("Volley-only response, independent of floor physics materials. Bounce multiplies Restitution.")]
        public HandBallResponse[] HandResponses={
            new HandBallResponse{Kind=BallKind.Volleyball,Bounce=1f,ReturnDrag=.08f},
            new HandBallResponse{Kind=BallKind.Perforated,Bounce=.95f,ReturnDrag=.12f},
            new HandBallResponse{Kind=BallKind.Bowling,Bounce=.90f,ReturnDrag=.02f},
            new HandBallResponse{Kind=BallKind.Foam,Bounce=.92f,ReturnDrag=.18f},
            new HandBallResponse{Kind=BallKind.Basketball,Bounce=.95f,ReturnDrag=.04f}};
        [Range(0f, 3f)] public float SwingGain = 1.25f;
        [Min(1f)] public float MaximumReturnSpeed = 14f;
        [Min(.05f)] public float RehitCooldown = .2f;
        [Min(.1f)] public float MaximumBallAge = 5f;
        [Header("Rally spike / block contact")]
        [Tooltip("Extra contact radius for the first touch of a set (spike). Blocks get no assist.")]
        [Min(0f)] public float SpikeTouchAssist = .06f;
        [Tooltip("Hand speed above which a spike follows the swing direction instead of the palm reflection (slower = tip).")]
        [Min(0f)] public float SpikeSwingSpeed = 2.5f;
        [Min(0f)] public float SpikePower = 1.6f;
        [Min(0f)] public float SpikeMinimumSpeed = 10f;
        [Min(1f)] public float SpikeMaximumSpeed = 20f;
        [Tooltip("Largest upward component (sine) allowed for a swing-driven spike.")]
        [Range(-1f,1f)] public float SpikeMaximumRise = .1f;
        [Header("Impact feedback")]
        [Tooltip("Relative normal speed (m/s) that gives nominal haptic gain and volume; slower touches are softer.")]
        [Min(.5f)] public float ImpactReferenceSpeed = 8f;
        [Min(0f)] public float MinimumImpactGain = .2f;
        [Header("Rally: where on the hand the ball is met (spike and block)")]
        [Tooltip("Fraction of the hand length from the wrist (0) to the fingertips (1) beyond which a contact counts as fingertips.")]
        [Range(.3f,1f)] public float FingertipZone = .7f;
        [Tooltip("A fingertip spike keeps this share of its speed (a tip, not a kill).")]
        [Range(0f,1f)] public float FingertipSpikeScale = .55f;
        [Tooltip("A fingertip block only partly turns the ball: 0 = full rebound, 1 = the ball carries on unchanged.")]
        [Range(0f,1f)] public float FingertipBlockPassThrough = .65f;
        [Tooltip("A spike counts as fingertips only from this far along the hand (an open hand meets the ball with the fingers' base).")]
        [Range(.3f,1f)] public float SpikeFingertipZone = .85f;
        [Header("Rally: forearms (block only)")]
        [Tooltip("Block turns add a forearm behind each wrist (no elbow is tracked: along the wrist's back axis, as Energy Duel does), solid like the palm, drawn as a translucent arm.")]
        public bool BlockForearms = true;
        [System.NonSerialized] [Tooltip("The tutorial holds the rally (no new ball) while a text is read.")]
        public bool HoldRally;
        public Vector3 ForearmCentre = new Vector3(0f, 0f, -.15f);
        public Vector3 ForearmSize = new Vector3(.085f, .075f, .26f);
        public Color ForearmColour = new Color(.87f, .67f, .53f, .55f);
        readonly BoxCollider[] _forearm = new BoxCollider[2];
        readonly Renderer[] _forearmVisual = new Renderer[2];
        MaterialPropertyBlock _forearmBlock;
        [Tooltip("A block off the side of the hand (not the palm face, or the outer 20% of its width) barely changes the ball's path: it carries on, slightly turned.")]
        [Range(0f,1f)] public float EdgeBlockPassThrough = .85f;
        [Tooltip("Haptic strength multiplier for a fingertip graze and for a solid palm / wrist contact.")]
        [Min(0f)] public float FingertipHapticScale = .4f;
        [Min(0f)] public float PalmHapticScale = 1.3f;
        [Header("Rally: set (toss) - caught, carried and pushed")]
        [Tooltip("Longest the ball may ride the hands (s); a real set is a short hold, not a bounce.")]
        [Range(.05f,.6f)] public float SetMaxHoldSeconds = .35f;
        [Tooltip("The ball leaves when the hands have pushed up faster than this (m/s) and start to slow down.")]
        [Min(0f)] public float SetReleaseSpeed = .8f;
        [Tooltip("Haptic strength of the soft catch and of the push, relative to a normal impact of the same speed.")]
        [Min(0f)] public float SetCatchHapticScale = .5f;
        [Min(0f)] public float SetReleaseHapticScale = .8f;
        readonly VolleySetHold _setHold = new VolleySetHold();
        [Tooltip("A spike takes the fastest hand velocity of this many physics steps (the swing, not one late sample).")]
        [Range(1,8)] public int SwingHistorySteps = 4;
        readonly Vector3[][] _swing = { new Vector3[8], new Vector3[8] };
        int _swingIndex;
        VolleyTrackedHand _setHand;
        bool _setBoth;
        /// <summary>True while a set is held on the hands.</summary>
        public bool HoldingSet => _setHold.Holding;
        [Min(.1f)] public float MaximumImpactGain = 1.5f;
        [Tooltip("Rally only: a hand within this distance of an untouched ball is reported for the missed-hit hint.")]
        [Min(0f)] public float NearHandDistance = .45f;
        public int Returns { get; private set; }
        public int TargetHits { get; private set; }
        public int BodyHits { get; private set; }
        public int Serves { get; private set; }
        [Min(0f)] public float TrackingGraceSeconds=1.5f;
        public bool TrackingSuspended { get; private set; }
        public bool TrackingReady => _trackingSeen && !TrackingSuspended && HeadIsTracked();
        bool _trackingSeen, _menuPaused, _ownsPause;
        float _trackingLostSeconds, _resumeTimeScale=1f;

        public void TickTracking(float seconds,bool anyHandReady)
        {
            if(anyHandReady){_trackingSeen=true;_trackingLostSeconds=0;}
            else _trackingLostSeconds+=Mathf.Max(0,seconds);
            TrackingSuspended=!anyHandReady && _trackingLostSeconds>=TrackingGraceSeconds;
            RefreshPause();
        }
        public void SetMenuPaused(bool paused){_menuPaused=paused;RefreshPause();}
        void RefreshPause()
        {
            bool pause=_menuPaused||TrackingSuspended;
            if(pause && !_ownsPause){_resumeTimeScale=Time.timeScale;Time.timeScale=0;_ownsPause=true;}
            else if(!pause && _ownsPause){Time.timeScale=_resumeTimeScale;_ownsPause=false;}
        }
        void OnDisable()
        {
            _menuPaused=false;TrackingSuspended=false;RefreshPause();
        }
        private Ball _ball;
        private float _ballAge;
        private float _nextServe;
        private float _lastContact = -100f;
        private float _trackingStable;
        private Vector3 _previousBallPosition;
        private bool _haveBallSample;
        private bool _subscribed;
        [Min(0f)] public float LauncherWarningSeconds=1f;
        BallLauncher _pendingLauncher;
        float _warningRemaining;

        private void Start()
        {
            bool rally=Drill==VolleyDrill.Block && Aerial!=null && Aerial.RallyEnabled;
            if(rally && Application.isPlaying)Aerial.EnsureRallyWiring();
            // The rally scores by landing position; floor targets would only distract.
            if(rally)foreach(var panel in Panels)panel.gameObject.SetActive(false);
            else if(Drill!=VolleyDrill.Block)
                Targets.BeginWave(0, Aerial!=null || Drill==VolleyDrill.Receive ? 1 : 3, Aerial!=null || Drill==VolleyDrill.Receive ? 1 : 3);
            foreach (var panel in Panels){panel.HitRegistered+=OnTargetContact;panel.HitFlashCompleted += OnTarget;}
            _subscribed = true;
            _nextServe = ServeInterval;
        }
        private void OnDestroy()
        {
            if (_subscribed) foreach (var panel in Panels) if (panel != null)
                {panel.HitRegistered-=OnTargetContact;panel.HitFlashCompleted -= OnTarget;}
        }

        private bool HeadIsTracked()
        {
            var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return hmd.isValid && hmd.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        }

        private void Update()
        {
            TickTracking(Time.unscaledDeltaTime,Left.Ready||Right.Ready);
            UpdateForearms();
            if(GameInputGate.IsBlocked || TrackingSuspended) return;
            bool rally=Drill==VolleyDrill.Block && Aerial!=null && Aerial.RallyEnabled;
            if (Aerial==null && FeedLaunchers != null && Head != null && CourtFrame != null)
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
                if (_ball.State == BallState.Idle || _ball.State == BallState.Dead)
                {
                    if(rally)Aerial.RegisterBallUnavailable();
                    _ball = null;
                }
                else if (_ballAge > MaximumBallAge)
                {
                    _ball.Kill("volley timeout");
                    if(rally)Aerial.RegisterBallUnavailable();
                    _ball = null;
                }
            }
            bool continuingAttack=!rally && Drill==VolleyDrill.Block && Aerial!=null && Aerial.AttackStarted && _ball==null;
            if(continuingAttack)Serve();
            if (!TrackingReady)
            {
                if(Aerial!=null && !continuingAttack)Aerial.CancelFeed();
                if(_pendingLauncher!=null){_pendingLauncher.GetComponent<VolleyFeederAim>()?.SetWarning(false);_pendingLauncher=null;}
                _trackingStable = 0f;
                _nextServe = 0f;
            }
            else
            {
                _trackingStable += Time.deltaTime;
                _nextServe -= Time.deltaTime;
                if(rally && !HoldRally && _trackingStable>=ReadySeconds
                    && Aerial.TickRally(Time.deltaTime,_ball!=null,out var rallyStart,out var rallyDestination,out var rallySeconds))
                    LaunchAerialBall(rallyStart,rallyDestination,rallySeconds);
                else if (!rally && !continuingAttack && _trackingStable >= ReadySeconds && _ball == null && _nextServe <= 0f) Serve();
            }
            if (StatusText != null)
            {
                string action = Drill == VolleyDrill.Receive ? "RECEIVE: angle your hands toward a target" : "SPIKE: strike the dropping ball toward a target";
                if(Aerial!=null)action=Aerial.Cue;
                string state = !TrackingReady ? "Show hands / pick up controllers" : _trackingStable < ReadySeconds ? "READY " + Mathf.CeilToInt(ReadySeconds - _trackingStable) : action;
                StatusText.text = state;
            }
            if (ScoreText != null) ScoreText.text = rally ? $"YOU {Aerial.PlayerScore} - {Aerial.OpponentScore} OPP   FIRST TO {Aerial.MatchPoints}   {Aerial.BannerTitle}"
                : Drill==VolleyDrill.Block ? $"BLOCKS {Returns}   BODY {BodyHits}"
                : $"{Drill.ToString().ToUpperInvariant()}   TARGET {TargetHits}   RETURNS {Returns}   BODY {BodyHits}";
        }

        private void Serve()
        {
            if(Aerial==null && FeedLaunchers!=null && FeedLaunchers.Length>0)
            {
                if(_pendingLauncher==null)
                {
                    _pendingLauncher=FeedLaunchers[Random.Range(0,FeedLaunchers.Length)];
                    _pendingLauncher.GetComponent<VolleyFeederAim>()?.SetWarning(true);
                    _warningRemaining=LauncherWarningSeconds;
                }
                _warningRemaining-=Time.deltaTime;
                if(_warningRemaining>0f)return;
            }
            Vector3 forward = Vector3.ProjectOnPlane(CourtFrame.forward, Vector3.up).normalized;
            Vector3 start = Head.position + forward * FeedDistance + Vector3.up * FeedHeightAboveHead;
            Vector3 destination = GetServeDestination();
            float lobExtra=Drill==VolleyDrill.Receive && Random.value<ReceiveLobChance ? ReceiveLobExtraSeconds : 0f;
            VolleyFeederAim firingAim=null;
            Vector3 velocity = VolleyMath.ServeVelocity(start, destination, FlightSeconds, Physics.gravity);
            if(Aerial!=null)
            {
                if(Drill==VolleyDrill.Block && !Aerial.AttackStarted && !Aerial.BeginToss())return;
                if(!Aerial.TickFeed(Time.deltaTime,out start,out destination,out float seconds))return;
                velocity=VolleyMath.ServeVelocity(start,destination,seconds,Physics.gravity);
            }
            else if (FeedLaunchers != null && FeedLaunchers.Length > 0)
            {
                var launcher = _pendingLauncher;
                if (launcher.TryGetComponent<VolleyFeederAim>(out var aim))
                {
                    // Pitch changes the outlet height, which changes the minimum net-clearance flight time.
                    for(int i=0;i<8;i++) velocity=aim.AimForShot(destination,GetFlightSeconds(launcher.MuzzlePosition,destination)+lobExtra,Drill==VolleyDrill.Receive?Time.fixedDeltaTime:0f);
                    firingAim=aim;
                }
                else velocity = VolleyMath.ServeVelocity(launcher.MuzzlePosition, destination, GetFlightSeconds(launcher.MuzzlePosition,destination), Physics.gravity)
                    - (Drill==VolleyDrill.Receive ? .5f*Physics.gravity*Time.fixedDeltaTime : Vector3.zero);
                start = launcher.MuzzlePosition;
            }
            _ball=Aerial!=null ? Aerial.TakePreparedBall() : null;
            if(_ball==null)_ball=Pool.Take();
            if(_ball==null)return;
            // Fixed flight time gives a readable feed independent of the former high-speed launcher rules.
            _ball.Body.linearDamping = 0f;
            _ball.LaunchIncoming(start, velocity);
            if(_pendingLauncher!=null){_pendingLauncher.GetComponent<VolleyFeederAim>()?.SetWarning(false);_pendingLauncher=null;}
            if(firingAim!=null)firingAim.PlayShotFeedback();
            _previousBallPosition = start; _haveBallSample = true;
            _lastContact = -100f; _ballAge = 0f; _nextServe = ServeInterval;
            Serves++;
        }

        void LaunchAerialBall(Vector3 start,Vector3 destination,float seconds)
        {
            if(Aerial==null)return;
            RetireRallyBall();
            _ball=Aerial.TakePreparedBall();
            if(_ball==null)return;
            _ball.Body.linearDamping=0f;
            _ball.LaunchIncoming(start,VolleyMath.ServeVelocity(start,destination,seconds,Physics.gravity));
            _previousBallPosition=start;_haveBallSample=true;_lastContact=-100f;_ballAge=0f;_nextServe=ServeInterval;Serves++;
        }
        /// <summary>The ball currently owned by the drill (in flight or rebounding), if any.</summary>
        public Ball ActiveBall=>_ball;
        /// <summary>Hands the rally ball to a scripted NPC play without killing it.</summary>
        public Ball ReleaseActiveBall()
        {
            var ball=_ball;_ball=null;_haveBallSample=false;
            return ball;
        }
        /// <summary>Rally reset: a blocked or bouncing ball is removed before the next attempt starts.</summary>
        public void RetireRallyBall()
        {
            if(_ball!=null){_ball.Kill("rally reset");_ball=null;}
            _haveBallSample=false;
        }
        /// <summary>The receive drill's 最初からやり直す (shared pause): counters from zero and a fresh serve sequence.</summary>
        public void RestartServes()
        {
            ResetCurrentAttempt();
            Serves=Returns=TargetHits=BodyHits=0;
        }
        public void ResetCurrentAttempt()
        {
            if(Aerial!=null)Aerial.CancelFeed();
            if(_ball!=null){_ball.Kill("volley mode or position change");_ball=null;}
            if(_pendingLauncher!=null){_pendingLauncher.GetComponent<VolleyFeederAim>()?.SetWarning(false);_pendingLauncher=null;}
            _haveBallSample=false;_nextServe=ServeInterval;
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
            if((Drill!=VolleyDrill.Receive && Drill!=VolleyDrill.Block) || ReceiveNet==null) return FlightSeconds;
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

        public Vector3 ReturnForBall(BallFeel feel,Vector3 incoming,Vector3 handVelocity,Vector3 normal)
        {
            float bounce=ResponseFor(feel)?.Bounce ?? 1f;
            return VolleyMath.ReturnVelocity(incoming,handVelocity,normal,Restitution*bounce,SwingGain,MaximumReturnSpeed);
        }
        public HandBallResponse ResponseFor(BallFeel feel)
        {
            if(feel!=null && HandResponses!=null)
                foreach(var response in HandResponses)if(response!=null && response.Kind==feel.Kind)return response;
            return null;
        }

        private void FixedUpdate()
        {
            Left.BeginPhysicsSample(); Right.BeginPhysicsSample();
            _swingIndex = (_swingIndex + 1) % 8;
            _swing[0][_swingIndex] = Left.Ready ? Left.Velocity : Vector3.zero;
            _swing[1][_swingIndex] = Right.Ready ? Right.Velocity : Vector3.zero;
            if(JoinedHands!=null) JoinedHands.Sample(Time.fixedDeltaTime);
            if (_setHold.Holding && (_ball == null || !_ball.gameObject.activeInHierarchy)) _setHold.Cancel();
            if (_setHold.Holding)
            {
                TickSetHold(Time.fixedDeltaTime);
                if (_ball != null) { _previousBallPosition = _ball.Body.position; _haveBallSample = true; }
            }
            else if (_ball != null && _ball.gameObject.activeInHierarchy && TrackingReady)
            {
                Vector3 current = _ball.Body.position;
                Vector3 previous = _haveBallSample ? _previousBallPosition : current;
                // Rally: one player touch per attack. A block that rebounds off the net drops instead of being hit back again.
                bool touchLocked = Aerial != null && Aerial.PlayerTouched;
                if (!touchLocked && Time.time - _lastContact >= RehitCooldown)
                {
                    var sphere = _ball.GetComponent<SphereCollider>();
                    float radius = sphere != null ? sphere.radius * Mathf.Max(_ball.transform.lossyScale.x, _ball.transform.lossyScale.y, _ball.transform.lossyScale.z) : .12f;
                    bool firstSpikeTouch = Aerial != null && Aerial.IsPlayerSpike && Aerial.AwaitingPlayerTouch;
                    float reach = radius + (firstSpikeTouch ? SpikeTouchAssist : 0f);
                    bool l = Contact(Left, previous, current, reach, out float lt, out var ln);
                    bool r = Contact(Right, previous, current, reach, out float rt, out var rn);
                    if(Aerial!=null && Drill==VolleyDrill.Spike)l=false;
                    bool joined=JoinedHands!=null && JoinedHands.Joined;
                    if(joined)
                    {
                        l=ContactBox(JoinedHands.Volume,JoinedHands.PreviousPosition,JoinedHands.PreviousRotation,previous,current,radius,out lt,out ln);
                        r=false; // A joined hit is solved once; never also bounce off the individual hands.
                    }
                    // Block: the forearms are solid too (the hands come first).
                    BoxCollider arm = null;
                    if (!l && !r && !joined && ForearmsActive)
                    {
                        float flt = 0f, frt = 0f; Vector3 fln = Vector3.up, frn = Vector3.up;
                        bool fl = Left.Ready && Forearm(0) != null && ContactBox(Forearm(0), Left.PreviousPhysicsPosition, Left.PreviousPhysicsRotation, previous, current, reach, out flt, out fln);
                        bool fr = Right.Ready && Forearm(1) != null && ContactBox(Forearm(1), Right.PreviousPhysicsPosition, Right.PreviousPhysicsRotation, previous, current, reach, out frt, out frn);
                        if (fl && (!fr || flt <= frt)) { l = true; lt = flt; ln = fln; arm = Forearm(0); }
                        else if (fr) { r = true; rt = frt; rn = frn; arm = Forearm(1); }
                    }
                    if (l || r)
                    {
                        var hand = l && (!r || lt <= rt) ? Left : Right;
                        var normal = hand == Left ? ln : rn;
                        if (!joined && Aerial != null && Aerial.HoldsSet) { BeginSetHold(hand); goto sampled; }
                        // Blocks and spikes both deflect physically; the rally scores where the ball lands.
                        {
                        var incoming = _ball.Body.linearVelocity;
                        var handVelocity = joined ? JoinedHands.Velocity : hand.Velocity;
                        // A spike leaves along the hand's motion at contact (the tangent of the arm's arc); only its speed
                        // takes the fastest recent sample, because tracking reads the hand slow at the moment of contact.
                        var swing = handVelocity;
                        if (!joined)
                        {
                            var fastest = SwingVelocity(hand == Left ? 0 : 1);
                            swing = handVelocity.sqrMagnitude > .25f ? handVelocity.normalized * Mathf.Max(handVelocity.magnitude, fastest.magnitude) : fastest;
                        }
                        bool spikeSwing = firstSpikeTouch && swing.magnitude >= SpikeSwingSpeed;
                        // A spike's speed comes mostly from how high it is met, the swing only adds to it (fast swings lose tracking).
                        var velocity = spikeSwing
                            ? (Aerial != null ? Aerial.SpikeShot(_ball.Body.position, swing, Aerial.SpikeSpeed(_ball.Body.position, swing.magnitude))
                                : VolleyMath.SpikeVelocity(swing,SpikePower,SpikeMinimumSpeed,SpikeMaximumSpeed,SpikeMaximumRise))
                            : ReturnForBall(_ball.Feel,incoming,handVelocity,normal);
                        // Spike and block: fingertips only graze the ball, the palm and wrist meet it solidly.
                        var point = Vector3.Lerp(previous, current, hand == Left ? lt : rt);
                        float zone = joined || arm != null ? 0f : HandZone(hand, point);
                        var touch = Aerial != null ? Aerial.CurrentPlayerTouch : VolleyAerialSequence.PlayerTouch.None;
                        bool zoned = touch == VolleyAerialSequence.PlayerTouch.Spike || touch == VolleyAerialSequence.PlayerTouch.Block;
                        bool spiking = touch == VolleyAerialSequence.PlayerTouch.Spike;
                        bool edge = zoned && !spiking && !joined && arm == null && HandEdge(hand, point, normal);
                        bool tip = zoned && (zone >= (spiking ? SpikeFingertipZone : FingertipZone) || edge);
                        if (tip && Aerial != null) Aerial.ShowTouchFlash(point);
                        if (tip) velocity = touch == VolleyAerialSequence.PlayerTouch.Spike ? velocity * FingertipSpikeScale
                            : Vector3.Lerp(velocity, incoming, edge ? EdgeBlockPassThrough : FingertipBlockPassThrough);
                        else if (touch == VolleyAerialSequence.PlayerTouch.Block && Aerial != null) velocity = Aerial.BlockShot(_ball.Body.position, velocity);
                        if (zoned)
                            Debug.Log($"[VolleyTouch] {touch} zone={zone:F2} tip={tip} edge={edge} swing={swing.magnitude:F1}m/s pitch={Mathf.Atan2(swing.y, new Vector2(swing.x, swing.z).magnitude) * Mathf.Rad2Deg:F0} out={velocity.magnitude:F1}m/s pitch={Mathf.Atan2(velocity.y, new Vector2(velocity.x, velocity.z).magnitude) * Mathf.Rad2Deg:F0}");
                        if(Aerial!=null && Aerial.OverrideTouchVelocity(handVelocity,_ball.Body.position,velocity,out var modelled))velocity=modelled; // serve / set / pass
                        if (_ball.Deflect(velocity))
                        {
                            // Incoming feeds stay calibrated; returns use per-kind gameplay drag, not floor settings.
                            _ball.Body.linearDamping=ResponseFor(_ball.Feel)?.ReturnDrag ?? 0f;
                            // Place just clear of the contact volume on the outgoing side, not at an anchor.
                            var volume = joined ? JoinedHands.Volume : arm != null ? arm : hand.ContactVolume;
                            var localHit = volume.transform.InverseTransformPoint(Vector3.Lerp(previous, current, hand == Left ? lt : rt)) - volume.center;
                            var half = volume.size * .5f;
                            for (int axis = 0; axis < 3; axis++) localHit[axis] = Mathf.Clamp(localHit[axis], -half[axis], half[axis]);
                            var localNormal = volume.transform.InverseTransformDirection(normal);
                            for (int axis = 0; axis < 3; axis++)
                                if (Mathf.Abs(localNormal[axis]) > .5f) localHit[axis] = Mathf.Sign(localNormal[axis]) * half[axis];
                            _ball.Body.position = volume.transform.TransformPoint(volume.center + localHit) + normal * (radius + .01f);
                            float relative = Mathf.Abs(Vector3.Dot(incoming - handVelocity, normal));
                            ReportHandImpactScaled(_ball,hand,joined,ImpactGain(relative)*(zoned ? tip ? FingertipHapticScale : PalmHapticScale : 1f));
                            _lastContact = Time.time; Returns++;
                            if(Aerial!=null){Aerial.RegisterPlayerTouch();if(zoned)Aerial.NoteTouchZone(zone,tip,touch==VolleyAerialSequence.PlayerTouch.Spike);}
                        }
                        }
                    }
                    else if(Aerial!=null && Aerial.AwaitingPlayerTouch)
                    {
                        // Diagnose "I hit it but nothing happened": was a hand there, and could it collide?
                        foreach(var hand in new[]{Left,Right})
                            if(Vector3.Distance(hand.transform.position,current)<NearHandDistance)
                                Aerial.NoteHandNearBall(hand.Ready && hand.ContactVolume!=null && hand.ContactVolume.enabled);
                    }
                }
                sampled:
                if(_ball!=null && !_setHold.Holding)
                {
                    _previousBallPosition = _ball.Body.position; _haveBallSample = true;
                    if(Aerial!=null)
                    {
                        var sphere = _ball.GetComponent<SphereCollider>();
                        float radius = sphere != null ? sphere.radius * Mathf.Max(_ball.transform.lossyScale.x, _ball.transform.lossyScale.y, _ball.transform.lossyScale.z) : .12f;
                        if(Aerial.SampleBall(_ball.Body.position,_ball.Body.linearVelocity,radius,Time.fixedDeltaTime,_ball.Body.linearDamping) && _ball!=null)
                            _ball.Body.linearVelocity=Aerial.DigVelocity; // a defender dug it: visible pop-up, no floor contact
                    }
                }
            }
            else _haveBallSample = false; // Never sweep across an unobserved tracking gap.
            Left.EndPhysicsSample(); Right.EndPhysicsSample();
        }

        bool ForearmsActive => BlockForearms && Aerial != null && Aerial.CurrentPlayerTouch == VolleyAerialSequence.PlayerTouch.Block;
        /// <summary>Behind the wrist, in the hand box's own frame (that frame's previous pose is the hand's, so the sweep stays exact).</summary>
        BoxCollider Forearm(int side)
        {
            if (_forearm[side] != null) return _forearm[side];
            var hand = side == 0 ? Left : Right;
            if (hand == null || hand.ContactVolume == null) return null;
            var go = new GameObject(side == 0 ? "Left forearm" : "Right forearm");
            go.transform.SetParent(hand.ContactVolume.transform, false);
            var box = go.AddComponent<BoxCollider>(); box.isTrigger = true; box.enabled = false;
            var scale = hand.ContactVolume.transform.lossyScale;
            var inverse = new Vector3(1f / Mathf.Max(1e-4f, Mathf.Abs(scale.x)), 1f / Mathf.Max(1e-4f, Mathf.Abs(scale.y)), 1f / Mathf.Max(1e-4f, Mathf.Abs(scale.z)));
            box.center = Vector3.Scale(ForearmCentre, inverse); box.size = Vector3.Scale(ForearmSize, inverse);
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule); visual.name = "Forearm visual";
            if (Application.isPlaying) Destroy(visual.GetComponent<Collider>()); else DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = box.center; visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            visual.transform.localScale = Vector3.Scale(new Vector3(ForearmSize.x, ForearmSize.z * .5f, ForearmSize.y), inverse);
            var renderer = visual.GetComponent<Renderer>();
            if (Aerial != null && Aerial.TargetMaterial != null) renderer.sharedMaterial = Aerial.TargetMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            if (_forearmBlock == null) _forearmBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_forearmBlock); _forearmBlock.SetColor("_BaseColor", ForearmColour); renderer.SetPropertyBlock(_forearmBlock);
            _forearmVisual[side] = renderer;
            return _forearm[side] = box;
        }
        /// <summary>The forearms are shown (and solid) only while the player blocks.</summary>
        void UpdateForearms()
        {
            bool block = BlockForearms && Aerial != null && Aerial.ShowsForearms;
            for (int side = 0; side < 2; side++)
            {
                var hand = side == 0 ? Left : Right;
                if (!block && _forearmVisual[side] == null) continue;
                Forearm(side);
                bool shown = block && hand.Ready;
                // The Energy Duel tutorial's forearm (the shared package's, in the shared hands' look); the capsule only without it.
                var arm = Arm(side);
                if (arm != null)
                {
                    arm.gameObject.SetActive(shown);
                    if (shown)
                    {
                        arm.ApplyPose(hand.transform.position, hand.transform.rotation, 0f, Hapbeat.DemoSwitch.DemoTutorialHand.StraightArm(hand.transform.rotation), ForearmFadeStart);
                        // Only its forearm: the hand itself is the shared tracked hand.
                        foreach (var mesh in arm.GetComponentsInChildren<SkinnedMeshRenderer>(true)) mesh.enabled = false;
                    }
                }
                // The capsule only while the package's forearm is not drawn (no skeleton, or before the shared hands resolve).
                bool joined = shown && arm != null && arm.ForearmShown;
                if (_forearmVisual[side] != null) _forearmVisual[side].enabled = shown && !joined;
                // The tracked hand stays opaque up to the joint where the forearm takes over (no translucent band at the wrist).
                var hands = Hapbeat.DemoSwitch.DemoHands.Instance;
                Vector2? fade = joined ? new Vector2(arm.HandFadeStart, arm.HandFadeLength) : (Vector2?)null;
                if (hands != null && hands.WristFade(side) != fade) hands.SetWristFade(side, fade);
            }
        }
        [Tooltip("Left, right: the Energy Duel tutorial forearm (the shared package's DemoTutorialHand with the placeholder skeleton), set by the scene setup.")]
        public Hapbeat.DemoSwitch.DemoTutorialHand[] BlockArms = new Hapbeat.DemoSwitch.DemoTutorialHand[2];
        [Tooltip("The drawn forearm stays opaque this far past the wrist, then fades out over the package's fade length.")]
        [Min(0f)] public float ForearmFadeStart = .18f;
        Hapbeat.DemoSwitch.DemoTutorialHand Arm(int side)
        {
            var arm = BlockArms != null && side < BlockArms.Length ? BlockArms[side] : null;
            return arm != null && Application.isPlaying && arm.EnsureBuilt() ? arm : null;
        }

        /// <summary>The fastest hand velocity of the last <see cref="SwingHistorySteps"/> physics steps (tracking can lag the swing at contact).</summary>
        Vector3 SwingVelocity(int side)
        {
            var best = _swing[side][_swingIndex];
            for (int k = 1; k < SwingHistorySteps; k++)
            {
                var v = _swing[side][(_swingIndex - k + 8) % 8];
                if (v.sqrMagnitude > best.sqrMagnitude) best = v;
            }
            return best;
        }
        Vector3 SetHandsCentre(out Vector3 velocity)
        {
            // Both hands carry the ball when both are on it; otherwise the hand that caught it.
            var a = HandCentre(Left); var b = HandCentre(Right);
            _setBoth = Left.Ready && Right.Ready && Vector3.Distance(a, b) < .35f;
            if (_setBoth) { velocity = (Left.Velocity + Right.Velocity) * .5f; return (a + b) * .5f; }
            velocity = _setHand.Velocity; return HandCentre(_setHand);
        }
        static Vector3 HandCentre(VolleyTrackedHand hand)
            => hand.ContactVolume != null ? hand.ContactVolume.transform.TransformPoint(hand.ContactVolume.center) : hand.transform.position;
        void BeginSetHold(VolleyTrackedHand hand)
        {
            _setHand = hand;
            var centre = SetHandsCentre(out var handVelocity);
            float relative = (_ball.Body.linearVelocity - handVelocity).magnitude;
            _setHold.MaxSeconds = SetMaxHoldSeconds; _setHold.ReleaseSpeed = SetReleaseSpeed;
            _setHold.Begin(_ball.Body.position, centre);
            _ball.Body.linearVelocity = Vector3.zero; _ball.Body.isKinematic = true;
            SetHaptic(ImpactGain(relative) * SetCatchHapticScale);
            _lastContact = Time.time;
        }
        void TickSetHold(float dt)
        {
            if (_ball == null) { _setHold.Cancel(); return; }
            var centre = SetHandsCentre(out var handVelocity);
            bool lost = !_setHand.Ready;
            if (!_setHold.Step(centre, handVelocity, dt, out var position, out var release) && !lost)
            {
                _ball.Body.MovePosition(position);
                return;
            }
            if (lost) { _setHold.Cancel(); release = handVelocity; }
            _ball.Body.isKinematic = false;
            var velocity = release;
            if (Aerial != null && Aerial.OverrideTouchVelocity(release, _ball.Body.position, release, out var modelled)) velocity = modelled;
            if (!_ball.Deflect(velocity)) return;
            _ball.Body.linearDamping = ResponseFor(_ball.Feel)?.ReturnDrag ?? 0f;
            // One touch, one sound: the catch was heard; the push is felt only.
            SetHaptic(ImpactGain(release.magnitude) * SetReleaseHapticScale, false);
            _lastContact = Time.time; Returns++;
            if (Aerial != null) Aerial.RegisterPlayerTouch();
        }
        void SetHaptic(float strength, bool sound = true)
        {
            if (sound)
            {
                if (_setBoth) ReportHandImpactScaled(_ball, null, true, strength);
                else ReportHandImpactScaled(_ball, _setHand, false, strength);
                return;
            }
            if (_setBoth || _setHand == Left) HapticEventRelay.ReportHapticOnly(_ball.ImpactEvent(DemoHapticEvent.LeftArmCollide), Left.transform.position, strength);
            if (_setBoth || _setHand == Right) HapticEventRelay.ReportHapticOnly(_ball.ImpactEvent(DemoHapticEvent.RightArmCollide), Right.transform.position, strength);
        }

        /// <summary>
        /// True for a contact off the side of the hand: the swept sphere hit a side face of the box (its normal is not
        /// along the palm normal), or the point is in the outer 20% of the hand's width.
        /// </summary>
        public static bool HandEdge(VolleyTrackedHand hand, Vector3 point, Vector3 normal)
        {
            var box = hand.ContactVolume;
            if (box == null) return false;
            if (Mathf.Abs(Vector3.Dot(normal.normalized, box.transform.up)) < .5f) return true;
            var size = Vector3.Scale(box.size, box.transform.lossyScale);
            int width = Mathf.Abs(size.x) <= Mathf.Abs(size.z) ? 0 : 2; // the shorter of the two in-plane axes
            var local = box.transform.InverseTransformPoint(point) - box.center;
            return Mathf.Abs(local[width]) > box.size[width] * .5f * .8f;
        }

        /// <summary>
        /// Where along the hand a world point lies: 0 at the wrist, 1 at the far end of the contact box (the fingertips).
        /// The box is wrist-rigid; its long horizontal axis runs from the wrist toward the fingers.
        /// </summary>
        public static float HandZone(VolleyTrackedHand hand, Vector3 point)
        {
            var box = hand.ContactVolume;
            if (box == null) return 0f;
            var wrist = box.transform.InverseTransformPoint(hand.transform.position);
            var local = box.transform.InverseTransformPoint(point);
            var size = Vector3.Scale(box.size, box.transform.lossyScale);
            int axis = Mathf.Abs(size.x) > Mathf.Abs(size.z) ? 0 : 2;
            if (Mathf.Abs(size.y) > Mathf.Abs(size[axis])) axis = 1;
            float a = box.center[axis] - box.size[axis] * .5f, b = box.center[axis] + box.size[axis] * .5f;
            float tipEnd = Mathf.Abs(b - wrist[axis]) >= Mathf.Abs(a - wrist[axis]) ? b : a;
            float span = tipEnd - wrist[axis];
            return Mathf.Abs(span) < 1e-4f ? 0f : Mathf.Clamp01((local[axis] - wrist[axis]) / span);
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
        private void ReportHandImpact(Ball ball,VolleyTrackedHand hand,bool joined)=>ReportHandImpactScaled(ball,hand,joined,1f);
        /// <summary>Haptic gain and impact volume follow the relative speed along the contact normal.</summary>
        public float ImpactGain(float relativeNormalSpeed)
            =>Mathf.Clamp(relativeNormalSpeed/ImpactReferenceSpeed,MinimumImpactGain,MaximumImpactGain);
        private void ReportHandImpactScaled(Ball ball,VolleyTrackedHand hand,bool joined,float strength)
        {
            if(joined)
            {
                // One audible impact, two explicitly sided haptic events, independent of which box side was hit.
                HapticEventRelay.ReportBallImpact(ball,DemoHapticEvent.LeftArmCollide,Left.transform.position,strength);
                HapticEventRelay.ReportHapticOnly(ball.ImpactEvent(DemoHapticEvent.RightArmCollide),Right.transform.position,strength);
            }
            else HapticEventRelay.ReportBallImpact(ball,hand.Side==GloveSide.Left ? DemoHapticEvent.LeftArmCollide : DemoHapticEvent.RightArmCollide,hand.transform.position,strength);
        }
        private void OnTargetContact(TargetPanel panel,Ball ball)
        {
            TargetHits++;
            HapticEventRelay.PlayAudioOnly(DemoHapticEvent.TargetHit,panel.transform.position);
        }
        private void OnTarget(TargetPanel panel)
        {
            if (_ball != null) { _ball.Kill("volley target"); _ball = null; }
        }
    }
}
