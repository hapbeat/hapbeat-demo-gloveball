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

        [SetUp]
        public void Open()
        {
            EditorSceneManager.OpenScene(Scene);
            _drill=Object.FindFirstObjectByType<VolleyDrillController>();_rally=_drill.Aerial;
            _tutorial=_drill.GetComponent<VolleyTutorial>();
            typeof(BallPool).GetMethod("Awake",Private).Invoke(_drill.Pool,null);
            typeof(VolleyAerialSequence).GetMethod("CaptureHome",Private).Invoke(_rally,null);
            VolleyTutorial.ResetLaunchForTests();
        }
        [TearDown]
        public void Clean(){if(_tutorial!=null)_tutorial.Stop(false);VolleyTutorial.ResetLaunchForTests();}

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
        public void SpikeBlockTutorialStartsAtOnceShowsTheDigThenBlocksAndStartsTheMatch()
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
            Assert.That(_rally.ScriptedDefence,Is.EqualTo(Script.DigStrongSpikes),"The next strong spike is dug: the play goes on.");

            // Their dig: the play goes on into their attack, which is the first block (no mode change inside the rep).
            typeof(VolleyAerialSequence).GetMethod("ContinueRally",Private).Invoke(_rally,new object[]{_drill.ReceiveNet.transform.position+Vector3.up*.8f+_drill.CourtFrame.forward*5f,true});
            Assert.That(_tutorial.Step,Is.EqualTo("sb_block"));
            Assert.That(_rally.Mode,Is.EqualTo(Mode.SpikeOnly),"Kept until the rep ends.");
            Assert.That(_rally.ScriptedDefence,Is.EqualTo(Script.NeverDig));
            Award(Phase.OpponentSpike,true,"BLOCK POINT!");
            Assert.That(_tutorial.Count,Is.EqualTo(1));
            Wait(.1f);
            Assert.That(_rally.Mode,Is.EqualTo(Mode.BlockOnly),"BLOCK from the next rep.");
            Award(Phase.OpponentSpike,false,"NO BLOCK");
            Assert.That(_tutorial.Count,Is.EqualTo(1));
            Assert.That(_tutorial.PanelHint,Is.EqualTo(VolleyTutorialText.Get("sb_block_hint").Body));
            Award(Phase.OpponentSpike,true,"BLOCK POINT!");
            Wait(.1f);
            Assert.That(_tutorial.Running,Is.EqualTo(VolleyTutorial.Course.None),"The match starts as soon as the goals are met.");
            Assert.That(_rally.Mode,Is.EqualTo(Mode.Alternate));
            Assert.That(_rally.ScriptedDefence,Is.EqualTo(Script.Natural));
            Assert.That(_rally.MatchPoints,Is.EqualTo(points));Assert.That(_drill.HoldRally,Is.False);
            Assert.That(_rally.PlayerScore+_rally.OpponentScore,Is.Zero,"The match from 0 - 0.");
        }

        [Test]
        public void WithoutTheirDigTheSecondStrongSpikeStillLeadsToTheBlocks()
        {
            _rally.Jump.AutomaticJump=true;
            _tutorial.Begin(VolleyTutorial.Course.SpikeBlock);
            Spike(.9f,true,"POINT!");Spike(.9f,true,"POINT!");
            Assert.That(_tutorial.Step,Is.EqualTo("sb_block_plain"));
            Wait(.1f);
            Assert.That(_rally.Mode,Is.EqualTo(Mode.BlockOnly));
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
            typeof(VolleyArmJump).GetProperty("Jumps").SetValue(_rally.Jump,_rally.Jump.Jumps+1);
            Wait(.1f);
            Assert.That(_tutorial.Step,Is.EqualTo("sb_spike"));Assert.That(_drill.HoldRally,Is.False);
            bool called=false;
            _rally.ActMoment+=(spike,jump)=>called|=spike && jump;
            // The spike turn's jump moment is raised whether or not AUTO JUMP is on.
            typeof(VolleyAerialSequence).GetField("_jumpMomentSent",Private).SetValue(_rally,false);
            typeof(VolleyAerialSequence).GetProperty("SpikeJumpTime").SetValue(_rally,0f);
            typeof(VolleyAerialSequence).GetMethod("TickPlayerSpike",Private).Invoke(_rally,new object[]{.02f});
            Assert.That(called,Is.True);
        }

        [Test]
        public void ReceiveTutorialWantsOneExcellentAndOneGoodAndExplainsEachGradeOnce()
        {
            _tutorial.Begin(VolleyTutorial.Course.Receive);
            Assert.That(_tutorial.Step,Is.EqualTo("rc_pass"));Assert.That(_rally.Mode,Is.EqualTo(Mode.Pass));
            Assert.That(_drill.HoldRally,Is.False);
            var grade=typeof(VolleyAerialSequence).GetMethod("ShowGrade",Private);
            grade.Invoke(_rally,new object[]{"BAD"});_tutorial.Tick(.05f);
            Assert.That(_tutorial.PanelHint,Is.EqualTo(VolleyTutorialText.Get("rc_bad").Body),"BAD explained the first time.");
            grade.Invoke(_rally,new object[]{"GOOD"});_tutorial.Tick(.05f);
            Assert.That(_tutorial.GotGood,Is.True);Assert.That(_tutorial.GotExcellent,Is.False);
            Assert.That(_tutorial.PanelHint,Is.EqualTo(VolleyTutorialText.Get("rc_good").Body));
            Assert.That(_tutorial.PanelBody,Is.EqualTo(VolleyTutorialText.Get("rc_try_excellent").Body),"Then aim for the setter.");
            grade.Invoke(_rally,new object[]{"BAD"});_tutorial.Tick(.05f);
            grade.Invoke(_rally,new object[]{"GOOD"});_tutorial.Tick(.05f);
            Assert.That(_tutorial.PanelHint,Is.EqualTo(VolleyTutorialText.Get("rc_bad").Body),"A second GOOD explains nothing again.");
            Assert.That(_tutorial.Running,Is.EqualTo(VolleyTutorial.Course.Receive));
            grade.Invoke(_rally,new object[]{"EXCELLENT"});_tutorial.Tick(.05f);
            Assert.That(_tutorial.GotExcellent,Is.True);
            Assert.That(_tutorial.PanelBody,Is.EqualTo(VolleyTutorialText.Get("rc_end").Body));
            // The match starts once the rep that met the goals has ended.
            SetPhase(Phase.MatchPlay);_tutorial.Tick(.05f);
            Assert.That(_tutorial.Running,Is.EqualTo(VolleyTutorial.Course.Receive));
            SetPhase(Phase.Recovery);_tutorial.Tick(.05f);
            Assert.That(_tutorial.Running,Is.EqualTo(VolleyTutorial.Course.None));
            Assert.That(_rally.Mode,Is.EqualTo(Mode.Pass));
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
