using System.IO;
using System.Linq;
using System.Reflection;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Mode = GloveBallDemo.Runtime.VolleyAerialSequence.RallyMode;
using Phase = GloveBallDemo.Runtime.VolleyAerialSequence.RallyPhase;
using Script = GloveBallDemo.Runtime.VolleyAerialSequence.DefenceScript;
using Taker = GloveBallDemo.Runtime.VolleyAerialSequence.PassTaker;

namespace GloveBallDemo.Tests
{
    /// <summary>The SPIKE + BLOCK and RECEIVE tutorials: goals, what advances them, never waiting for the voice, and the voice lines.</summary>
    public class VolleyTutorialTests
    {
        const string Scene="Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        VolleyDrillController _drill;
        VolleyAerialSequence _rally;
        VolleyTutorial _tutorial;
        float _timeScale;

        [SetUp]
        public void Open()
        {
            EditorSceneManager.OpenScene(Scene);
            _drill=Object.FindFirstObjectByType<VolleyDrillController>();_rally=_drill.Aerial;
            _tutorial=_drill.GetComponent<VolleyTutorial>();
            _timeScale=Time.timeScale;
            typeof(BallPool).GetMethod("Awake",Private).Invoke(_drill.Pool,null);
            typeof(VolleyAerialSequence).GetMethod("CaptureHome",Private).Invoke(_rally,null);
            VolleyTutorial.ResetLaunchForTests();
        }
        [TearDown]
        public void Clean()
        {
            if(_tutorial!=null)_tutorial.Stop(false);VolleyTutorial.ResetLaunchForTests();
            // A RECEIVE rep's move to the player's spot runs in slow motion: never leave it set (it would be saved to the project).
            _rally.CancelFeed();Time.timeScale=_timeScale;
        }

        void Wait(float seconds){for(float t=0f;t<seconds;t+=.05f)_tutorial.Tick(.05f);}
        void SetPhase(Phase phase)=>typeof(VolleyAerialSequence).GetProperty("Phase").SetValue(_rally,phase);
        void Award(Phase phase,bool player,string text)
        {
            SetPhase(phase);
            typeof(VolleyAerialSequence).GetMethod("Award",Private).Invoke(_rally,new object[]{player,text,Vector3.zero});
        }
        void Spike(float quality,bool player,string text)
        {
            typeof(VolleyAerialSequence).GetProperty("LastSpikeQuality").SetValue(_rally,quality);
            Award(Phase.PlayerSpike,player,text);
        }

