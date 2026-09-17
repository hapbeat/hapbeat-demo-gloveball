using NUnit.Framework;
using UnityEngine;
using UnityEditor.SceneManagement;
using GloveBallDemo.Runtime;

namespace GloveBallDemo.Tests
{
    public class VolleyHandMenuTests
    {
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
