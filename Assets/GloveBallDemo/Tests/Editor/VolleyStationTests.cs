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
            Assert.That(done(),Is.True,what+" (phase "+_rally.Phase+", cue "+_rally.Cue+", embodying "+_rally.Embodying
                +", serve timer "+typeof(VolleyAerialSequence).GetField("_serveTimer",Private).GetValue(_rally)
                +", wait "+typeof(VolleyAerialSequence).GetField("_serveWait",Private).GetValue(_rally)+")");
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

        GameObject Ring(int index)=>_rally.transform.Find("TOSS target ring "+index)?.gameObject;

        [Test]
        public void TossTheirServeIsPassedUpByATeammateAndAGoodPushSetsTheHitter()
        {
            _rally.SetMode(Mode.Set);_rally.SetAssist=1f;
            Launch(Phase.PlayerSet);
            Assert.That(_rally.Serving,Is.EqualTo(VolleyAerialSequence.Team.Them),"They serve, our back row passes to the player.");
            Assert.That(_rally.RoleTouch,Is.EqualTo(Touch.Set));
            Assert.That(_rally.Embodied,Is.SameAs(_rally.Ally),"The player is the setter at the net.");
            Assert.That(_rally.HoldsSet,Is.True,"The drill catches the set instead of bouncing it.");
            Assert.That(_destination.y-_rally.GroundedEye.y,Is.EqualTo(.35f).Within(.01f),"The pass comes down just above the head.");
            Assert.That(Ring(1).activeSelf && Ring(2).activeSelf,Is.True,"Rings mark both hitters' spots.");
            Assert.That(Ring(1).GetComponent<LineRenderer>().GetPosition(0).y,Is.EqualTo(_rally.HitterSpot(0).y).Within(.01f));
            var ball=_destination;
            // The push of the hold: up and toward our left (the left hitter).
            Assert.That(_rally.OverrideTouchVelocity(Vector3.up*2.5f-Right*1.5f,ball,Vector3.zero,out var set),Is.True);
            _rally.RegisterPlayerTouch();
            Fly(ball,set);
            Assert.That(_rally.Phase,Is.EqualTo(Phase.MatchPlay),"A hitter took the set ("+_rally.Cue+")");
            Assert.That(Ring(1).activeSelf,Is.False,"Rings go once the set is judged.");
            Assert.That(_rally.StationQuality,Is.GreaterThanOrEqualTo(_rally.PerfectSetQuality));
            Assert.That(_rally.Teammates[1].transform.position.x-Net.x,Is.LessThan(-1f),"The left hitter attacks it.");
            RunUntil(()=>_rally.Phase==Phase.Recovery,6f,"the attack ends the rep");
            Assert.That(_rally.Outcome,Does.EndWith("PERFECT SET"));
            Assert.That(_rally.PlayerScore,Is.EqualTo(1),"A good set counts, kill or dig.");
        }

        [Test]
        public void TossHitterSpotsChangeEveryRep()
        {
            _rally.SetMode(Mode.Set);
            Launch(Phase.PlayerSet);
            var first=_rally.HitterSpot(0);
            Assert.That(first.x-Net.x,Is.InRange(_rally.LeftHitterLateral.x-.6f,_rally.LeftHitterLateral.y+.6f));
            Assert.That(_rally.OverrideTouchVelocity(Vector3.up*.2f,_destination,Vector3.zero,out var set),Is.True);
            _rally.RegisterPlayerTouch();
            Fly(_destination,set);
            Assert.That(_rally.Outcome,Is.EqualTo("SET DROPPED"),"A push with no lift never reaches a hitter.");
            Assert.That(_rally.OpponentScore,Is.EqualTo(1));Assert.That(_rally.StationQuality,Is.Zero);
            Launch(Phase.PlayerSet);
            Assert.That(Vector3.Distance(_rally.HitterSpot(0),first),Is.GreaterThan(.05f));
        }

        [Test]
        public void PassTheServeArrivesAtForearmHeightAndAnIdealPassIsSetAndAttacked()
        {
            _rally.SetMode(Mode.Pass);_rally.PassAssist=1f;
            Launch(Phase.PlayerReceive);
            Assert.That(_rally.RoleTouch,Is.EqualTo(Touch.Receive));
            Assert.That(_destination.y-_drill.CourtFrame.position.y,Is.EqualTo(_rally.ReceiveHeight).Within(.01f));
            Assert.That(_drill.JoinedHands,Is.SameAs(_rally.ReceiveVolume),"Forearm platform while passing.");
            Assert.That(_rally.OverrideTouchVelocity(Vector3.zero,_destination,Forward*3f,out var pass),Is.True);
            _rally.RegisterPlayerTouch();
            Fly(_destination,pass);
            Assert.That(_rally.Phase,Is.EqualTo(Phase.MatchPlay),"Our setter took the pass ("+_rally.Cue+")");
            Assert.That(_rally.StationQuality,Is.GreaterThanOrEqualTo(.75f));
            RunUntil(()=>_rally.Phase==Phase.Recovery,8f,"set, attack and the end of the rep");
            Assert.That(_rally.Outcome,Does.EndWith("PERFECT PASS"));
            Assert.That(_rally.PlayerScore,Is.EqualTo(1));
            RunUntil(()=>_launched && _rally.Phase==Phase.PlayerReceive,_rally.RallyResetSeconds+12f,"the next serve comes to the player");
        }

        [Test]
        public void PassBelowTheNetIsNotSetEvenWhenTheSetterCouldReachIt()
        {
            _rally.SetMode(Mode.Pass);_rally.PassAssist=0f;
            Launch(Phase.PlayerReceive);
            // A flat pass toward the setter that never rises above the tape.
            Assert.That(_rally.OverrideTouchVelocity(Vector3.zero,_destination,Forward*4.5f+Vector3.up*3f,out var pass),Is.True);
            _rally.RegisterPlayerTouch();
            Fly(_destination,pass);
            Assert.That(_rally.Phase,Is.EqualTo(Phase.Recovery));
            Assert.That(_rally.Outcome,Is.EqualTo("TOO LOW FOR YOUR SETTER").Or.EqualTo("OVER THE NET"));
            Assert.That(_rally.OpponentScore,Is.EqualTo(1));
        }

        [Test]
        public void PassOverTheNetEndsTheRepWithoutTheirDefencePlayingOn()
        {
            _rally.SetMode(Mode.Pass);_rally.PassAssist=0f;_rally.TheirDigSuccess=1f;_rally.TheirDigReach=5f;_rally.TheirDigSpeed=20f;
            Launch(Phase.PlayerReceive);
            Assert.That(_rally.OverrideTouchVelocity(Vector3.zero,_destination,Forward*9f+Vector3.up*6f,out var pass),Is.True);
            _rally.RegisterPlayerTouch();
            Fly(_destination,pass);
            Assert.That(_rally.Phase,Is.EqualTo(Phase.Recovery),"No NPC rally after a pass that went over.");
            Assert.That(_rally.Outcome,Is.EqualTo("OVER THE NET").Or.EqualTo("OUT"));
            Assert.That(_rally.OpponentScore,Is.EqualTo(1));
        }

        [Test]
        public void SetHoldCarriesTheBallAndReleasesWithThePush()
        {
            var hold=new VolleySetHold{MaxSeconds=.35f,ReleaseSpeed=.8f};
            hold.Begin(new Vector3(0f,2f,0f),new Vector3(0f,1.95f,0f));
            Vector3 position,release;
            // The hands give way under the ball, then push up to 2.4 m/s and slow down.
            float[] speeds={-.6f,-.3f,.4f,1.2f,2f,2.4f,1.9f};
            int released=-1;
            for(int i=0;i<speeds.Length;i++)
                if(hold.Step(new Vector3(0f,1.95f+i*.01f,0f),new Vector3(-.5f,speeds[i],0f),.02f,out position,out release)){released=i;break;}
            Assert.That(released,Is.EqualTo(6),"Leaves when the push slows down.");
            Assert.That(hold.Peak.y,Is.EqualTo(2.4f));Assert.That(hold.Give,Is.EqualTo(-.6f));
            hold.Begin(Vector3.up*2f,Vector3.up*2f);
            bool left=false;for(int i=0;i<30 && !left;i++)left=hold.Step(Vector3.up*2f,Vector3.zero,.02f,out position,out release);
            Assert.That(left,Is.True);Assert.That(hold.Seconds,Is.EqualTo(.36f).Within(.021f),"Never held longer than the limit.");
            hold.Begin(Vector3.up*1.9f,Vector3.up*2f);
            hold.Step(Vector3.up*2f,Vector3.zero,.02f,out position,out release);
            Assert.That(position.y-2f,Is.EqualTo(.06f).Within(1e-4f),"The ball sits on the fingers, not inside the hands.");
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