        [Test]
        public void SpikeBlockTutorialStartsAtOnceSpikesThenBlocksAndStartsTheMatch()
        {
            Assert.That(_tutorial,Is.Not.Null,"The scene setup adds the tutorial.");
            _rally.Jump.AutomaticJump=true;
            int points=_rally.MatchPoints;
            _tutorial.Begin(VolleyTutorial.Course.SpikeBlock);
            Assert.That(_tutorial.Step,Is.EqualTo("sb_spike_auto"),"AUTO JUMP on: straight to the spikes, no intro.");
            Assert.That(_drill.HoldRally,Is.False,"The ball never waits for the voice.");
            Assert.That(_rally.Mode,Is.EqualTo(Mode.SpikeOnly));
            Assert.That(_rally.ScriptedDefence,Is.EqualTo(Script.NeverDig),"A strong spike in is a point.");
            Assert.That(_rally.MatchPoints,Is.GreaterThan(100),"Practice points never end a match.");

            Spike(.2f,true,"POINT!");
            Assert.That(_tutorial.Count,Is.Zero,"A weak spike does not count.");
            Assert.That(_tutorial.PanelHint,Is.EqualTo(VolleyTutorialText.Get("sb_spike_low_auto").Body));
            Spike(.9f,false,"NET");
            Assert.That(_tutorial.Count,Is.Zero);
            Spike(.9f,true,"POINT!");
            Assert.That(_tutorial.Count,Is.EqualTo(1));
            Assert.That(_rally.ScriptedDefence,Is.EqualTo(Script.NeverDig),"Every strong spike is a point: nothing is dug.");
            Spike(.9f,true,"POINT!");
            Assert.That(_tutorial.Step,Is.EqualTo("sb_block"),"Two strong spikes, then the block.");
            Assert.That(_tutorial.Count,Is.Zero);
            Wait(.1f);
            Assert.That(_rally.Mode,Is.EqualTo(Mode.BlockOnly),"BLOCK from the next rep.");
            Award(Phase.OpponentSpike,false,"NO BLOCK");
            Assert.That(_tutorial.Count,Is.Zero);
            Assert.That(_tutorial.PanelHint,Is.EqualTo(VolleyTutorialText.Get("sb_block_hint").Body));
            Award(Phase.OpponentSpike,true,"BLOCK POINT!");
            Assert.That(_tutorial.Count,Is.EqualTo(1));
            Award(Phase.OpponentSpike,true,"BLOCK POINT!");
            Assert.That(_tutorial.PanelBody,Is.EqualTo(VolleyTutorialText.Get("sb_end").Body),"The match announced, shortly...");
            Assert.That(_tutorial.Panel.gameObject.activeSelf,Is.False,"...by voice only: a panel would read as more tutorial.");
            Wait(.1f);
            Assert.That(_tutorial.Running,Is.EqualTo(VolleyTutorial.Course.None),"The match starts as soon as the goals are met.");
            Assert.That(_rally.Mode,Is.EqualTo(Mode.Alternate));
            Assert.That(_rally.ScriptedDefence,Is.EqualTo(Script.Natural));
            Assert.That(_rally.MatchPoints,Is.EqualTo(points));Assert.That(_drill.HoldRally,Is.False);
            Assert.That(_rally.PlayerScore+_rally.OpponentScore,Is.Zero,"The match from 0 - 0.");
        }

        [Test]
        public void TheReceivePracticeServesTheSameTwoBallsInTurnToOneSetter()
        {
            _tutorial.Begin(VolleyTutorial.Course.Receive);
            Assert.That(_rally.PracticeServes,Is.True);
            var start=typeof(VolleyAerialSequence).GetMethod("StartStationRep",Private);
            var server=typeof(VolleyAerialSequence).GetMethod("Server",Private);
            var actorAt=typeof(VolleyAerialSequence).GetMethod("ActorAt",Private);
            var them=VolleyAerialSequence.Team.Them;var us=VolleyAerialSequence.Team.Us;
            Vector3 Home(object actor)=>(Vector3)actor.GetType().GetField("Home").GetValue(actor);
            typeof(VolleyAerialSequence).GetMethod("EnsureMatchCast",Private).Invoke(_rally,null);
            var froms=new System.Collections.Generic.List<Vector3>();var setters=new System.Collections.Generic.List<Vector3>();
            for(int i=0;i<4;i++)
            {
                start.Invoke(_rally,null);
                froms.Add(Home(server.Invoke(_rally,new object[]{them})));
                setters.Add(Home(actorAt.Invoke(_rally,new object[]{us,0})));
            }
            Assert.That(Vector3.Distance(froms[0],froms[2]),Is.LessThan(.001f),"The first serve's spot every other time.");
            Assert.That(Vector3.Distance(froms[1],froms[3]),Is.LessThan(.001f));
            Assert.That(Vector3.Distance(froms[0],froms[1]),Is.GreaterThan(2f),"Two different spots.");
            Assert.That(setters.TrueForAll(s=>Vector3.Distance(s,setters[0])<.001f),Is.True,"One setter spot.");
            _tutorial.Stop(false);
            Assert.That(_rally.PracticeServes,Is.False);
        }

