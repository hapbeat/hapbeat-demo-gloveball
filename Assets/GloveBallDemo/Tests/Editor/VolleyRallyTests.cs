using System.Reflection;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Turn=GloveBallDemo.Runtime.VolleyAerialSequence.RallyTurn;
using Phase=GloveBallDemo.Runtime.VolleyAerialSequence.RallyPhase;
using Mode=GloveBallDemo.Runtime.VolleyAerialSequence.RallyMode;

namespace GloveBallDemo.Tests
{
    public class VolleyRallyTests
    {
        const string Scene="Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        VolleyDrillController _drill;
        VolleyAerialSequence _rally;
        VolleyArmJump _jump;
        Vector3 _start,_destination;
        float _seconds;
        bool _launched;
        int _announcements;
        Random.State _random;

        [SetUp]
        public void OpenRallyScene()
        {
            EditorSceneManager.OpenScene(Scene);
            _drill=Object.FindFirstObjectByType<VolleyDrillController>();_rally=_drill.Aerial;_jump=_rally.Jump;
            typeof(BallPool).GetMethod("Awake",Private).Invoke(_drill.Pool,null);
            _rally.TossLauncher.GetComponent<VolleyFeederAim>().ShotClip=null; // No PC audio in tests.
            _rally.SpikeClip=null;
            _jump.Floor.Origin.Camera.transform.localPosition=Vector3.up*1.6f;
            _announcements=0;_rally.TurnAnnounced+=_=>_announcements++;
            _random=Random.state;Random.InitState(311);
        }
        [TearDown]
        public void Clean()
        {
            _rally.CancelFeed();_drill.RetireRallyBall();
            _jump.Floor.SetStanceOffset(Vector3.zero);_jump.ResetJump();
            Random.state=_random;
        }

        /// <summary>One 10 ms frame: jump first (VolleyArmJump runs earlier), then the rally with the drill's real ball state.</summary>
        void Frame()
        {
            _jump.Tick(.01f,true,.2f,.2f,false);
            if(_rally.TickRally(.01f,_drill.ActiveBall!=null,out var s,out var d,out var f)){_launched=true;_start=s;_destination=d;_seconds=f;}
        }
        void RunFor(float seconds){for(float t=0f;t<seconds-1e-4f;t+=.01f)Frame();}
        void RunUntil(System.Func<bool> done,float limit,string what)
        {
            for(float t=0f;t<limit && !done();t+=.01f)Frame();
            Assert.That(done(),Is.True,what);
        }
        void Launch(){_launched=false;RunUntil(()=>_launched,3f,"ball released");}
        Vector3 Forward=>Vector3.ProjectOnPlane(_drill.CourtFrame.forward,Vector3.up).normalized;
        void DiscardPrepared(){var ball=_rally.TakePreparedBall();if(ball!=null)_drill.Pool.Return(ball,"test");}

