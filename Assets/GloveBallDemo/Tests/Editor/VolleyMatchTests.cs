using System.Reflection;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Phase=GloveBallDemo.Runtime.VolleyAerialSequence.RallyPhase;
using Mode=GloveBallDemo.Runtime.VolleyAerialSequence.RallyMode;
using Team=GloveBallDemo.Runtime.VolleyAerialSequence.Team;
using Touch=GloveBallDemo.Runtime.VolleyAerialSequence.PlayerTouch;
using Level=GloveBallDemo.Runtime.VolleyAerialSequence.Involvement;

namespace GloveBallDemo.Tests
{
    /// <summary>6 v 6 match: scripted NPC play, physical player touches, rotation, vignetted glides and landing-decided points.</summary>
    public class VolleyMatchTests
    {
        const string Scene="Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        VolleyDrillController _drill;
        VolleyAerialSequence _rally;
        VolleyArmJump _jump;
        Vector3 _start,_destination;
        float _seconds,_scale;
        bool _launched;
        Mode _sceneMode;
        Random.State _random;

        [SetUp]
        public void OpenMatch()
        {
            EditorSceneManager.OpenScene(Scene);
            _drill=Object.FindFirstObjectByType<VolleyDrillController>();_rally=_drill.Aerial;_jump=_rally.Jump;
            typeof(BallPool).GetMethod("Awake",Private).Invoke(_drill.Pool,null);
            _rally.TossLauncher.GetComponent<VolleyFeederAim>().ShotClip=null; // No PC audio in tests.
            _rally.SpikeClip=null;
            _jump.Floor.Origin.Camera.transform.localPosition=Vector3.up*1.6f;
            _sceneMode=_rally.Mode;
            _rally.Mode=Mode.Match;_rally.PlayerInvolvement=Level.SpikeBlock;
            _scale=Time.timeScale;_random=Random.state;Random.InitState(521);
        }
        [TearDown]
        public void Clean()
        {
            _rally.CancelFeed();_drill.RetireRallyBall();
            _jump.Floor.SetStanceOffset(Vector3.zero);_jump.ResetJump();
            Time.timeScale=_scale;Random.state=_random;
        }

        void Serve(Team team)=>typeof(VolleyAerialSequence).GetField("_serving",Private).SetValue(_rally,team);
        void Slot(int slot)=>typeof(VolleyAerialSequence).GetField("_playerSlot",Private).SetValue(_rally,slot);
        /// <summary>This rally's spot of the player's own (hidden) teammate.</summary>
        Vector3 PlayerHome()
        {
            var actor=typeof(VolleyAerialSequence).GetField("_embodied",Private).GetValue(_rally);
            return (Vector3)actor.GetType().GetField("Home").GetValue(actor);
        }
        static float FlatDistance(Vector3 a,Vector3 b){a.y=b.y=0f;return Vector3.Distance(a,b);}
        /// <summary>One 10 ms frame. A launch toward the player is handed to the drill, as the real drill does.</summary>
        void Frame()
        {
            _jump.Tick(.01f,true,.2f,.2f,false);
            if(!_rally.TickRally(.01f,_drill.ActiveBall!=null,out var s,out var d,out var f))return;
            _launched=true;_start=s;_destination=d;_seconds=f;
            typeof(VolleyDrillController).GetMethod("LaunchAerialBall",Private).Invoke(_drill,new object[]{s,d,f});
        }
        void RunUntil(System.Func<bool> done,float limit,string what)
        {
            for(float t=0f;t<limit && !done();t+=.01f)Frame();
            Assert.That(done(),Is.True,what);
        }
        void Launch(Phase phase,float limit=40f)
        {
            _launched=false;
            for(float t=0f;t<limit && !(_launched && _rally.Phase==phase);t+=.01f)Frame();
            Assert.That(_launched && _rally.Phase==phase,Is.True,"launch toward the player as "+phase+" (now "+_rally.Phase+", "+_rally.Outcome+", "+_rally.PlayerScore+"-"+_rally.OpponentScore+")");
        }
        bool InPhysicsWindow=>_rally.Phase==Phase.PlayerSpike || _rally.Phase==Phase.OpponentSpike || _rally.Phase==Phase.PlayerServe
            || _rally.Phase==Phase.PlayerReceive || _rally.Phase==Phase.PlayerSet;
        /// <summary>Integrates a free flight at physics rate (two rally frames per step) until an NPC takes the ball or the point ends.</summary>
        void Fly(Vector3 position,Vector3 velocity,System.Func<bool> stop=null)
        {
            for(int i=0;i<400 && InPhysicsWindow && (stop==null || !stop());i++)
            {
                Frame();Frame();
                _rally.SampleBall(position,velocity,.11f,.02f);
                velocity+=Physics.gravity*.02f;position+=velocity*.02f;
                if(position.y<.11f){position.y=.11f;velocity.y=-velocity.y*.6f;}
            }
        }
        Vector3 Forward=>Vector3.ProjectOnPlane(_drill.CourtFrame.forward,Vector3.up).normalized;
        float NetZ=>_drill.ReceiveNet.transform.position.z;
        VolleyOpponentPrototype[] Everyone()
        {
            var list=new System.Collections.Generic.List<VolleyOpponentPrototype>{_rally.Ally};
            list.AddRange(_rally.Teammates);list.AddRange(_rally.Blockers);list.AddRange(_rally.Defenders);
            return list.ToArray();
        }
        void PerfectTheirDefence(){_rally.TheirDigSuccess=1f;_rally.TheirDigReaction=0f;_rally.TheirDigSpeed=20f;_rally.TheirDigReach=3f;}
        void PerfectOurDefence(){_rally.OurDigSuccess=1f;_rally.OurDigReaction=0f;_rally.OurDigSpeed=20f;_rally.OurDigReach=3f;}