        [Test]
        public void WithAutoJumpOffTheTutorialTeachesTheJumpFirstAndCallsTheJump()
        {
            _rally.Jump.AutomaticJump=false;
            _tutorial.Begin(VolleyTutorial.Course.SpikeBlock);
            Assert.That(_tutorial.Step,Is.EqualTo("sb_jump"));Assert.That(_drill.HoldRally,Is.True,"No ball while learning to jump.");
            Assert.That(_rally.Mode,Is.EqualTo(Mode.SpikeOnly),"Practised where it is used: a RECEIVE never jumps.");
            typeof(VolleyArmJump).GetProperty("Jumps").SetValue(_rally.Jump,_rally.Jump.Jumps+1);
            Wait(.1f);
            Assert.That(_tutorial.Step,Is.EqualTo("sb_jump"),"Two jumps.");Assert.That(_tutorial.PanelCounter,Is.EqualTo("1 / 2"));
            // The second jump counts when it lands, not the moment it takes off.
            typeof(VolleyArmJump).GetProperty("Airborne").SetValue(_rally.Jump,true);
            typeof(VolleyArmJump).GetProperty("Jumps").SetValue(_rally.Jump,_rally.Jump.Jumps+1);
            Wait(.1f);
            Assert.That(_tutorial.Step,Is.EqualTo("sb_jump"),"Still in the air.");Assert.That(_tutorial.PanelCounter,Is.EqualTo("2 / 2"));
            typeof(VolleyArmJump).GetProperty("Airborne").SetValue(_rally.Jump,false);
            Wait(.1f);
            Assert.That(_tutorial.Step,Is.EqualTo("sb_spike"));Assert.That(_drill.HoldRally,Is.False);
            bool called=false;
            _rally.JumpCall+=spike=>called|=spike;
            typeof(VolleyAerialSequence).GetField("_jumpCallSent",Private).SetValue(_rally,false);
            typeof(VolleyAerialSequence).GetField("_spikeCallAt",Private).SetValue(_rally,0f);
            typeof(VolleyAerialSequence).GetMethod("TickPlayerSpike",Private).Invoke(_rally,new object[]{.02f});
            Assert.That(called,Is.True);
        }

        /// <summary>A RECEIVE pass graded <paramref name="grade"/> and set by <paramref name="taker"/>.</summary>
        void Pass(string grade,VolleyAerialSequence.PassTaker taker)
        {
            typeof(VolleyAerialSequence).GetMethod("ShowGrade",Private).Invoke(_rally,new object[]{grade});
            typeof(VolleyAerialSequence).GetProperty("LastPassTaker").SetValue(_rally,taker);
            _tutorial.Tick(.05f);
        }

        [Test]
        public void ReceiveTutorialsSecondGoalMarksTheOthersAndRestartsAPassToTheSetter()
        {
            _tutorial.Begin(VolleyTutorial.Course.Receive);
            Assert.That(_rally.MarkOtherPassers,Is.False,"First the setter is the target.");
            Pass("GOOD",Taker.Setter);
            Assert.That(_tutorial.GotSetter,Is.True,"The setter played it: GOOD (it had to move) counts as well as EXCELLENT.");
            Assert.That(_tutorial.PanelHint,Is.EqualTo(VolleyTutorialText.Get("rc_setter_moved").Body),"...and why it is only GOOD.");
            Assert.That(_rally.MarkOtherPassers,Is.True,"Then the two others (yellow), not the setter.");
            SetPhase(Phase.MatchPlay);
            Pass("EXCELLENT",Taker.Setter);
            Assert.That(_tutorial.GotOther,Is.False);
            Assert.That(_tutorial.PanelHint,Is.EqualTo(VolleyTutorialText.Get("rc_try_other").Body));
            Assert.That(_rally.Phase,Is.EqualTo(Phase.Idle),"To the setter again: that rep ends at once, the next serve comes.");
            Pass("GOOD",Taker.Other);
            Assert.That(_rally.MarkOtherPassers,Is.False,"Goals met: the setter is the target in the match.");
        }

