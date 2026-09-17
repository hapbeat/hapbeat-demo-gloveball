using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR;
using UnityEditor.SceneManagement;
using GloveBallDemo.Runtime;

namespace GloveBallDemo.Tests
{
    public class VolleyReceiveHeightTests
    {
        [Test]
        public void FloorPoseHasNoAdditionalEyeHeight()
        {
            Assert.That(VolleyFloorTracking.OffsetForMode(TrackingOriginModeFlags.Floor,1.6f),Is.Zero);
            Assert.That(VolleyFloorTracking.OffsetForMode(TrackingOriginModeFlags.Device,1.6f),Is.EqualTo(1.6f));
        }
        [Test]
        public void LowReceiveClearanceAndHeightAreIndependentOfCrouching()
        {
            EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
            var d=UnityEngine.Object.FindFirstObjectByType<VolleyDrillController>();
            var random=Random.state;
            try
            {
                foreach(float headHeight in new[]{.9f,1.6f,3.2f})
                {
                    d.Head.position=new Vector3(1,headHeight,-5f);
                    for(int i=0;i<8;i++)Assert.That(d.GetServeDestination().y,Is.InRange(.45f,.85f));
                }
                foreach(var launcher in d.FeedLaunchers)
                foreach(float height in new[]{.45f,.65f,.85f})
                {
                    var dest=new Vector3(1,height,-4.35f);var aim=launcher.GetComponent<VolleyFeederAim>();var velocity=Vector3.zero;float seconds=0;
                    for(int i=0;i<8;i++){seconds=d.GetFlightSeconds(launcher.MuzzlePosition,dest);velocity=aim.AimForShot(dest,seconds);}
                    var start=launcher.MuzzlePosition; var f=(d.ReceiveNet.bounds.center.z-start.z)/(dest.z-start.z);
                    var crossing=start+velocity*(seconds*f)+Physics.gravity*(.5f*seconds*seconds*f*f);
                    Assert.That(crossing.y,Is.GreaterThanOrEqualTo(d.ReceiveNet.bounds.max.y+.19f));
                    Assert.That(Vector3.Distance(start+velocity*seconds+Physics.gravity*(.5f*seconds*seconds),dest),Is.LessThan(.002f));
                    Assert.That(seconds,Is.LessThan(1.5f));
                }
            }
            finally{Random.state=random;}
        }
    }
}