        [Test]
        public void SceneStartsInSpikeBlockTurnsWithTheMatchTeamReady()
        {
            Assert.That(_sceneMode,Is.EqualTo(Mode.Alternate),"The Volley APK opens in the spike / block turns; stations and the 6 v 6 match are options.");
            Assert.That(_rally.Teammates,Has.Length.EqualTo(5));
            foreach(var mate in _rally.Teammates)Assert.That(mate.JerseyColour,Is.EqualTo(_rally.Ally.JerseyColour));
            Assert.That(_rally.ReceiveVolume,Is.Not.Null);
        }

        [Test]
        public void ServeLeadsToAPlayerTouchWithElevenOthersOnCourt()
        {
            _launched=false;
            RunUntil(()=>_launched,40f,"a physical touch for the player");
            Assert.That(_rally.Phase,Is.EqualTo(Phase.PlayerSpike).Or.EqualTo(Phase.OpponentSpike),"Level 1: the player only spikes or blocks.");
            Assert.That(_rally.Embodied,Is.SameAs(_rally.Teammates[1]),"The player is the front-left teammate.");
            Assert.That(_rally.Embodied.gameObject.activeSelf,Is.False,"The player's own teammate is hidden.");
            Assert.That(_rally.FadeAlpha,Is.Zero,"The match never blanks the view.");
            int onCourt=0;foreach(var actor in Everyone())if(actor.gameObject.activeSelf)onCourt++;
            Assert.That(onCourt,Is.EqualTo(11));
            Assert.That(_rally.Opponent.gameObject.activeSelf,Is.False,"The turn-mode attacker is not part of the match.");
            Assert.That(_rally.Embodying,Is.False);Assert.That(Time.timeScale,Is.EqualTo(_scale),"Slow motion ends with the switch.");
            var head=_rally.GroundedEye;
            if(_rally.Phase==Phase.OpponentSpike)
            {
                Assert.That(_rally.RoleTouch,Is.EqualTo(Touch.Block));
                Assert.That(Mathf.Abs(_destination.x-head.x),Is.LessThanOrEqualTo(_rally.BlockLateralLimit+.01f),"Always reachable for the block.");
                Assert.That(NetZ-head.z,Is.EqualTo(.3f).Within(.05f),"Block stance at the net.");
            }
            else
            {
                Assert.That(_rally.RoleTouch,Is.EqualTo(Touch.Spike));
                Assert.That(_destination.y,Is.EqualTo(head.y+_jump.JumpHeight+_rally.SpikeReachAboveEye).Within(.01f));
            }
        }

        [Test]
        public void SpikeIntoTheOpenCourtScoresAndTheWinnerServes()
        {
            Serve(Team.Them);_rally.TheirDigSuccess=0f;
            Launch(Phase.PlayerSpike);
            _rally.RegisterPlayerTouch();
            Fly(_destination,new Vector3(0f,-7f,14f));
            Assert.That(_rally.Phase,Is.EqualTo(Phase.Recovery));
            Assert.That(_rally.Outcome,Is.EqualTo("POINT!"));Assert.That(_rally.PlayerScore,Is.EqualTo(1));
            Assert.That(_rally.Serving,Is.EqualTo(Team.Us),"The rally winner serves next.");
        }