        [Test]
        public void SceneHasColouredTeamsASetterAndThreeSolidBlockers()
        {
            Assert.That(_rally.RallyEnabled,Is.True);
            Assert.That(_rally.Ally,Is.Not.Null);Assert.That(_rally.Ally,Is.Not.SameAs(_rally.Opponent));
            Assert.That(_rally.Ally.Role,Is.EqualTo(VolleyOpponentPrototype.MotionRole.Set));
            Assert.That(_rally.Ally.OverrideKit && _rally.Opponent.OverrideKit,Is.True);
            Assert.That(_rally.Ally.JerseyColour,Is.Not.EqualTo(_rally.Opponent.JerseyColour),"Teams need different jerseys.");
            Assert.That(_rally.Blockers,Has.Length.EqualTo(3));
            foreach(var blocker in _rally.Blockers)
            {
                Assert.That(blocker.Role,Is.EqualTo(VolleyOpponentPrototype.MotionRole.Block));
                Assert.That(blocker.JerseyColour,Is.EqualTo(_rally.Opponent.JerseyColour));
                Assert.That(blocker.GetComponent<VolleyBlockerContact>().Rally,Is.SameAs(_rally));
                Assert.That(blocker.GetComponentsInChildren<Collider>(true),Has.Length.GreaterThanOrEqualTo(8));
                Assert.That(blocker.LeftHand,Is.Not.Null);Assert.That(blocker.RightHand,Is.Not.Null);
            }
            for(int i=1;i<_drill.FeedLaunchers.Length;i++)Assert.That(_drill.FeedLaunchers[i].gameObject.activeSelf,Is.False,"The pass drops from above, not from a feeder.");
            Assert.That(_drill.Targets.gameObject.activeSelf,Is.False,"Points come from the landing spot, not floor targets.");
            foreach(var ghost in Object.FindObjectsByType<VolleyGhostHand>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                Assert.That(ghost.GetComponent<VolleyHandModelResolver>().Resolve(),Is.True,"Hand mesh is instantiated at runtime.");
                Assert.That(ghost.Mesh.sharedMaterial.name,Is.EqualTo("VolleySkinHand"),"Skin-tone player hands.");
                Assert.That(ghost.Mesh.sharedMaterial.HasProperty("_FadeCenter"),Is.True,"Built on the ghost-hand shader so the wrist fades out.");
                Assert.That(ghost.Mesh.sharedMaterials,Has.Length.EqualTo(2));
                Assert.That(ghost.Mesh.sharedMaterials[1].name,Is.EqualTo("DepthOnly"),"Depth pass keeps fingers and UI from showing through.");
            }
            Assert.That(_drill.Left.FitContactToHand && _drill.Right.FitContactToHand,Is.True,"Hit area follows the visible hand.");
            Assert.That(_rally.Defenders,Has.Length.EqualTo(3));
            foreach(var defender in _rally.Defenders)Assert.That(defender.Role,Is.EqualTo(VolleyOpponentPrototype.MotionRole.Dig));
        }

