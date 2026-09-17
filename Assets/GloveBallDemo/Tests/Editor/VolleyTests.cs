using GloveBallDemo.Core;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;

namespace GloveBallDemo.Tests
{
    public class VolleyTests
    {
        [Test]
        public void JoinedSurfaceHasHysteresisAndDropsOutWhenEitherHandIsLost()
        {
            Assert.That(VolleyJoinedHands.ShouldJoin(false,true,true,.17f,.18f,.24f),Is.True);
            Assert.That(VolleyJoinedHands.ShouldJoin(false,true,true,.21f,.18f,.24f),Is.False);
            Assert.That(VolleyJoinedHands.ShouldJoin(true,true,true,.21f,.18f,.24f),Is.True);
            Assert.That(VolleyJoinedHands.ShouldJoin(true,true,true,.25f,.18f,.24f),Is.False);
            Assert.That(VolleyJoinedHands.ShouldJoin(true,false,true,.1f,.18f,.24f),Is.False);
        }

        [Test]
        public void JoinedSurfaceAveragesWristPosesWithoutChangingDimensions()
        {
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var go=new GameObject("join test"); SceneManager.MoveGameObjectToScene(go,scene);
                var joined=go.AddComponent<VolleyJoinedHands>(); joined.Volume=go.AddComponent<BoxCollider>();
                joined.Volume.size=new Vector3(.32f,.1f,.24f);
                var ready=typeof(VolleyTrackedHand).GetField("<Ready>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                var hands=new VolleyTrackedHand[2];
                for(int i=0;i<2;i++)
                {
                    var h=new GameObject("hand"); SceneManager.MoveGameObjectToScene(h,scene);
                    hands[i]=h.AddComponent<VolleyTrackedHand>(); hands[i].ContactVolume=h.AddComponent<BoxCollider>();
                    h.transform.position=new Vector3(i==0 ? -.05f : .05f,1,0);
                    h.transform.rotation=Quaternion.Euler(30,0,i==0 ? -10 : 10);
                    ready.SetValue(hands[i],true);
                }
                joined.Left=hands[0]; joined.Right=hands[1]; joined.Sample(.02f);
                Assert.That(joined.Joined,Is.True);
                Assert.That(Vector3.Distance(joined.transform.position,new Vector3(0,1,0)),Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(joined.transform.rotation,Quaternion.Euler(30,0,0)),Is.LessThan(.01f));
                Assert.That(joined.Volume.size,Is.EqualTo(new Vector3(.32f,.1f,.24f)));
                ready.SetValue(hands[0],false); joined.Sample(.02f);
                Assert.That(joined.Joined,Is.False); Assert.That(joined.Volume.enabled,Is.False);
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }

        [Test]
        public void ServeDestinationUsesCurrentHeadPositionWithVerticalAndLateralSpread()
        {
            var scene=EditorSceneManager.NewPreviewScene(); var randomState=Random.state;
            try
            {
                var go=new GameObject("feed test"); SceneManager.MoveGameObjectToScene(go,scene);
                var drill=go.AddComponent<VolleyDrillController>(); drill.CourtFrame=go.transform;
                var head=new GameObject("head"); SceneManager.MoveGameObjectToScene(head,scene); drill.Head=head.transform;
                head.transform.position=new Vector3(0,1.6f,-5); Random.InitState(23); var a=drill.GetServeDestination();
                var move=new Vector3(1,.3f,.5f); head.transform.position+=move; Random.InitState(23); var b=drill.GetServeDestination();
                Assert.That(Vector3.Distance(b-a,move),Is.LessThan(.001f));
                Assert.That(Mathf.Abs((b-head.transform.position).x),Is.LessThanOrEqualTo(drill.LateralSpread));
                Assert.That(Mathf.Abs((b-head.transform.position).y-drill.ContactHeightFromHead),Is.LessThanOrEqualTo(drill.VerticalSpread));
            }
            finally {Random.state=randomState; EditorSceneManager.ClosePreviewScene(scene);}
        }
        [TestCase("LeftHand")]
        [TestCase("RightHand")]
        public void GhostModelHasCompleteJointMappingAndMaterials(string name)
        {
            const string art="Assets/GloveBallDemo/Art/UnityGhostHands/";
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var model=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(art+"Models/"+name+".fbx");
                Assert.That(model,Is.Not.Null);
                var instance=Object.Instantiate(model); SceneManager.MoveGameObjectToScene(instance,scene);
                var mesh=instance.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(mesh,Is.Not.Null);
                Assert.That(mesh.bones.Length,Is.GreaterThanOrEqualTo(26));
                instance.AddComponent<UnityEngine.XR.Hands.XRHandTrackingEvents>();
                var skeleton=instance.AddComponent<UnityEngine.XR.Hands.XRHandSkeletonDriver>();
                skeleton.jointTransformReferences=new System.Collections.Generic.List<UnityEngine.XR.Hands.JointToTransformReference>();
                skeleton.rootTransform=instance.GetComponentsInChildren<Transform>().First(x=>x.name.ToLowerInvariant().Contains("wrist"));
                var missing=new System.Collections.Generic.List<string>();
                skeleton.FindJointsFromRoot(missing);
                Assert.That(missing,Is.Empty);
                foreach(var materialName in new[]{"Unity_Hand_Medium","DepthOnly"})
                {
                    var material=UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(art+"Materials/"+materialName+".mat");
                    Assert.That(material,Is.Not.Null);
                    Assert.That(material.shader,Is.Not.Null);
                    Assert.That(material.shader.name,Does.Not.Contain("InternalErrorShader"));
                    Assert.That(UnityEditor.ShaderUtil.ShaderHasError(material.shader),Is.False);
                }
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }

        [Test]
        public void ThickBoxHasPalmAndThumbSideFaces()
        {
            Assert.That(VolleyMath.SweptBoxContact(Vector3.up, Vector3.down, new Vector3(.075f,.045f,.10f),.05f,out _,out var top),Is.True);
            Assert.That(top,Is.EqualTo(Vector3.up));
            Assert.That(VolleyMath.SweptBoxContact(Vector3.right, Vector3.left, new Vector3(.075f,.045f,.10f),.05f,out _,out var thumb),Is.True);
            Assert.That(thumb,Is.EqualTo(Vector3.right));
            Assert.That(VolleyMath.SweptBoxContact(new Vector3(1,1,0),new Vector3(-1,1,0),new Vector3(.075f,.045f,.10f),.05f,out _,out _),Is.False);
        }

        [Test]
        public void TemporaryTrackingLossDoesNotDeleteAnAirborneBall()
        {
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var go=new GameObject("tracking-loss-repro"); SceneManager.MoveGameObjectToScene(go,scene);
                var drill=go.AddComponent<VolleyDrillController>();
                drill.Left=go.AddComponent<VolleyTrackedHand>();
                var right=new GameObject("right"); SceneManager.MoveGameObjectToScene(right,scene);
                drill.Right=right.AddComponent<VolleyTrackedHand>();
                var b=new GameObject("airborne"); SceneManager.MoveGameObjectToScene(b,scene);
                var ball=b.AddComponent<Ball>(); ball.BindPool(null); ball.LaunchIncoming(Vector3.up*2,Vector3.forward);
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                typeof(VolleyDrillController).GetField("_ball",flags).SetValue(drill,ball);
                typeof(VolleyDrillController).GetMethod("Update",flags).Invoke(drill,null);
                Assert.That(ball.gameObject.activeSelf,Is.True,"Losing hand tracking must stop serves, not erase a live ball.");
                Assert.That(ball.State,Is.EqualTo(BallState.Incoming));
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }

        [Test]
        public void HighSpeedHandBallCrossingIsNotMissed()
        {
            Assert.That(VolleyMath.SweptContact(new Vector3(0,0,1), new Vector3(0,0,-1),
                Vector3.zero, Vector3.zero, .2f, out float t), Is.True);
            Assert.That(t, Is.EqualTo(.4f).Within(.001f));
            Assert.That(VolleyMath.SweptContact(new Vector3(2,0,1), new Vector3(2,0,-1),
                Vector3.zero, Vector3.zero, .2f, out _), Is.False);
        }
        [Test]
        public void HandSweepAlsoHitsStationaryBall()
        {
            Assert.That(VolleyMath.SweptContact(Vector3.zero, Vector3.zero,
                Vector3.left, Vector3.right, .2f, out _), Is.True);
        }
        [Test]
        public void ReceiveFaceAndSwingControlReturnWithoutTargetAssist()
        {
            var incoming = new Vector3(0,-3,-5);
            var normal = new Vector3(0,1,1).normalized;
            var received = VolleyMath.ReturnVelocity(incoming, Vector3.zero, normal, 1,1,20);
            Assert.That(received.y, Is.GreaterThan(0));
            Assert.That(received.z, Is.GreaterThan(0));
            var flat = VolleyMath.ReturnVelocity(incoming, Vector3.zero, Vector3.up,1,1,20);
            Assert.That(flat.z, Is.LessThan(0), "Badly angled receive may travel toward torso; never auto-correct away");
            var spike = VolleyMath.ReturnVelocity(Vector3.down*4, new Vector3(0,-3,5),Vector3.forward, .9f,1.5f,14);
            Assert.That(spike.z, Is.GreaterThan(0));
            Assert.That(spike.y, Is.LessThan(0));
            Assert.That(spike.magnitude, Is.LessThanOrEqualTo(14.001f));
        }
        [Test]
        public void FeedArrivesAtSpecifiedHeightAndTime()
        {
            var origin = new Vector3(0,2.2f,1);
            var contact = new Vector3(.2f,1.2f,-4);
            float t = .85f;
            var v = VolleyMath.ServeVelocity(origin,contact,t,Physics.gravity);
            Assert.That(Vector3.Distance(origin + v*t + .5f*Physics.gravity*t*t, contact), Is.LessThan(.0001f));
        }
        [Test]
        public void VolleyDeflectionNeverGrabsAndHeldBallsCannotBeHit()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("volley ball test"); SceneManager.MoveGameObjectToScene(go,scene);
                var ball = go.AddComponent<Ball>();
                ball.BindPool(null); // EditMode does not invoke Awake; use the normal pool initialization seam.
                ball.LaunchIncoming(Vector3.up,Vector3.back);
                Assert.That(ball.Deflect(Vector3.forward*4),Is.True);
                Assert.That(ball.State,Is.EqualTo(BallState.Thrown));
                Assert.That(ball.transform.parent,Is.Null);
                Assert.That(ball.Body.isKinematic,Is.False);
                var anchor = new GameObject("anchor"); SceneManager.MoveGameObjectToScene(anchor,scene);
                ball.TryGrab(anchor.transform);
                Assert.That(ball.Deflect(Vector3.forward),Is.False);
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }
        [TestCase("VolleyReceive-codex")]
        [TestCase("VolleySpike-codex")]
        public void DrillCopyHasNoActiveGrabButtonsAndHasBothTrackingHands(string name)
        {
            var scene = EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/"+name+".unity",OpenSceneMode.Additive);
            try
            {
                var components=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
                Assert.That(components.OfType<DemoGameController>().Count(),Is.Zero);
                Assert.That(components.OfType<GloveController>().Count(c=>c.isActiveAndEnabled),Is.Zero);
                Assert.That(components.OfType<QuestMenuController>().Count(c=>c.isActiveAndEnabled),Is.Zero);
                var drill=components.OfType<VolleyDrillController>().Single();
                Assert.That(drill.Left.Side,Is.EqualTo(GloveSide.Left));
                Assert.That(drill.Right.Side,Is.EqualTo(GloveSide.Right));
                Assert.That(drill.Left.TrackingSpace,Is.EqualTo(drill.Head.parent));
                foreach (var hand in new[] { drill.Left, drill.Right })
                {
                    Assert.That(hand.ContactVolume,Is.Not.Null);
                    Assert.That(hand.ContactVolume.isTrigger,Is.True);
                    Assert.That(hand.ContactVolume.size,Is.EqualTo(new Vector3(.12f,.07f,.20f)));
                    Assert.That(hand.ContactVolume.center,Is.EqualTo(new Vector3(0,0,.08f)));
                    var ghost=hand.Visual.GetComponent<VolleyGhostHand>();
                    Assert.That(ghost,Is.Not.Null);
                    Assert.That(ghost.Hand,Is.EqualTo(hand));
                    Assert.That(ghost.Skeleton.jointTransformReferences.Count,Is.EqualTo(26));
                    Assert.That(ghost.Skeleton.handTrackingEvents.handedness,Is.EqualTo(
                        hand.Side==GloveSide.Left ? UnityEngine.XR.Hands.Handedness.Left : UnityEngine.XR.Hands.Handedness.Right));
                    Assert.That(ghost.Mesh.sharedMaterials.Length,Is.EqualTo(2));
                    Assert.That(ghost.Mesh.sharedMaterials.All(x=>x!=null),Is.True);
                }
                var poolData=new UnityEditor.SerializedObject(drill.Pool);
                var kinds=poolData.FindProperty("_launchBallKinds");
                Assert.That(Enumerable.Range(0,kinds.arraySize).Select(i=>kinds.GetArrayElementAtIndex(i).intValue),Is.EqualTo(new[]{0,1,2,4}));
                if(drill.Drill==VolleyDrill.Receive) Assert.That(drill.ContactHeightFromHead,Is.EqualTo(-.15f).Within(.001f));
                if(drill.Drill==VolleyDrill.Receive)
                {
                    Assert.That(drill.FeedLaunchers.Length,Is.EqualTo(3));
                    Assert.That(drill.FeedLaunchers.All(l=>l.gameObject.activeInHierarchy && !l.enabled),Is.True);
                    foreach(var launcher in drill.FeedLaunchers)
                    {
                        Assert.That(launcher.transform.Find("Ball Feeder Skin"),Is.Not.Null);
                        Assert.That(launcher.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="Body"||r.name=="Barrel").All(r=>!r.enabled),Is.True);
                        var muzzle=launcher.transform.InverseTransformPoint(launcher.MuzzlePosition);
                        Assert.That(muzzle.z,Is.EqualTo(-.6f).Within(.001f));
                        var visualDirection=launcher.transform.Find("Ball Feeder Skin").TransformDirection(Vector3.down);
                        Assert.That(Vector3.Dot(visualDirection.normalized,(launcher.MuzzlePosition-launcher.transform.position).normalized),Is.GreaterThan(.99f));
                    }
                    var court=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<MeshRenderer>()).Single(r=>r.name=="court");
                    Assert.That(court.sharedMaterial.name,Is.EqualTo("ReceiveCourtBlue"));
                    Assert.That(court.sharedMaterial.GetTexture("_BaseMap"),Is.Null);
                    drill.Targets.BeginWave(0,1,1);
                    Assert.That(drill.Targets.ActiveTargetCount,Is.EqualTo(1));
                    var target=drill.Panels.Single(p=>p.gameObject.activeSelf);
                    Assert.That(target.transform.position.y,Is.InRange(2.95f,3.65f));
                    Assert.That(target.transform.position.x,Is.InRange(-3f,3f));
                    var position=target.transform.position;
                    Assert.That(drill.Targets.RegisterHit(target),Is.True);
                    Assert.That(drill.Targets.ActiveTargetCount,Is.EqualTo(1));
                    Assert.That(Vector3.Distance(drill.Panels.Single(p=>p.gameObject.activeSelf).transform.position,position),Is.GreaterThan(.01f));
                    Assert.That(drill.JoinedHands,Is.Not.Null);
                    var warning=components.OfType<VolleyTrackingWarning>().Single();
                    Assert.That(warning.transform.parent,Is.EqualTo(drill.Head));
                    var net=scene.GetRootGameObjects().Single(o=>o.name=="Volley Net");
                    var bounds=net.GetComponentsInChildren<Renderer>().First().bounds;
                    Assert.That(bounds.max.y,Is.EqualTo(2.55f).Within(.01f));
                    Assert.That(net.GetComponent<BoxCollider>().bounds.max.y,Is.EqualTo(2.43f).Within(.01f));
                }
                Assert.That(drill.Panels.Length,Is.GreaterThanOrEqualTo(3));
                Assert.That(components.OfType<VolleyBodySurface>().Single().Drill,Is.EqualTo(drill));
            }
            finally {EditorSceneManager.CloseScene(scene,true);}
        }
    }
}
