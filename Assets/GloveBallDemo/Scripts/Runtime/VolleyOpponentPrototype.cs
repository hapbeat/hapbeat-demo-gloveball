using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>PROTOTYPE: kinematic mannequin + synchronized preview ball; not a production opponent or physics drill.</summary>
    public sealed class VolleyOpponentPrototype : MonoBehaviour
    {
        public Transform Torso, Head, LeftUpperArm, LeftForearm, RightUpperArm, RightForearm;
        public Transform LeftThigh, LeftShin, RightThigh, RightShin, PreviewBall;
        public Transform Target;
        public TextMesh Status;
        public enum MotionVariant { A_Readable, B_FastArm, C_PowerTwist }
        public enum MotionRole { Spike, Set }
        public MotionVariant Variant;
        [Tooltip("Spike is the opposing attacker. Set is the friendly teammate that presents a high ball to the player.")]
        public MotionRole Role;
        public Transform LeftHand, RightHand, LeftShoe, RightShoe;
        [Range(.1f,2f)] public float PlaybackSpeed=1f;
        [Min(1f)] public float CycleSeconds=3.5f;
        public float JumpHeight=.65f;
        public float BallFlightSeconds=.7f;
        [Range(0f,1f)] public float PreviewPhase;
        public bool Animate=true;
        [Tooltip("Disable the preview ball when a live drill owns its pooled ball.")]
        public bool PreviewOnly=true;
        public Vector3 ReleasePosition
        {
            get
            {
                if (Role == MotionRole.Set)
                    return transform.TransformPoint(new Vector3(0f, 1.92f, .26f));
                var shoulder=Shoulder(.52f,Jump(.52f),out var rotation);
                return transform.TransformPoint(shoulder+rotation*new Vector3(.25f,.56f,.22f));
            }
        }
        void Update() { if(Animate) PreviewPhase=(Time.time*PlaybackSpeed%CycleSeconds)/CycleSeconds; Pose(PreviewPhase); }
        static Vector3 Mix(Vector3 a,Vector3 b,float t)=>Vector3.Lerp(a,b,Mathf.SmoothStep(0,1,t));
        void Limb(Transform limb,Vector3 a,Vector3 b,float width)
        {
            limb.localPosition=(a+b)*.5f; limb.localRotation=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);
            limb.localScale=new Vector3(width,(b-a).magnitude*.5f,width);
        }
        Vector3 Shoulder(float phase,float jump,out Quaternion rotation)
        {
            float wind=Mathf.Sin(Mathf.InverseLerp(.25f,.52f,phase)*Mathf.PI);
            float follow=Mathf.Sin(Mathf.InverseLerp(.52f,.82f,phase)*Mathf.PI);
            float twist=Variant==MotionVariant.C_PowerTwist?42f:Variant==MotionVariant.B_FastArm?15f:24f;
            rotation=Quaternion.Euler(-12f*wind+20f*follow,-twist*wind+twist*.65f*follow,0);
            return new Vector3(0,.95f+jump,0)+rotation*Vector3.up*.53f;
        }
        public void ArmPose(float phase,bool right,out Vector3 elbow,out Vector3 hand)
        {
            float side=right?1f:-1f;
            var restE=new Vector3(side*.04f,-.28f,.02f);var restH=new Vector3(side*.06f,-.56f,.07f);
            var backE=new Vector3(side*.07f,-.22f,-.24f);var backH=new Vector3(side*.08f,-.43f,-.43f);
            var upE=new Vector3(side*.05f,.27f,.1f);var upH=new Vector3(side*.02f,.55f,.19f);
            if(phase<.13f){float t=phase/.13f;elbow=Mix(restE,backE,t);hand=Mix(restH,backH,t);return;}
            // Drive forward below the chest, then lift in front of the body; never sweep up behind the back.
            var frontE=new Vector3(side*.05f,-.17f,.25f);var frontH=new Vector3(side*.04f,-.29f,.48f);
            if(phase<.21f){float t=(phase-.13f)/.08f;elbow=Mix(backE,frontE,t);hand=Mix(backH,frontH,t);return;}
            if(phase<.31f){float t=(phase-.21f)/.10f;elbow=Mix(frontE,upE,t);hand=Mix(frontH,upH,t);return;}
            if(!right)
            {
                // Non-hitting arm points up, then pulls down as the hitting arm accelerates.
                float pull=Mathf.InverseLerp(.40f,.59f,phase);
                elbow=Mix(upE,new Vector3(-.1f,-.18f,.1f),pull);
                hand=Mix(upH,new Vector3(.12f,-.32f,.17f),pull);
                if(phase>.72f){elbow=Mix(elbow,restE,(phase-.72f)/.18f);hand=Mix(hand,restH,(phase-.72f)/.18f);}return;
            }
            var windE=new Vector3(.17f,.20f,-.22f);var windH=new Vector3(.1f,.38f,-.33f);
            if(phase<.40f){float t=(phase-.31f)/.09f;elbow=Mix(upE,windE,t);hand=Mix(upH,windH,t);return;}
            float strikeStart=Variant==MotionVariant.B_FastArm?.47f:Variant==MotionVariant.C_PowerTwist?.435f:.42f;
            var hitE=new Vector3(.03f,.25f,.08f);var hitH=new Vector3(0,.56f,.22f);
            if(phase<.52f){float t=Mathf.InverseLerp(strikeStart,.52f,phase);elbow=Mix(windE,hitE,t);hand=Mix(windH,hitH,t);return;}
            var endE=new Vector3(-.06f,-.12f,.25f);var endH=new Vector3(-.34f,-.35f,.32f);
            float end=Variant==MotionVariant.B_FastArm?.59f:.65f;
            if(phase<end){float t=Mathf.InverseLerp(.52f,end,phase);elbow=Mix(hitE,endE,t);hand=Mix(hitH,endH,t);return;}
            elbow=Mix(endE,restE,Mathf.InverseLerp(.73f,.91f,phase));hand=Mix(endH,restH,Mathf.InverseLerp(.73f,.91f,phase));
        }
        float Jump(float phase)=>phase<.25f ? -.18f*Mathf.Sin(phase/.25f*Mathf.PI) : phase<.75f ? JumpHeight*Mathf.Sin((phase-.25f)/.5f*Mathf.PI) : 0f;
        [ContextMenu("Preview Current Phase")]
        public void PreviewCurrentPhase()=>Pose(PreviewPhase);
        public void Pose(float phase)
        {
            if (Role == MotionRole.Set)
            {
                PoseSet(phase);
                return;
            }
            var jump=Jump(phase);
            var hip=new Vector3(0,.95f+jump,0);
            var shoulder=Shoulder(phase,jump,out var bodyRotation);
            Torso.localPosition=(hip+shoulder)*.5f; Torso.localScale=new Vector3(.42f,.53f,.25f);
            Torso.localRotation=bodyRotation;
            Head.localPosition=shoulder+bodyRotation*Vector3.up*.24f;Head.localRotation=bodyRotation;
            var lHip=hip+Vector3.left*.13f; var rHip=hip+Vector3.right*.13f;
            var feetY=Mathf.Max(0,jump);
            var lk=new Vector3(-.15f,.49f+jump,phase<.25f?.12f:0);
            var rk=new Vector3(.15f,.49f+jump,phase<.25f?.12f:0);
            Limb(LeftThigh,lHip,lk,.15f); Limb(LeftShin,lk,new Vector3(-.15f,feetY+.08f,0),.11f);
            Limb(RightThigh,rHip,rk,.15f); Limb(RightShin,rk,new Vector3(.15f,feetY+.08f,0),.11f);
            var ls=shoulder+bodyRotation*Vector3.left*.25f;var rs=shoulder+bodyRotation*Vector3.right*.25f;
            ArmPose(phase,false,out var le,out var lh);ArmPose(phase,true,out var re,out var rh);
            le=ls+bodyRotation*le;lh=ls+bodyRotation*lh;re=rs+bodyRotation*re;rh=rs+bodyRotation*rh;
            Limb(LeftUpperArm,ls,le,.105f);Limb(LeftForearm,le,lh,.085f);
            Limb(RightUpperArm,rs,re,.105f);Limb(RightForearm,re,rh,.085f);
            if(LeftHand!=null){LeftHand.localPosition=lh;LeftHand.localRotation=Quaternion.FromToRotation(Vector3.up,(lh-le).normalized);}
            if(RightHand!=null){RightHand.localPosition=rh;RightHand.localRotation=Quaternion.FromToRotation(Vector3.up,(rh-re).normalized);}
            if(LeftShoe!=null)LeftShoe.localPosition=new Vector3(-.15f,feetY+.06f,.08f);
            if(RightShoe!=null)RightShoe.localPosition=new Vector3(.15f,feetY+.06f,.08f);
            if(!PreviewOnly)return;
            var hitShoulder=Shoulder(.52f,Jump(.52f),out var hitRotation);
            var release=transform.TransformPoint(hitShoulder+hitRotation*new Vector3(.25f,.56f,.22f));
            float elapsed=(phase-.52f)*CycleSeconds;
            if(elapsed<0f)
            {
                PreviewBall.gameObject.SetActive(phase>.18f);
                PreviewBall.position=release+Vector3.up*(.35f*Mathf.Sin(Mathf.InverseLerp(.18f,.52f,phase)*Mathf.PI));
            }
            else
            {
                PreviewBall.gameObject.SetActive(elapsed<BallFlightSeconds);
                var velocity=GloveBallDemo.Core.VolleyMath.ServeVelocity(release,Target.position,BallFlightSeconds,Physics.gravity);
                PreviewBall.position=release+velocity*elapsed+Physics.gravity*(.5f*elapsed*elapsed);
            }
            if(Status!=null)Status.text=Variant.ToString().Replace('_',' ')+"\n"+(phase<.13f?"BACKSWING":phase<.31f?"ARMS UP / JUMP":phase<.52f?"WIND UP":phase<.66f?"HIT / FOLLOW":"LAND / RESET");
        }

        /// <summary>Readable overhead set: a small crouch, both hands up in front, then a soft follow-through.</summary>
        void PoseSet(float phase)
        {
            float crouch = phase < .2f ? -.12f * Mathf.Sin(phase / .2f * Mathf.PI) : 0f;
            float raise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.12f, .5f, phase));
            float follow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.52f, .78f, phase));
            var hip = new Vector3(0f, .95f + crouch, 0f);
            var shoulder = new Vector3(0f, 1.48f + crouch, .03f);
            Torso.localPosition = (hip + shoulder) * .5f;
            Torso.localScale = new Vector3(.42f, .53f, .25f);
            Torso.localRotation = Quaternion.Euler(-5f * raise, 0f, 0f);
            Head.localPosition = shoulder + Vector3.up * .24f;
            Head.localRotation = Quaternion.identity;
            var lHip = hip + Vector3.left * .13f;
            var rHip = hip + Vector3.right * .13f;
            var lk = new Vector3(-.15f, .49f + crouch, phase < .2f ? .11f : 0f);
            var rk = new Vector3(.15f, .49f + crouch, phase < .2f ? .11f : 0f);
            Limb(LeftThigh, lHip, lk, .15f); Limb(LeftShin, lk, new Vector3(-.15f, .08f, 0f), .11f);
            Limb(RightThigh, rHip, rk, .15f); Limb(RightShin, rk, new Vector3(.15f, .08f, 0f), .11f);
            var ls = shoulder + Vector3.left * .25f;
            var rs = shoulder + Vector3.right * .25f;
            var restLE = new Vector3(-.14f, 1.12f, .1f);
            var restRE = new Vector3(.14f, 1.12f, .1f);
            var restLH = new Vector3(-.2f, .82f, .18f);
            var restRH = new Vector3(.2f, .82f, .18f);
            var setLE = new Vector3(-.1f, 1.64f, .23f);
            var setRE = new Vector3(.1f, 1.64f, .23f);
            var setLH = new Vector3(-.09f, 1.91f, .28f);
            var setRH = new Vector3(.09f, 1.91f, .28f);
            var le = Vector3.Lerp(restLE, setLE, raise);
            var re = Vector3.Lerp(restRE, setRE, raise);
            var lh = Vector3.Lerp(restLH, setLH, raise) + Vector3.forward * (.07f * follow);
            var rh = Vector3.Lerp(restRH, setRH, raise) + Vector3.forward * (.07f * follow);
            Limb(LeftUpperArm, ls, le, .105f); Limb(LeftForearm, le, lh, .085f);
            Limb(RightUpperArm, rs, re, .105f); Limb(RightForearm, re, rh, .085f);
            if (LeftHand != null) { LeftHand.localPosition = lh; LeftHand.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up); }
            if (RightHand != null) { RightHand.localPosition = rh; RightHand.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up); }
            if (LeftShoe != null) LeftShoe.localPosition = new Vector3(-.15f, .06f, .08f);
            if (RightShoe != null) RightShoe.localPosition = new Vector3(.15f, .06f, .08f);
            if (PreviewBall != null) PreviewBall.gameObject.SetActive(false);
            if (Status != null) Status.text = "FRIENDLY SET\n" + (phase < .2f ? "READY" : phase < .52f ? "HANDS UP" : "SET / FOLLOW");
        }
    }
}