        [Test]
        public void AlternatingTurnsBlinkMoveTheStanceAndRearrangeTheTeams()
        {
            _rally.Mode=Mode.Alternate;_rally.FirstTurn=Turn.Spike;
            var eye=_drill.Head.position;var home=_rally.Opponent.transform.position;
            Frame();
            Assert.That(_rally.Phase,Is.EqualTo(Phase.TurnChange));
            Assert.That(_jump.Floor.StanceOffset,Is.EqualTo(Vector3.zero),"Nothing moves before the view is dark.");
            RunUntil(()=>_rally.TurnStarted,1f,"spike turn applied");
            Assert.That(_rally.FadeAlpha,Is.EqualTo(1f));Assert.That(_rally.CurrentTurn,Is.EqualTo(Turn.Spike));
            Assert.That(Vector3.Dot(_drill.Head.position-eye,Forward),Is.EqualTo(-_rally.SpikeStandBackDistance).Within(.001f),"Spike stance is further from the net.");
            Assert.That(_rally.Opponent.gameObject.activeSelf,Is.False,"Spike turn: three blockers and three defenders; the attacker is off.");
            Assert.That(_jump.JumpHeight,Is.EqualTo(_rally.SpikeJumpHeight));Assert.That(_jump.Duration,Is.EqualTo(_rally.SpikeJumpSeconds));
            RunUntil(()=>_rally.Phase==Phase.AllyReceive,3f,"first spike attempt");
            Assert.That(_rally.FadeAlpha,Is.Zero);

            int active=0;float netZ=_drill.ReceiveNet.transform.position.z;float? previousX=null;
            for(int i=0;i<_rally.Blockers.Length;i++)if(_rally.ActiveBlockers[i])
            {
                active++;
                var p=_rally.Blockers[i].transform.position;
                Assert.That(_rally.Blockers[i].gameObject.activeSelf,Is.True);
                Assert.That(p.z-netZ,Is.EqualTo(_rally.BlockerNetDistance).Within(.01f));
                if(previousX.HasValue)Assert.That(p.x-previousX.Value,Is.GreaterThanOrEqualTo(_rally.BlockerMinSpacing-.001f));
                previousX=p.x;
                Assert.That(_rally.Blockers[i].JumpHeight,Is.InRange(_rally.BlockerJumpMin,_rally.BlockerJumpMax),"Blockers vary in reach.");
                Assert.That(_rally.Blockers[i].GetComponentInChildren<Collider>().enabled,Is.True);
            }
            Assert.That(active,Is.EqualTo(3),"Three blockers every spike.");
            var dropping=(Ball)typeof(VolleyAerialSequence).GetField("_preparedBall",Private).GetValue(_rally);
            Assert.That(dropping.transform.position.y,Is.GreaterThan(_rally.Ally.ReleasePosition.y+_rally.AllyDropHeight-.05f),"The pass falls onto the setter from above.");
            Launch();
            Assert.That(Vector3.Distance(_start,_rally.Ally.ReleasePosition),Is.LessThan(.001f));
            Assert.That(_seconds,Is.EqualTo(_rally.AllyFlightSeconds));
            Assert.That(_destination.y,Is.EqualTo(_drill.Head.position.y+_jump.JumpHeight+_rally.SpikeReachAboveEye).Within(.001f));
            Assert.That(Vector3.Dot(_destination-_drill.Head.position,Forward),Is.EqualTo(_rally.SpikeContactForward+_rally.SpikeApproachDistance).Within(.001f),
                "The set meets the player at the end of the approach, close to the net.");
            float nearest=float.MaxValue;
            for(int i=0;i<_rally.Blockers.Length;i++)if(_rally.ActiveBlockers[i])
                nearest=Mathf.Min(nearest,Mathf.Abs(_rally.Blockers[i].transform.position.x-_destination.x));
            Assert.That(nearest,Is.LessThanOrEqualTo(_rally.ReadingBlockerRange+.001f),"One blocker reads the set.");
            DiscardPrepared();
            _rally.RegisterBallUnavailable();
            Assert.That(_rally.Outcome,Is.EqualTo("MISSED"));Assert.That(_rally.OpponentScore,Is.EqualTo(1));

            RunUntil(()=>_rally.Phase==Phase.TurnChange,3f,"one spike, then the block turn is announced");
            RunUntil(()=>_rally.CurrentTurn==Turn.Block,1f,"block turn applied");
            Assert.That(_jump.Floor.StanceOffset,Is.EqualTo(Vector3.zero));
            Assert.That(_jump.JumpHeight,Is.EqualTo(_rally.BlockJumpHeight));
            Assert.That(_rally.Opponent.transform.position,Is.EqualTo(home));
            // Six opponents: attacker, setter, idle middle and three defenders; our setter is off court.
            Assert.That(_rally.Opponent.gameObject.activeSelf,Is.True);
            Assert.That(_rally.Blockers[0].Role,Is.EqualTo(VolleyOpponentPrototype.MotionRole.Set));
            Assert.That(_rally.Blockers[0].gameObject.activeSelf && _rally.Blockers[1].gameObject.activeSelf,Is.True);
            Assert.That(_rally.Blockers[2].gameObject.activeSelf,Is.False);
            foreach(var defender in _rally.Defenders)Assert.That(defender.gameObject.activeSelf,Is.True);
            foreach(var blocker in _rally.Blockers)Assert.That(blocker.GetComponentInChildren<Collider>(true).enabled,Is.False,"Nobody is solid in the block turn.");
            Assert.That(_rally.Ally.gameObject.activeSelf,Is.False);
            RunUntil(()=>_rally.Phase==Phase.OpponentReceive,3f,"opponent setter receives the pass");
            RunUntil(()=>_rally.Phase==Phase.OpponentSet,3f,"opponent attack starts");
            Assert.That(_announcements,Is.EqualTo(2));
        }

