using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using GloveBallDemo.Runtime;
using GloveBallDemo.Core;

namespace GloveBallDemo.Tests
{
    public class VolleyAerialTests
    {
        [TestCase(1f,1)] [TestCase(2f,0)]
        public void LostTrackingPreservesOnlyRecentJumpPreparation(float gap,int expected)
        {
            var go=new GameObject();var jump=go.AddComponent<VolleyArmJump>();
            try{
                jump.Tick(.05f,true,-.6f,-.6f,false);
                for(int i=0;i<Mathf.RoundToInt(gap/.05f);i++)jump.Tick(.05f,false,-.6f,-.6f,false);
                Assert.That(jump.Jumps,Is.Zero,"No jump without an observed upward pose.");
                jump.Tick(.05f,true,-.3f,-.3f,false);
                Assert.That(jump.Jumps,Is.EqualTo(expected));
            }finally{Object.DestroyImmediate(go);}
        }
        [Test]
        public void TrackingGraceAndMenuPauseHaveIndependentLifetimes()
        {
            var go=new GameObject();var drill=go.AddComponent<VolleyDrillController>();float original=Time.timeScale;
            try{
                Time.timeScale=.7f;drill.TickTracking(.1f,true);drill.TickTracking(1.4f,false);
                Assert.That(drill.TrackingSuspended,Is.False);Assert.That(Time.timeScale,Is.EqualTo(.7f));
                drill.TickTracking(.2f,false);Assert.That(drill.TrackingSuspended,Is.True);Assert.That(Time.timeScale,Is.Zero);
                drill.SetMenuPaused(true);drill.TickTracking(.1f,true);Assert.That(Time.timeScale,Is.Zero);
                drill.SetMenuPaused(false);Assert.That(Time.timeScale,Is.EqualTo(.7f));
                drill.SetMenuPaused(true);drill.TickTracking(2f,false);drill.SetMenuPaused(false);
                Assert.That(Time.timeScale,Is.Zero,"Closing a menu cannot unpause lost tracking.");
                drill.TickTracking(.1f,true);Assert.That(Time.timeScale,Is.EqualTo(.7f));
            }finally{Object.DestroyImmediate(go);Time.timeScale=original;}
        }
        [Test]
        public void SideTossEndsAtTheSpikeHandAndTransfersTheSameBall()
        {
            EditorSceneManager.OpenScene(Folder+"VolleyBlock-codex.unity");
            var drill=Object.FindFirstObjectByType<VolleyDrillController>();var aerial=drill.Aerial;
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(BallPool).GetMethod("Awake",flags).Invoke(drill.Pool,null);
            var aim=aerial.TossLauncher.GetComponent<VolleyFeederAim>();aim.ShotClip=null; // No PC audio in tests.
            try{
                Assert.That(aerial.TossLauncher.gameObject.activeInHierarchy,Is.True);
                Assert.That(aerial.BeginToss(),Is.True);Assert.That(drill.Pool.ActiveCount,Is.EqualTo(1));
                var field=typeof(VolleyAerialSequence).GetField("_preparedBall",flags);
                var ball=(Ball)field.GetValue(aerial);var start=ball.transform.position;
                Assert.That(ball.gameObject.activeSelf,Is.True);Assert.That(ball.Body.detectCollisions,Is.False);
                Assert.That(aerial.TickFeed(aerial.WindupSeconds*.5f,out _,out _,out _),Is.False);
                Assert.That(Vector3.Distance(ball.transform.position,start),Is.GreaterThan(.5f));
                Assert.That(aerial.TickFeed(aerial.WindupSeconds*.5f,out var hit,out _,out _),Is.True);
                Assert.That(Vector3.Distance(ball.transform.position,hit),Is.LessThan(.001f));
                Assert.That(aerial.TakePreparedBall(),Is.SameAs(ball));Assert.That(ball.Body.detectCollisions,Is.True);
                drill.Pool.Return(ball,"test");
                Assert.That(aerial.BeginToss(),Is.True);aerial.CancelFeed();
                Assert.That(drill.Pool.ActiveCount,Is.Zero,"Cancelling returns the staged ball, even though its state is Idle.");
            }finally{aerial.CancelFeed();}
        }
        [Test]
        public void JumpPreparationSurvivesBriefTrackingLoss()
        {
            var go=new GameObject();var jump=go.AddComponent<VolleyArmJump>();
            try{
                jump.Tick(.05f,true,-.6f,-.6f,false);
                jump.Tick(.05f,false,-.6f,-.6f,false);
                jump.Tick(.05f,true,-.3f,-.3f,false);
                Assert.That(jump.Jumps,Is.EqualTo(1),"A short lost frame must not erase the lowered-arm preparation.");
            }finally{Object.DestroyImmediate(go);}
        }

        [Test]
        public void WarningDoesNotFlashOnSingleLostFrame()
        {
            var go=new GameObject();var right=new GameObject();var ui=new GameObject("warning",typeof(RectTransform));
            try{
                var warning=go.AddComponent<VolleyTrackingWarning>();
                warning.Left=go.AddComponent<VolleyTrackedHand>();warning.Right=right.AddComponent<VolleyTrackedHand>();
                var graphic=ui.AddComponent<UnityEngine.UI.Image>();warning.Graphics=new UnityEngine.UI.Graphic[]{graphic};
                typeof(VolleyTrackingWarning).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(warning,null);
                Assert.That(graphic.enabled,Is.False,"Do not warn immediately on tracking loss.");
            }finally{Object.DestroyImmediate(go);Object.DestroyImmediate(right);Object.DestroyImmediate(ui);}
        }
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
