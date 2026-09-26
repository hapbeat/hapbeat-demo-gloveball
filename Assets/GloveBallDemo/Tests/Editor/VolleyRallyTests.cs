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
            _drill.Targets.BeginWave(0,1,1);
            Assert.That(_drill.Targets.ActiveTargetCount,Is.EqualTo(1));
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
            Assert.That(Vector3.Distance(_rally.Opponent.transform.position,home),Is.GreaterThan(1f),"The attacker waits in the back row.");
            RunUntil(()=>_rally.Phase==Phase.AllyReceive,3f,"first spike attempt");
            Assert.That(_rally.FadeAlpha,Is.Zero);

            int active=0;float netZ=_drill.ReceiveNet.transform.position.z;
            for(int i=0;i<_rally.Blockers.Length;i++)if(_rally.ActiveBlockers[i])
            {
                active++;
                Assert.That(_rally.Blockers[i].transform.position.z-netZ,Is.EqualTo(_rally.BlockerNetDistance).Within(.01f));
                Assert.That(_rally.Blockers[i].GetComponentInChildren<Collider>().enabled,Is.True);
            }
            Assert.That(active,Is.InRange(_rally.MinBlockers,_rally.MaxBlockers));
            var dropping=(Ball)typeof(VolleyAerialSequence).GetField("_preparedBall",Private).GetValue(_rally);
            Assert.That(dropping.transform.position.y,Is.GreaterThan(_rally.Ally.ReleasePosition.y+_rally.AllyDropHeight-.05f),"The pass falls onto the setter from above.");
            Launch();
            Assert.That(Vector3.Distance(_start,_rally.Ally.ReleasePosition),Is.LessThan(.001f));
            Assert.That(_seconds,Is.EqualTo(_rally.AllyFlightSeconds));
            Assert.That(_destination.y,Is.EqualTo(_drill.Head.position.y+_jump.JumpHeight+_rally.SpikeReachAboveEye).Within(.001f));
            DiscardPrepared();
            _rally.RegisterBallUnavailable();
            Assert.That(_rally.Outcome,Is.EqualTo("MISSED"));

            RunUntil(()=>_rally.Phase==Phase.TurnChange,3f,"one spike, then the block turn is announced");
            RunUntil(()=>_rally.CurrentTurn==Turn.Block,1f,"block turn applied");
            Assert.That(_jump.Floor.StanceOffset,Is.EqualTo(Vector3.zero));
            Assert.That(_rally.Opponent.transform.position,Is.EqualTo(home));
            foreach(var blocker in _rally.Blockers)Assert.That(blocker.GetComponentInChildren<Collider>().enabled,Is.False,"Standby blockers are not solid.");
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
            float flight=_rally.AllyFlightSeconds;
            for(int i=0;i<_rally.Blockers.Length;i++)if(_rally.ActiveBlockers[i])
                Assert.That(_rally.BlockerJumpTimes[i]+VolleyOpponentPrototype.BlockPeakPhase*_rally.BlockerJumpSeconds,
                    Is.InRange(flight+_rally.BlockerReactionSeconds-_rally.BlockerTimingJitter-.001f,flight+_rally.BlockerReactionSeconds+_rally.BlockerTimingJitter+.001f));
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

        [Test]
        public void SpikeOutcomesScoreBlockedAndInCourtBalls()
        {
            _rally.Mode=Mode.SpikeOnly;
            var launch=typeof(VolleyDrillController).GetMethod("LaunchAerialBall",Private);
            RunUntil(()=>_rally.Phase==Phase.AllyReceive,3f,"spike attempt");
            Launch();launch.Invoke(_drill,new object[]{_start,_destination,_seconds});
            var ball=_drill.ActiveBall;Assert.That(ball,Is.Not.Null);
            Assert.That(_rally.RegisterSpikeBlocked(ball,ball.transform.position),Is.False,"An untouched set cannot be blocked.");
            _rally.RegisterPlayerSpike();
            Assert.That(_rally.RegisterSpikeBlocked(ball,ball.transform.position),Is.True);
            Assert.That(_rally.Outcome,Is.EqualTo("BLOCKED"));Assert.That(_rally.SpikesBlocked,Is.EqualTo(1));
            RunUntil(()=>_rally.Phase==Phase.AllyReceive,4f,"next spike after recovery");
            Assert.That(_drill.ActiveBall,Is.Null,"Recovery retires the rebounding ball.");

            Launch();launch.Invoke(_drill,new object[]{_start,_destination,_seconds});
            ball=_drill.ActiveBall;_rally.RegisterPlayerSpike();
            ball.Body.isKinematic=true;
            ball.transform.position=_drill.ReceiveNet.transform.position+Forward*3f+Vector3.up*.1f;
            Frame();
            Assert.That(_rally.Outcome,Is.EqualTo("POINT!"));Assert.That(_rally.SpikePoints,Is.EqualTo(1));
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
