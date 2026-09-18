using System.Reflection;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GloveBallDemo.Tests
{
    public class VolleyRallyTests
    {
        const string Scene="Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;

        [SetUp]
        public void ApplyRallySceneWiring()=>VolleyRallySetup.Apply();

        [Test]
        public void RallySceneWiresOneFriendlySetterAndOneFloorTarget()
        {
            EditorSceneManager.OpenScene(Scene);
            var drill=Object.FindFirstObjectByType<VolleyDrillController>();var aerial=drill.Aerial;
            Assert.That(aerial.RallyEnabled,Is.True);
            Assert.That(aerial.Opponent,Is.Not.Null);Assert.That(aerial.Ally,Is.Not.Null);
            Assert.That(aerial.Ally,Is.Not.SameAs(aerial.Opponent));
            Assert.That(aerial.Ally.Role,Is.EqualTo(VolleyOpponentPrototype.MotionRole.Set));
            Assert.That(aerial.AllyTossLauncher,Is.Not.Null);
            drill.Targets.BeginWave(0,1,1);
            Assert.That(drill.Targets.ActiveTargetCount,Is.EqualTo(1));
            foreach(var panel in drill.Panels)if(panel.gameObject.activeSelf)
            {
                Assert.That(panel.transform.position.y,Is.InRange(.08f,.14f));
                Assert.That(Vector3.Dot(panel.transform.forward,Vector3.up),Is.GreaterThan(.99f));
            }
        }

        [Test]
        public void RallyFlowsFromOpponentBlockToFriendlySetWithoutOverlappingBalls()
        {
            EditorSceneManager.OpenScene(Scene);
            var drill=Object.FindFirstObjectByType<VolleyDrillController>();var aerial=drill.Aerial;
            typeof(BallPool).GetMethod("Awake",Private).Invoke(drill.Pool,null);
            aerial.TossLauncher.GetComponent<VolleyFeederAim>().ShotClip=null;
            aerial.AllyTossLauncher.GetComponent<VolleyFeederAim>().ShotClip=null;
            try
            {
                aerial.CancelFeed();
                Assert.That(aerial.TickRally(.01f,out _,out _,out _),Is.False);
                Assert.That(aerial.Phase,Is.EqualTo(VolleyAerialSequence.RallyPhase.OpponentSet));
                Assert.That(aerial.TickRally(aerial.WindupSeconds,out _,out _,out _),Is.True);
                var opponentBall=aerial.TakePreparedBall();
                Assert.That(opponentBall,Is.Not.Null);drill.Pool.Return(opponentBall,"test opponent attack");
                Assert.That(aerial.RegisterOpponentBlock(),Is.True);
                Assert.That(aerial.TickRally(aerial.AllyRecoverySeconds,out _,out _,out _),Is.False);
                Assert.That(aerial.Phase,Is.EqualTo(VolleyAerialSequence.RallyPhase.AllySet));
                Assert.That(drill.Pool.ActiveCount,Is.EqualTo(1));
                Assert.That(aerial.TickRally(aerial.AllySetSeconds,out var start,out var destination,out var seconds),Is.True);
                Assert.That(seconds,Is.EqualTo(aerial.AllyFlightSeconds));
                Assert.That(destination.y,Is.GreaterThan(drill.Head.position.y));
                var setBall=aerial.TakePreparedBall();
                Assert.That(setBall,Is.Not.Null);drill.Pool.Return(setBall,"test friendly set");
                Assert.That(drill.Pool.ActiveCount,Is.Zero);
            }
            finally{aerial.CancelFeed();}
        }
    }
}
