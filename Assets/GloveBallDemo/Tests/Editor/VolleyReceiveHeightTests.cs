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
        public void ReceivePassesHmdPlaneAtRequestedHeightInUnityPhysics()
        {
            EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
            var d=Object.FindFirstObjectByType<VolleyDrillController>();
            Assert.That(d.ContactForwardDistance,Is.Zero);
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var physics=scene.GetPhysicsScene();
                foreach(var launcher in d.FeedLaunchers)
                foreach(float height in new[]{.8f,.9f,1f})
                foreach(float extra in new[]{0f,.3f})
                {
                    var dest=d.GetContactCentre();dest.y=height;
                    var aim=launcher.GetComponent<VolleyFeederAim>();Vector3 velocity=default;
                    for(int i=0;i<8;i++)velocity=aim.AimForShot(dest,d.GetFlightSeconds(launcher.MuzzlePosition,dest)+extra,Time.fixedDeltaTime);
                    var go=new GameObject("Ball trajectory test");
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
                    var body=go.AddComponent<Rigidbody>();body.position=launcher.MuzzlePosition;body.linearDamping=0;body.linearVelocity=velocity;
                    bool crossed=false;
                    for(int i=0;i<200;i++)
                    {
                        var prev=body.position;physics.Simulate(Time.fixedDeltaTime);var next=body.position;
                        if(next.z>dest.z)continue;
                        float y=Mathf.Lerp(prev.y,next.y,(prev.z-dest.z)/(prev.z-next.z));
                        Assert.That(y,Is.EqualTo(height).Within(.003f),launcher.name);
                        crossed=true;break;
                    }
                    Assert.That(crossed,Is.True);Object.DestroyImmediate(go);
                }
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
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
                    for(int i=0;i<8;i++)Assert.That(d.GetServeDestination().y,Is.InRange(.8f,1f));
                }
                foreach(var launcher in d.FeedLaunchers)
                foreach(float height in new[]{.8f,.9f,1f})
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
