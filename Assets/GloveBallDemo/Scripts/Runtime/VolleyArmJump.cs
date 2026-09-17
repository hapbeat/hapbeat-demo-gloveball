using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Two-arm, button-free jump. Detection is head-relative so virtual lift cannot trigger itself.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class VolleyArmJump : MonoBehaviour
    {
        public VolleyFloorTracking Floor;
        public VolleyTrackedHand Left, Right;
        [Min(.1f)] public float JumpHeight=.75f;
        [Min(.4f)] public float Duration=1f;
        [Min(.1f)] public float Cooldown=.5f;
        [Tooltip("Both wrists must first be this far below the eyes.")]
        public float LoweredDistance=.35f;
        public float RequiredRise=.18f;
        public float MinimumUpwardSpeed=.8f;
        public float GestureWindow=.8f;
        public bool Airborne { get; private set; }
        public int Jumps { get; private set; }
        public float Lift { get; private set; }
        float _elapsed, _cooldown, _gestureAge, _baseLeft, _baseRight, _previousLeft, _previousRight;
        bool _sampled, _armed;

        void Update()
        {
            // Use tracking-space coordinates, never the elevated world-space eye/hand positions.
            var space=Floor.Origin.CameraFloorOffsetObject.transform;
            float head=space.InverseTransformPoint(Floor.Origin.Camera.transform.position).y;
            Tick(Time.deltaTime,Left.Ready&&Right.Ready&&!Left.IsEstimated&&!Right.IsEstimated,
                space.InverseTransformPoint(Left.transform.position).y-head,
                space.InverseTransformPoint(Right.transform.position).y-head,GameInputGate.IsBlocked,head);
            Floor.SetVirtualLift(Lift);
        }
        public static float HeightAt(float time,float duration,float height)
        {
            float t=Mathf.Clamp01(time/Mathf.Max(.1f,duration));
            return 4f*height*t*(1f-t);
        }
        public void Tick(float dt,bool tracked,float leftY,float rightY,bool paused,float headY=0f)
        {
            if(paused){ResetJump();return;}
            if(Airborne)
            {
                _elapsed+=Mathf.Max(0,dt);Lift=HeightAt(_elapsed,Duration,JumpHeight);
                if(_elapsed>=Duration){Airborne=false;Lift=0;_cooldown=Cooldown;}
                _sampled=false;_armed=false;return;
            }
            _cooldown=Mathf.Max(0,_cooldown-dt);
            if(!tracked || dt<=0 || dt>.12f){_sampled=false;_armed=false;return;}
            // Absolute tracking-space wrist speed rejects a head crouch with stationary hands.
            float lv=_sampled?(leftY+headY-_previousLeft)/dt:0;
            float rv=_sampled?(rightY+headY-_previousRight)/dt:0;
            _previousLeft=leftY+headY;_previousRight=rightY+headY;_sampled=true;
            if(_cooldown>0)return;
            if(!_armed && leftY<-LoweredDistance && rightY<-LoweredDistance)
            {_armed=true;_baseLeft=leftY;_baseRight=rightY;_gestureAge=0;}
            if(!_armed)return;
            _gestureAge+=dt;
            if(_gestureAge>GestureWindow){_armed=false;return;}
            if(leftY-_baseLeft>=RequiredRise && rightY-_baseRight>=RequiredRise
                && lv>=MinimumUpwardSpeed && rv>=MinimumUpwardSpeed)
            {Airborne=true;_elapsed=0;Lift=0;Jumps++;_armed=false;_sampled=false;}
        }
        public void ResetJump()
        {
            Airborne=false;Lift=0;_elapsed=0;_sampled=false;_armed=false;_cooldown=Cooldown;
            if(Floor!=null)Floor.SetVirtualLift(0);
        }
        void OnDisable()=>ResetJump();
    }
}
