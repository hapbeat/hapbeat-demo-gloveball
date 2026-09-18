using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using GloveBallDemo.Runtime;
using GloveBallDemo.Core;

namespace GloveBallDemo.Tests
{
    public class VolleyAerialTests
    {
        const string Folder="Assets/GloveBallDemo/Scenes/";
        [Test]
        public void ArmSwingJumpsOnceAndCannotBeRetriggeredByRaisedHandsOrTrackingReacquire()
        {
            var go=new GameObject();var jump=go.AddComponent<VolleyArmJump>();
            try
            {
                jump.Tick(.05f,true,-.6f,-.6f,false);
                for(int i=1;i<=4;i++)jump.Tick(.05f,true,-.6f+i*.065f,-.6f+i*.065f,false);
                Assert.That(jump.Airborne,Is.True);Assert.That(jump.Jumps,Is.EqualTo(1));
                for(int i=0;i<60;i++)jump.Tick(.05f,true,.2f,.2f,false);
                Assert.That(jump.Lift,Is.Zero);Assert.That(jump.Jumps,Is.EqualTo(1));
                jump.Tick(.05f,false,-.6f,-.6f,false);jump.Tick(.05f,true,.3f,.3f,false);
                Assert.That(jump.Jumps,Is.EqualTo(1));
                jump.Tick(.05f,true,-.6f,-.6f,false);jump.Tick(.05f,true,-.2f,-.6f,false);
                Assert.That(jump.Jumps,Is.EqualTo(1),"One arm alone cannot jump");
                jump.Tick(.05f,true,-.2f,-.2f,true);Assert.That(jump.Airborne,Is.False);
            }
            finally{Object.DestroyImmediate(go);}
        }
        [Test]
        public void CrouchingWithStationaryWristsCannotTriggerJump()
        {
            var go=new GameObject();var jump=go.AddComponent<VolleyArmJump>();
            try
            {
                jump.Tick(.05f,true,-.6f,-.6f,false,1.6f);
                jump.Tick(.05f,true,-.3f,-.3f,false,1.3f);
                Assert.That(jump.Airborne,Is.False);
            }
            finally{Object.DestroyImmediate(go);}
        }
        [Test]
        public void LiftMovesHeadAndHandsNotCourtAndCalibrationIsRestored()
        {
            EditorSceneManager.OpenScene(Folder+"VolleyJumpSpike-codex.unity");
            var floor=Object.FindFirstObjectByType<VolleyFloorTracking>();
            var root=floor.Origin.transform.position;var camera=floor.Origin.Camera.transform;var offset=floor.Origin.CameraFloorOffsetObject.transform;
            var hand=new GameObject().transform;hand.SetParent(offset,false);var h=hand.position;var eye=camera.position;
            floor.SetVirtualLift(.75f);Assert.That(camera.position.y,Is.EqualTo(eye.y+.75f).Within(.001f));
            Assert.That(hand.position.y,Is.EqualTo(h.y+.75f).Within(.001f));Assert.That(floor.Origin.transform.position,Is.EqualTo(root));
            floor.SetVirtualLift(0);Assert.That(camera.position.y,Is.EqualTo(eye.y).Within(.001f));
            Assert.That(VolleyArmJump.HeightAt(.5f,1,.75f),Is.EqualTo(.75f).Within(.001f));
            Object.DestroyImmediate(hand.gameObject);
        }
        [TestCase(false)] [TestCase(true)]
        public void NewScenesAreWiredAndFeedBallisticsClearTheNet(bool block)
        {
            EditorSceneManager.OpenScene(Folder+(block?"VolleyBlock-codex.unity":"VolleyJumpSpike-codex.unity"));
            var drill=Object.FindFirstObjectByType<VolleyDrillController>();var aerial=drill.Aerial;
            Assert.That(aerial,Is.Not.Null);Assert.That(aerial.Jump.Floor,Is.Not.Null);
            Assert.That(drill.JoinedHands,Is.Null);Assert.That(Object.FindFirstObjectByType<VolleyHandMenu>(),Is.Not.Null);
            aerial.Jump.Floor.Origin.Camera.transform.localPosition=Vector3.up*1.6f;
            Assert.That(aerial.TickFeed(.1f,out _,out _,out _),Is.False,"Wind-up cannot release early");
            Assert.That(aerial.TickFeed(2f,out var start,out var destination,out float seconds),Is.True);
            var velocity=VolleyMath.ServeVelocity(start,destination,seconds,Physics.gravity);
            Assert.That(Vector3.Distance(start+velocity*seconds+.5f*Physics.gravity*seconds*seconds,destination),Is.LessThan(.001f));
            if(block)
            {
                Assert.That(aerial.Opponent.PreviewOnly,Is.False);
                Assert.That(Object.FindFirstObjectByType<VolleyHeadSurface>(),Is.Not.Null);
                foreach(bool face in new[]{false,true})
                {
                    destination=aerial.Destination(0,0,face);seconds=aerial.SolveBlockShot(start,ref destination);
                    velocity=VolleyMath.ServeVelocity(start,destination,seconds,Physics.gravity);
                    Assert.That(velocity.y,Is.LessThanOrEqualTo(.001f),"Spike must never leave upward");
                    float t=(drill.ReceiveNet.transform.position.z-start.z)/(destination.z-start.z)*seconds;
                    Assert.That((start+velocity*t+.5f*Physics.gravity*t*t).y,Is.GreaterThanOrEqualTo(drill.ReceiveNet.bounds.max.y+.119f));
                    float floorTime=(velocity.y+Mathf.Sqrt(velocity.y*velocity.y+2f*9.81f*start.y))/9.81f;
                    Assert.That((start+velocity*floorTime).z,Is.InRange(-9f,0f),"Unblocked spike lands in player court");
                }
            }
            else
            {
                Assert.That(aerial.TossLauncher.gameObject.activeSelf,Is.True);
                drill.Targets.BeginWave(0,1,1);Assert.That(drill.Targets.ActiveTargetCount,Is.EqualTo(1));
                foreach(var panel in drill.Panels)if(panel.gameObject.activeSelf)
                {Assert.That(panel.transform.position.y,Is.InRange(.45f,.75f));Assert.That(panel.transform.position.z,Is.InRange(2.5f,6f));Assert.That(Vector3.Dot(panel.transform.forward,Vector3.up),Is.GreaterThan(.99f));}
            }
        }
        [Test]
        public void ReceiveHasNoJumpOrAerialFeed()
        {
            EditorSceneManager.OpenScene(Folder+"VolleyReceive-codex.unity");
            Assert.That(Object.FindFirstObjectByType<VolleyArmJump>(),Is.Null);
            Assert.That(Object.FindFirstObjectByType<VolleyDrillController>().Aerial,Is.Null);
        }
    }
}
