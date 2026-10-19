using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// Their back-row defence in the SPIKE / BLOCK turns. A ball is played only where a defender's hands are: standing, its
    /// platform (both forearms) meets the ball at the height the ball comes down to; diving, it turns to the ball, lunges
    /// and tips forward about its feet with both arms stretched ahead, so its hands end on the ball. The chance (ball speed,
    /// a dive less) decides beforehand whether it gets the ball: a dive that will not get it ends visibly short of it, and a
    /// platform that does not control it shanks the ball off the arms (their touch: the player's point where it lands).
    /// Planned every physics step from the ball's own flight; the committed defender moves at physics rate with the ball.
    /// </summary>
    public sealed partial class VolleyAerialSequence
    {
        [Header("Their defence: reach (SPIKE / BLOCK turns)")]
        [Tooltip("A standing defender gets its platform to a ball this far (m) from where it stands with no step: a ball at its body is still played, even before it could react.")]
        [Min(0f)] public float ArmReach=.45f;
        [Tooltip("A diving defender lunges this far (m) as it falls; its stretched arms reach the rest.")]
        [Min(0f)] public float DiveSlide=.5f;
        [Tooltip("The fall of a dive (s): the hands reach the ball as it ends.")]
        [Min(.12f)] public float DiveFallSeconds=.3f;
        [Tooltip("A dive tips the body forward at most this far (degrees from upright).")]
        [Range(20f,80f)] public float DiveMaxTilt=65f;
        [Tooltip("A dive that will not get the ball (the chance failed) ends this far short of it (m): it visibly misses.")]
        [Min(.2f)] public float DiveMissShort=.55f;
        [Tooltip("The ball is played when it passes within this of the defender's hands (m).")]
        [Min(.1f)] public float HandContactRadius=.3f;
        [Tooltip("A diver stays down this long (s), then gets up over the next.")]
        public Vector2 DiveRecoverSeconds=new Vector2(.6f,.5f);

        enum DigKind { None, Platform, Dive }
        // PlanLockSeconds: the plan holds over the last moments before the contact. A hard spike moves farther in one physics
        // step than HandContactRadius, so a plan re-aimed at the next sample every step kept the hands a step ahead of the ball.
        const float PlanStep=.02f, MinDiveFall=.12f, PlanLockSeconds=.1f;
        const int PlanSamples=200;
        readonly Vector3[] _planPosition=new Vector3[PlanSamples], _planVelocity=new Vector3[PlanSamples];
        DigKind _digKind;
        Vector3 _digContact, _digFacing, _digRoot;
        float _digContactAt, _digPhase;
        bool _digResolved, _defenceTouched, _diveStarted, _scriptedDig;
        /// <summary>The tutorial's scripted dig runs this much faster (it must get there to show the play going on).</summary>
        float DefenderRunSpeed=>_scriptedDig ? DefenderSpeed*3f : DefenderSpeed;
        float _diveStartAt, _diveFall, _diveTilt, _diveSlide;
        Vector3 _diveFrom, _diveDir;
        float[] _downClock=new float[0], _downTilt=new float[0];
        Vector3[] _downDir=new Vector3[0];

        /// <summary>Their committed defender plays it standing (platform) or diving.</summary>
        public bool DigIsDive=>_digKind==DigKind.Dive;
        /// <summary>Their defender has played (or missed) the current ball.</summary>
        public bool DigResolved=>_digResolved;

        void ResetDefencePlan()
        {
            _digger=-1;_digRolledFor=-1;_digSuccess=false;_digKind=DigKind.None;
            _digResolved=false;_defenceTouched=false;_diveStarted=false;_scriptedDig=false;
        }
        void EnsureDefenceArrays()
        {
            int count=Defenders!=null ? Defenders.Length : 0;
            if(_downClock.Length==count)return;
            _downClock=new float[count];_downTilt=new float[count];_downDir=new Vector3[count];
            for(int i=0;i<count;i++)_downClock[i]=-1f;
        }
        /// <summary>A new turn: everyone stands on its spot again (a diver does not get up on the old spot).</summary>
        void ResetDefenders()
        {
            EnsureDefenceArrays();
            for(int i=0;i<_downClock.Length;i++)_downClock[i]=-1f;
        }

        /// <summary>
        /// Finds the defender who can get its hands to the ball first: standing (its platform where the ball comes down to
        /// platform height), else diving (where its stretched arms reach as it falls). Rolls once per defender and way of
        /// playing whether it will get the ball. Not re-planned once the ball is played or a dive has begun.
        /// </summary>
        void PlanDig(Vector3 position,Vector3 velocity,float damping)
        {
            if(_digResolved || _diveStarted)return;
            if(_digger>=0 && _digContactAt-_sinceTouch<=PlanLockSeconds)return;
            _digger=-1;_digKind=DigKind.None;
            if(Defenders==null || Defenders.Length==0)return;
            // The tutorial: no dig at all, or a strong spike always dug (to show the play going on).
            bool scripted=ScriptedDefence==DefenceScript.DigStrongSpikes && Phase==RallyPhase.PlayerSpike && LastSpikeStrong;
            if(ScriptedDefence==DefenceScript.NeverDig || (ScriptedDefence==DefenceScript.DigStrongSpikes && !scripted))return;
            _scriptedDig=scripted;
            float floor=FloorY;
            int count=0;
            var p=position;var v=velocity;
            for(int i=0;i<PlanSamples;i++)
            {
                _planPosition[i]=p;_planVelocity[i]=v;count++;
                if(v.y<0f && p.y-floor<=.12f)break;
                v+=Physics.gravity*PlanStep;v*=Mathf.Clamp01(1f-damping*PlanStep);p+=v*PlanStep;
            }
            // A ball that comes down outside their court is left alone.
            var local=_planPosition[count-1]-NetCentre;
            float depth=Vector3.Dot(local,Forward),side=Vector3.Dot(local,Right);
            if(depth<=0f || depth>CourtDepth+LineTolerance || Mathf.Abs(side)>CourtHalfWidth+LineTolerance)return;
            float reactLeft=scripted ? 0f : Mathf.Max(0f,DefenderReactionSeconds-_sinceTouch);
            float best=float.MaxValue,arriving=0f;
            for(int i=0;i<Defenders.Length;i++)
            {
                var defender=Defenders[i];
                if(defender==null || !defender.gameObject.activeSelf)continue;
                if(TryPlatform(i,count,reactLeft,out float cost,out int k) || TryDive(i,count,reactLeft,out cost,out k))
                {
                    if(cost>=best)continue;
                    best=cost;_digger=i;arriving=_planVelocity[k].magnitude;
                }
            }
            if(_digger<0)return;
            // Re-run for the chosen one to keep its plan (the loop above may have planned others after it).
            if(!TryPlatform(_digger,count,reactLeft,out _,out _))TryDive(_digger,count,reactLeft,out _,out _);
            // One roll per defender and way of playing: a hard-driven ball is often not controlled even when reached, a soft one rarely.
            int roll=_digger*2+(_digKind==DigKind.Dive ? 1 : 0);
            if(roll!=_digRolledFor)
            {
                _digRolledFor=roll;
                if(scripted){_digSuccess=true;return;}
                _digSuccess=Random.value<Mathf.Lerp(DigEasyChance,DigHardChance,Mathf.InverseLerp(DigSpeedRange.x,DigSpeedRange.y,arriving))
                    *(_digKind==DigKind.Dive ? DiveChance : 1f);
            }
        }

        /// <summary>The first point of the flight the defender's platform can meet in time; sets the plan when found.</summary>
        bool TryPlatform(int index,int count,float reactLeft,out float cost,out int sample)
        {
            cost=0f;sample=-1;
            var defender=Defenders[index];
            float scale=Mathf.Max(.1f,defender.transform.lossyScale.y);
            float top=VolleyOpponentPrototype.DigHands(0f).y*scale,bottom=VolleyOpponentPrototype.DigHands(1f).y*scale;
            var root=Flat(defender.transform.position);
            for(int k=1;k<count;k++)
            {
                var q=_planPosition[k];var vq=_planVelocity[k];
                float y=q.y-FloorY;
                if(vq.y>=0f || y>top || y<bottom)continue;
                float phase=PlatformPhase(y/scale);
                var hands=VolleyOpponentPrototype.DigHands(phase)*scale;
                var face=Flat(-vq);face=face.sqrMagnitude>1e-4f ? face.normalized : -Forward;
                var stand=Flat(q)-face*hands.z;
                float distance=(stand-root).magnitude,t=k*PlanStep;
                bool reflex=distance<=ArmReach;
                // The short cover leaves a hard ball it would have to move for.
                if(index==CoverIndex && vq.magnitude>CoverMaxSpeed && !reflex && !_scriptedDig)continue;
                if(!reflex && (distance-ArmReach)/DefenderRunSpeed>t-reactLeft)continue;
                cost=distance;sample=k;
                _digKind=DigKind.Platform;_digContact=q;_digContactAt=_sinceTouch+t;_digPhase=phase;_digFacing=face;
                _digRoot=new Vector3(stand.x,defender.transform.position.y,stand.z);
                return true;
            }
            return false;
        }

        /// <summary>The first point its stretched arms can reach as it falls (after running, if there is time); sets the plan.</summary>
        bool TryDive(int index,int count,float reactLeft,out float cost,out int sample)
        {
            cost=0f;sample=-1;
            var defender=Defenders[index];
            float scale=Mathf.Max(.1f,defender.transform.lossyScale.y);
            var hands=VolleyOpponentPrototype.DiveHands*scale;
            float length=hands.magnitude,lean=Mathf.Atan2(hands.z,hands.y);
            var root=Flat(defender.transform.position);
            for(int k=1;k<count;k++)
            {
                var q=_planPosition[k];var vq=_planVelocity[k];
                float y=q.y-FloorY,t=k*PlanStep;
                if(vq.y>=0f || y<=.15f || y>=length)continue;
                float tilt=(Mathf.Acos(y/length)-lean)*Mathf.Rad2Deg;
                if(tilt<15f || tilt>DiveMaxTilt)continue;
                float fall=Mathf.Min(DiveFallSeconds,t);
                if(fall<MinDiveFall)continue;
                float reach=Mathf.Sqrt(length*length-y*y);
                float slide=(Flat(q)-root).magnitude-reach;
                if(slide<0f)continue; // nearer than a dive: the platform's ball
                float run=index==CoverIndex && vq.magnitude>CoverMaxSpeed && !_scriptedDig ? 0f : DefenderRunSpeed*Mathf.Max(0f,t-fall-reactLeft);
                if(slide>DiveSlide+run)continue;
                cost=10f+slide;sample=k; // any standing play goes first
                _digKind=DigKind.Dive;_digContact=q;_digContactAt=_sinceTouch+t;_digPhase=1f;_digFacing=Flat(q)-root;
                _digRoot=defender.transform.position;
                return true;
            }
            return false;
        }

        /// <summary>The dig phase whose platform is at <paramref name="height"/> (unscaled; the platform lowers as the phase grows).</summary>
        static float PlatformPhase(float height)
        {
            float low=0f,high=1f;
            for(int i=0;i<14;i++)
            {
                float mid=(low+high)*.5f;
                if(VolleyOpponentPrototype.DigHands(mid).y>height)low=mid;else high=mid;
            }
            return (low+high)*.5f;
        }

        /// <summary>Moves the committed defender with the ball (physics rate): to its platform spot, or into its dive.</summary>
        void StepDefence(float step)
        {
            if(_digger<0 || _digResolved)return;
            EnsureDefenceArrays();
            var defender=Defenders[_digger];
            if(defender==null)return;
            float now=_sinceTouch,left=_digContactAt-now;
            if(_digKind==DigKind.Platform)
            {
                var root=defender.transform.position;
                float distance=Flat(_digRoot-root).magnitude;
                // Within arm's reach it just shifts (even before it could react); further, it runs once it has reacted.
                float speed=distance<=ArmReach+.01f ? Mathf.Max(DefenderSpeed,distance/Mathf.Max(step,left))
                    : now>=DefenderReactionSeconds || _scriptedDig ? DefenderRunSpeed : 0f;
                defender.transform.position=Vector3.MoveTowards(root,_digRoot,speed*step);
                defender.transform.rotation=Quaternion.RotateTowards(defender.transform.rotation,Quaternion.LookRotation(_digFacing,Vector3.up),540f*step);
                // Down into the platform over the last 0.4 s, to the height it meets the ball at.
                Act(defender,_digPhase*Mathf.Clamp01(1f-left/.4f));
                return;
            }
            if(_downClock[_digger]>=0f)return; // down on the floor: getting up is TickDefenders'
            if(!_diveStarted)
            {
                if(left>DiveFallSeconds)
                {
                    // Still time: once it has reacted, it runs toward the ball until a lunge will do.
                    if(now>=DefenderReactionSeconds || _scriptedDig)
                    {
                        var toward=Flat(_digContact-defender.transform.position);
                        float reach=Mathf.Sqrt(Mathf.Max(0f,Sq(VolleyOpponentPrototype.DiveHands.magnitude*defender.transform.lossyScale.y)-Sq(_digContact.y-FloorY)));
                        float need=toward.magnitude-reach-DiveSlide;
                        if(need>0f)defender.transform.position=Vector3.MoveTowards(defender.transform.position,defender.transform.position+toward.normalized*need,DefenderRunSpeed*step);
                    }
                    Act(defender,.3f);
                    return;
                }
                BeginDive(defender,Mathf.Max(MinDiveFall,left));
            }
            float u=Mathf.Clamp01((now-_diveStartAt)/_diveFall);
            PoseFall(defender,u);
            if(u>=1f){_downClock[_digger]=0f;_downTilt[_digger]=_diveTilt;_downDir[_digger]=_diveDir;}
        }
        static float Sq(float x)=>x*x;

        /// <summary>
        /// The dive: turned to the ball, it lunges <see cref="_diveSlide"/> and tips forward <see cref="_diveTilt"/> about its
        /// feet, its stretched hands ending on the ball - or, when the chance failed, <see cref="DiveMissShort"/> short of it.
        /// </summary>
        void BeginDive(VolleyOpponentPrototype defender,float fall)
        {
            var toward=Flat(_digContact-defender.transform.position);
            float x=toward.magnitude;
            _diveDir=x>1e-3f ? toward/x : -Forward;
            var hands=VolleyOpponentPrototype.DiveHands*Mathf.Max(.1f,defender.transform.lossyScale.y);
            float length=hands.magnitude,lean=Mathf.Atan2(hands.z,hands.y);
            float y=Mathf.Clamp(_digContact.y-FloorY,.05f,length*.99f);
            float aim=_digSuccess ? x : Mathf.Max(0f,x-DiveMissShort);
            float reach=Mathf.Sqrt(length*length-y*y);
            float tilt=Mathf.Acos(y/length)-lean,slide=aim-reach;
            if(slide<0f){slide=0f;tilt=Mathf.Asin(Mathf.Clamp01(aim/length))-lean;} // nearer: it tips less (hands higher)
            _diveSlide=Mathf.Min(slide,DiveSlide);
            _diveTilt=Mathf.Clamp(tilt*Mathf.Rad2Deg,0f,DiveMaxTilt);
            _diveFrom=defender.transform.position;_diveFall=fall;_diveStartAt=_sinceTouch;_diveStarted=true;
        }
        void PoseFall(VolleyOpponentPrototype defender,float u)
        {
            float e=Mathf.SmoothStep(0f,1f,u);
            defender.transform.position=_diveFrom+_diveDir*(_diveSlide*e);
            defender.transform.rotation=Quaternion.AngleAxis(_diveTilt*e,Vector3.Cross(Vector3.up,_diveDir))*Quaternion.LookRotation(_diveDir,Vector3.up);
            defender.PoseDive(Mathf.Clamp01(u*1.4f)); // the arms stretch out a little ahead of the fall
        }

        /// <summary>
        /// The ball meets the committed defender's hands: dug (the drill applies <see cref="DigVelocity"/>; in the flowing
        /// rally the play goes on), or shanked off a platform that did not control it. A failed dive never meets it.
        /// </summary>
        bool TryDig(Vector3 position,Vector3 velocity)
        {
            if(_digger<0 || _digResolved)return false;
            var defender=Defenders[_digger];
            bool dive=_digKind==DigKind.Dive;
            if(dive && (!_digSuccess || !_diveStarted || _sinceTouch<_diveStartAt+_diveFall*.7f))return false;
            var hands=defender.HandsCentre;
            var from=_havePhysicsSample ? _previousBallPosition : position;
            float gap=DistanceToSegment(hands,from,position);
            bool late=_sinceTouch>=_digContactAt;
            if(gap>HandContactRadius && !(late && gap<HandContactRadius*2f))return false;
            _digResolved=true;
            PlaySpikeAudio(position);
            if(!_digSuccess)
            {
                // Off the arms, on away from the net and to a side: their touch, so the player's point wherever it lands.
                _defenceTouched=true;
                var along=Flat(velocity);along=along.sqrMagnitude>.01f ? along.normalized : Forward;
                if(Vector3.Dot(along,Forward)<.3f)along=(along+Forward).normalized;
                var side=Vector3.Cross(Vector3.up,along)*(Random.value<.5f ? -1f : 1f);
                DigVelocity=along*Random.Range(2.5f,4.5f)+side*Random.Range(2f,4f)+Vector3.up*Random.Range(2.5f,4f);
                Cue="SHANKED!";
                return true;
            }
            // Pop the ball up toward the opponent's own front court, as a real dig would.
            DigVelocity=Vector3.up*5.5f-Forward*Mathf.Clamp(Vector3.Dot(position-NetCentre,Forward)*.35f,0f,2.5f);
            // The tutorial's scripted dig always goes on to their attack (the player then blocks).
            if(DigFlows){ContinueRally(position,!dive || _scriptedDig);return false;}
            Award(false,Phase==RallyPhase.OpponentSpike ? "BLOCK DUG" : "DUG",position);
            return true;
        }
        static float DistanceToSegment(Vector3 point,Vector3 a,Vector3 b)
        {
            var ab=b-a;float t=ab.sqrMagnitude>1e-8f ? Mathf.Clamp01(Vector3.Dot(point-a,ab)/ab.sqrMagnitude) : 0f;
            return Vector3.Distance(point,a+ab*t);
        }

        /// <summary>Frame rate: a diver stays down, then gets up; everyone not playing the ball walks back to its spot facing the net.</summary>
        void TickDefenders(float dt)
        {
            if(Defenders==null)return;
            EnsureDefenceArrays();
            bool live=Phase==RallyPhase.OpponentSpike || Phase==RallyPhase.PlayerSpike;
            var facing=Quaternion.LookRotation(-Forward,Vector3.up);
            for(int i=0;i<Defenders.Length;i++)
            {
                var defender=Defenders[i];
                if(defender==null || !defender.gameObject.activeSelf)continue;
                if(_downClock[i]>=0f){GetUp(i,dt);continue;}
                if(live && i==_digger)continue; // moved with the ball at physics rate, then holds its dig until the point is decided
                defender.transform.position=Vector3.MoveTowards(defender.transform.position,_defenderHome[i],DefenderSpeed*dt);
                defender.transform.rotation=Quaternion.RotateTowards(defender.transform.rotation,facing,360f*dt);
                if(defender.PreviewPhase>0f)Act(defender,Mathf.MoveTowards(defender.PreviewPhase,0f,dt*2f));
            }
        }
        void GetUp(int index,float dt)
        {
            _downClock[index]+=dt;
            var defender=Defenders[index];
            float rise=Mathf.Clamp01((_downClock[index]-DiveRecoverSeconds.x)/Mathf.Max(.05f,DiveRecoverSeconds.y));
            float e=Mathf.SmoothStep(0f,1f,rise);
            defender.transform.rotation=Quaternion.AngleAxis(_downTilt[index]*(1f-e),Vector3.Cross(Vector3.up,_downDir[index]))*Quaternion.LookRotation(_downDir[index],Vector3.up);
            defender.PoseDive(1f-e);
            if(rise>=1f){_downClock[index]=-1f;Act(defender,0f);}
        }
    }
}