        [Test]
        public void ReceiveTutorialWantsOnePassToTheSetterAndOneToAnotherPlayerAndExplainsEachOnce()
        {
            int points=_rally.MatchPoints;
            _tutorial.Begin(VolleyTutorial.Course.Receive);
            Assert.That(_tutorial.Step,Is.EqualTo("rc_pass"));Assert.That(_rally.Mode,Is.EqualTo(Mode.Pass));
            Assert.That(_drill.HoldRally,Is.False);
            Pass("BAD",Taker.None);
            Assert.That(_tutorial.PanelHint,Is.EqualTo(VolleyTutorialText.Get("rc_bad").Body),"BAD explained the first time.");
            Pass("GOOD",Taker.Other);
            Assert.That(_tutorial.GotOther,Is.True);Assert.That(_tutorial.GotSetter,Is.False);
            Assert.That(_tutorial.PanelHint,Is.EqualTo(VolleyTutorialText.Get("rc_good").Body));
            Assert.That(_tutorial.PanelBody,Is.EqualTo(VolleyTutorialText.Get("rc_try_setter").Body),"Then aim for the setter.");
            Pass("BAD",Taker.None);
            Pass("GOOD",Taker.Other);
            Assert.That(_tutorial.PanelHint,Is.EqualTo(VolleyTutorialText.Get("rc_bad").Body),"A second pass to another player explains nothing again.");
            Assert.That(_tutorial.Running,Is.EqualTo(VolleyTutorial.Course.Receive));
            Pass("EXCELLENT",Taker.Setter);
            Assert.That(_tutorial.GotSetter,Is.True);
            Assert.That(_tutorial.PanelBody,Is.EqualTo(VolleyTutorialText.Get("rc_end_"+points).Body));
            // The match starts once the rep that met the goals has ended.
            SetPhase(Phase.MatchPlay);_tutorial.Tick(.05f);
            Assert.That(_tutorial.Running,Is.EqualTo(VolleyTutorial.Course.Receive));
            SetPhase(Phase.Recovery);_tutorial.Tick(.05f);
            Assert.That(_tutorial.Running,Is.EqualTo(VolleyTutorial.Course.None));
            Assert.That(_rally.Mode,Is.EqualTo(Mode.Pass));
        }

        [Test]
        public void TheVoiceIsNotOnThePanel()
        {
            _tutorial.Begin(VolleyTutorial.Course.Receive);
            Assert.That(_tutorial.Panel.GetComponentsInChildren<AudioSource>(true),Is.Empty,"Hiding the panel at the match start must not silence its announcement.");
            Assert.That(_tutorial.GetComponents<AudioSource>(),Has.Length.EqualTo(2));
        }

        [Test]
        public void TheFirstStartBeginsWithTheTutorialOnceAndARetryNever()
        {
            Assert.That(_tutorial.StartOnLaunch,Is.True);
            Assert.That(VolleyTutorial.CourseFor(Mode.Alternate),Is.EqualTo(VolleyTutorial.Course.SpikeBlock));
            Assert.That(VolleyTutorial.CourseFor(Mode.Pass),Is.EqualTo(VolleyTutorial.Course.Receive));
            Assert.That(VolleyTutorial.CourseFor(Mode.Match),Is.EqualTo(VolleyTutorial.Course.None));
            var launch=(System.Collections.IEnumerator)typeof(VolleyTutorial).GetMethod("LaunchTutorial",Private).Invoke(_tutorial,null);
            while(launch.MoveNext()){}
            Assert.That(_tutorial.Running,Is.EqualTo(VolleyTutorial.Course.SpikeBlock),"The default mode's tutorial at the first start.");
            _tutorial.Stop(false);
            launch=(System.Collections.IEnumerator)typeof(VolleyTutorial).GetMethod("LaunchTutorial",Private).Invoke(_tutorial,null);
            while(launch.MoveNext()){}
            Assert.That(_tutorial.Running,Is.EqualTo(VolleyTutorial.Course.None),"Only once per start of the app.");
            VolleyTutorial.ResetLaunchForTests();VolleyTutorial.SkipLaunchTutorial();
            launch=(System.Collections.IEnumerator)typeof(VolleyTutorial).GetMethod("LaunchTutorial",Private).Invoke(_tutorial,null);
            while(launch.MoveNext()){}
            Assert.That(_tutorial.Running,Is.EqualTo(VolleyTutorial.Course.None),"Session option tutorial off.");
        }

