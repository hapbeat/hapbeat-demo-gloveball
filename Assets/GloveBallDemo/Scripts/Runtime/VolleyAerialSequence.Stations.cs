using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// One-skill modes on the 6 v 6 court (<see cref="RallyMode.Set"/> = TOSS, <see cref="RallyMode.Pass"/> = PASS). The
    /// player stays on one spot and makes one physical touch per rep; the rep ends as soon as our attack is decided, and
    /// the score counts the player's good touches (never a long NPC rally).
    /// TOSS: they serve, our back row passes up to the player at the setter spot; the player catches the ball on the
    /// fingers and pushes it to one of two hitters whose spots change every rep (rings mark where to set). The set's
    /// quality decides how the hitter attacks (hard spike, normal spike or a roll shot) and whether it counts.
    /// PASS: they serve to the player, who passes on the forearm platform to our setter at the net. A pass counts when
    /// our setter can set it: it must come down within the setter's reach and rise above the net first.
    /// </summary>
    public sealed partial class VolleyAerialSequence
    {
        [Header("TOSS (set)")]
        [Tooltip("Pull of the set's aim toward the hitter it is heading to (0 = raw push, 1 = always on target). Height is never helped.")]
        [Range(0f,1f)] public float SetAssist=.5f;
        [Tooltip("Upward ball speed for a push with no upward hand speed, and added per m/s of upward push.")]
        [Min(0f)] public float SetLiftBase=1f;
        [Min(0f)] public float SetLiftGain=2.6f;
        [Tooltip("Horizontal ball speed per m/s of horizontal push.")]
        [Min(0f)] public float SetAimGain=2.2f;
        [Tooltip("How far a hitter moves from their spot to reach a set (m). Quality falls off linearly to this distance.")]
        [Min(.1f)] public float HitterReach=1.3f;
        [Tooltip("Random hitter spots per rep (lateral to our right, from the net): left hitter range, right hitter range, depth range.")]
        public Vector2 LeftHitterLateral=new Vector2(-3.6f,-1.6f);
        public Vector2 RightHitterLateral=new Vector2(2.6f,3.8f);
        public Vector2 HitterDepth=new Vector2(.6f,1.3f);
        [Tooltip("Set quality needed for the rep to count, and for a hard spike.")]
        [Range(0f,1f)] public float GoodSetQuality=.4f;
        [Range(0f,1f)] public float PerfectSetQuality=.75f;
        [Min(.05f)] public float TargetRingRadius=.35f;
        [Header("PASS (receive to the setter)")]
        [Tooltip("Pull of the player's pass toward the ideal pass to the setter (0 = raw physics, 1 = always ideal).")]
        [Range(0f,1f)] public float PassAssist=.5f;
        [Tooltip("Our setter's spot (lateral to our right, depth from the net) and the ideal pass apex above the floor.")]
        public Vector2 PassTarget=new Vector2(1f,1f);
        [Min(2f)] public float PassApexHeight=4f;
        [Tooltip("A pass must rise this far above the net tape for our setter to play it.")]
        [Min(0f)] public float PassMinClearance=.4f;
        [Tooltip("Pass quality is 1 within this distance of the setter's spot and 0 at this distance plus the falloff (m).")]
        [Min(0f)] public float PassPerfectRadius=.5f;
        [Min(.1f)] public float PassFalloff=1.8f;
        [Tooltip("Height above the floor where the serve reaches the player's forearms.")]
        [Min(.5f)] public float ReceiveHeight=1.1f;
        [Tooltip("Kill chance of our attack after the worst and the best pass.")]
        [Range(0f,1f)] public float StationKillMin=.3f;
        [Range(0f,1f)] public float StationKillMax=.9f;

        float _stationQuality=-1f, _setArrival, _setClock;
        bool _setPlanned, _passTooLow;
        MatchActor _setHitter;
        Vector3 _setContact;
        readonly Vector3[] _hitterSpot=new Vector3[2];
        readonly LineRenderer[] _rings=new LineRenderer[2];
        Material _ringMaterial;

        bool Station=>Mode==RallyMode.Set || Mode==RallyMode.Pass;
        /// <summary>Modes driven by the 6 v 6 engine (cast, scripted segments, physical player touches).</summary>
        bool MatchEngine=>Mode==RallyMode.Match || Station;
        int StationSlot=>Mode==RallyMode.Set ? 0 : 4;
        /// <summary>Quality of the player's last set or pass (0..1), -1 before it is judged.</summary>
        public float StationQuality=>_stationQuality;
        /// <summary>The drill holds the ball on the hands instead of bouncing it (a set).</summary>
        public bool HoldsSet=>MatchEngine && Phase==RallyPhase.PlayerSet && AwaitingPlayerTouch;
        /// <summary>Where the two TOSS hitters strike a perfect set this rep (left, right).</summary>
        public Vector3 HitterSpot(int index)=>_hitterSpot[index];

        static string QualityWord(float q)=>q>=.75f ? "PERFECT" : q>=.4f ? "GOOD" : "LOOSE";
        Vector3 SetterSpot=>SetHead(TeamPoint(Team.Us,PassTarget.x,.8f));

        void StartStationRep()
        {
            _stationQuality=-1f;_setHitter=null;_setPlanned=false;_passTooLow=false;
            var me=Me;
            if(me==null)return;
            // A fixed spot (no formation jitter), so the player is not moved between reps.
            me.Home=me.Target=Mode==RallyMode.Set ? SetterSpot : TeamPoint(Team.Us,.5f,6.2f);
            _serving=Team.Them;
            var server=Server(Team.Them);
            if(server!=null)server.Home=server.Target=TeamPoint(Team.Them,SlotBase[server.Slot].x,CourtDepth+.7f);
            if(Mode==RallyMode.Set)RandomizeHitters();
            EmbodyRequest(me.Home,Mode==RallyMode.Set ? PlayerTouch.Set : PlayerTouch.Receive,true);
            ShowRings(Mode==RallyMode.Set);
            Cue=Mode==RallyMode.Set ? "TOSS: CATCH THE PASS ON YOUR FINGERS, PUSH IT TO A RING" : "PASS THE SERVE HIGH TO YOUR SETTER AT THE NET";
            Phase=RallyPhase.MatchPlay;_serveTimer=ServeDelaySeconds;_serveWait=0f;
        }

        /// <summary>TOSS: two hitters on new spots every rep; each starts a few steps back and approaches when the set flies.</summary>
        void RandomizeHitters()
        {
            for(int i=0;i<2;i++)
            {
                var hitter=ActorAt(Team.Us,i==0 ? 2 : 1);
                if(hitter==null)continue;
                var range=i==0 ? LeftHitterLateral : RightHitterLateral;
                float lateral=Random.Range(range.x,range.y),depth=Random.Range(HitterDepth.x,HitterDepth.y);
                var root=TeamPoint(Team.Us,lateral,depth);root.y=FloorY;
                _hitterSpot[i]=hitter.A.ReleasePositionAt(root,TeamFacing(Team.Us));
                hitter.Home=TeamPoint(Team.Us,lateral,depth+2.5f);
            }
        }
        int HitterIndex(MatchActor hitter)=>hitter!=null && hitter.Slot==1 ? 1 : 0;
        Vector3 HitterContact(MatchActor hitter)=>_hitterSpot[HitterIndex(hitter)];
        MatchActor NearestHitter(Vector3 point)
        {
            MatchActor best=null;float bestDistance=float.MaxValue;
            for(int slot=1;slot<=2;slot++)
            {
                var hitter=ActorAt(Team.Us,slot);
                if(hitter==null || hitter==_embodied)continue;
                float distance=Flat(point-HitterContact(hitter)).magnitude;
                if(distance<bestDistance){bestDistance=distance;best=hitter;}
            }
            return best;
        }

        void ShowRings(bool show)
        {
            for(int i=0;i<2;i++)
            {
                if(_rings[i]==null)
                {
                    if(!show)continue;
                    var go=new GameObject("TOSS target ring "+(i+1)){hideFlags=HideFlags.DontSave};
                    go.transform.SetParent(transform,false);
                    var ring=go.AddComponent<LineRenderer>();
                    if(_ringMaterial==null)
                    {
                        _ringMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit")){hideFlags=HideFlags.DontSave};
                        _ringMaterial.SetColor("_BaseColor",new Color(1f,.85f,.2f));
                    }
                    ring.sharedMaterial=_ringMaterial;ring.loop=true;ring.useWorldSpace=true;
                    ring.startWidth=ring.endWidth=.025f;ring.positionCount=32;
                    _rings[i]=ring;
                }
                _rings[i].gameObject.SetActive(show);
                if(!show)continue;
                // Horizontal: a good set drops through it.
                for(int k=0;k<32;k++)
                {
                    float a=k*Mathf.PI*2f/32f;
                    _rings[i].SetPosition(k,_hitterSpot[i]+new Vector3(Mathf.Cos(a),0f,Mathf.Sin(a))*TargetRingRadius);
                }
            }
        }

        /// <summary>TOSS: our receiver's pass flies (physically) to just above the player's head.</summary>
        void StationPassToPlayer(Vector3 from)
        {
            RequestLaunch(()=>{var head=GroundedEye;return (from,head+Forward*.2f+Vector3.up*.35f,PassSeconds+.2f);},RallyPhase.PlayerSet,PlayerTouch.Set);
            Cue="HERE IT COMES — CATCH IT ON YOUR FINGERS AND PUSH";
        }

        /// <summary>Ballistic velocity from <paramref name="from"/> to <paramref name="to"/> peaking at <paramref name="apex"/> (world y).</summary>
        static Vector3 ArcVelocity(Vector3 from,Vector3 to,float apex)
        {
            float g=Mathf.Abs(Physics.gravity.y);
            apex=Mathf.Max(apex,Mathf.Max(from.y,to.y)+.3f);
            float up=Mathf.Sqrt(2f*g*(apex-from.y));
            float seconds=up/g+Mathf.Sqrt(2f*(apex-to.y)/g);
            var flat=Flat(to-from)/seconds;
            return new Vector3(flat.x,up,flat.z);
        }

        /// <summary>
        /// TOSS: the push (the fastest hand velocity of the hold) decides height and direction; only the aim is pulled
        /// toward the hitter it is heading to, by <see cref="SetAssist"/>.
        /// </summary>
        public Vector3 StationSetVelocity(Vector3 ball,Vector3 handVelocity)
        {
            var raw=Flat(handVelocity)*SetAimGain+Vector3.up*(SetLiftBase+SetLiftGain*Mathf.Max(0f,handVelocity.y));
            float g=Mathf.Abs(Physics.gravity.y);
            float apex=ball.y+raw.y*raw.y/(2f*g);
            float contactY=Mathf.Min(_hitterSpot[0].y,_hitterSpot[1].y);
            // Too low to ever reach a hitter: no help, the height is the player's alone.
            if(apex<=contactY)return raw;
            float time=raw.y/g+Mathf.Sqrt(2f*(apex-contactY)/g);
            var hitter=NearestHitter(ball+Flat(raw)*time);
            if(hitter==null)return raw;
            var aim=Flat(HitterContact(hitter)-ball)/time;
            return Vector3.Lerp(Flat(raw),aim,SetAssist)+Vector3.up*raw.y;
        }
        /// <summary>PASS: the platform's physical rebound pulled toward a pass that peaks over our setter.</summary>
        public Vector3 StationPassVelocity(Vector3 ball,Vector3 physical)
        {
            var target=TeamPoint(Team.Us,PassTarget.x,PassTarget.y);target.y=FloorY+SetHeight;
            return Vector3.Lerp(physical,ArcVelocity(ball,target,FloorY+PassApexHeight),PassAssist);
        }

        /// <summary>TOSS, every physics step after the set leaves the hands: pick the hitter, run and jump them, hand the ball over.</summary>
        bool TrackStationSet(Vector3 position,Vector3 velocity,float radius,float step,float damping)
        {
            if(!_setPlanned)
            {
                _setPlanned=true;
                PlanStationSet(position,velocity,damping);
                ShowRings(false);
            }
            if(_setHitter!=null)
            {
                _setClock+=step;
                _setHitter.Phase=.52f*Mathf.Clamp01(_setClock/Mathf.Max(.05f,_setArrival));
                if(_setClock<_setArrival-.005f && !(velocity.y<0f && position.y<=_setContact.y))return false;
                var ball=Drill.ReleaseActiveBall();
                if(ball==null)return false;
                AdoptBall(ball);PlaceScriptedBall(position);
                AwaitedTouch=PlayerTouch.None;Phase=RallyPhase.MatchPlay;
                var hitter=_setHitter;_setHitter=null;
                hitter.Clock=0f;_touches[0]=3;_lastTouchTeam=Team.Us;
                PlaySpikeAudio(position);
                StationAttack(position);
                return false;
            }
            if(DetectLanding(position,velocity,radius,step,out var landing)){_lastBallPosition=landing;ResolveMatch(landing);}
            return false;
        }
        void PlanStationSet(Vector3 position,Vector3 velocity,float damping)
        {
            float contactY=Mathf.Min(_hitterSpot[0].y,_hitterSpot[1].y);
            var p=position;var v=velocity;float t=0f;const float dt=.02f;bool found=false;
            for(int i=0;i<250;i++)
            {
                if(v.y<0f && p.y<=contactY){found=true;break;}
                if(p.y<FloorY)break;
                v+=Physics.gravity*dt;v*=Mathf.Clamp01(1f-damping*dt);p+=v*dt;t+=dt;
            }
            if(!found || SideOf(p)!=Team.Us){_stationQuality=0f;Cue=found ? "OVER THE NET" : "TOO LOW FOR A HITTER";return;}
            var hitter=NearestHitter(p);
            float distance=hitter!=null ? Flat(p-HitterContact(hitter)).magnitude : float.MaxValue;
            if(distance>HitterReach){_stationQuality=0f;Cue="NO HITTER CAN REACH IT";return;}
            float q=1f-distance/HitterReach;
            if(-Vector3.Dot(p-NetCentre,Forward)<.35f){q=Mathf.Min(q,.2f);Cue="TOO TIGHT TO THE NET";}
            else Cue=QualityWord(q)+" SET";
            _stationQuality=q;
            _setHitter=hitter;_setContact=p;_setArrival=t;_setClock=0f;
            // The hitter's root goes where its hand meets the ball; it runs there and winds up to strike on arrival.
            hitter.Facing=TeamFacing(Team.Us);
            var root=p-hitter.A.ReleasePositionAt(Vector3.zero,hitter.Facing);root.y=FloorY;
            hitter.Target=root;hitter.Delay=0f;
            hitter.Speed=Mathf.Max(NpcRunSpeed,Flat(root-hitter.A.transform.position).magnitude/Mathf.Max(.25f,t*.8f));
            hitter.A.Role=VolleyOpponentPrototype.MotionRole.Spike;hitter.Anim=Anim.Spike;hitter.Clock=-1f;hitter.Phase=0f;
        }

        /// <summary>PASS: our setter has the player's pass; judge it by how close it came down to the setter's spot.</summary>
        void JudgeStationPass(Vector3 position)
        {
            var target=TeamPoint(Team.Us,PassTarget.x,PassTarget.y);
            float q=1f-Mathf.Clamp01((Flat(position-target).magnitude-PassPerfectRadius)/PassFalloff);
            _stationQuality=q;
            Cue=QualityWord(q)+" PASS";
        }

        /// <summary>
        /// Our attack ends the rep. TOSS: the set's quality decides the attack (hard spike, spike, or an off-balance roll
        /// shot) and the rep counts from a good set. PASS: a pass our setter could set always counts; its quality only
        /// raises the kill chance. Either way the play stops at the kill or at their dig.
        /// </summary>
        void StationAttack(Vector3 hit)
        {
            float q=Mathf.Clamp01(_stationQuality);
            bool toss=Mode==RallyMode.Set;
            bool counts=!toss || q>=GoodSetQuality;
            float speed, kill;
            string shot;
            if(toss)
            {
                if(q>=PerfectSetQuality){speed=NpcAttackSpeed+3f;kill=.9f;shot="HARD SPIKE";}
                else if(q>=GoodSetQuality){speed=NpcAttackSpeed;kill=.55f;shot="SPIKE";}
                else{speed=7f;kill=.15f;shot="OFF BALANCE — ROLL SHOT";}
            }
            else{speed=NpcAttackSpeed;kill=Mathf.Lerp(StationKillMin,StationKillMax,q);shot="SPIKE";}
            string touch=QualityWord(q)+(toss ? " SET" : " PASS");
            var them=Team.Them;
            if(Random.value<kill)
            {
                // The spot in their court farthest from every one of their players.
                Vector3 best=TeamPoint(them,0f,4f);float bestGap=-1f;
                for(int i=0;i<16;i++)
                {
                    var spot=TeamPoint(them,Random.Range(-3.6f,3.6f),Random.Range(1.5f,8.2f));
                    float gap=float.MaxValue;
                    foreach(var actor in _cast)if(actor.Team==them && actor.A.gameObject.activeSelf)gap=Mathf.Min(gap,Flat(spot-actor.A.transform.position).magnitude);
                    if(gap>bestGap){bestGap=gap;best=spot;}
                }
                best.y=FloorY;
                float seconds=Mathf.Max(Flat(best-hit).magnitude/speed,ClearNetSeconds(hit,best,.2f));
                StartSegment(hit,best,seconds,()=>Award(counts,"KILL! — "+touch,best));
                Cue=shot;
                return;
            }
            MatchActor digger=null;float nearest=float.MaxValue;
            var aim=TeamPoint(them,Random.Range(-2.5f,2.5f),Random.Range(4f,7f));
            foreach(var actor in _cast)
            {
                if(actor.Team!=them || actor.Slot<=2 || !actor.A.gameObject.activeSelf)continue;
                float distance=Flat(aim-actor.A.transform.position).magnitude;
                if(distance<nearest){nearest=distance;digger=actor;}
            }
            if(digger==null){StartSegment(hit,aim,.6f,()=>Award(counts,touch,aim));return;}
            var dig=digger.A.transform.position-TeamForward(them)*.3f;dig.y=FloorY+DigHeight;
            float flight=Mathf.Max(Flat(dig-hit).magnitude/speed,ClearNetSeconds(hit,dig,.2f));
            StartSegment(hit,dig,flight,()=>{
                digger.Anim=Anim.Dig;digger.Clock=0f;PlaySpikeAudio(dig);
                Award(counts,"DUG — "+touch,dig);
            });
            Cue=shot;
        }
    }
}
