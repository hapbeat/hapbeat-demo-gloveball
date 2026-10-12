using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// One-skill modes on the 6 v 6 court (<see cref="RallyMode.Set"/> = TOSS, <see cref="RallyMode.Pass"/> = PASS). The
    /// player stays on one spot and makes one physical touch per rep; the rep ends as soon as our attack is decided, and
    /// the score counts the player's good touches (never a long NPC rally).
    /// TOSS: they serve, our back row passes up to the player at the right end of the net; the player catches the ball
    /// on the fingers and pushes it to the near or the far hitter (both move every rep; rings mark where to set). Their
    /// three blockers stack two in front of one hitter, so reading them is part of the choice. The set is graded
    /// (EXCELLENT / GOOD / BAD with what to change), decides how the hitter attacks, and the rep counts on a kill.
    /// PASS: they serve to the player (the ball comes to slightly different spots), who passes on the forearm platform
    /// to whichever of our three front-row players it comes down nearest; it counts when that player can set it: it
    /// must come down within reach and rise above the net first.
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
        [Tooltip("The player sets from this lateral (to our right of the net centre): the right end of the net.")]
        public float SetterLateral=3f;
        [Tooltip("Random hitter spots per rep (lateral to our right of the net centre, from the net): near hitter, far hitter, depth.")]
        public Vector2 NearHitterLateral=new Vector2(.2f,1.2f);
        public Vector2 FarHitterLateral=new Vector2(-3.6f,-2.4f);
        public Vector2 HitterDepth=new Vector2(.7f,1.2f);
        [Tooltip("Each of their blockers in front of the hitter keeps this share of the kill chance.")]
        [Range(0f,1f)] public float BlockerKillFactor=.55f;
        [Tooltip("Set quality needed for the rep to count, and for a hard spike.")]
        [Range(0f,1f)] public float GoodSetQuality=.4f;
        [Range(0f,1f)] public float PerfectSetQuality=.75f;
        [Min(.05f)] public float TargetRingRadius=.35f;
        [Header("PASS (receive to the setter)")]
        [Tooltip("Pull of the player's pass toward the ideal pass to the setter (0 = raw physics, 1 = always ideal).")]
        [Range(0f,1f)] public float PassAssist=.5f;
        [Tooltip("Random offset of each of our front-row players from their spot every rep (m).")]
        [Min(0f)] public float FrontRowJitter=.6f;
        [Tooltip("Ideal pass apex above the floor.")]
        [Min(2f)] public float PassApexHeight=4f;
        [Tooltip("A pass must rise this far above the net tape for our setter to play it.")]
        [Min(0f)] public float PassMinClearance=.4f;
        [Tooltip("Pass quality is 1 within this distance of the front-row player and 0 at this distance plus the falloff (m).")]
        [Min(0f)] public float PassPerfectRadius=.5f;
        [Min(.1f)] public float PassFalloff=1.8f;
        [Tooltip("Height above the floor where the serve reaches the player's forearms.")]
        [Min(.5f)] public float ReceiveHeight=1.3f;
        [Tooltip("Random spread of the serve around that point: sideways, forward / back, height (m). The player need not step.")]
        public Vector3 ReceiveSpread=new Vector3(.35f,.2f,.1f);
        [Tooltip("Kill chance of our attack after the worst and the best pass.")]
        [Range(0f,1f)] public float StationKillMin=.3f;
        [Range(0f,1f)] public float StationKillMax=.9f;

        float _stationQuality=-1f, _setArrival, _setClock;
        bool _setPlanned, _passTooLow;
        MatchActor _setHitter;
        Vector3 _setContact;
        readonly Vector3[] _hitterSpot=new Vector3[2];
        readonly LineRenderer[] _rings=new LineRenderer[3];
        readonly Material[] _ringMaterials=new Material[3];
        int _setBlockers;
        static readonly Color[] RingColours={new Color(.2f,.9f,1f),new Color(1f,.85f,.2f),Color.white};

        bool Station=>Mode==RallyMode.Set || Mode==RallyMode.Pass;
        /// <summary>Modes driven by the 6 v 6 engine (cast, scripted segments, physical player touches).</summary>
        bool MatchEngine=>Mode==RallyMode.Match || Station;
        int StationSlot=>Mode==RallyMode.Set ? 0 : 4;
        /// <summary>Quality of the player's last set or pass (0..1), -1 before it is judged.</summary>
        public float StationQuality=>_stationQuality;
        /// <summary>The drill holds the ball on the hands instead of bouncing it (a set).</summary>
        public bool HoldsSet=>MatchEngine && Phase==RallyPhase.PlayerSet && AwaitingPlayerTouch;
        /// <summary>Where the TOSS hitters strike a perfect set this rep (0 near, 1 far).</summary>
        public Vector3 HitterSpot(int index)=>_hitterSpot[index];
        /// <summary>Their blockers standing in front of the hitter the last set went to.</summary>
        public int SetBlockers=>_setBlockers;

        static string QualityWord(float q)=>q>=.75f ? "PERFECT" : q>=.4f ? "GOOD" : "LOOSE";
        static string Grade(float q)=>q>=.75f ? "EXCELLENT" : q>=.4f ? "GOOD" : "BAD";
        Vector3 SetterSpot=>SetHead(TeamPoint(Team.Us,SetterLateral,.8f));

        void StartStationRep()
        {
            _stationQuality=-1f;_setHitter=null;_setPlanned=false;_passTooLow=false;_setBlockers=0;
            var me=Me;
            if(me==null)return;
            // A fixed spot (no formation jitter), so the player is not moved between reps.
            me.Home=me.Target=Mode==RallyMode.Set ? SetterSpot : TeamPoint(Team.Us,.5f,6.2f);
            _serving=Team.Them;
            var server=Server(Team.Them);
            if(server!=null)server.Home=server.Target=TeamPoint(Team.Them,SlotBase[server.Slot].x,CourtDepth+.7f);
            if(Mode==RallyMode.Set){RandomizeHitters();StackTheirBlockers();}
            else RandomizeFrontRow();
            EmbodyRequest(me.Home,Mode==RallyMode.Set ? PlayerTouch.Set : PlayerTouch.Receive,true);
            ShowRings(Mode==RallyMode.Set);ShowCrossing(false,Vector3.zero,0f);
            Cue=Mode==RallyMode.Set ? "TOSS: NEAR OR FAR HITTER? WATCH THEIR BLOCKERS, AIM FOR A RING" : "PASS HIGH TO ONE OF YOUR FRONT-ROW PLAYERS";
            Phase=RallyPhase.MatchPlay;_serveTimer=ServeDelaySeconds;_serveWait=0f;
        }

        /// <summary>TOSS: a near and a far hitter on new spots every rep; each starts a few steps back and approaches when the set flies.</summary>
        void RandomizeHitters()
        {
            for(int i=0;i<2;i++)
            {
                var hitter=Hitter(i);
                if(hitter==null)continue;
                var range=i==0 ? NearHitterLateral : FarHitterLateral;
                float lateral=Random.Range(range.x,range.y),depth=Random.Range(HitterDepth.x,HitterDepth.y);
                var root=TeamPoint(Team.Us,lateral,depth);root.y=FloorY;
                _hitterSpot[i]=hitter.A.ReleasePositionAt(root,TeamFacing(Team.Us));
                hitter.Home=TeamPoint(Team.Us,lateral,depth+2.5f);
            }
        }
        /// <summary>0: the near hitter (our middle, slot 1); 1: the far hitter (our outside, slot 2).</summary>
        MatchActor Hitter(int index)=>ActorAt(Team.Us,index==0 ? 1 : 2);
        int HitterIndex(MatchActor hitter)=>hitter!=null && hitter.Slot==1 ? 0 : 1;
        Vector3 HitterContact(MatchActor hitter)=>_hitterSpot[HitterIndex(hitter)];
        /// <summary>TOSS: their three blockers at the net, two in front of one hitter and one in front of the other.</summary>
        void StackTheirBlockers()
        {
            bool stackNear=Random.value<.5f;
            float near=Lateral(_hitterSpot[0]),far=Lateral(_hitterSpot[1]);
            float[] laterals=stackNear ? new[]{near-.45f,near+.45f,far} : new[]{near,far-.45f,far+.45f};
            for(int slot=0;slot<3;slot++)
            {
                var blocker=ActorAt(Team.Them,slot);
                if(blocker==null)continue;
                blocker.Home=NetCentre+Right*laterals[slot]+Forward*BlockerNetDistance;
                blocker.Facing=TeamFacing(Team.Them);
            }
        }
        int BlockersFacing(float worldLateral)
        {
            int count=0;
            foreach(var actor in _cast)
                if(actor.Team==Team.Them && actor.Slot<=2 && actor.A.gameObject.activeSelf && Mathf.Abs(Lateral(actor.Home)-worldLateral)<1f)count++;
            return count;
        }
        /// <summary>PASS: our three front-row players stand somewhere a little different every rep.</summary>
        void RandomizeFrontRow()
        {
            for(int slot=0;slot<3;slot++)
            {
                var actor=ActorAt(Team.Us,slot);
                if(actor==null)continue;
                actor.Home=TeamPoint(Team.Us,SlotBase[slot].x+Random.Range(-FrontRowJitter,FrontRowJitter),1f+Random.Range(-.2f,.4f));
            }
        }
        MatchActor NearestFrontRow(Vector3 point)
        {
            MatchActor best=null;float bestDistance=float.MaxValue;
            for(int slot=0;slot<3;slot++)
            {
                var actor=ActorAt(Team.Us,slot);
                if(actor==null || actor==_embodied)continue;
                float distance=Flat(point-actor.A.transform.position).magnitude;
                if(distance<bestDistance){bestDistance=distance;best=actor;}
            }
            return best;
        }
        MatchActor NearestHitter(Vector3 point)
        {
            MatchActor best=null;float bestDistance=float.MaxValue;
            for(int index=0;index<2;index++)
            {
                var hitter=Hitter(index);
                if(hitter==null || hitter==_embodied)continue;
                float distance=Flat(point-HitterContact(hitter)).magnitude;
                if(distance<bestDistance){bestDistance=distance;best=hitter;}
            }
            return best;
        }

        LineRenderer Ring(int index)
        {
            if(_rings[index]!=null)return _rings[index];
            var go=new GameObject(index<2 ? "TOSS target ring "+(index+1) : "TOSS set crossing"){hideFlags=HideFlags.DontSave};
            go.transform.SetParent(transform,false);
            var ring=go.AddComponent<LineRenderer>();
            _ringMaterials[index]=new Material(Shader.Find("Universal Render Pipeline/Unlit")){hideFlags=HideFlags.DontSave};
            _ringMaterials[index].SetColor("_BaseColor",RingColours[index]);
            ring.sharedMaterial=_ringMaterials[index];ring.loop=true;ring.useWorldSpace=true;
            ring.startWidth=ring.endWidth=index<2 ? .04f : .03f;ring.positionCount=32;
            return _rings[index]=ring;
        }
        /// <summary>A vertical circle around <paramref name="centre"/>, turned toward the player so it reads as a target.</summary>
        void DrawCircle(LineRenderer ring,Vector3 centre,float radius)
        {
            var toward=Flat(GroundedEye-centre);
            if(toward.sqrMagnitude<1e-4f)toward=-Forward;
            var side=Vector3.Cross(Vector3.up,toward.normalized);
            for(int k=0;k<32;k++)
            {
                float a=k*Mathf.PI*2f/32f;
                ring.SetPosition(k,centre+(side*Mathf.Cos(a)+Vector3.up*Mathf.Sin(a))*radius);
            }
        }
        void ShowRings(bool show)
        {
            for(int i=0;i<2;i++)
            {
                if(_rings[i]==null && !show)continue;
                var ring=Ring(i);
                ring.gameObject.SetActive(show);
                if(show)DrawCircle(ring,_hitterSpot[i],TargetRingRadius);
            }
        }
        /// <summary>Where the player's set came down to hitting height, coloured by its grade (stays until the next rep).</summary>
        void ShowCrossing(bool show,Vector3 at,float q)
        {
            if(_rings[2]==null && !show)return;
            var ring=Ring(2);
            ring.gameObject.SetActive(show);
            if(!show)return;
            _ringMaterials[2].SetColor("_BaseColor",q>=PerfectSetQuality ? new Color(.3f,1f,.4f) : q>=GoodSetQuality ? new Color(1f,.85f,.2f) : new Color(1f,.35f,.25f));
            DrawCircle(ring,at,.13f);
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
        /// <summary>PASS: the platform's physical rebound pulled toward a pass that peaks over the front-row player it heads for.</summary>
        public Vector3 StationPassVelocity(Vector3 ball,Vector3 physical)
        {
            // Where the raw rebound comes down to setting height picks the player; the assist only helps toward them.
            float g=Mathf.Abs(Physics.gravity.y),setY=FloorY+SetHeight;
            float apex=ball.y+Mathf.Max(0f,physical.y)*Mathf.Max(0f,physical.y)/(2f*g);
            float time=Mathf.Max(0f,physical.y)/g+(apex>setY ? Mathf.Sqrt(2f*(apex-setY)/g) : 0f);
            var player=NearestFrontRow(ball+Flat(physical)*Mathf.Max(.3f,time));
            if(player==null)return physical;
            var target=player.Home;target.y=setY;
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
            if(!found){_stationQuality=0f;Cue="BAD SET — TOO LOW: PUSH UP HARDER";return;}
            if(SideOf(p)!=Team.Us){_stationQuality=0f;Cue="BAD SET — OVER THE NET: PUSH ALONG THE NET";return;}
            var hitter=NearestHitter(p);
            float distance=hitter!=null ? Flat(p-HitterContact(hitter)).magnitude : float.MaxValue;
            float q=distance>HitterReach ? 0f : 1f-distance/HitterReach;
            if(-Vector3.Dot(p-NetCentre,Forward)<.35f)q=Mathf.Min(q,.2f);
            _stationQuality=q;
            ShowCrossing(true,p,q);
            Cue=Grade(q)+" SET"+(hitter!=null && q<PerfectSetQuality ? " — "+SetAdvice(position,p,HitterContact(hitter)) : "!");
            if(distance>HitterReach)return;
            _setBlockers=BlockersFacing(Lateral(HitterContact(hitter)));
            ArmBlock(Team.Them,Lateral(p),t);
            _setHitter=hitter;_setContact=p;_setArrival=t;_setClock=0f;
            // The hitter's root goes where its hand meets the ball; it runs there and winds up to strike on arrival.
            hitter.Facing=TeamFacing(Team.Us);
            var root=p-hitter.A.ReleasePositionAt(Vector3.zero,hitter.Facing);root.y=FloorY;
            hitter.Target=root;hitter.Delay=0f;
            hitter.Speed=Mathf.Max(NpcRunSpeed,Flat(root-hitter.A.transform.position).magnitude/Mathf.Max(.25f,t*.8f));
            hitter.A.Role=VolleyOpponentPrototype.MotionRole.Spike;hitter.Anim=Anim.Spike;hitter.Clock=-1f;hitter.Phase=0f;
        }

        /// <summary>What to change, from where the set came down (<paramref name="crossing"/>) against the ring it was meant for.</summary>
        string SetAdvice(Vector3 from,Vector3 crossing,Vector3 target)
        {
            var error=Flat(crossing-target);
            var along=Flat(target-from).normalized;
            float length=Vector3.Dot(error,along),net=Vector3.Dot(error,Forward);
            if(Mathf.Abs(length)>=Mathf.Abs(net))return length>0f ? "TOO LONG: PUSH SOFTER" : "TOO SHORT: PUSH HARDER";
            return net>0f ? "TOO TIGHT: KEEP IT OFF THE NET" : "TOO FAR OFF THE NET: PUSH TOWARD THE NET";
        }

        /// <summary>PASS: a front-row player has the player's pass; judge it by how close it came down above them.</summary>
        void JudgeStationPass(Vector3 position,MatchActor player)
        {
            var target=player!=null ? player.Home : position;
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
            float speed, kill;
            string shot;
            if(toss)
            {
                if(q>=PerfectSetQuality){speed=NpcAttackSpeed+3f;kill=.9f;shot="HARD SPIKE";}
                else if(q>=GoodSetQuality){speed=NpcAttackSpeed;kill=.55f;shot="SPIKE";}
                else{speed=7f;kill=.15f;shot="OFF BALANCE — ROLL SHOT";}
                kill*=Mathf.Pow(BlockerKillFactor,_setBlockers);
                if(_setBlockers>0)shot+=_setBlockers>1 ? " INTO A DOUBLE BLOCK" : " PAST ONE BLOCKER";
            }
            else{speed=NpcAttackSpeed;kill=Mathf.Lerp(StationKillMin,StationKillMax,q);shot="SPIKE";}
            string touch=toss ? Grade(q)+" SET" : QualityWord(q)+" PASS";
            var them=Team.Them;
            bool killed=Random.value<kill;
            // TOSS counts on a kill (the set and the read of their block); PASS counts once a teammate could set it.
            bool counts=toss ? killed : true;
            if(toss && !killed && _setBlockers>0 && Random.value<.35f*_setBlockers)
            {
                // Stuffed: straight back down on our side.
                var tape=NetCentre+Right*Lateral(hit)+Vector3.up*(NetTop+.25f);
                var down=TeamPoint(Team.Us,Lateral(hit)+Random.Range(-1f,1f),Random.Range(.6f,2f));down.y=FloorY;
                StartSegment(hit,tape,.15f,()=>{PlaySpikeAudio(tape);StartSegment(tape,down,.5f,()=>Award(false,"BLOCKED — "+touch,down));});
                Cue=shot;
                return;
            }
            if(killed)
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
