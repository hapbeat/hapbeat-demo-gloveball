using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GloveBallDemo.Tests
{
    public class VolleyBeginnerTests
    {
        [Test]
        public void DefaultBlockJumpsOncePerAttackNearIncomingContact()
        {
            EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity");
            var a=Object.FindFirstObjectByType<VolleyAerialSequence>();var jump=a.Jump;
            Assert.That(a.BeginnerBlock,Is.True);
            var random=Random.state;Random.InitState(197);
            try
            {
                for(int attack=0;attack<12;attack++)
                {
                    bool fired=false;float flight=0;
                    for(int frame=0;frame<130 && !fired;frame++)
                    {
                        jump.Tick(.01f,true,.2f,.2f,false); // No low-hand preparation is needed.
                        jump.Floor.SetVirtualLift(jump.Lift);
                        fired=a.TickFeed(.01f,out _,out _,out flight);
                        Assert.That(jump.Jumps,Is.LessThanOrEqualTo(attack+1));
                    }
                    Assert.That(fired,Is.True);Assert.That(jump.Jumps,Is.EqualTo(attack+1));
                    Assert.That(a.ScheduledJumpTime,Is.InRange(a.WindupSeconds-.35f,a.WindupSeconds-.29f));
                    jump.Tick(flight,true,.2f,.2f,false);
                    Assert.That(jump.Lift,Is.GreaterThan(jump.JumpHeight*.65f),"Player should be near jump peak when the spike arrives.");
                    for(int i=0;i<200;i++)jump.Tick(.01f,true,.2f,.2f,false);
                    Assert.That(jump.Jumps,Is.EqualTo(attack+1),"Held high hands cannot add automatic jumps.");
                    jump.Floor.SetVirtualLift(0);
                }
            }
            finally{Random.state=random;}
        }

        [Test]
        public void AutoJumpPausesInPlaceAndZeroTimeDoesNotStartAFeed()
        {
            EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity");
            var a=Object.FindFirstObjectByType<VolleyAerialSequence>();var jump=a.Jump;
            Assert.That(a.TickFeed(0,out _,out _,out _),Is.False);Assert.That(a.AttackStarted,Is.False);
            Assert.That(jump.TryStartAutomaticJump(),Is.True);jump.Tick(.3f,true,0,0,false);
            float height=jump.Lift;jump.Tick(1f,true,0,0,true);
            Assert.That(jump.Airborne,Is.True);Assert.That(jump.Lift,Is.EqualTo(height));
            Assert.That(jump.TryStartAutomaticJump(),Is.False);
        }

        [Test]
        public void MenuSwitchesBetweenDefaultAutomaticAndAdvancedHandJump()
        {
            EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity");
            var menu=Object.FindFirstObjectByType<VolleyHandMenu>();var a=menu.Drill.Aerial;var original=Time.timeScale;
            try
            {
                menu.SetOpen(true);menu.Activate(8);
                Assert.That(a.BeginnerBlock,Is.False);Assert.That(a.Jump.UseHeightThreshold,Is.True);
                Assert.That(Time.timeScale,Is.Zero);Assert.That(a.Jump.TryStartAutomaticJump(),Is.False);
                menu.SetOpen(false);
                for(int i=0;i<12;i++)a.Jump.Tick(.05f,true,-.3f,-.3f,false);
                a.Jump.Tick(.05f,true,0,0,false);Assert.That(a.Jump.Jumps,Is.EqualTo(1));
                menu.SetOpen(true);menu.Activate(8);
                Assert.That(a.BeginnerBlock,Is.True);Assert.That(a.Jump.Airborne,Is.False);
                Assert.That(a.AttackStarted,Is.False);
            }
            finally{menu.SetOpen(false);Time.timeScale=original;Object.DestroyImmediate(menu);}
        }
    }
}