        [Test]
        public void PickingAModeFromTheMenuEndsTheTutorial()
        {
            var menu=Object.FindFirstObjectByType<VolleyHandMenu>();
            float scale=Time.timeScale;
            try
            {
                _tutorial.Begin(VolleyTutorial.Course.SpikeBlock);
                menu.SetOpen(true);menu.Play(Mode.BlockOnly);
                Assert.That(_tutorial.Running,Is.EqualTo(VolleyTutorial.Course.None));
                Assert.That(_drill.HoldRally,Is.False);Assert.That(_rally.Mode,Is.EqualTo(Mode.BlockOnly));
                Assert.That(_rally.ScriptedDefence,Is.EqualTo(Script.Natural));
            }
            finally{menu.SetOpen(false);Time.timeScale=scale;}
        }

        [Test]
        public void ThePanelStaysOnOurSideOfTheNet()
        {
            _tutorial.Begin(VolleyTutorial.Course.SpikeBlock);
            // Standing half a metre from the net (the block spot).
            var forward=Vector3.ProjectOnPlane(_drill.CourtFrame.forward,Vector3.up).normalized;
            var net=_drill.ReceiveNet.transform.position;
            _drill.Head.position=new Vector3(net.x,_drill.Head.position.y,net.z)-forward*.5f;
            typeof(VolleyTutorial).GetMethod("Place",Private).Invoke(_tutorial,null);
            Assert.That(Vector3.Dot(_tutorial.Panel.position-net,forward),Is.LessThan(0f),"Not past the net.");
        }

        [TestCase(VolleyTutorial.DemoMove.Spike)][TestCase(VolleyTutorial.DemoMove.Block)]
        [TestCase(VolleyTutorial.DemoMove.Jump)][TestCase(VolleyTutorial.DemoMove.Receive)]
        public void DemoHandsMoveSmoothlyAndLoop(VolleyTutorial.DemoMove move)
        {
            Assert.That(VolleyTutorial.DemoPose(move,false,0f,out var first,out var firstRotation,out _),Is.True);
            Vector3 previous=first;Quaternion previousRotation=firstRotation;
            for(float t=.02f;t<6f;t+=.02f)
            {
                VolleyTutorial.DemoPose(move,false,t,out var wrist,out var rotation,out _);
                Assert.That(Vector3.Distance(wrist,previous),Is.LessThan(.06f),move+" jumps at "+t);
                Assert.That(Quaternion.Angle(rotation,previousRotation),Is.LessThan(15f),move+" turns sharply at "+t);
                previous=wrist;previousRotation=rotation;
            }
            VolleyTutorial.DemoPose(move,true,.5f,out var left,out _,out _);
            VolleyTutorial.DemoPose(move,false,.5f,out var right,out _,out _);
            Assert.That(left.x,Is.EqualTo(-right.x).Within(1e-5f),"The left hand mirrors the right.");
        }

        [Test]
        public void EveryLineHasItsVoiceTextAndClip()
        {
            var json=File.ReadAllText("Voice/voice-lines.json");
            foreach(var line in VolleyTutorialText.Lines)
            {
                Assert.That(json,Does.Contain("\"id\": \""+line.Id+"\""),line.Id);
                Assert.That(json,Does.Contain("\"text\": \""+line.Voice+"\""),line.Id+" voice text matches the table.");
                Assert.That(_tutorial.Clips.Any(c=>c!=null && c.name==line.Id),Is.True,line.Id+" has its narration clip.");
            }
        }
    }
}