        [Test]
        public void AutomaticSpikeJumpPeaksWhenTheSetArrivesNotAtRelease()
        {
            _rally.Mode=Mode.SpikeOnly;_jump.AutomaticJump=true;
            RunUntil(()=>_rally.Phase==Phase.AllyReceive,3f,"spike attempt");
            Launch();DiscardPrepared();
            Assert.That(_jump.Jumps,Is.Zero,"No jump while the setter is still receiving.");
            Assert.That(_rally.SpikeJumpTime,Is.GreaterThan(.3f),"Jumping at the set release lands before the ball arrives.");
            RunFor(_rally.SpikeJumpTime-.05f);
            Assert.That(_jump.Jumps,Is.Zero);
            RunFor(_rally.AllyFlightSeconds-_rally.SpikeJumpTime+.05f);
            Assert.That(_jump.Jumps,Is.EqualTo(1));
            Assert.That(_jump.Lift,Is.GreaterThan(_jump.JumpHeight*.95f),"The player is at the top of the jump when the set arrives.");
            var stanceStart=-Forward*_rally.SpikeStandBackDistance;
            Assert.That(Vector3.Dot(_jump.Floor.StanceOffset-stanceStart,Forward),Is.EqualTo(_rally.SpikeApproachDistance).Within(.01f),"Approach finished at the peak.");
            float flight=_rally.AllyFlightSeconds;
            for(int i=0;i<_rally.Blockers.Length;i++)if(_rally.ActiveBlockers[i])
                Assert.That(_rally.BlockerJumpTimes[i]+VolleyOpponentPrototype.BlockPeakPhase*_rally.BlockerJumpSeconds,
                    Is.InRange(flight+_rally.BlockerReactionSeconds-_rally.BlockerTimingJitter-.001f,flight+_rally.BlockerReactionSeconds+_rally.BlockerTimingJitter+.001f));
            _rally.RegisterBallUnavailable();
            RunUntil(()=>_rally.Phase==Phase.TurnChange,4f,"quiet blink back to the approach start");
            RunUntil(()=>_rally.Phase==Phase.AllyReceive,3f,"next spike");
            Assert.That(_jump.Floor.StanceOffset,Is.EqualTo(stanceStart));
            Assert.That(_announcements,Is.EqualTo(1),"The reset blink is not a new turn.");
        }

        [Test]
        public void AttackerBodyTurnTellsTheShotDirectionAndShotsStayReachable()
        {
            _rally.Mode=Mode.BlockOnly;_rally.AttackShotDeviation=0f;
            var home=_rally.Opponent.transform.rotation;
            for(int i=0;i<6;i++)
            {
                RunUntil(()=>_rally.Phase==Phase.OpponentSet,5f,"attack "+i);
                float shown=Quaternion.Angle(home,_rally.Opponent.transform.rotation);
                Assert.That(shown,Is.EqualTo(Mathf.Abs(_rally.AttackYaw)).Within(.01f),"The body turn is visible during the wind-up.");
                Launch();DiscardPrepared();
                var body=Quaternion.AngleAxis(_rally.AttackYaw,Vector3.up)*-Forward;
                var shot=Vector3.ProjectOnPlane(_destination-_start,Vector3.up).normalized;
                Assert.That(Vector3.Angle(body,shot),Is.LessThan(.5f),"With no deviation the shot follows the body.");
                _rally.RegisterBallUnavailable();
            }
            _rally.AttackShotDeviation=20f;
            var far=_drill.Head.position+Forward*3f+Vector3.right*3f;
            for(int i=0;i<40;i++)Assert.That(Mathf.Abs(_rally.AttackLateral(far)),Is.LessThanOrEqualTo(_rally.BlockLateralLimit+.0001f),"Never out of reach.");
        }

