using System.Reflection;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GloveBallDemo.Tests
{
    /// <summary>The player's spike and block: where the ball goes after a solid contact, and where their attack is aimed.</summary>
    public class VolleySpikeBlockTests
    {
        const string Scene="Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        VolleyDrillController _drill;
        VolleyAerialSequence _rally;
        Random.State _random;

        [SetUp]
        public void Open()
        {
            EditorSceneManager.OpenScene(Scene);
            _drill=Object.FindFirstObjectByType<VolleyDrillController>();_rally=_drill.Aerial;
            _rally.Jump.Floor.Origin.Camera.transform.localPosition=Vector3.up*1.6f;
            _random=Random.state;Random.InitState(11);
        }
        [TearDown]
        public void Clean()=>Random.state=_random;

        Vector3 Forward=>Vector3.ProjectOnPlane(_drill.CourtFrame.forward,Vector3.up).normalized;
        Vector3 Net=>_drill.ReceiveNet.transform.position;
        float NetTop=>_drill.ReceiveNet.bounds.max.y;
        float Floor=>_drill.CourtFrame.position.y;

        /// <summary>Height when crossing the net plane, and where it lands (no drag).</summary>
        void Fly(Vector3 start,Vector3 velocity,out float atNet,out Vector3 landing)
        {
            atNet=float.NaN;var p=start;var v=velocity;
            for(int i=0;i<1000;i++)
            {
                var next=p+v*.005f;v+=Physics.gravity*.005f;
                if(float.IsNaN(atNet) && Vector3.Dot(p-Net,Forward)<0f && Vector3.Dot(next-Net,Forward)>=0f)atNet=next.y;
                p=next;
                if(p.y<=Floor)break;
            }
            landing=p;
        }
        Vector3 SpikeContact=>Net-Forward*.5f+Vector3.up*3.3f;
        float Depth(Vector3 landing)=>Vector3.Dot(landing-Net,Forward);

        [TestCase(0f)]
        [TestCase(-50f)]
        [TestCase(20f)]
        public void ASpikeClearsTheNetAndComesDownSharplyInTheirCourt(float swingPitch)
        {
            var swing=Quaternion.AngleAxis(-swingPitch,Vector3.Cross(Vector3.up,Forward))*Forward*6f;
            var v=_rally.SpikeShot(SpikeContact,swing,14f);
            Fly(SpikeContact,v,out var atNet,out var landing);
            Assert.That(v.y,Is.LessThan(0f),"A spike goes down, never floats ("+swingPitch+"° swing).");
            Assert.That(atNet,Is.GreaterThan(NetTop+.05f),"Clears the tape.");
            Assert.That(Depth(landing),Is.InRange(.5f,_rally.CourtDepth),"Lands in their court.");
            Assert.That(v.magnitude,Is.EqualTo(14f).Within(3f));
        }

        [Test]
        public void ASteeperSwingLandsShorter()
        {
            var side=Vector3.Cross(Vector3.up,Forward);
            Fly(SpikeContact,_rally.SpikeShot(SpikeContact,Quaternion.AngleAxis(50f,side)*Forward*6f,14f),out _,out var steep);
            Fly(SpikeContact,_rally.SpikeShot(SpikeContact,Forward*6f,14f),out _,out var flat);
            Assert.That(Depth(steep),Is.LessThan(Depth(flat)-1f));
        }

        [Test]
        public void APalmBlockThatWouldDropOnOurSideGoesBackOverTheNet()
        {
            var contact=Net-Forward*_rally.BlockStandDistance+Forward*_drill.ContactForwardDistance+Vector3.up*2.9f;
            // A steep spike off still palms: the plain rebound goes down in front of the player.
            var physical=Forward*.5f+Vector3.down*4f;
            Fly(contact,physical,out _,out var dropped);
            Assert.That(Depth(dropped),Is.LessThan(0f),"Without help it stays on our side.");
            var v=_rally.BlockShot(contact,physical);
            Fly(contact,v,out var atNet,out var landing);
            Assert.That(atNet,Is.GreaterThan(NetTop),"Over the tape.");
            Assert.That(Depth(landing),Is.InRange(.3f,_rally.CourtDepth),"Down in their court.");
        }

        [Test]
        public void ASidewaysReboundIsTurnedBackNoMoreThan20Degrees()
        {
            var contact=Net-Forward*_rally.BlockStandDistance+Forward*_drill.ContactForwardDistance+Vector3.up*2.9f;
            var right=Vector3.Cross(Vector3.up,Forward);
            // Off an angled hand: the rebound goes almost as much sideways as forward.
            var v=_rally.BlockShot(contact,Forward*3f-right*3f+Vector3.down*1f);
            Fly(contact,v,out _,out var landing);
            var flat=Vector3.ProjectOnPlane(landing-contact,Vector3.up);
            Assert.That(Vector3.Angle(flat,Forward),Is.LessThanOrEqualTo(_rally.BlockMaximumYaw+1f),"Never a 45° deflection.");
            Assert.That(Depth(landing),Is.GreaterThan(0f));
        }

        [Test]
        public void AHardSpikeStraightAtADefenderIsPlayableEvenBeforeItCouldReact()
        {
            var plan=typeof(VolleyAerialSequence).GetMethod("PlanDig",Private);
            var digger=typeof(VolleyAerialSequence).GetField("_digger",Private);
            typeof(VolleyAerialSequence).GetField("_sinceTouch",Private).SetValue(_rally,0f);
            for(int i=0;i<_rally.Defenders.Length;i++)
            {
                var defender=_rally.Defenders[i];
                if(defender==null)continue;
                bool active=defender.gameObject.activeSelf;defender.gameObject.SetActive(true);
                try
                {
                    // About 22 m/s, 0.25 s from the hand to the defender's forearms: shorter than its reaction.
                    var target=defender.transform.position+Vector3.up*(_rally.DigHeight-.05f);
                    var start=target-Forward*5f+Vector3.up*2.2f;const float Seconds=.25f;
                    var velocity=(target-start)/Seconds-.5f*Physics.gravity*Seconds;
                    Assert.That(Seconds,Is.LessThan(_rally.DefenderReactionSeconds));
                    for(int other=0;other<_rally.Defenders.Length;other++)
                        if(other!=i && _rally.Defenders[other]!=null)_rally.Defenders[other].gameObject.SetActive(false);
                    plan.Invoke(_rally,new object[]{start,velocity,0f});
                    Assert.That(digger.GetValue(_rally),Is.EqualTo(i),"Defender "+i+" plays a hard ball straight at it ("+velocity.magnitude.ToString("0.0")+" m/s).");
                }
                finally{foreach(var d in _rally.Defenders)if(d!=null)d.gameObject.SetActive(d==defender ? active : d.gameObject.activeSelf);}
            }
        }

        /// <summary>Their defence: one defender at <paramref name="at"/> (court frame: x right, z from the net), the others away.</summary>
        VolleyOpponentPrototype OneDefender(Vector2 at)
        {
            typeof(VolleyAerialSequence).GetMethod("CaptureHome",Private).Invoke(_rally,null);
            var defenders=_rally.Defenders;
            for(int i=0;i<defenders.Length;i++)defenders[i].gameObject.SetActive(i==0);
            var defender=defenders[0];
            defender.transform.SetPositionAndRotation(Net+Right*at.x+Forward*at.y-Vector3.up*(Net.y-Floor),Quaternion.LookRotation(-Forward,Vector3.up));
            _rally.SetMode(VolleyAerialSequence.RallyMode.SpikeOnly);
            typeof(VolleyAerialSequence).GetProperty("Phase").SetValue(_rally,VolleyAerialSequence.RallyPhase.PlayerSpike);
            typeof(VolleyAerialSequence).GetMethod("BeginBallInPlay",Private).Invoke(_rally,new object[]{Net});
            typeof(VolleyAerialSequence).GetField("_playerTouched",Private).SetValue(_rally,true);
            return defender;
        }
        Vector3 Right=>Vector3.Cross(Vector3.up,Forward);
        /// <summary>Velocity from <paramref name="start"/> through <paramref name="target"/> in <paramref name="seconds"/> (no drag).</summary>
        static Vector3 Through(Vector3 start,Vector3 target,float seconds)=>(target-start)/seconds-.5f*Physics.gravity*seconds;
        /// <summary>Feeds the flight to the rally at physics rate until the point is decided; the closest the hands came, and at the dig.</summary>
        float Defend(VolleyOpponentPrototype defender,Vector3 position,Vector3 velocity,out float atDig,out float tilt)
        {
            float closest=float.MaxValue;atDig=float.NaN;tilt=0f;
            for(int i=0;i<300 && _rally.Phase==VolleyAerialSequence.RallyPhase.PlayerSpike;i++)
            {
                bool dug=_rally.SampleBall(position,velocity,.11f,.02f);
                float gap=Vector3.Distance(defender.HandsCentre,position);
                closest=Mathf.Min(closest,gap);
                tilt=Mathf.Max(tilt,Vector3.Angle(defender.transform.up,Vector3.up));
                if(dug || _rally.DigResolved && float.IsNaN(atDig)){atDig=gap;if(dug)velocity=_rally.DigVelocity;}
                velocity+=Physics.gravity*.02f;position+=velocity*.02f;
                if(position.y<.11f+Floor){position.y=.11f+Floor;velocity.y=-velocity.y*.6f;}
            }
            return closest;
        }

        [Test]
        public void AJumpSaysWhenItTakesOffAndLandsForItsLightHaptics()
        {
            Assert.That(_drill.JumpHapticGain,Is.EqualTo(.3f).Within(.001f));
            Assert.That(_drill.LandHapticGain,Is.EqualTo(.6f).Within(.001f));
            var jump=_rally.Jump;
            jump.AutomaticJump=true;jump.Suppressed=false;jump.ResetJump();
            int took=0,landed=0;jump.TookOff+=()=>took++;jump.Landed+=()=>landed++;
            jump.Tick(jump.Cooldown+.1f,true,-1f,-1f,false); // cooldown over
            Assert.That(jump.TryStartAutomaticJump(),Is.True);
            Assert.That(took,Is.EqualTo(1));
            for(float t=0f;t<jump.Duration+.2f;t+=.05f)jump.Tick(.05f,true,-1f,-1f,false);
            Assert.That(landed,Is.EqualTo(1),"Landed once, at the end of the jump.");
            jump.TryStartAutomaticJump();jump.ResetJump();
            Assert.That(landed,Is.EqualTo(1),"A reset is not a landing.");
        }

        /// <summary>Only <paramref name="keep"/> of their players stays on court (the rest stand down).</summary>
        void OnlyOnCourt(VolleyOpponentPrototype keep)
        {
            foreach(var d in _rally.Defenders)if(d!=null)d.gameObject.SetActive(d==keep);
            foreach(var b in _rally.Blockers)if(b!=null)b.gameObject.SetActive(b==keep);
            if(_rally.Opponent!=null)_rally.Opponent.gameObject.SetActive(_rally.Opponent==keep);
        }

        [Test]
        public void AHardSharpSpikeAwayFromEveryoneIsPutAway()
        {
            _rally.DigEasyChance=_rally.DigHardChance=1f;_rally.DiveChance=1f;
            var defender=OneDefender(new Vector2(-2.8f,6.5f));
            OnlyOnCourt(defender);
            // Down 3 m from it 0.45 s after the hit: before anyone reacts.
            var start=Net-Forward*.5f+Vector3.up*3.3f;
            var target=defender.transform.position+Right*3f+Forward*-.5f+Vector3.up*.3f;
            Defend(defender,start,Through(start,target,.45f),out _,out _);
            Assert.That(_rally.Outcome,Is.EqualTo("POINT!"));
        }

        [Test]
        public void AHardSpikeAtTheirBodyIsMetByTheArms()
        {
            _rally.DigEasyChance=_rally.DigHardChance=1f;
            var defender=OneDefender(new Vector2(0f,6f));
            OnlyOnCourt(defender);
            // Hard (about 17 m/s) and straight at its platform: a step of the ball is longer than the contact radius.
            var target=defender.transform.position-Forward*.45f+Vector3.up*.8f;
            var start=Net-Forward*.5f+Vector3.up*3.3f;
            var velocity=Through(start,target,.38f);
            Assert.That(velocity.magnitude,Is.GreaterThan(15f));
            Defend(defender,start,velocity,out float atDig,out _);
            Assert.That(_rally.Outcome,Is.EqualTo("DUG"));
            Assert.That(atDig,Is.LessThanOrEqualTo(_rally.HandContactRadius*2f));
        }

        [Test]
        public void ASoftBallIsRunDownNotWatched()
        {
            _rally.DigEasyChance=_rally.DigHardChance=1f;
            var defender=OneDefender(new Vector2(-2.8f,6.5f));
            OnlyOnCourt(defender);
            // Floated: in the air 1.2 s, down 2.2 m from it.
            var target=defender.transform.position+Right*2.2f+Vector3.up*.8f;
            var start=Net-Forward*.3f+Vector3.up*2.8f;
            Defend(defender,start,Through(start,target,1.2f),out float atDig,out _);
            Assert.That(_rally.Outcome,Is.EqualTo("DUG"));
            Assert.That(atDig,Is.LessThanOrEqualTo(_rally.HandContactRadius*2f));
        }

        [Test]
        public void TheirFrontRowPlaysASoftBallThatComesDownNearIt()
        {
            _rally.DigEasyChance=_rally.DigHardChance=1f;
            OneDefender(new Vector2(-2.8f,6.5f));
            var blocker=_rally.Blockers[2];
            OnlyOnCourt(blocker);
            blocker.transform.SetPositionAndRotation(Net+Right*1.5f+Forward*.6f-Vector3.up*(Net.y-Floor),Quaternion.LookRotation(-Forward,Vector3.up));
            // A weak block: the ball floats back over the net and comes down 1.3 m behind the blocker after a second.
            var target=blocker.transform.position+Forward*1.3f+Vector3.up*.8f;
            var start=Net-Forward*.3f+Vector3.up*2.6f;
            Defend(blocker,start,Through(start,target,1f),out float atDig,out _);
            Assert.That(_rally.Outcome,Is.EqualTo("DUG"),"Their front row plays it.");
            Assert.That(atDig,Is.LessThanOrEqualTo(_rally.HandContactRadius*2f));
            Assert.That(blocker.Role,Is.EqualTo(VolleyOpponentPrototype.MotionRole.Dig),"It digs (its block role back after the point).");
        }

        [Test]
        public void ADiveThatGetsTheBallEndsWithItsHandsOnIt()
        {
            _rally.DigEasyChance=_rally.DigHardChance=1f;_rally.DiveChance=1f;
            var defender=OneDefender(new Vector2(-2.8f,6f));
            // Down to 0.4 m about 1.5 m to its side, 0.45 s after the hit: too far to run, a dive gets there.
            var target=defender.transform.position+Right*1.5f+Vector3.up*.4f;
            var start=target-Forward*4f+Vector3.up*2.5f;
            Defend(defender,start,Through(start,target,.45f),out float atDig,out float tilt);
            Assert.That(_rally.Outcome,Is.EqualTo("DUG"));
            Assert.That(_rally.DigIsDive,Is.True,"It dived.");
            Assert.That(tilt,Is.GreaterThan(20f),"Tipped toward the ball.");
            Assert.That(atDig,Is.LessThanOrEqualTo(_rally.HandContactRadius*2f),"The ball is at its hands when it is dug.");
        }

        [Test]
        public void ADiveThatWillNotGetTheBallEndsVisiblyShort()
        {
            _rally.DigEasyChance=_rally.DigHardChance=1f;_rally.DiveChance=0f;
            var defender=OneDefender(new Vector2(-2.8f,6f));
            var target=defender.transform.position+Right*1.5f+Vector3.up*.4f;
            var start=target-Forward*4f+Vector3.up*2.5f;
            float closest=Defend(defender,start,Through(start,target,.45f),out _,out float tilt);
            Assert.That(tilt,Is.GreaterThan(10f),"It still dives.");
            Assert.That(closest,Is.GreaterThan(_rally.HandContactRadius),"...but its hands never reach the ball.");
            Assert.That(_rally.Outcome,Is.EqualTo("POINT!"));Assert.That(_rally.PlayerScore,Is.EqualTo(1));
        }

        [Test]
        public void APlatformThatDoesNotControlTheBallShanksItOffTheArms()
        {
            _rally.DigEasyChance=_rally.DigHardChance=0f;
            var defender=OneDefender(new Vector2(0f,5f));
            // Straight at it, slowly: it plays it where it stands.
            var target=defender.transform.position+Vector3.up*.8f;
            var start=target-Forward*5f+Vector3.up*1.5f;
            Defend(defender,start,Through(start,target,.9f),out float atDig,out _);
            Assert.That(atDig,Is.LessThanOrEqualTo(_rally.HandContactRadius*2f),"It touches the ball with its arms.");
            Assert.That(_rally.Outcome,Is.EqualTo("SHANKED!"));Assert.That(_rally.PlayerScore,Is.EqualTo(1),"Their touch: the player's point.");
        }

        [Test]
        public void TheirHitterComesFromTheLeftFrontAndRightOfThePlayerEvenly()
        {
            typeof(BallPool).GetMethod("Awake",Private).Invoke(_drill.Pool,null);
            typeof(VolleyAerialSequence).GetMethod("CaptureHome",Private).Invoke(_rally,null);
            var begin=typeof(VolleyAerialSequence).GetMethod("BeginOpponentReceive",Private);
            var prepared=typeof(VolleyAerialSequence).GetField("_preparedBall",Private);
            var targets=(Vector3[])typeof(VolleyAerialSequence).GetField("_blockerTargets",Private).GetValue(_rally);
            foreach(float standing in new[]{0f,.7f})
            {
                _rally.Jump.Floor.Origin.Camera.transform.localPosition=new Vector3(standing,1.6f,0f);
                int left=0,front=0,right=0;
                for(int i=0;i<30;i++)
                {
                    prepared.SetValue(_rally,null);
                    Assert.That(begin.Invoke(_rally,new object[]{null}),Is.True);
                    var ball=prepared.GetValue(_rally) as Ball;if(ball!=null)_drill.Pool.Return(ball,"test");
                    float hand=Vector3.Dot(_rally.Opponent.ReleasePosition-_rally.GroundedEye,Right);
                    if(hand<-.6f)left++;else if(hand>.6f)right++;else front++;
                    float middle=Vector3.Dot(targets[1]-Net,Right),hitter=Vector3.Dot(_rally.Opponent.ReleasePosition-Net,Right);
                    Assert.That(Mathf.Abs(middle-hitter),Is.GreaterThan(1.2f),"Their idle middle stands clear of the hitter.");
                }
                Assert.That(left,Is.EqualTo(10),"From the left once in every three (standing "+standing+" m right).");
                Assert.That(right,Is.EqualTo(10),"From the right once in every three (standing "+standing+" m right).");
            }
        }

        [Test]
        public void OurSetterIsAlwaysOnThePlayersRight()
        {
            typeof(VolleyAerialSequence).GetMethod("CaptureHome",Private).Invoke(_rally,null);
            for(int i=0;i<10;i++)
            {
                typeof(VolleyAerialSequence).GetMethod("StartFlowTurn",Private).Invoke(_rally,new object[]{VolleyAerialSequence.RallyTurn.Block});
                typeof(VolleyAerialSequence).GetMethod("StartFlowTurn",Private).Invoke(_rally,new object[]{VolleyAerialSequence.RallyTurn.Spike});
                Assert.That(Vector3.Dot(_rally.Ally.transform.position-Net,Right),Is.GreaterThan(.5f));
            }
        }

        [Test]
        public void NoJumpInReceiveOrTossWhateverAutoJumpSays()
        {
            var jump=_rally.Jump;
            foreach(bool auto in new[]{false,true})
            {
                jump.AutomaticJump=auto;
                foreach(var mode in new[]{VolleyAerialSequence.RallyMode.Pass,VolleyAerialSequence.RallyMode.Set})
                {
                    _rally.SetMode(mode);
                    Assert.That(jump.Suppressed,Is.True,mode+" never jumps");
                    jump.ResetJump();jump.Tick(1f,true,-.6f,-.6f,false);
                    // Hands swung up from low (a pass's follow-through): the jump gesture.
                    jump.Tick(.05f,true,-.6f,-.6f,false);jump.Tick(.1f,true,.1f,.1f,false);
                    Assert.That(jump.Airborne,Is.False,mode+" (auto "+auto+")");
                    Assert.That(jump.TryStartAutomaticJump(),Is.False);
                }
                _rally.SetMode(VolleyAerialSequence.RallyMode.SpikeOnly);
                Assert.That(jump.Suppressed,Is.False,"SPIKE jumps.");
            }
        }

        [Test]
        public void AJumpOffTheMomentIsCalledEarlyOrLateAndTheTipSaysSo()
        {
            _rally.Jump.AutomaticJump=false;
            var resolve=typeof(VolleyAerialSequence).GetMethod("ResolveJumpTiming",Private);
            void Rep(float jumpAt,bool block)
            {
                typeof(VolleyAerialSequence).GetField("_dropSeconds",Private).SetValue(_rally,1f);
                typeof(VolleyAerialSequence).GetProperty("SpikeJumpTime").SetValue(_rally,.8f);
                typeof(VolleyAerialSequence).GetField("_autoJumpAt",Private).SetValue(_rally,.9f);
                typeof(VolleyAerialSequence).GetField("_attemptClock",Private).SetValue(_rally,3f);
                typeof(VolleyAerialSequence).GetField("_jumpAt",Private).SetValue(_rally,jumpAt);
                resolve.Invoke(_rally,new object[]{block});
            }
            Rep(1.8f,false);Assert.That(_rally.LastJumpTiming,Is.EqualTo(VolleyAerialSequence.JumpTiming.Good));
            Assert.That(VolleyRallyPresenter.TipFor(_rally,false,false),Is.EqualTo("いちばん高いところで、上から下へたたこう"));
            Rep(1.4f,false);Assert.That(_rally.LastJumpTiming,Is.EqualTo(VolleyAerialSequence.JumpTiming.Early));
            Assert.That(VolleyRallyPresenter.TipFor(_rally,false,false),Does.Contain("早い"));
            Rep(2.2f,true);Assert.That(_rally.LastJumpTiming,Is.EqualTo(VolleyAerialSequence.JumpTiming.Late),"Block: against the attack's jump moment.");
            Assert.That(VolleyRallyPresenter.TipFor(_rally,true,false),Does.Contain("遅い"));
            Rep(float.NaN,true);Assert.That(_rally.LastJumpTiming,Is.EqualTo(VolleyAerialSequence.JumpTiming.NoJump));
            Assert.That(VolleyRallyPresenter.TipFor(_rally,true,true),Is.Null,"A block that went over needs no tip.");
            _rally.Jump.AutomaticJump=true;Rep(1.4f,false);
            Assert.That(VolleyRallyPresenter.TipFor(_rally,false,false),Does.Not.Contain("ジャンプ"),"AUTO JUMP: the timing is not the player's.");
        }

        [Test]
        public void QuietBackgroundMusicLoopsUnderThePlay()
        {
            Assert.That(_rally.Music,Is.Not.Null,"A CC0 drum loop (Audio/Music/sources.json).");
            Assert.That(_rally.Music.length,Is.EqualTo(7.5f).Within(.01f),"Cut to the loop's own length: no gap when it repeats.");
            Assert.That(_rally.MusicVolume,Is.EqualTo(.1f).Within(.001f),"Just audible under the voice and the ball's sounds.");
        }

        [Test]
        public void WithTheHandJumpTheirAttackComesWhereTheHandsWillBe()
        {
            _rally.Jump.AutomaticJump=false;_rally.Jump.ResetJump();
            var lift=typeof(VolleyAerialSequence).GetMethod("BlockLiftAtArrival",Private);
            Assert.That((float?)lift.Invoke(_rally,null),Is.EqualTo(0f),"On the ground: at raised hands, not over them.");
            var grounded=_rally.Destination(0f,0f,false,0f);
            Assert.That(grounded.y-_rally.GroundedEye.y,Is.EqualTo(_rally.BlockReachAboveEye).Within(.001f));
            _rally.Jump.AutomaticJump=true;
            Assert.That(lift.Invoke(_rally,null),Is.Null,"AUTO JUMP: the full jump (it times the jump itself).");
        }

        [Test]
        public void TheJumpIsCalledHalfwayToTheSetsApex()
        {
            _rally.SetMode(VolleyAerialSequence.RallyMode.SpikeOnly);
            typeof(BallPool).GetMethod("Awake",Private).Invoke(_drill.Pool,null);
            typeof(VolleyAerialSequence).GetMethod("CaptureHome",Private).Invoke(_rally,null);
            Assert.That(typeof(VolleyAerialSequence).GetMethod("BeginAllyReceive",Private).Invoke(_rally,new object[]{null}),Is.True);
            typeof(VolleyAerialSequence).GetField("_rallyTimer",Private).SetValue(_rally,99f);
            var args=new object[]{.02f,null,null,null};
            Assert.That(typeof(VolleyAerialSequence).GetMethod("TickAllyReceive",Private).Invoke(_rally,args),Is.True,"The set leaves.");
            var start=(Vector3)args[1];var destination=(Vector3)args[2];float seconds=(float)args[3];
            float g=Mathf.Abs(Physics.gravity.y),apex=((destination.y-start.y)/seconds+.5f*g*seconds)/g;
            float call=(float)typeof(VolleyAerialSequence).GetField("_spikeCallAt",Private).GetValue(_rally);
            Assert.That(call,Is.EqualTo(apex*.5f).Within(.01f));
            Assert.That(call,Is.LessThan(_rally.SpikeJumpTime-.2f),"Well before the automatic jump: time to act on it.");
        }

        [Test]
        public void ABlockIsSolidWhereverTheHandMeetsIt()
        {
            Assert.That(typeof(VolleyDrillController).GetField("FingertipBlockPassThrough"),Is.Null,"No fingertip block that lets the ball through.");
            Assert.That(typeof(VolleyDrillController).GetField("EdgeBlockPassThrough"),Is.Null);
            Assert.That(_drill.HapticsAtMaximum,Is.True,"For now every hand touch at the full gain (the clip's own strength).");
            Assert.That(typeof(VolleyDrillController).GetField("FingertipHapticScale"),Is.Null,"Where on the hand never weakens the feel.");
            Assert.That(_drill.ReceiveMinimumGain,Is.GreaterThanOrEqualTo(1f),"A receive is felt like a good one.");
        }

        [Test]
        public void TheBlockForearmsAreTheTutorialForearmWithItsSkeleton()
        {
            Assert.That(_drill.BlockArms.Length,Is.EqualTo(2));
            for(int side=0;side<2;side++)
            {
                Assert.That(_drill.BlockArms[side],Is.Not.Null);
                Assert.That(_drill.BlockArms[side].Left,Is.EqualTo(side==0));
                Assert.That(_drill.BlockArms[side].ShowForearm,Is.True);
                Assert.That(_drill.BlockArms[side].PlaceholderSkeleton,Is.Not.Null,"Without its skeleton the package draws nothing.");
            }
        }

        [Test]
        public void AHighContactMakesTheFastSpikeMoreThanAFastSwing()
        {
            float top=_rally.GroundedEye.y+_rally.Jump.JumpHeight+_rally.SpikeReachAboveEye;
            float highSlow=_rally.SpikeSpeed(Vector3.up*top,_drill.SpikeSwingSpeed);
            float lowFast=_rally.SpikeSpeed(Vector3.up*(top-_rally.SpikeHeightWindow),_rally.SpikeFullSwing);
            Assert.That(highSlow,Is.GreaterThan(lowFast),"Height counts more than swing speed.");
            Assert.That(_rally.SpikeSpeed(Vector3.up*top,_rally.SpikeFullSwing),Is.EqualTo(_drill.SpikeMaximumSpeed).Within(.01f));
        }

        [Test]
        public void ResetViewAlsoSetsTheEyeHeight()
        {
            var menu=Object.FindFirstObjectByType<VolleyHandMenu>();var floor=_rally.Jump.Floor;var original=Time.timeScale;
            try
            {
                floor.Origin.Camera.transform.localPosition=Vector3.up*1.25f;
                menu.SetOpen(true);menu.RepositionToStart();
                Assert.That(floor.EyeHeight,Is.EqualTo(floor.StandingEyeHeight).Within(.01f),"視線をリセット puts the eyes at the standing height too.");
            }
            finally{menu.SetOpen(false);Time.timeScale=original;}
        }

        [Test]
        public void AttackLinesAre3MetresFromTheNet()
        {
            var root=GameObject.Find("Volley attack lines (3 m)");
            Assert.That(root,Is.Not.Null);
            int lines=0;
            foreach(Transform strip in root.transform)
            {
                if(!strip.name.StartsWith("Attack line"))continue;
                lines++;
                float rearEdge=Mathf.Abs(Vector3.Dot(strip.position-Net,Forward))+strip.lossyScale.y*.5f;
                Assert.That(rearEdge,Is.EqualTo(3f).Within(.005f),"Rear edge 3 m from the centre line.");
                Assert.That(strip.lossyScale.y,Is.EqualTo(.05f).Within(.001f));
                Assert.That(strip.lossyScale.x,Is.EqualTo(9f).Within(.01f));
            }
            Assert.That(lines,Is.EqualTo(2));
            Assert.That(NetTop,Is.EqualTo(2.43f).Within(.005f),"Men's net height.");
        }

        [Test]
        public void TheirAttackIsCentredOnThePlayerWithANarrowSpread()
        {
            var field=typeof(VolleyAerialSequence).GetField("_attackYaw",Private);
            var opponent=_rally.Opponent;opponent.Role=VolleyOpponentPrototype.MotionRole.Spike;
            float sum=0f,max=0f;const int Count=400;
            for(int i=0;i<Count;i++)
            {
                field.SetValue(_rally,Random.Range(-_rally.AttackBodyYaw,_rally.AttackBodyYaw));
                float lateral=_rally.AttackLateral(opponent.ReleasePosition);
                sum+=lateral;max=Mathf.Max(max,Mathf.Abs(lateral));
            }
            Assert.That(Mathf.Abs(sum/Count),Is.LessThan(.08f),"Not biased to the player's left (the hitter's right hand).");
            Assert.That(max,Is.LessThanOrEqualTo(_rally.BlockLateralLimit+1e-4f));
        }
    }
}
