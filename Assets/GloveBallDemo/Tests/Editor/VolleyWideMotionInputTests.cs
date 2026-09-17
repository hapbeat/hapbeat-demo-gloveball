using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using GloveBallDemo.Runtime;

namespace GloveBallDemo.Tests
{
    public class VolleyWideMotionInputTests
    {
        [Test]
        public void EstimatedWristDefaultsToVisualOnlyAndCannotSweepOrAddVelocity()
        {
            EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
            var d=Object.FindFirstObjectByType<VolleyDrillController>();var h=d.Left;
            Assert.That(h.EnableWideMotion,Is.True);Assert.That(h.AllowEstimatedContacts,Is.False);
            var pose=new Pose(new Vector3(-.2f,.8f,.2f),Quaternion.identity);
            h.ApplyWidePose(pose,1);h.ApplyWidePose(pose,2);
            Assert.That(h.IsEstimated,Is.True);Assert.That(h.Ready,Is.False);Assert.That(h.Velocity,Is.EqualTo(Vector3.zero));
            h.AllowEstimatedContacts=true;h.ApplyWidePose(pose,3);Assert.That(h.Ready,Is.True);
            h.BeginPhysicsSample();Assert.That(h.PreviousPhysicsPosition,Is.EqualTo(h.transform.position));h.EndPhysicsSample();
            pose.position+=Vector3.right*.5f;h.ApplyWidePose(pose,3.1f);Assert.That(h.Ready,Is.False,"Discontinuous estimate must settle");
            h.BeginPhysicsSample();Assert.That(h.PreviousPhysicsPosition,Is.EqualTo(h.transform.position));
            h.ApplyWidePose(pose,4);Assert.That(h.Ready,Is.True);Assert.That(h.Velocity,Is.EqualTo(Vector3.zero));
            d.Right.AllowEstimatedContacts=true;d.Right.ApplyWidePose(pose,1);d.Right.ApplyWidePose(pose,4);
            d.JoinedHands.Sample(.02f);Assert.That(d.JoinedHands.Joined,Is.False,"Estimates cannot make a moving merged collider");
        }
        [Test]
        public void EstimatedPoseMovesThroughSameFloorAndJumpSpaceAndGhostRoot()
        {
            EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyJumpSpike-codex.unity");
            var d=Object.FindFirstObjectByType<VolleyDrillController>();var f=d.Aerial.Jump.Floor;
            f.SetVirtualLift(.75f);var p=new Pose(new Vector3(.2f,.8f,.2f),Quaternion.identity);
            d.Left.ApplyWidePose(p,1);
            Assert.That(Vector3.Distance(d.Left.transform.position,d.Left.TrackingSpace.TransformPoint(p.position)),Is.LessThan(.001f));
            var ghosts=Object.FindObjectsByType<VolleyGhostHand>(FindObjectsSortMode.None);
            Assert.That(ghosts.Length,Is.GreaterThanOrEqualTo(2));
            foreach(var ghost in ghosts)if(ghost.Hand==d.Left)
            {
                ghost.UpdateVisual();Assert.That(ghost.Mesh.enabled,Is.True);Assert.That(ghost.Skeleton.enabled,Is.False);
                Assert.That(ghost.Skeleton.rootTransform.position,Is.EqualTo(d.Left.transform.position));
            }
            f.SetVirtualLift(0);
        }
    }
}