        [Test]
        public void BlockerHangsNearTheTopWithArmsUp()
        {
            var blocker=_rally.Blockers[0];blocker.gameObject.SetActive(true);
            blocker.Pose(0f);float rest=blocker.Torso.localPosition.y;
            blocker.Pose(VolleyOpponentPrototype.BlockPeakPhase);float peak=blocker.Torso.localPosition.y-rest;
            foreach(float phase in new[]{.42f,.75f})
            {
                blocker.Pose(phase);
                Assert.That(blocker.Torso.localPosition.y-rest,Is.GreaterThan(peak*.9f),"Still near the top at phase "+phase);
                Assert.That(blocker.RightHand.localPosition.y,Is.GreaterThan(2.1f),"Arms stay up over the net.");
            }
        }

        /// <summary>Integrates a free flight and feeds it to SampleBall at physics rate until the rally decides the point.</summary>
        void Fly(Vector3 position,Vector3 velocity)
        {
            for(int i=0;i<300 && _rally.Phase==Phase.PlayerSpike;i++)
            {
                if(_rally.SampleBall(position,velocity,.11f,.02f))velocity=_rally.DigVelocity;
                velocity+=Physics.gravity*.02f;position+=velocity*.02f;
                if(position.y<.11f){position.y=.11f;velocity.y=-velocity.y*.6f;}
            }
        }

        [Test]
        public void DefendersDigSlowShotsButNotHardSpikesAwayFromThem()
        {
            _rally.Mode=Mode.SpikeOnly;
            RunUntil(()=>_rally.Phase==Phase.AllyReceive,3f,"spike attempt");
            var net=_drill.ReceiveNet.transform.position;
            LaunchLive();_rally.RegisterPlayerTouch();
            // Soft push toward the middle defender (depth 6): about a second in the air.
            Fly(net+new Vector3(0f,3f,-.3f),new Vector3(0f,3.4f,5.4f));
            Assert.That(_rally.Outcome,Is.EqualTo("DUG"));Assert.That(_rally.OpponentScore,Is.EqualTo(1));
            RunUntil(()=>_rally.Phase==Phase.AllyReceive,6f,"next spike");

            LaunchLive();_rally.RegisterPlayerTouch();
            // Hard, steep spike into the gap in front of the defenders: ~0.3 s flight, nobody can get there.
            Fly(net+new Vector3(-1.4f,3.2f,-.3f),new Vector3(0f,-9f,13f));
            Assert.That(_rally.Outcome,Is.EqualTo("POINT!"));Assert.That(_rally.PlayerScore,Is.EqualTo(1));
        }

        [Test]
        public void FastSwingDropoutIsBridgedAlongTheSwingAndStaysCollidable()
        {
            foreach(var hand in new[]{_drill.Left,_drill.Right})
                Assert.That(hand.FastSwingLossSeconds,Is.GreaterThan(hand.BriefLossSeconds),"Rally hands bridge longer dropouts.");
            var go=new GameObject("swing");
            try
            {
                var hand=go.AddComponent<VolleyTrackedHand>();hand.FastSwingLossSeconds=.22f;hand.FastSwingPredictionDistance=.6f;
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(VolleyTrackedHand).GetField("_haveSample",flags).SetValue(hand,true);
                typeof(VolleyTrackedHand).GetField("_lastSampleTime",flags).SetValue(hand,1f);
                typeof(VolleyTrackedHand).GetField("<Ready>k__BackingField",flags).SetValue(hand,true);
                typeof(VolleyTrackedHand).GetField("<Velocity>k__BackingField",flags).SetValue(hand,new Vector3(0f,-4f,6f));
                Assert.That(hand.ContinueBriefLoss(1.18f),Is.True,"0.18 s dropout during a hard swing is bridged.");
                Assert.That(hand.Ready,Is.True);
                Assert.That(Vector3.Angle(hand.transform.position,new Vector3(0f,-4f,6f)),Is.LessThan(.1f),"Continues along the swing.");
                Assert.That(hand.transform.position.magnitude,Is.LessThanOrEqualTo(.6001f));
                Assert.That(hand.ContinueBriefLoss(1.3f),Is.False);
            }
            finally{Object.DestroyImmediate(go);}
        }

