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
        [Min(1f)] public float CycleSeconds=3.5f;
        public float JumpHeight=.65f;
        public float BallFlightSeconds=.7f;
        [Range(0f,1f)] public float PreviewPhase;
        public bool Animate=true;
        void Update() { if(Animate) PreviewPhase=(Time.time%CycleSeconds)/CycleSeconds; Pose(PreviewPhase); }
        static Vector3 Mix(Vector3 a,Vector3 b,float t)=>Vector3.Lerp(a,b,Mathf.SmoothStep(0,1,t));
        void Limb(Transform limb,Vector3 a,Vector3 b,float width)
        {
            limb.localPosition=(a+b)*.5f; limb.localRotation=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);
            limb.localScale=new Vector3(width,(b-a).magnitude*.5f,width);
        }
        public void Pose(float phase)
        {
            var jump=phase<.25f ? -.18f*Mathf.Sin(phase/.25f*Mathf.PI) : phase<.75f ? JumpHeight*Mathf.Sin((phase-.25f)/.5f*Mathf.PI) : 0f;
            var hip=new Vector3(0,.95f+jump,0);
            var shoulder=new Vector3(0,1.48f+jump,0);
            Torso.localPosition=(hip+shoulder)*.5f; Torso.localScale=new Vector3(.42f,.53f,.25f);
            Head.localPosition=shoulder+Vector3.up*.24f;
            var lHip=hip+Vector3.left*.13f; var rHip=hip+Vector3.right*.13f;
            var feetY=Mathf.Max(0,jump);
            var lk=new Vector3(-.15f,.49f+jump,phase<.25f?.12f:0);
            var rk=new Vector3(.15f,.49f+jump,phase<.25f?.12f:0);
            Limb(LeftThigh,lHip,lk,.15f); Limb(LeftShin,lk,new Vector3(-.15f,feetY+.08f,0),.11f);
            Limb(RightThigh,rHip,rk,.15f); Limb(RightShin,rk,new Vector3(.15f,feetY+.08f,0),.11f);
            var ls=shoulder+Vector3.left*.25f;var rs=shoulder+Vector3.right*.25f;
            var le=ls+new Vector3(-.08f,.20f,.12f);var lh=ls+new Vector3(-.02f,.42f,.22f);
            var windElbow=rs+new Vector3(.12f,.18f,-.2f);
            var windHand=rs+new Vector3(.1f,.38f,-.35f);
            var hitElbow=rs+new Vector3(.03f,.25f,.08f);
            var hitHand=rs+new Vector3(0,.56f,.22f);
            Vector3 re,rh;
            if(phase<.52f){float t=Mathf.InverseLerp(.40f,.52f,phase);re=Mix(windElbow,hitElbow,t);rh=Mix(windHand,hitHand,t);}
            else {float t=Mathf.InverseLerp(.52f,.66f,phase);re=Mix(hitElbow,rs+new Vector3(.02f,-.15f,.25f),t);rh=Mix(hitHand,rs+new Vector3(-.2f,-.35f,.35f),t);}
            Limb(LeftUpperArm,ls,le,.105f);Limb(LeftForearm,le,lh,.085f);
            Limb(RightUpperArm,rs,re,.105f);Limb(RightForearm,re,rh,.085f);
            var release=transform.TransformPoint(new Vector3(.25f,1.48f+JumpHeight*Mathf.Sin((.52f-.25f)/.5f*Mathf.PI)+.56f,.22f));
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
            if(Status!=null)Status.text="OPPONENT MOTION PROTOTYPE\n"+(phase<.25f?"PREPARE":phase<.52f?"JUMP / WIND UP":phase<.66f?"CONTACT / SWING":"LAND")+"\nNo gameplay / no haptics";
        }
    }
}
