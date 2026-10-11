using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// One-skill stations on the 6 v 6 court (<see cref="RallyMode.Set"/>, <see cref="RallyMode.Pass"/>). The player stays on
    /// one spot and makes one physical touch per rep; NPC teammates finish the play, and the quality of the player's touch
    /// decides how likely our attack scores.
    /// SET: a back-row teammate passes up to the player at the setter spot (front right); the push sets the outside or the
    /// middle hitter, whoever the ball comes down to. PASS: the opponent serves; the player passes it on the forearm
    /// platform to our setter at the net, who sets our hitter.
    /// </summary>
    public sealed partial class VolleyAerialSequence
    {
        [Header("Stations: set / pass")]
        [Tooltip("Pull of the set's aim toward the hitter it is heading to (0 = raw push, 1 = always on target). Height is never helped.")]
        [Range(0f,1f)] public float SetAssist=.5f;
        [Tooltip("Set: upward ball speed with no push, and added per m/s of upward push.")]
        [Min(0f)] public float SetLiftBase=3.5f;
        [Min(0f)] public float SetLiftGain=1.6f;
        [Tooltip("Set: horizontal ball speed per m/s of horizontal push.")]
        [Min(0f)] public float SetAimGain=1.8f;
        [Tooltip("How far a hitter moves from their lane spot to reach a set (m). Quality falls off linearly to this distance.")]
        [Min(.1f)] public float HitterReach=1.3f;
        [Tooltip("Pull of the player's pass toward the ideal pass to the setter (0 = raw physics, 1 = always ideal).")]
        [Range(0f,1f)] public float PassAssist=.5f;
        [Tooltip("Pass: our setter's spot (lateral to our right, depth from the net) and the ideal pass apex above the floor.")]
        public Vector2 PassTarget=new Vector2(1f,1f);
        [Min(2f)] public float PassApexHeight=4f;
        [Tooltip("Pass quality is 1 within this distance of the setter's spot and 0 at this distance plus the falloff (m).")]
        [Min(0f)] public float PassPerfectRadius=.5f;
        [Min(.1f)] public float PassFalloff=1.8f;
        [Tooltip("Kill chance of our attack after the worst and the best player touch.")]
        [Range(0f,1f)] public float StationKillMin=.3f;
        [Range(0f,1f)] public float StationKillMax=.9f;

        float _stationQuality=-1f, _setArrival, _setClock;
        bool _setPlanned;
        MatchActor _setHitter;
        Vector3 _setContact;

        bool Station=>Mode==RallyMode.Set || Mode==RallyMode.Pass;
        /// <summary>Modes driven by the 6 v 6 engine (cast, scripted segments, physical player touches).</summary>
        bool MatchEngine=>Mode==RallyMode.Match || Station;
        int StationSlot=>Mode==RallyMode.Set ? 0 : 4;
        /// <summary>Quality of the player's last set or pass (0..1), -1 before it is judged.</summary>
        public float StationQuality=>_stationQuality;

        static string QualityWord(float q)=>q>=.75f ? "PERFECT" : q>=.4f ? "GOOD" : "LOOSE";

        void StartStationRep()
        {
            _stationQuality=-1f;_setHitter=null;_setPlanned=false;
            var me=Me;
            if(me==null)return;
            // A fixed spot (no formation jitter), so the player is not moved between reps.
            me.Home=me.Target=Mode==RallyMode.Set ? TeamPoint(Team.Us,PassTarget.x,.7f) : TeamPoint(Team.Us,.5f,6.2f);
            if(Mode==RallyMode.Pass)
            {
                _serving=Team.Them;
                var server=Server(Team.Them);
                server.Home=server.Target=TeamPoint(Team.Them,SlotBase[server.Slot].x,CourtDepth+.7f);
                EmbodyRequest(me.Home,PlayerTouch.Receive,true);
                Cue="PASS THE SERVE TO YOUR SETTER AT THE NET";
            }
            else
            {
                _serving=Team.Us;
                EmbodyRequest(me.Home,PlayerTouch.Set,true);
                Cue="SET: PUSH THE BALL UP TO A HITTER";
            }
            Phase=RallyPhase.MatchPlay;_serveTimer=ServeDelaySeconds;_serveWait=0f;
        }

        /// <summary>Set station: the back-middle teammate digs a ball up to the player.</summary>
        void BeginStationFeed()
        {
            var feeder=ActorAt(Team.Us,4);
            if(feeder==null)return;
            var from=feeder.A.transform.position+Vector3.up*.9f+Forward*.3f;
            feeder.Anim=Anim.Dig;feeder.Clock=0f;
            EnsureScriptedBall(from);
            _touches[0]=1;_lastTouchTeam=Team.Us;
            RequestLaunch(()=>{var head=GroundedEye;return (from,head+Forward*.1f+Vector3.up*.3f,PassSeconds+.25f);},RallyPhase.PlayerSet,PlayerTouch.Set);
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
        /// <summary>Where a hitter in <paramref name="slot"/> strikes a perfect set (their hand at full reach in the air).</summary>
        Vector3 HitterContact(MatchActor hitter)
        {
            var root=TeamPoint(Team.Us,SlotBase[hitter.Slot].x,.9f);root.y=FloorY;
            return hitter.A.ReleasePositionAt(root,TeamFacing(Team.Us));
        }
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

        /// <summary>
        /// Set station: the push decides height (upward speed) and direction (horizontal speed); the result is pulled toward
        /// the ideal set for whichever hitter it is heading to by <see cref="SetAssist"/>.
        /// </summary>
        public Vector3 StationSetVelocity(Vector3 ball,Vector3 handVelocity)
        {
            var raw=Flat(handVelocity)*SetAimGain+Vector3.up*(SetLiftBase+SetLiftGain*Mathf.Max(0f,handVelocity.y));
            float g=Mathf.Abs(Physics.gravity.y);
            float apex=ball.y+raw.y*raw.y/(2f*g);
            // Where the raw set would come down to hitting height decides the intended hitter.
            float contactY=HitterContact(ActorAt(Team.Us,2)).y;
            float down=apex>contactY ? Mathf.Sqrt(2f*(apex-contactY)/g) : 0f;
            // Too low to ever reach a hitter: no help, the height is the player's alone.
            if(apex<=contactY)return raw;
            float time=raw.y/g+down;
            var hitter=NearestHitter(ball+Flat(raw)*time);
            if(hitter==null)return raw;
            // Only the aim is helped: the horizontal speed is pulled toward the one that meets the hitter at that time.
            var aim=Flat(HitterContact(hitter)-ball)/time;
            return Vector3.Lerp(Flat(raw),aim,SetAssist)+Vector3.up*raw.y;
        }
        /// <summary>Pass station: the platform's physical rebound pulled toward a pass that peaks over our setter.</summary>
        public Vector3 StationPassVelocity(Vector3 ball,Vector3 physical)
        {
            var target=TeamPoint(Team.Us,PassTarget.x,PassTarget.y);target.y=FloorY+SetHeight;
            return Vector3.Lerp(physical,ArcVelocity(ball,target,FloorY+PassApexHeight),PassAssist);
        }

        /// <summary>Set station, every physics step after the player's set: pick the hitter, run and jump them, hand the ball over.</summary>
        bool TrackStationSet(Vector3 position,Vector3 velocity,float radius,float step,float damping)
        {
            if(!_setPlanned)
            {
                _setPlanned=true;
                PlanStationSet(position,velocity,damping);
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
            var hitter=ActorAt(Team.Us,2);
            if(hitter==null)return;
            float contactY=HitterContact(hitter).y;
            var p=position;var v=velocity;float t=0f;const float dt=.02f;bool found=false;
            for(int i=0;i<250;i++)
            {
                if(v.y<0f && p.y<=contactY){found=true;break;}
                if(p.y<FloorY)break;
                v+=Physics.gravity*dt;v*=Mathf.Clamp01(1f-damping*dt);p+=v*dt;t+=dt;
            }
            if(!found || SideOf(p)!=Team.Us){_stationQuality=0f;Cue=found ? "OVER THE NET" : "TOO LOW FOR A HITTER";return;}
            hitter=NearestHitter(p);
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

        /// <summary>Pass station: our setter has the player's pass; judge it by how close it came down to the setter's spot.</summary>
        void JudgeStationPass(Vector3 position)
        {
            var target=TeamPoint(Team.Us,PassTarget.x,PassTarget.y);
            float q=1f-Mathf.Clamp01((Flat(position-target).magnitude-PassPerfectRadius)/PassFalloff);
            _stationQuality=q;
            Cue=QualityWord(q)+" PASS";
        }

        /// <summary>Our attack after a station rep: kill into the open court, or their back row digs it and the rep ends.</summary>
        void StationAttack(Vector3 hit)
        {
            float q=Mathf.Clamp01(_stationQuality);
            var them=Team.Them;
            if(Random.value<Mathf.Lerp(StationKillMin,StationKillMax,q))
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
                float seconds=Mathf.Max(Flat(best-hit).magnitude/NpcAttackSpeed,ClearNetSeconds(hit,best,.2f));
                StartSegment(hit,best,seconds,()=>ResolveMatch(best));
                Cue="KILL!";
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
            if(digger==null){StartSegment(hit,aim,.6f,()=>ResolveMatch(aim));return;}
            var dig=digger.A.transform.position-TeamForward(them)*.3f;dig.y=FloorY+DigHeight;
            float flight=Mathf.Max(Flat(dig-hit).magnitude/NpcAttackSpeed,ClearNetSeconds(hit,dig,.2f));
            StartSegment(hit,dig,flight,()=>{
                digger.Anim=Anim.Dig;digger.Clock=0f;PlaySpikeAudio(dig);
                Award(false,"DUG — "+QualityWord(q)+(Mode==RallyMode.Set ? " SET" : " PASS"),dig);
            });
            Cue="THEIR DEFENCE...";
        }
    }
}
