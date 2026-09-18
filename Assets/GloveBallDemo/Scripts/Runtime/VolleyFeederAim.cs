using UnityEngine;
using GloveBallDemo.Core;

namespace GloveBallDemo.Runtime
{
    /// <summary>Unit-scale pivot: local +Z is the outlet direction. The stand is not a child.</summary>
    public sealed class VolleyFeederAim : MonoBehaviour
    {
        public Transform Pivot;
        public Transform Muzzle;
        [Min(1f)] public float DegreesPerSecond = 120f;
        public AudioClip ShotClip;
        [Range(0f,1f)] public float ShotVolume=.7f;
        [Min(0f)] public float RecoilDistance=.10f;
        Vector3 _restPosition; bool _recoiling; float _shotTime;
        AudioSource _shotSource;

        public void PlayShotFeedback()
        {
            if(!_recoiling)_restPosition=Pivot.localPosition;
            _recoiling=true;_shotTime=Time.time;
            if(ShotClip==null)return;
            if(_shotSource==null)
            {
                _shotSource=gameObject.AddComponent<AudioSource>();_shotSource.playOnAwake=false;
                _shotSource.spatialBlend=1f;_shotSource.minDistance=2f;_shotSource.maxDistance=25f;_shotSource.dopplerLevel=0f;
            }
            _shotSource.PlayOneShot(ShotClip,ShotVolume);
        }
        void LateUpdate()
        {
            if(!_recoiling)return;
            float phase=(Time.time-_shotTime)/.25f;
            if(phase>=1f){Pivot.localPosition=_restPosition;_recoiling=false;return;}
            var direction=Pivot.parent!=null?Pivot.parent.InverseTransformDirection(Pivot.forward):Pivot.forward;
            Pivot.localPosition=_restPosition-direction*(RecoilDistance*Mathf.Sin(Mathf.PI*phase));
        }
        void OnDisable(){if(_recoiling){Pivot.localPosition=_restPosition;_recoiling=false;}}

        public Quaternion SolveRotation(Vector3 destination, float seconds, Vector3 gravity, float physicsStep = 0f)
        {
            var rotation = Pivot.rotation;
            var offset = Pivot.InverseTransformPoint(Muzzle.position);
            // The muzzle moves with pitch. Solve that dependency rather than aiming from the stand.
            for (int i = 0; i < 12; i++)
            {
                var origin = Pivot.position + rotation * offset;
                var velocity = VolleyMath.ServeVelocity(origin, destination, seconds, gravity) - .5f * gravity * physicsStep;
                rotation = Quaternion.LookRotation(velocity, Vector3.up);
            }
            return rotation;
        }

        public void Track(Vector3 destination, float seconds, float deltaTime)
        {
            if(_recoiling)return;
            Pivot.rotation = Quaternion.RotateTowards(Pivot.rotation,
                SolveRotation(destination, seconds, Physics.gravity), DegreesPerSecond * deltaTime);
        }

        public Vector3 AimForShot(Vector3 destination, float seconds, float physicsStep = 0f)
        {
            Pivot.rotation = SolveRotation(destination, seconds, Physics.gravity, physicsStep);
            // PhysX advances velocity before position (semi-implicit Euler).
            return VolleyMath.ServeVelocity(Muzzle.position, destination, seconds, Physics.gravity) - .5f * Physics.gravity * physicsStep;
        }
    }
}
