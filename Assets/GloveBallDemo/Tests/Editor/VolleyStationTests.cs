using System.Reflection;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Phase=GloveBallDemo.Runtime.VolleyAerialSequence.RallyPhase;
using Mode=GloveBallDemo.Runtime.VolleyAerialSequence.RallyMode;
using Touch=GloveBallDemo.Runtime.VolleyAerialSequence.PlayerTouch;

namespace GloveBallDemo.Tests
{
    /// <summary>Set and pass stations: one physical touch per rep, judged by where it lets our teammates play the ball.</summary>
    public class VolleyStationTests
    {
        const string Scene="Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        VolleyDrillController _drill;
        VolleyAerialSequence _rally;
        VolleyArmJump _jump;
        Vector3 _start,_destination;
        float _scale;
        bool _launched;
        Random.State _random;

        [SetUp]
        public void Open()
        {
            EditorSceneManager.OpenScene(Scene);
            _drill=Object.FindFirstObjectByType<VolleyDrillController>();_rally=_drill.Aerial;_jump=_rally.Jump;
            typeof(BallPool).GetMethod("Awake",Private).Invoke(_drill.Pool,null);
            _rally.TossLauncher.GetComponent<VolleyFeederAim>().ShotClip=null; // No PC audio in tests.
            _rally.SpikeClip=null;
            _jump.Floor.Origin.Camera.transform.localPosition=Vector3.up*1.6f;
            _scale=Time.timeScale;_random=Random.state;Random.InitState(73);
        }
        [TearDown]
        public void Clean()
        {
            _rally.CancelFeed();_drill.RetireRallyBall();
            _jump.Floor.SetStanceOffset(Vector3.zero);_jump.ResetJump();
            Time.timeScale=_scale;Random.state=_random;
        }

        void Frame()
        {
            _jump.Tick(.01f,true,.2f,.2f,false);
            if(!_rally.TickRally(.01f,_drill.ActiveBall!=null,out var s,out var d,out var f))return;
            _launched=true;_start=s;_destination=d;
            typeof(VolleyDrillController).GetMethod("LaunchAerialBall",Private).Invoke(_drill,new object[]{s,d,f});
        }
        void RunUntil(System.Func<bool> done,float limit,string what)
        {
            for(float t=0f;t<limit && !done();t+=.01f)Frame();
            Assert.That(done(),Is.True,what+" (phase "+_rally.Phase+", cue "+_rally.Cue+")");
        }
        void Launch(Phase phase)
        {
            _launched=false;
            RunUntil(()=>_launched && _rally.Phase==phase,20f,"launch toward the player as "+phase);
        }
        /// <summary>Free flight at physics rate until a teammate takes the ball or the rep ends.</summary>
        void Fly(Vector3 position,Vector3 velocity)
        {
            for(int i=0;i<400 && (_rally.Phase==Phase.PlayerSet || _rally.Phase==Phase.PlayerReceive);i++)
            {
                Frame();Frame();
                _rally.SampleBall(position,velocity,.11f,.02f);
                velocity+=Physics.gravity*.02f;position+=velocity*.02f;
                if(position.y<.11f){position.y=.11f;velocity.y=-velocity.y*.6f;}
            }
        }
        Vector3 Forward=>Vector3.ProjectOnPlane(_drill.CourtFrame.forward,Vector3.up).normalized;
        Vector3 Right=>Vector3.Cross(Vector3.up,Forward);
        Vector3 Net=>_drill.ReceiveNet.transform.position;

        [Test]
        public void SetStationAGoodPushReachesTheOutsideHitterWhoAttacks()
        {
            _rally.SetMode(Mode.Set);
            Launch(Phase.PlayerSet);
            Assert.That(_rally.RoleTouch,Is.EqualTo(Touch.Set));
            Assert.That(_rally.Embodied,Is.SameAs(_rally.Ally),"The player is the setter at the net.");
            var ball=_destination;
            // Push up and toward our left (the outside hitter's lane).
            Assert.That(_rally.OverrideTouchVelocity(Vector3.up*2f-Right*1.6f,ball,Vector3.zero,out var set),Is.True);
            _rally.RegisterPlayerTouch();
            Fly(ball,set);
            Assert.That(_rally.Phase,Is.EqualTo(Phase.MatchPlay),"A hitter took the set ("+_rally.Cue+")");
            Assert.That(_rally.StationQuality,Is.GreaterThanOrEqualTo(.4f));
            Assert.That(_rally.Teammates[1].transform.position.x-Net.x,Is.LessThan(-1f),"The outside hitter (our left) attacks it.");
            RunUntil(()=>_rally.Phase==Phase.Recovery,6f,"the attack ends the rep");
            Assert.That(_rally.Outcome,Is.EqualTo("POINT!").Or.StartsWith("DUG"));
        }

