using System.IO;
using System.Linq;
using System.Reflection;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Mode = GloveBallDemo.Runtime.VolleyAerialSequence.RallyMode;
using Phase = GloveBallDemo.Runtime.VolleyAerialSequence.RallyPhase;

namespace GloveBallDemo.Tests
{
    /// <summary>The SPIKE + BLOCK and RECEIVE tutorials: steps, what advances them, holding the rally, and the voice lines.</summary>
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
        }
        [TearDown]
        public void Clean(){if(_tutorial!=null)_tutorial.Stop(false);}

        void Wait(float seconds){for(float t=0f;t<seconds;t+=.05f)_tutorial.Tick(.05f);}
        void Award(Phase phase,bool player,string text)
        {
            typeof(VolleyAerialSequence).GetProperty("Phase").SetValue(_rally,phase);
            typeof(VolleyAerialSequence).GetMethod("Award",Private).Invoke(_rally,new object[]{player,text,Vector3.zero});
        }

        [Test]
        public void SpikeBlockTutorialTeachesBothThenStartsTheTurns()
        {
            Assert.That(_tutorial,Is.Not.Null,"The scene setup adds the tutorial.");
            _rally.Jump.AutomaticJump=true;
            int points=_rally.MatchPoints;
            _tutorial.Begin(VolleyTutorial.Course.SpikeBlock);
            Assert.That(_tutorial.Step,Is.EqualTo("sb_intro"));
            Assert.That(_drill.HoldRally,Is.True,"No ball while the intro is read.");
            Wait(30f);
            Assert.That(_tutorial.Step,Is.EqualTo("sb_spike_auto"),"AUTO JUMP on: no jump step.");
            Assert.That(_rally.Mode,Is.EqualTo(Mode.SpikeOnly));Assert.That(_drill.HoldRally,Is.False);
            Assert.That(_tutorial.Move,Is.EqualTo(VolleyTutorial.DemoMove.Spike));
            Award(Phase.PlayerSpike,false,"NET");
            Assert.That(_tutorial.Count,Is.Zero,"A spike into the net does not count.");
            Award(Phase.PlayerSpike,true,"POINT!");
            Award(Phase.PlayerSpike,false,"DUG");
            Assert.That(_tutorial.Count,Is.EqualTo(2),"A point or a spike they dug both count.");
            Wait(10f);
            Assert.That(_tutorial.Step,Is.EqualTo("sb_block"));Assert.That(_rally.Mode,Is.EqualTo(Mode.BlockOnly));
            Award(Phase.PlayerSpike,true,"POINT!");
            Assert.That(_tutorial.Count,Is.Zero,"A spike does not count in the block step.");
            Award(Phase.OpponentSpike,true,"BLOCK POINT!");
            Award(Phase.OpponentSpike,false,"BLOCK DUG");
            Assert.That(_tutorial.Count,Is.EqualTo(2),"A block point or a block they dug both count (mode "+_rally.Mode+", phase "+_rally.Phase+").");
            Wait(10f);
            Assert.That(_tutorial.Step,Is.EqualTo("sb_end"));Assert.That(_drill.HoldRally,Is.True);
            Wait(40f);
            Assert.That(_tutorial.Running,Is.EqualTo(VolleyTutorial.Course.None));
            Assert.That(_rally.Mode,Is.EqualTo(Mode.Alternate),"SPIKE + BLOCK starts after it.");
            Assert.That(_rally.MatchPoints,Is.EqualTo(points));Assert.That(_drill.HoldRally,Is.False);
        }

        [Test]
        public void WithAutoJumpOffTheTutorialTeachesTheJumpFirst()
        {
            _rally.Jump.AutomaticJump=false;
            _tutorial.Begin(VolleyTutorial.Course.SpikeBlock);
            Wait(30f);
            Assert.That(_tutorial.Step,Is.EqualTo("sb_jump"));Assert.That(_drill.HoldRally,Is.True);
            typeof(VolleyArmJump).GetProperty("Jumps").SetValue(_rally.Jump,_rally.Jump.Jumps+1);
            Wait(10f);
            Assert.That(_tutorial.Step,Is.EqualTo("sb_spike"));
        }

        [Test]
        public void ReceiveTutorialCountsGoodOrExcellentPasses()
        {
            _tutorial.Begin(VolleyTutorial.Course.Receive);
            Assert.That(_tutorial.Step,Is.EqualTo("rc_intro"));
            Wait(30f);
            Assert.That(_tutorial.Step,Is.EqualTo("rc_pass"));Assert.That(_rally.Mode,Is.EqualTo(Mode.Pass));
            var grade=typeof(VolleyAerialSequence).GetMethod("ShowGrade",Private);
            grade.Invoke(_rally,new object[]{"BAD"});_tutorial.Tick(.05f);
            Assert.That(_tutorial.Count,Is.Zero);
            grade.Invoke(_rally,new object[]{"GOOD"});_tutorial.Tick(.05f);
            grade.Invoke(_rally,new object[]{"EXCELLENT"});_tutorial.Tick(.05f);
            Assert.That(_tutorial.Count,Is.EqualTo(2));
            Wait(10f);
            Assert.That(_tutorial.Step,Is.EqualTo("rc_end"));
            Wait(60f);
            Assert.That(_tutorial.Running,Is.EqualTo(VolleyTutorial.Course.None));
            Assert.That(_rally.Mode,Is.EqualTo(Mode.Pass));
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
            }
            finally{menu.SetOpen(false);Time.timeScale=scale;}
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