        [Test]
        public void DugSpikeTurnsIntoTheirAttackAtThePlayersBlockAndAMissedBlockComesBack()
        {
            Serve(Team.Them);PerfectTheirDefence();PerfectOurDefence();
            Launch(Phase.PlayerSpike);
            _rally.RegisterPlayerTouch();
            // A soft push: in the air long enough for a defender to get there.
            Fly(_destination,new Vector3(0f,3f,4f));
            Assert.That(_rally.PlayerScore+_rally.OpponentScore,Is.Zero,"A dig is not a point: the rally continues.");
            Assert.That(_rally.Touches(Team.Them),Is.GreaterThanOrEqualTo(1));

            Launch(Phase.OpponentSpike,15f);
            Assert.That(_rally.RoleTouch,Is.EqualTo(Touch.Block),"The player became our blocker for their attack.");
            Assert.That(NetZ-_rally.GroundedEye.z,Is.EqualTo(.3f).Within(.05f));
            Assert.That(_rally.PlayerScore+_rally.OpponentScore,Is.Zero);

            // Miss the block: the attack continues into our court, our back row digs, our setter sets, the player spikes again.
            var velocity=GloveBallDemo.Core.VolleyMath.ServeVelocity(_start,_destination,_seconds,Physics.gravity);
            Fly(_start,velocity);
            Assert.That(_rally.PlayerScore+_rally.OpponentScore,Is.Zero,"Our defence kept the rally alive.");
            Launch(Phase.PlayerSpike,15f);
            Assert.That(_rally.RoleTouch,Is.EqualTo(Touch.Spike));
        }

        [Test]
        public void LevelTwoPlayerReceivesTheServeOnTheForearmPlatform()
        {
            _rally.PlayerInvolvement=Level.AddReceiveServe;Serve(Team.Them);
            Launch(Phase.PlayerReceive);
            Assert.That(_rally.RoleTouch,Is.EqualTo(Touch.Receive));
            Assert.That(_destination.y-_drill.CourtFrame.position.y,Is.EqualTo(_rally.ReceiveHeight).Within(.01f));
            Assert.That(_drill.JoinedHands,Is.SameAs(_rally.ReceiveVolume),"Forearm platform only while receiving.");
            Assert.That(_rally.ReceiveVolume.gameObject.activeSelf,Is.True);
            Assert.That(_rally.AwaitingPlayerTouch,Is.True);
        }

        [Test]
        public void LevelTwoPlayerServeFollowsTheSwing()
        {
            _rally.PlayerInvolvement=Level.AddReceiveServe;Serve(Team.Us);Slot(5);
            Launch(Phase.PlayerServe);
            Assert.That(FlatDistance(_rally.GroundedEye,PlayerHome()),Is.LessThan(.05f),"Serving from the player's own spot behind the line.");
            Assert.That(_rally.RoleTouch,Is.EqualTo(Touch.Serve));
            Assert.That(_rally.OverrideTouchVelocity(new Vector3(0f,-1f,9f),_destination,out var serve),Is.True);
            Assert.That(serve.normalized.y,Is.LessThanOrEqualTo(_rally.ServeMaximumRise+.001f));
            Assert.That(serve.magnitude,Is.InRange(_rally.ServeMinimumSpeed,_rally.ServeMaximumSpeed));
            Assert.That(_rally.OverrideTouchVelocity(new Vector3(0f,0f,1f),_destination,out _),Is.False,"A slow touch keeps the normal contact physics.");
        }

        [Test]
        public void LevelThreePlayerPassThenSpikesAndADugBallIsSetByThePlayer()
        {
            _rally.PlayerInvolvement=Level.AddSet;Serve(Team.Them);Slot(0);PerfectOurDefence();PerfectTheirDefence();
            Launch(Phase.PlayerReceive);
            _rally.RegisterPlayerTouch();
            Assert.That(_rally.Touches(Team.Us),Is.EqualTo(1));
            // After the player's own pass our setter plays the second ball, to the player.
            Fly(_destination,new Vector3(0f,6f,-1.5f)); // the player stands near the net: pass back into our court
            Launch(Phase.PlayerSpike,15f);
            Assert.That(_rally.RoleTouch,Is.EqualTo(Touch.Spike));

            // Their attack gets past the block, our back row digs it and the player glides in to set.
            _rally.RegisterPlayerTouch();
            Fly(_destination,new Vector3(0f,3f,4f));
            Launch(Phase.OpponentSpike,15f);
            Fly(_start,GloveBallDemo.Core.VolleyMath.ServeVelocity(_start,_destination,_seconds,Physics.gravity));
            Launch(Phase.PlayerSet,15f);
            Assert.That(_rally.RoleTouch,Is.EqualTo(Touch.Set));
            var ball=_rally.GroundedEye+Forward*.25f+Vector3.up*.3f;
            Assert.That(_rally.OverrideTouchVelocity(new Vector3(-2f,3f,0f),ball,out var set),Is.True,"Push to the left: outside hitter.");
            float t=_rally.AllyFlightSeconds;
            var arrival=ball+set*t+.5f*Physics.gravity*t*t;
            Assert.That(arrival.x-_drill.ReceiveNet.transform.position.x,Is.EqualTo(-2.4f).Within(.01f),"The set heads for the outside hitter's lane.");
            _rally.RegisterPlayerTouch();
            Fly(ball,set);
            Assert.That(_rally.Phase,Is.EqualTo(Phase.MatchPlay),"The outside hitter takes the set over and attacks.");
            Assert.That(_rally.Touches(Team.Us),Is.EqualTo(2));
            Assert.That(_rally.Embodied,Is.SameAs(_rally.Ally),"No switch: the player stays the front-right teammate.");
        }

