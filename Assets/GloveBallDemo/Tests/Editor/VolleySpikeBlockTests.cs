using System.Reflection;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GloveBallDemo.Tests
{
    /// <summary>The player's spike and block: where the ball goes after a solid contact, and where their attack is aimed.</summary>
    public class VolleySpikeBlockTests
    {
        const string Scene="Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        VolleyDrillController _drill;
        VolleyAerialSequence _rally;
        Random.State _random;

        [SetUp]
        public void Open()
        {
            EditorSceneManager.OpenScene(Scene);
            _drill=Object.FindFirstObjectByType<VolleyDrillController>();_rally=_drill.Aerial;
            _rally.Jump.Floor.Origin.Camera.transform.localPosition=Vector3.up*1.6f;
            _random=Random.state;Random.InitState(11);
        }
        [TearDown]
        public void Clean()=>Random.state=_random;

        Vector3 Forward=>Vector3.ProjectOnPlane(_drill.CourtFrame.forward,Vector3.up).normalized;
        Vector3 Net=>_drill.ReceiveNet.transform.position;
        float NetTop=>_drill.ReceiveNet.bounds.max.y;
        float Floor=>_drill.CourtFrame.position.y;

        /// <summary>Height when crossing the net plane, and where it lands (no drag).</summary>
        void Fly(Vector3 start,Vector3 velocity,out float atNet,out Vector3 landing)
        {
            atNet=float.NaN;var p=start;var v=velocity;
            for(int i=0;i<1000;i++)
            {
                var next=p+v*.005f;v+=Physics.gravity*.005f;
                if(float.IsNaN(atNet) && Vector3.Dot(p-Net,Forward)<0f && Vector3.Dot(next-Net,Forward)>=0f)atNet=next.y;
                p=next;
                if(p.y<=Floor)break;
            }
            landing=p;
        }
        Vector3 SpikeContact=>Net-Forward*.5f+Vector3.up*3.3f;
        float Depth(Vector3 landing)=>Vector3.Dot(landing-Net,Forward);

        [TestCase(0f)]
        [TestCase(-50f)]
        [TestCase(20f)]
        public void ASpikeClearsTheNetAndComesDownSharplyInTheirCourt(float swingPitch)
        {
            var swing=Quaternion.AngleAxis(-swingPitch,Vector3.Cross(Vector3.up,Forward))*Forward*6f;
            var v=_rally.SpikeShot(SpikeContact,swing,14f);
            Fly(SpikeContact,v,out var atNet,out var landing);
            Assert.That(v.y,Is.LessThan(0f),"A spike goes down, never floats ("+swingPitch+"° swing).");
            Assert.That(atNet,Is.GreaterThan(NetTop+.05f),"Clears the tape.");
            Assert.That(Depth(landing),Is.InRange(.5f,_rally.CourtDepth),"Lands in their court.");
            Assert.That(v.magnitude,Is.EqualTo(14f).Within(3f));
        }

        [Test]
        public void ASteeperSwingLandsShorter()
        {
            var side=Vector3.Cross(Vector3.up,Forward);
            Fly(SpikeContact,_rally.SpikeShot(SpikeContact,Quaternion.AngleAxis(50f,side)*Forward*6f,14f),out _,out var steep);
            Fly(SpikeContact,_rally.SpikeShot(SpikeContact,Forward*6f,14f),out _,out var flat);
            Assert.That(Depth(steep),Is.LessThan(Depth(flat)-1f));
        }

        [Test]
        public void APalmBlockThatWouldDropOnOurSideGoesBackOverTheNet()
        {
            var contact=Net-Forward*_rally.BlockStandDistance+Forward*_drill.ContactForwardDistance+Vector3.up*2.9f;
            // A steep spike off still palms: the plain rebound goes down in front of the player.
            var physical=Forward*.5f+Vector3.down*4f;
            Fly(contact,physical,out _,out var dropped);
            Assert.That(Depth(dropped),Is.LessThan(0f),"Without help it stays on our side.");
            var v=_rally.BlockShot(contact,physical);
            Fly(contact,v,out var atNet,out var landing);
            Assert.That(atNet,Is.GreaterThan(NetTop),"Over the tape.");
            Assert.That(Depth(landing),Is.InRange(.3f,_rally.CourtDepth),"Down in their court.");
        }

        [Test]
        public void ASidewaysReboundIsTurnedBackNoMoreThan20Degrees()
        {
            var contact=Net-Forward*_rally.BlockStandDistance+Forward*_drill.ContactForwardDistance+Vector3.up*2.9f;
            var right=Vector3.Cross(Vector3.up,Forward);
            // Off an angled hand: the rebound goes almost as much sideways as forward.
            var v=_rally.BlockShot(contact,Forward*3f-right*3f+Vector3.down*1f);
            Fly(contact,v,out _,out var landing);
            var flat=Vector3.ProjectOnPlane(landing-contact,Vector3.up);
            Assert.That(Vector3.Angle(flat,Forward),Is.LessThanOrEqualTo(_rally.BlockMaximumYaw+1f),"Never a 45° deflection.");
            Assert.That(Depth(landing),Is.GreaterThan(0f));
        }

        [Test]
        public void TheSideOfTheHandIsAnEdgeThePalmFaceIsNot()
        {
            var hand=_drill.Right;var box=hand.ContactVolume;
            var centre=box.transform.TransformPoint(box.center);
            Assert.That(VolleyDrillController.HandEdge(hand,centre,box.transform.up),Is.False,"Palm face, middle of the hand.");
            Assert.That(VolleyDrillController.HandEdge(hand,centre,box.transform.right),Is.True,"A side face.");
            var size=box.size;int width=Mathf.Abs(size.x)<=Mathf.Abs(size.z) ? 0 : 2;
            var local=box.center;local[width]+=size[width]*.5f*.95f;
            Assert.That(VolleyDrillController.HandEdge(hand,box.transform.TransformPoint(local),box.transform.up),Is.True,"The outer rim of the palm.");
        }

        [Test]
        public void TheirDefenceCoversShortBallsAndHardBallsAreOftenShanked()
        {
            Assert.That(_rally.CoverDepth,Is.LessThan(_rally.DefenderDepth-2f),"One defender plays short behind the block.");
            Assert.That(_rally.DigHardChance,Is.LessThan(_rally.DigEasyChance-.4f));
        }

        [Test]
        public void AttackLinesAre3MetresFromTheNet()
        {
            var root=GameObject.Find("Volley attack lines (3 m)");
            Assert.That(root,Is.Not.Null);
            int lines=0;
            foreach(Transform strip in root.transform)
            {
                if(!strip.name.StartsWith("Attack line"))continue;
                lines++;
                float rearEdge=Mathf.Abs(Vector3.Dot(strip.position-Net,Forward))+strip.lossyScale.y*.5f;
                Assert.That(rearEdge,Is.EqualTo(3f).Within(.005f),"Rear edge 3 m from the centre line.");
                Assert.That(strip.lossyScale.y,Is.EqualTo(.05f).Within(.001f));
                Assert.That(strip.lossyScale.x,Is.EqualTo(9f).Within(.01f));
            }
            Assert.That(lines,Is.EqualTo(2));
            Assert.That(NetTop,Is.EqualTo(2.43f).Within(.005f),"Men's net height.");
        }

        [Test]
        public void TheirAttackIsCentredOnThePlayerWithANarrowSpread()
        {
            var field=typeof(VolleyAerialSequence).GetField("_attackYaw",Private);
            var opponent=_rally.Opponent;opponent.Role=VolleyOpponentPrototype.MotionRole.Spike;
            float sum=0f,max=0f;const int Count=400;
            for(int i=0;i<Count;i++)
            {
                field.SetValue(_rally,Random.Range(-_rally.AttackBodyYaw,_rally.AttackBodyYaw));
                float lateral=_rally.AttackLateral(opponent.ReleasePosition);
                sum+=lateral;max=Mathf.Max(max,Mathf.Abs(lateral));
            }
            Assert.That(Mathf.Abs(sum/Count),Is.LessThan(.08f),"Not biased to the player's left (the hitter's right hand).");
            Assert.That(max,Is.LessThanOrEqualTo(_rally.BlockLateralLimit+1e-4f));
        }
    }
}
