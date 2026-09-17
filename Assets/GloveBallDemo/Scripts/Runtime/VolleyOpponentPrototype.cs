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
        public MotionVariant Variant;
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
        void ArmPose(float phase,bool right,out Vector3 elbow,out Vector3 hand)
        {
            float side=right?1f:-1f;
            var restE=new Vector3(side*.04f,-.28f,.02f);var restH=new Vector3(side*.06f,-.56f,.07f);
            var backE=new Vector3(side*.07f,-.22f,-.24f);var backH=new Vector3(side*.08f,-.43f,-.43f);
            var upE=new Vector3(side*.05f,.27f,.1f);var upH=new Vector3(side*.02f,.55f,.19f);
            if(phase<.13f){float t=phase/.13f;elbow=Mix(restE,backE,t);hand=Mix(restH,backH,t);return;}
            if(phase<.31f){float t=(phase-.13f)/.18f;elbow=Mix(backE,upE,t);hand=Mix(backH,upH,t);return;}
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
    }
}