        [Test]
        public void ImpactFeedbackScalesWithRelativeSpeed()
        {
            Assert.That(_drill.ImpactGain(2f),Is.LessThan(_drill.ImpactGain(8f)));
            Assert.That(_drill.ImpactGain(8f),Is.EqualTo(1f).Within(.001f));
            Assert.That(_drill.ImpactGain(.1f),Is.EqualTo(_drill.MinimumImpactGain));
            Assert.That(_drill.ImpactGain(40f),Is.EqualTo(_drill.MaximumImpactGain));
        }

        [Test]
        public void FastBounceBetweenSamplesStillLandsWhereTheBallTouchedDown()
        {
            _rally.Mode=Mode.SpikeOnly;
            RunUntil(()=>_rally.Phase==Phase.AllyReceive,3f,"spike attempt");
            LaunchLive();_rally.RegisterPlayerTouch();
            var net=_drill.ReceiveNet.transform.position;
            Vector3 won=default;_rally.PointScored+=(player,at)=>won=at;
            // 20 ms physics steps: above the floor descending, then already rising again. Never sampled below 0.3 m.
            _rally.SampleBall(net+new Vector3(4.3f,.35f,3f),new Vector3(0f,-12f,6f),.11f,.02f);
            Assert.That(_rally.Phase,Is.EqualTo(Phase.PlayerSpike));
            _rally.SampleBall(net+new Vector3(4.3f,.3f,3.12f),new Vector3(0f,8f,5f),.11f,.02f);
            Assert.That(_rally.Outcome,Is.EqualTo("POINT!"),"Touchdown inside the sideline is in, even if the ball then rolls out.");
            Assert.That(won.z-net.z,Is.InRange(3f,3.13f));Assert.That(won.x,Is.EqualTo(4.3f).Within(.001f));
        }

        [Test]
        public void SwingDrivenSpikeFollowsTheSwingAndNeverClimbs()
        {
            var down=GloveBallDemo.Core.VolleyMath.SpikeVelocity(new Vector3(0f,-6f,8f),1.6f,10f,20f,.1f);
            Assert.That(down.normalized,Is.EqualTo(new Vector3(0f,-.6f,.8f)).Using(Vector3EqualityComparerWithin(.001f)));
            Assert.That(down.magnitude,Is.EqualTo(16f).Within(.001f));
            var up=GloveBallDemo.Core.VolleyMath.SpikeVelocity(new Vector3(0f,5f,3f),1.6f,10f,20f,.1f);
            Assert.That(up.normalized.y,Is.EqualTo(.1f).Within(.001f),"An upward swing is flattened.");
            Assert.That(GloveBallDemo.Core.VolleyMath.SpikeVelocity(new Vector3(0f,0f,2.6f),1.6f,10f,20f,.1f).magnitude,Is.EqualTo(10f).Within(.001f));
            Assert.That(GloveBallDemo.Core.VolleyMath.SpikeVelocity(new Vector3(0f,-20f,20f),1.6f,10f,20f,.1f).magnitude,Is.EqualTo(20f).Within(.001f));
        }
        static System.Collections.Generic.IEqualityComparer<Vector3> Vector3EqualityComparerWithin(float tolerance)
            =>new VectorWithin(tolerance);
        sealed class VectorWithin:System.Collections.Generic.IEqualityComparer<Vector3>
        {
            readonly float _t;public VectorWithin(float t)=>_t=t;
            public bool Equals(Vector3 a,Vector3 b)=>Vector3.Distance(a,b)<=_t;
            public int GetHashCode(Vector3 v)=>0;
        }

