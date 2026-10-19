using System.Collections.Generic;
using System.Reflection;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GloveBallDemo.Tests
{
    public class VolleyFeedbackTests
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static object Invoke(object target,string name,params object[] args)
            =>target.GetType().GetMethod(name,Private).Invoke(target,args);

        [Test]
        public void TheBlockSceneJumpsOnAQuickPushNotASlowRaise()
        {
            EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity");
            var jump=Object.FindFirstObjectByType<VolleyArmJump>();
            jump.AutomaticJump=false;jump.Suppressed=false;jump.ResetJump();
            jump.Tick(1f,true,-.30f,-.30f,false);
            for(int i=1;i<=120;i++)jump.Tick(.05f,true,-.30f+i*.0025f,-.30f+i*.0025f,false);
            Assert.That(jump.Jumps,Is.Zero,"A slow raise (5 cm a second) is not a jump.");
            jump.Tick(.1f,true,.15f,.15f,false);
            Assert.That(jump.Jumps,Is.EqualTo(1),"A quick push up of both hands is, from wherever they are.");
            for(int i=0;i<60;i++)jump.Tick(.05f,true,.15f,.15f,false);
            Assert.That(jump.Jumps,Is.EqualTo(1),"Held raised hands cannot auto-repeat jump.");
        }

        [Test]
        public void AQuickPushUpOfBothHandsJumpsFromAnyHeight()
        {
            var go=new GameObject();var jump=go.AddComponent<VolleyArmJump>();jump.UseQuickRise=true;
            try{
                jump.Tick(1f,true,.3f,.3f,false);
                jump.Tick(.05f,true,.3f,.3f,false);Assert.That(jump.Jumps,Is.Zero,"Hands held high (looking up): no jump.");
                jump.Tick(.1f,true,.45f,.3f,false);Assert.That(jump.Jumps,Is.Zero,"One hand only.");
                jump.Tick(.1f,true,.45f,.42f,false);Assert.That(jump.Jumps,Is.EqualTo(1),"Both up 12 cm or more within 0.3 s, above the eyes, with no lowering first.");
                for(int i=0;i<40;i++)jump.Tick(.05f,true,.45f,.42f,false);
                Assert.That(jump.Jumps,Is.EqualTo(1),"Held raised hands cannot repeat.");
                for(int i=0;i<=20;i++)jump.Tick(.05f,true,.45f+i*.006f,.42f+i*.006f,false);
                Assert.That(jump.Jumps,Is.EqualTo(1),"A slow lift (12 cm over a second) is not a jump.");
                jump.Tick(.05f,true,.57f,.54f,false);jump.Tick(.1f,true,.7f,.67f,false);
                Assert.That(jump.Jumps,Is.EqualTo(2),"A quick push again is, from where the hands are.");
                for(int i=0;i<20;i++)jump.Tick(.05f,false,0,0,false);
                Assert.That(jump.Jumps,Is.EqualTo(2),"Untracked hands never jump.");
            }finally{Object.DestroyImmediate(go);}
        }

        [Test]
        public void BlockHasLaunchAudioAndSpikeAudioIsRequestedOnlyAtContact()
        {
            EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity");
            var aerial=Object.FindFirstObjectByType<VolleyAerialSequence>();
            Assert.That(aerial.TossLauncher.GetComponent<VolleyFeederAim>().ShotClip,Is.Not.Null);
            Assert.That(aerial.SpikeClip,Is.Not.Null);
            int sounds=0;Vector3 position=default;
            aerial.SpikeAudioRequested+=(clip,p)=>{sounds++;position=p;};
            aerial.TickFeed(aerial.WindupSeconds*.5f,out _,out _,out _);Assert.That(sounds,Is.Zero);
            aerial.TickFeed(aerial.WindupSeconds*.5f,out var hit,out _,out _);
            Assert.That(sounds,Is.EqualTo(1));Assert.That(position,Is.EqualTo(hit));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(4)]
        public void JoinedImpactRequestsBothSidedHapticsButOnlyOneSound(int kind)
        {
            EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
            var drill=Object.FindFirstObjectByType<VolleyDrillController>();
            var relay=Object.FindFirstObjectByType<HapticEventRelay>();Invoke(relay,"Awake");
            typeof(HapticEventRelay).GetField("_audioSource",Private).SetValue(relay,null);
            var audio=new List<DemoHapticEvent>();var haptics=new List<DemoHapticEvent>();
            relay.AudioRequested+=(evt,p,g)=>audio.Add(evt);relay.EventReported+=(evt,p,g)=>haptics.Add(evt);
            Invoke(drill.Pool,"Awake");var ball=drill.Pool.Take();ball.SelectVariant(kind);
            try{
                foreach(var hand in new[]{drill.Left,drill.Right})
                {
                    audio.Clear();haptics.Clear();
                    Invoke(drill,"ReportHandImpact",ball,hand,true);
                    Assert.That(haptics,Is.EqualTo(new[]{ball.ImpactEvent(DemoHapticEvent.LeftArmCollide),ball.ImpactEvent(DemoHapticEvent.RightArmCollide)}));
                    Assert.That(audio,Has.Count.EqualTo(1));
                    foreach(var evt in haptics)Assert.That(typeof(HapticEventRelay).GetMethod("GetHapbeatTrigger").Invoke(relay,new object[]{evt}),Is.Not.Null);
                }
            }finally{drill.Pool.Return(ball,"test");Invoke(relay,"OnDestroy");}
        }

        [Test]
        public void TargetAudioOccursOnContactNotAfterFlashAndNeverSendsHaptics()
        {
            EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity");
            var drill=Object.FindFirstObjectByType<VolleyDrillController>();
            var relay=Object.FindFirstObjectByType<HapticEventRelay>();
            Invoke(relay,"Awake");
            typeof(HapticEventRelay).GetField("_audioSource",Private).SetValue(relay,null);
            var audio=new List<DemoHapticEvent>();var haptics=new List<DemoHapticEvent>();
            relay.AudioRequested+=(evt,p,g)=>audio.Add(evt);relay.EventReported+=(evt,p,g)=>haptics.Add(evt);
            var go=new GameObject("feedback test ball",typeof(SphereCollider),typeof(Rigidbody));
            var ball=go.AddComponent<Ball>();Invoke(ball,"Awake");
            try
            {
                Invoke(drill,"Start");
                var panel=System.Array.Find(drill.Panels,p=>p.gameObject.activeSelf);
                ball.LaunchIncoming(Vector3.zero,Vector3.forward);ball.Deflect(Vector3.forward);
                Assert.That((bool)Invoke(panel,"TryRegisterHit",ball),Is.True);
                Assert.That(audio,Is.EqualTo(new[]{DemoHapticEvent.TargetHit}),"Contact must request audio before any flash timer ticks.");
                Assert.That(haptics,Is.Empty);
                Invoke(panel,"TickFlash",1f);
                Assert.That(audio,Has.Count.EqualTo(1));Assert.That(haptics,Is.Empty);
            }
            finally{Object.DestroyImmediate(go);Invoke(relay,"OnDestroy");}
        }
    }
}
