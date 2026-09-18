using NUnit.Framework;
using UnityEngine;
using UnityEditor.SceneManagement;
using GloveBallDemo.Runtime;

namespace GloveBallDemo.Tests
{
    public class VolleyHandMenuTests
    {
        [Test]
        public void PinchUsesMidpointWithoutSteeringWithIndexCurl()
        {
            var wrist=new Pose(new Vector3(0,0,.4f),Quaternion.identity);
            var index=new Pose(new Vector3(0,.1f,.6f),Quaternion.identity);
            var thumb=new Pose(new Vector3(0,0,.5f),Quaternion.identity);
            var open=VolleyHandMenu.JointRay(wrist,index,thumb,Vector3.zero);
            Assert.That(open.origin,Is.EqualTo((index.position+thumb.position)*.5f));
            index.position=thumb.position;
            var closed=VolleyHandMenu.JointRay(wrist,index,thumb,Vector3.zero);
            Assert.That(closed.direction,Is.EqualTo(open.direction));
            var smoother=new VolleyHandMenu.HandRaySmoother();
            smoother.Sample(open.origin,open.direction,.02f,2);
            var filtered=smoother.Sample(closed.origin,closed.direction,.02f,2);
            Assert.That(Vector3.Distance(filtered.origin,open.origin),Is.LessThan(Vector3.Distance(closed.origin,open.origin)));
            // Menu is paused: caller supplies unscaled time, and reacquisition must not sweep from stale pose.
            smoother.Reset();
            Assert.That(smoother.Sample(Vector3.one,Vector3.right,.02f,2).origin,Is.EqualTo(Vector3.one));
        }
        [Test]
        public void PalmTowardHeadPinchOpensWithoutMetaAimAndDoesNotRepeatWhileHeld()
        {
            EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
            var menu=Object.FindFirstObjectByType<VolleyHandMenu>();
            // Unity XR Hands PalmDirection is rotation * Vector3.down. +90 X points palm toward -Z/head.
            var wrist=new Pose(new Vector3(0,0,.4f),Quaternion.Euler(90,0,0));
            var index=new Pose(new Vector3(0,.1f,.4f),Quaternion.identity);
            var thumb=new Pose(index.position+Vector3.right*.01f,Quaternion.identity);
            bool held=VolleyHandMenu.IsPalmPinch(wrist,index,thumb,Vector3.zero);
            try
            {
                menu.ProcessMenuGesture(1,false,held);menu.ProcessMenuGesture(1.6f,false,held);
                Assert.That(menu.IsOpen,Is.True,"Left palm-facing pinch must open menu without MetaAimHand");
                menu.ProcessMenuGesture(3,false,held);Assert.That(menu.IsOpen,Is.True);
            }
            finally{menu.SetOpen(false);Object.DestroyImmediate(menu);}
        }
        [Test]
        public void BackOfHandFacingHeadIsNotAMenuGesture()
        {
            var wrist=new Pose(new Vector3(0,0,.4f),Quaternion.Euler(-90,0,0));
            var index=new Pose(Vector3.zero,Quaternion.identity);var thumb=new Pose(Vector3.right*.01f,Quaternion.identity);
            Assert.That(VolleyHandMenu.IsPalmPinch(wrist,index,thumb,Vector3.zero),Is.False);
        }
        [Test]
        public void CalibrationOffsetsHeadAndHandsTogetherAndPreservesCrouching()
        {
            EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
            var floor=Object.FindFirstObjectByType<VolleyFloorTracking>();var head=floor.Origin.Camera.transform;
            var offset=floor.Origin.CameraFloorOffsetObject.transform;offset.localPosition=Vector3.zero;
            head.localPosition=new Vector3(0,2.2f,0);floor.StandingEyeHeight=1.6f;floor.HeightCorrection=0f;
            floor.CalibrateStandingHeight();Assert.That(floor.EyeHeight,Is.EqualTo(1.6f).Within(.001f));
            Assert.That(floor.HeightCorrection,Is.EqualTo(-.6f).Within(.001f));
            head.localPosition-=Vector3.up*.4f;Assert.That(floor.EyeHeight,Is.EqualTo(1.2f).Within(.001f));
            floor.ClearCalibration();Assert.That(floor.HeightCorrection,Is.Zero);Assert.That(offset.localPosition.y,Is.Zero.Within(.001f));
        }
        [Test]
        public void MenuPausesChangesHandsModeAndRestoresPreviousTimeScale()
        {
            EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
            var menu=Object.FindFirstObjectByType<VolleyHandMenu>();var original=Time.timeScale;
            try
            {
                Time.timeScale=.4f;menu.SetOpen(true);Assert.That(Time.timeScale,Is.Zero);Assert.That(GameInputGate.IsBlocked,Is.True);
                menu.Drill.Left.InputMode=VolleyInputMode.Automatic;menu.Activate(1);
                Assert.That(menu.Drill.Left.InputMode,Is.EqualTo(VolleyInputMode.HandsOnly));Assert.That(menu.Drill.Right.InputMode,Is.EqualTo(VolleyInputMode.HandsOnly));
                var row=menu.transform.Find("Volley hand menu/Menu row 1");
                var ray=new Ray(menu.Drill.Head.position,(row.position-menu.Drill.Head.position).normalized);
                menu.ProcessPointer(1,true,ray,false);menu.ProcessPointer(1,true,ray,true);
                Assert.That(menu.Drill.Left.InputMode,Is.EqualTo(VolleyInputMode.Automatic),"Ray + new pinch selects the row");
                menu.ProcessPointer(1,true,ray,true);
                Assert.That(menu.Drill.Left.InputMode,Is.EqualTo(VolleyInputMode.Automatic),"Held pinch cannot repeat");
                menu.ProcessPointer(1,false,ray,false);menu.ProcessPointer(1,true,ray,true);
                Assert.That(menu.Drill.Left.InputMode,Is.EqualTo(VolleyInputMode.Automatic),"Tracking reacquire cannot click");
                menu.Activate(0);Assert.That(Time.timeScale,Is.EqualTo(.4f));Assert.That(GameInputGate.IsBlocked,Is.False);
            }
            finally{menu.SetOpen(false);Time.timeScale=original;Object.DestroyImmediate(menu);}
        }
    }
}