        [TestCase(Mode.SpikeOnly,Turn.Spike)] [TestCase(Mode.BlockOnly,Turn.Block)]
        public void SingleTurnModesNeverSwitch(Mode mode,Turn turn)
        {
            _rally.Mode=mode;_rally.FirstTurn=turn==Turn.Spike ? Turn.Block : Turn.Spike;
            var attempt=turn==Turn.Spike ? Phase.AllyReceive : Phase.OpponentSet;
            for(int i=0;i<3;i++)
            {
                RunUntil(()=>_rally.Phase==attempt,4f,"attempt "+i);
                Assert.That(_rally.CurrentTurn,Is.EqualTo(turn));
                Launch();DiscardPrepared();
                _rally.RegisterBallUnavailable();
                Assert.That(_rally.Phase,Is.EqualTo(Phase.Recovery));
            }
            Assert.That(_announcements,Is.EqualTo(1),"Only the initial turn is announced.");
        }

        Ball LaunchLive()
        {
            Launch();
            typeof(VolleyDrillController).GetMethod("LaunchAerialBall",Private).Invoke(_drill,new object[]{_start,_destination,_seconds});
            Assert.That(_drill.ActiveBall,Is.Not.Null);
            return _drill.ActiveBall;
        }
        /// <summary>Puts the live ball on the floor at court coordinates (x = right, depth = toward the opponent) and ticks one frame.</summary>
        void LandAt(Ball ball,float x,float depth)
        {
            ball.Body.isKinematic=true;
            var net=_drill.ReceiveNet.transform.position;
            ball.transform.position=new Vector3(net.x+x,.1f,net.z)+Forward*depth;
            Frame();
        }

        [Test]
        public void SpikePointsAreDecidedWhereTheBallLands()
        {
            _rally.Mode=Mode.SpikeOnly;
            RunUntil(()=>_rally.Phase==Phase.AllyReceive,3f,"spike attempt");
            var ball=LaunchLive();
            Assert.That(_rally.RegisterSpikeBlocked(ball,ball.transform.position),Is.False,"An untouched set cannot be blocked.");
            _rally.RegisterPlayerTouch();
            Assert.That(_rally.RegisterSpikeBlocked(ball,ball.transform.position),Is.True);
            Assert.That(_rally.Phase,Is.EqualTo(Phase.PlayerSpike),"A blocker touch does not end the rally by itself.");
            LandAt(ball,0f,-2f);
            Assert.That(_rally.Outcome,Is.EqualTo("BLOCKED"));Assert.That(_rally.OpponentScore,Is.EqualTo(1));
            Assert.That(_drill.ActiveBall,Is.SameAs(ball),"The rebounding ball stays visible during recovery.");
            RunUntil(()=>_rally.Phase==Phase.AllyReceive,5f,"next spike after recovery");
            Assert.That(_drill.ActiveBall,Is.Null,"Recovery retires the ball before the next attempt.");

            ball=LaunchLive();_rally.RegisterPlayerTouch();
            LandAt(ball,1f,3f);
            Assert.That(_rally.Outcome,Is.EqualTo("POINT!"));Assert.That(_rally.PlayerScore,Is.EqualTo(1));
            RunUntil(()=>_rally.Phase==Phase.AllyReceive,5f,"third spike");

            ball=LaunchLive();_rally.RegisterPlayerTouch();_rally.RegisterSpikeBlocked(ball,ball.transform.position);
            LandAt(ball,6f,3f);
            Assert.That(_rally.Outcome,Is.EqualTo("BLOCK OUT!"));Assert.That(_rally.PlayerScore,Is.EqualTo(2));
            RunUntil(()=>_rally.Phase==Phase.AllyReceive,5f,"fourth spike");

            ball=LaunchLive();_rally.RegisterPlayerTouch();
            LandAt(ball,0f,10f);
            Assert.That(_rally.Outcome,Is.EqualTo("OUT"));Assert.That(_rally.OpponentScore,Is.EqualTo(2));
        }