        [Test]
        public void SetStationASetToTheRightHasNoHitterAndDrops()
        {
            _rally.SetMode(Mode.Set);
            Launch(Phase.PlayerSet);
            // Our right, behind the setter: nobody hits there in this station.
            Assert.That(_rally.OverrideTouchVelocity(Vector3.up*2f+Right*2f,_destination,Vector3.zero,out var set),Is.True);
            _rally.RegisterPlayerTouch();
            Fly(_destination,set);
            Assert.That(_rally.Phase,Is.EqualTo(Phase.Recovery));
            Assert.That(_rally.Outcome,Is.EqualTo("SET DROPPED").Or.EqualTo("OUT"));
            Assert.That(_rally.OpponentScore,Is.EqualTo(1));Assert.That(_rally.StationQuality,Is.Zero);
        }

        [Test]
        public void PassStationAnIdealPassReachesTheSetterAndOurHitterAttacks()
        {
            _rally.SetMode(Mode.Pass);_rally.PassAssist=1f;
            Launch(Phase.PlayerReceive);
            Assert.That(_rally.RoleTouch,Is.EqualTo(Touch.Receive));
            Assert.That(_drill.JoinedHands,Is.SameAs(_rally.ReceiveVolume),"Forearm platform while passing.");
            Assert.That(_rally.OverrideTouchVelocity(Vector3.zero,_destination,Forward*3f,out var pass),Is.True);
            _rally.RegisterPlayerTouch();
            Fly(_destination,pass);
            Assert.That(_rally.Phase,Is.EqualTo(Phase.MatchPlay),"Our setter took the pass ("+_rally.Cue+")");
            Assert.That(_rally.StationQuality,Is.GreaterThanOrEqualTo(.75f));
            RunUntil(()=>_rally.Phase==Phase.Recovery,8f,"set, attack and the end of the rep");
            Assert.That(_rally.Outcome,Is.EqualTo("POINT!").Or.StartsWith("DUG"));
            RunUntil(()=>_launched && _rally.Phase==Phase.PlayerReceive,_rally.RallyResetSeconds+12f,"the next serve still comes from them to the player");
        }

        [Test]
        public void PassStationAShankDropsAndCountsAgainstThePlayer()
        {
            _rally.SetMode(Mode.Pass);_rally.PassAssist=0f;
            Launch(Phase.PlayerReceive);
            Assert.That(_rally.OverrideTouchVelocity(Vector3.zero,_destination,Right*4f+Vector3.up*1f,out var pass),Is.True);
            _rally.RegisterPlayerTouch();
            Fly(_destination,pass);
            Assert.That(_rally.Phase,Is.EqualTo(Phase.Recovery));
            Assert.That(_rally.Outcome,Is.EqualTo("PASS DROPPED").Or.EqualTo("OUT"));
            Assert.That(_rally.OpponentScore,Is.EqualTo(1));
        }

        [Test]
        public void HandZoneRunsFromTheWristToTheFingertips()
        {
            var hand=_drill.Right;var box=hand.ContactVolume;
            Assert.That(VolleyDrillController.HandZone(hand,hand.transform.position),Is.LessThan(.15f),"The wrist end.");
            float best=0f;
            foreach(var sign in new[]{-1f,1f})
                foreach(var axis in new[]{Vector3.right,Vector3.forward})
                {
                    var local=box.center+Vector3.Scale(axis,box.size)*.5f*sign;
                    best=Mathf.Max(best,VolleyDrillController.HandZone(hand,box.transform.TransformPoint(local)));
                }
            Assert.That(best,Is.EqualTo(1f).Within(.01f),"The far end is the fingertips.");
        }
    }
}
