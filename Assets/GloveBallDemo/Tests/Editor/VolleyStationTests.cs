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

        GameObject Ring(int index)=>_rally.transform.Find("TOSS target "+index)?.gameObject;

        [Test]
        public void TossTheirServeIsPassedUpByATeammateAndAGoodPushSetsTheNearHitter()
        {
            _rally.SetMode(Mode.Set);_rally.SetAssist=1f;
            Launch(Phase.PlayerSet);
            Assert.That(_rally.Serving,Is.EqualTo(VolleyAerialSequence.Team.Them),"They serve, our back row passes to the player.");
            Assert.That(_rally.RoleTouch,Is.EqualTo(Touch.Set));
            Assert.That(_rally.Embodied,Is.SameAs(_rally.Ally),"The player is the setter at the net.");
            Assert.That(_rally.HoldsSet,Is.True,"The drill catches the set instead of bouncing it.");
            Assert.That(_destination.y-_rally.GroundedEye.y,Is.EqualTo(.35f).Within(.01f),"The pass comes down just above the head.");
            Assert.That(Ring(1).activeSelf && Ring(2).activeSelf,Is.True,"Ghost balls mark both hitters' spots.");
            Assert.That(Ring(1).GetComponent<Collider>(),Is.Null,"A target never blocks the ball.");
            Assert.That(Vector3.Distance(Ring(1).transform.position,_rally.HitterSpot(0)),Is.LessThan(.01f),"The near target is the near hitter's spot.");
            Assert.That(_rally.GroundedEye.x-Net.x,Is.EqualTo(_rally.SetterLateral).Within(.3f),"The player sets from the right end of the net.");
            var ball=_destination;
            // The push of the hold: up and a little toward our left (the near hitter).
            Assert.That(_rally.OverrideTouchVelocity(Vector3.up*2.5f-Right*.8f,ball,Vector3.zero,out var set),Is.True);
            _rally.RegisterPlayerTouch();
            Fly(ball,set);
            Assert.That(_rally.Phase,Is.EqualTo(Phase.MatchPlay),"A hitter took the set ("+_rally.Cue+")");
            Assert.That(Ring(1).activeSelf,Is.False,"Targets go once the set is judged.");
            Assert.That(_rally.LastGrade,Is.EqualTo("EXCELLENT"),"The grade shows small in the view.");
            Assert.That(_rally.StationQuality,Is.GreaterThanOrEqualTo(_rally.PerfectSetQuality));
            Assert.That(_rally.Cue,Does.StartWith("EXCELLENT SET").Or.Contains("SPIKE"));
            Assert.That(_rally.transform.Find("TOSS set crossing").gameObject.activeSelf,Is.True,"Where the set came down is marked.");
            Assert.That(_rally.SetBlockers,Is.InRange(1,2),"One or two of their blockers face the hitter.");
            RunUntil(()=>_rally.Phase==Phase.Recovery,6f,"the attack ends the rep");
            Assert.That(_rally.Outcome,Does.EndWith("EXCELLENT SET").Or.StartsWith("DUG"));
            if(_rally.Outcome.StartsWith("KILL"))Assert.That(_rally.PlayerScore,Is.EqualTo(1),"A kill is our point.");
            else if(_rally.Outcome.StartsWith("DUG"))Assert.That(_rally.PlayerScore+_rally.OpponentScore,Is.Zero,"A dig carries the rally on: no point.");
            else Assert.That(_rally.OpponentScore,Is.EqualTo(1),"A stuff block is theirs.");
        }

        [Test]
        public void TossHitterSpotsChangeEveryRep()
        {
            _rally.SetMode(Mode.Set);
            Launch(Phase.PlayerSet);
            var first=_rally.HitterSpot(0);
            Assert.That(first.x-Net.x,Is.InRange(_rally.NearHitterLateral.x-.6f,_rally.NearHitterLateral.y+.6f));
            // Their blockers: two in front of one hitter, one in front of the other.
            int near=0,far=0;
            foreach(var blocker in _rally.Blockers)
            {
                float lateral=blocker.transform.position.x-Net.x;
                if(Mathf.Abs(lateral-(_rally.HitterSpot(0).x-Net.x))<1.1f)near++;
                if(Mathf.Abs(lateral-(_rally.HitterSpot(1).x-Net.x))<1.1f)far++;
            }
            Assert.That(near+far,Is.EqualTo(3));Assert.That(Mathf.Max(near,far),Is.EqualTo(2));
            Assert.That(_rally.OverrideTouchVelocity(Vector3.up*.2f,_destination,Vector3.zero,out var set),Is.True);
            _rally.RegisterPlayerTouch();
            Fly(_destination,set);
            Assert.That(_rally.Outcome,Is.EqualTo("SET DROPPED"),"A push with no lift never reaches a hitter.");
            Assert.That(_rally.Cue,Is.Not.Null);
            Assert.That(_rally.OpponentScore,Is.EqualTo(1));Assert.That(_rally.StationQuality,Is.Zero);
            Launch(Phase.PlayerSet);
            Assert.That(Vector3.Distance(_rally.HitterSpot(0),first),Is.GreaterThan(.05f));
        }

        [Test]
        public void TossTooLongIsGradedBadWithWhatToChange()
        {
            _rally.SetMode(Mode.Set);_rally.SetAssist=0f;
            Launch(Phase.PlayerSet);
            Assert.That(_rally.OverrideTouchVelocity(Vector3.up*2.5f-Right*3f,_destination,Vector3.zero,out var set),Is.True);
            _rally.RegisterPlayerTouch();
            for(int i=0;i<3;i++){Frame();_rally.SampleBall(_destination+set*.02f*i,set,.11f,.02f);}
            Assert.That(_rally.Cue,Does.StartWith("BAD SET").And.Contains("TOO LONG"));
        }

        [Test]
        public void PassTheServeArrivesAtForearmHeightAndAnIdealPassIsSetAndAttacked()
        {
            _rally.SetMode(Mode.Pass);_rally.PassAssist=1f;
            Launch(Phase.PlayerReceive);
            Assert.That(_rally.RoleTouch,Is.EqualTo(Touch.Receive));
            Assert.That(_destination.y-_drill.CourtFrame.position.y,Is.EqualTo(_rally.ReceiveHeight).Within(_rally.ReceiveSpread.z+.01f));
            Assert.That(_drill.JoinedHands,Is.SameAs(_rally.ReceiveVolume),"Forearm platform while passing.");
            Assert.That(_rally.OverrideTouchVelocity(Vector3.zero,_destination,Forward*3f,out var pass),Is.True);
            _rally.RegisterPlayerTouch();
            Fly(_destination,pass);
            Assert.That(_rally.Phase,Is.EqualTo(Phase.MatchPlay),"Our setter took the pass ("+_rally.Cue+")");
            Assert.That(_rally.StationQuality,Is.GreaterThanOrEqualTo(.75f));
            RunUntil(()=>_rally.Phase==Phase.Recovery,8f,"set, attack and the end of the rep");
            Assert.That(_rally.Outcome,Is.EqualTo("KILL! — PERFECT PASS").Or.StartsWith("DUG"));
            Assert.That(_rally.OpponentScore,Is.Zero);
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
            Assert.That(_rally.Outcome,Is.EqualTo("TOO LOW FOR YOUR SETTER"));
            Assert.That(_rally.OpponentScore,Is.EqualTo(1));
        }

        [Test]
        public void PassOverTheNetIsTheirChanceBallAndTheyAttackThePlayer()
        {
            _rally.SetMode(Mode.Pass);_rally.PassAssist=0f;_rally.TheirDigSuccess=1f;_rally.TheirDigReach=5f;_rally.TheirDigSpeed=20f;
            Launch(Phase.PlayerReceive);
            Assert.That(_rally.OverrideTouchVelocity(Vector3.zero,_destination,Forward*9f+Vector3.up*6f,out var pass),Is.True);
            _rally.RegisterPlayerTouch();
            Fly(_destination,pass);
            Assert.That(_rally.Phase,Is.EqualTo(Phase.Recovery),"No NPC rally after a pass that went over.");
            Assert.That(_rally.Outcome,Does.StartWith("OVER THE NET"));
            Assert.That(_rally.PlayerScore+_rally.OpponentScore,Is.Zero,"Their chance ball: no point, the rally carries on.");
            Assert.That(_rally.AttackNext,Is.True);
            Launch(Phase.PlayerReceive);
            Assert.That(_rally.Cue,Is.EqualTo("DIG IT UP!"),"They attack the player instead of serving.");
            Assert.That(_rally.AttackNext,Is.False);
        }

        [Test]
        public void SetHoldCarriesTheBallAndReleasesWithThePush()
        {
            var hold=new VolleySetHold{MaxSeconds=.35f,ReleaseSpeed=.8f,Smoothing=0f};
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
            // One wild tracking sample is smoothed, not taken as the push.
            var smooth=new VolleySetHold{MaxSeconds=.35f,ReleaseSpeed=.8f,Smoothing=.5f};
            smooth.Begin(Vector3.up*2f,Vector3.up*2f);
            foreach(var v in new[]{.5f,.5f,8f,.5f})smooth.Step(Vector3.up*2f,Vector3.up*v,.02f,out position,out release);
            Assert.That(smooth.Peak.y,Is.LessThan(5f));
        }

        [Test]
        public void ATurnChangePlacesTheHeadAtTheBlockSpotWhateverHappenedBefore()
        {
            var floor=_jump.Floor;
            Assert.That(Net.z-floor.RepositionTarget.position.z,Is.EqualTo(_rally.BlockStandDistance).Within(.01f),"The block spot is 0.6 m off the net.");
            // Something moved the rig 1 m toward the net (an alignment mid-spike, the player's steps).
            floor.Origin.CameraFloorOffsetObject.transform.position+=Forward*1f;
            floor.PlaceStance(Vector3.zero);
            var head=floor.Origin.Camera.transform.position;
            Assert.That(Net.z-head.z,Is.EqualTo(_rally.BlockStandDistance).Within(.01f),"Back at the block spot.");
            floor.PlaceStance(-Forward*_rally.SpikeStandBackDistance);
            head=floor.Origin.Camera.transform.position;
            Assert.That(Net.z-head.z,Is.EqualTo(_rally.BlockStandDistance+_rally.SpikeStandBackDistance).Within(.01f),"The spike stance, 1.5 m off the net.");
        }

        [Test]
        public void StartAlignmentKeepsTheStanceSoABlockTurnIsNotPushedIntoTheNet()
        {
            var floor=_jump.Floor;
            Assert.That(floor.StanceAnchor,Is.Not.Null);
            var alignment=Object.FindFirstObjectByType<Hapbeat.DemoSwitch.XrStartAlignment>();
            var anchor=(Transform)typeof(Hapbeat.DemoSwitch.XrStartAlignment).GetField("_anchor",Private).GetValue(alignment);
            Assert.That(anchor,Is.SameAs(floor.StanceAnchor),"視線をリセット / start alignment use the stance anchor.");
            floor.SetStanceOffset(-Forward*1.2f); // a spike turn
            Assert.That(Vector3.Distance(anchor.position,floor.RepositionTarget.position-Forward*1.2f),Is.LessThan(1e-4f));
            floor.SetStanceOffset(Vector3.zero);  // back to the block spot
            Assert.That(Vector3.Distance(anchor.position,floor.RepositionTarget.position),Is.LessThan(1e-4f));
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