        [Test]
        public void BlockedBallStaysInPlayAndMustReachTheOpponentCourt()
        {
            _rally.Mode=Mode.BlockOnly;
            RunUntil(()=>_rally.Phase==Phase.OpponentSet,3f,"opponent attack");
            var ball=LaunchLive();
            _rally.RegisterPlayerTouch();
            Assert.That(_rally.Phase,Is.EqualTo(Phase.OpponentSpike),"Touching the spike is not a point by itself.");
            Assert.That(_drill.ActiveBall,Is.SameAs(ball),"The blocked ball is not removed.");
            LandAt(ball,0f,1.5f);
            Assert.That(_rally.Outcome,Is.EqualTo("BLOCK POINT!"));Assert.That(_rally.PlayerScore,Is.EqualTo(1));
            RunUntil(()=>_rally.Phase==Phase.OpponentSet,5f,"second attack");

            ball=LaunchLive();_rally.RegisterPlayerTouch();
            LandAt(ball,.5f,-1.5f);
            Assert.That(_rally.Outcome,Is.EqualTo("BLOCK FELL ON YOUR SIDE"));Assert.That(_rally.OpponentScore,Is.EqualTo(1));
            RunUntil(()=>_rally.Phase==Phase.OpponentSet,5f,"third attack");

            ball=LaunchLive();
            LandAt(ball,0f,-1f);
            Assert.That(_rally.Outcome,Does.StartWith("NO BLOCK"));Assert.That(_rally.OpponentScore,Is.EqualTo(2));
        }

        [Test]
        public void FirstToMatchPointsWinsThenANewMatchStarts()
        {
            _rally.Mode=Mode.SpikeOnly;_rally.MatchPoints=2;
            bool? won=null;_rally.MatchEnded+=w=>won=w;
            for(int i=0;i<2;i++)
            {
                RunUntil(()=>_rally.Phase==Phase.AllyReceive,5f,"spike "+i);
                var ball=LaunchLive();_rally.RegisterPlayerTouch();LandAt(ball,0f,3f);
            }
            Assert.That(_rally.PlayerScore,Is.EqualTo(2));
            RunUntil(()=>_rally.Phase==Phase.MatchOver,3f,"match over after the last point is shown");
            Assert.That(won,Is.True);Assert.That(_rally.PlayerWonMatch,Is.True);
            RunUntil(()=>_rally.Phase==Phase.TurnChange,_rally.MatchEndSeconds+1f,"new match re-announces the first turn");
            Assert.That(_rally.PlayerScore+_rally.OpponentScore,Is.Zero);
        }

        [Test]
        public void MenuCyclesTurnModes()
        {
            var menu=Object.FindFirstObjectByType<VolleyHandMenu>();var original=Time.timeScale;
            try
            {
                _rally.Mode=Mode.Alternate;
                menu.SetOpen(true);menu.Activate(9);Assert.That(_rally.Mode,Is.EqualTo(Mode.SpikeOnly));
                menu.Activate(9);Assert.That(_rally.Mode,Is.EqualTo(Mode.BlockOnly));
                menu.Activate(9);Assert.That(_rally.Mode,Is.EqualTo(Mode.Alternate));
                Assert.That(_rally.TurnStarted,Is.False,"A mode change re-announces the turn.");
            }
            finally{menu.SetOpen(false);Time.timeScale=original;Object.DestroyImmediate(menu);}
        }

        [Test]
        public void QuestBuildStartsInTheSpikeAndBlockScene()
        {
            var names=(string[])System.Type.GetType("VolleyQuestBuilder, Assembly-CSharp-Editor",true)
                .GetField("SceneNames",BindingFlags.Public|BindingFlags.Static).GetValue(null);
            Assert.That(names[0],Is.EqualTo("VolleyBlock-codex"));
        }
    }
}