        [Test]
        public void MovesGlideBehindAVignetteInSlowMotionWithoutBlanking()
        {
            _jump.Floor.SetStanceOffset(Forward*-3f); // start away from the player's spot so the first move is a real glide
            RunUntil(()=>_rally.Embodying,1f,"first glide");
            Assert.That(Time.timeScale,Is.EqualTo(_rally.SlowMotionScale));
            float largestStep=0f;var last=_rally.GroundedEye;
            for(float t=0f;t<3f && _rally.Embodying;t+=.01f)
            {
                Frame();
                Assert.That(_rally.FadeAlpha,Is.Zero,"Never blank.");
                largestStep=Mathf.Max(largestStep,FlatDistance(_rally.GroundedEye,last));last=_rally.GroundedEye;
            }
            Assert.That(_rally.Embodying,Is.False);
            // Smoothstep peaks at 1.5x the mean speed; one 10 ms game frame is 10 ms / SlowMotionScale of real time.
            Assert.That(largestStep,Is.LessThan(_rally.GlideSpeed*1.6f*.01f/_rally.SlowMotionScale),"A continuous glide, not a jump cut.");
            Assert.That(FlatDistance(_rally.GroundedEye,PlayerHome()),Is.LessThan(.05f),"At the player's own spot.");
            Assert.That(Time.timeScale,Is.EqualTo(_scale));
            Assert.That(_rally.VignetteAlpha,Is.GreaterThan(0f),"The vignette opens after the glide.");
            RunUntil(()=>_rally.VignetteAlpha<=0f,1f,"vignette open");
            RunUntil(()=>_rally.Embodying || _launched,40f,"next glide or launch");
            _rally.CancelFeed();
            Assert.That(Time.timeScale,Is.EqualTo(_scale),"Cancelling restores the game speed.");
        }

        [Test]
        public void SideOutRotatesThePlayerClockwiseToTheNextSpot()
        {
            Serve(Team.Them);_rally.TheirDigSuccess=0f;
            Launch(Phase.PlayerSpike);
            Assert.That(_rally.PlayerSlot,Is.EqualTo(2));
            _rally.RegisterPlayerTouch();
            Fly(_destination,new Vector3(0f,-7f,14f));
            Assert.That(_rally.PlayerScore,Is.EqualTo(1));
            Assert.That(_rally.PlayerSlot,Is.EqualTo(1),"Front left -> front middle.");
            RunUntil(()=>_rally.Phase==Phase.MatchPlay && !_rally.Embodying,_rally.RallyResetSeconds+4f,"next rally from the new spot");
            Assert.That(_rally.Embodied,Is.SameAs(_rally.Teammates[0]));
            Assert.That(_rally.Teammates[1].gameObject.activeSelf,Is.True,"The old spot's teammate is back on court.");
            Assert.That(FlatDistance(_rally.GroundedEye,PlayerHome()),Is.LessThan(.05f));
            Assert.That(_rally.ModeLabel,Does.Contain("FRONT MIDDLE"));
        }

        [Test]
        public void MatchWithoutAnyPlayerTouchStillEndsEveryRallyAndRestarts()
        {
            bool over=false;_rally.MatchEnded+=_=>over=true;
            for(int rally=0;rally<40 && !over;rally++)
            {
                _launched=false;
                RunUntil(()=>_launched || over,40f,"next player touch (rally "+rally+")");
                if(over)break;
                // The player never touches: the ball follows its launch and the rally must still resolve.
                Fly(_start,GloveBallDemo.Core.VolleyMath.ServeVelocity(_start,_destination,_seconds,Physics.gravity));
            }
            Assert.That(over,Is.True,"Someone reaches match point.");
            Assert.That(Mathf.Max(_rally.PlayerScore,_rally.OpponentScore),Is.EqualTo(_rally.MatchPoints));
            RunUntil(()=>_rally.PlayerScore+_rally.OpponentScore==0 && _rally.Phase==Phase.MatchPlay,_rally.MatchEndSeconds+3f,"a new match starts");
        }
    }
}
