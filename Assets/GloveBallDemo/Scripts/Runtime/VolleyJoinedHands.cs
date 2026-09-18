using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>One rigid receive surface while both tracked hands are close. Never auto-aims.</summary>
    public sealed class VolleyJoinedHands : MonoBehaviour
    {
        public VolleyTrackedHand Left;
        public VolleyTrackedHand Right;
        public BoxCollider Volume;
        [Min(.01f)] public float JoinDistance=.18f;
        [Min(.01f)] public float SeparateDistance=.24f;
        [Min(.001f)] public float SmoothingSeconds=.06f;
        public bool Joined { get; private set; }
        public Vector3 Velocity { get; private set; }
        public Vector3 PreviousPosition { get; private set; }
        public Quaternion PreviousRotation { get; private set; }
        bool _haveOrientation;

        public static Quaternion StableSurfaceRotation(Quaternion left,Quaternion right,Quaternion previous,bool havePrevious)
        {
            var forward=left*Vector3.forward+right*Vector3.forward;
            if(forward.sqrMagnitude<.01f)forward=havePrevious?previous*Vector3.forward:left*Vector3.forward;
            forward.Normalize();
            var l=left*Vector3.up;var r=right*Vector3.up;
            // A box's two face normals describe the same surface. Align their hemispheres before averaging.
            if(Vector3.Dot(l,r)<0f)r=-r;
            var up=Vector3.ProjectOnPlane(l+r,forward).normalized;
            if(up.sqrMagnitude<.01f)up=Vector3.ProjectOnPlane(havePrevious?previous*Vector3.up:left*Vector3.up,forward).normalized;
            if(havePrevious && Vector3.Dot(up,previous*Vector3.up)<0f)up=-up;
            return Quaternion.LookRotation(forward,up);
        }

        public static bool ShouldJoin(bool previouslyJoined,bool leftReady,bool rightReady,float distance,float join,float separate)
            => leftReady && rightReady && distance <= (previouslyJoined ? Mathf.Max(join,separate) : join);

        public void Sample(float dt)
        {
            var l=Left.transform.position;
            var r=Right.transform.position;
            bool wasJoined=Joined;
            Joined=ShouldJoin(Joined,Left.Ready&&!Left.IsEstimated,Right.Ready&&!Right.IsEstimated,Vector3.Distance(l,r),JoinDistance,SeparateDistance);
            Volume.enabled=Joined;
            Left.ContactVolume.enabled=!Joined && Left.Ready;
            Right.ContactVolume.enabled=!Joined && Right.Ready;
            if(!Joined) { Velocity=Vector3.zero; return; }
            var position=(l+r)*.5f;
            var rotation=StableSurfaceRotation(Left.transform.rotation,Right.transform.rotation,transform.rotation,_haveOrientation);
            if(_haveOrientation)rotation=Quaternion.RotateTowards(transform.rotation,rotation,360f*dt);
            _haveOrientation=true;
            PreviousPosition=transform.position; PreviousRotation=transform.rotation;
            float alpha=1f-Mathf.Exp(-dt/Mathf.Max(.001f,SmoothingSeconds));
            if(!wasJoined)
            {
                transform.SetPositionAndRotation(position,rotation);
                PreviousPosition=position; PreviousRotation=rotation;
                Velocity=(Left.Velocity+Right.Velocity)*.5f;
            }
            else
            {
                transform.SetPositionAndRotation(Vector3.Lerp(transform.position,position,alpha),Quaternion.Slerp(transform.rotation,rotation,alpha));
                Velocity=Vector3.Lerp(Velocity,(Left.Velocity+Right.Velocity)*.5f,alpha);
            }
        }
        private void OnDrawGizmosSelected()
        {
            if(Volume==null || (Application.isPlaying && !Joined)) return;
            Gizmos.color=Color.yellow;
            Gizmos.matrix=Volume.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Volume.center,Volume.size);
            Gizmos.matrix=Matrix4x4.identity;
        }
    }
}
