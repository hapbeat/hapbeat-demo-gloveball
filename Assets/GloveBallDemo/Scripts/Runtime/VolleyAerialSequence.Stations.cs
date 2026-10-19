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
        [Tooltip("A set is graded by what the hitter can do with it. EXCELLENT: the hitter barely has to adjust (m), it peaks this far over the tape (time to approach), and it comes down within this band off the net.")]
        [Min(0f)] public float ExcellentSetAdjust=.6f;
        [Min(0f)] public float ExcellentSetRise=1f;
        public Vector2 ExcellentSetOffNet=new Vector2(.4f,1.5f);
        [Tooltip("GOOD: reachable, peaks at least this far over the tape, and inside this band off the net (the 3 m line).")]
        [Min(0f)] public float GoodSetRise=.5f;
        public Vector2 GoodSetOffNet=new Vector2(.3f,3f);
        [Tooltip("Set quality needed for the rep to count, and for a hard spike.")]
        [Range(0f,1f)] public float GoodSetQuality=.4f;
        [Range(0f,1f)] public float PerfectSetQuality=.75f;
        [Tooltip("Ghost spheres mark where to set (near / far hitter) and where the set came down.")]
        [Min(.05f)] public float TargetRadius=.3f;
        [Tooltip("Transparent material for the target spheres (tinted per sphere).")]
        public Material TargetMaterial;
        [Tooltip("Hand speed used for the set is capped here: a noisy tracking sample must not launch the ball sky-high (m/s).")]
        [Min(.5f)] public float SetMaxPushSpeed=3.5f;
        [Header("PASS (receive to the setter)")]
        [Tooltip("Pull of the player's pass toward the ideal pass to the setter (0 = raw physics, 1 = always ideal).")]
        [Range(0f,1f)] public float PassAssist=.5f;
        [Tooltip("The forearm rebound is scaled by this before the assist (the volleyball came off the platform too lively).")]
        [Range(.3f,1f)] public float PassReboundScale=.8f;
        [Tooltip("Their attack at the player when the rally carries on after an overpass or a dug spike: faster and lower than a serve.")]
        [Min(4f)] public float TheirAttackSpeed=12f;
        [Min(0f)] public float TheirAttackDrop=.15f;
        [Tooltip("Their attack after a BAD pass went over as a free ball: they had all the time, so it is harder.")]
        [Min(4f)] public float TheirStrongAttackSpeed=15f;
        [Min(0f)] public float TheirStrongAttackDrop=.35f;
        [Tooltip("Random offset of each of our front-row players from their spot every rep (m).")]
        [Min(0f)] public float FrontRowJitter=.6f;
        [Tooltip("Ideal pass apex above the floor.")]
        [Min(2f)] public float PassApexHeight=4f;
        [Tooltip("A pass must rise this far above the net tape for our setter to play it.")]
        [Min(0f)] public float PassMinClearance=.4f;
        [Tooltip("Our setter (slot 0) stands somewhere new every rep: lateral to our right of the net centre, off the net. A disc under the setter is the target.")]
        public Vector2 SetterLateralRange=new Vector2(-.5f,2.5f);
        public Vector2 SetterOffNetRange=new Vector2(1f,2.5f);
        [Tooltip("RECEIVE: the two other front-row players (hitters) stand anywhere within this of the court centre (m, sideways)...")]
        [Min(0f)] public float FrontRowLateral=3.6f;
        [Tooltip("...at least this far sideways from the setter (m), so it is clear who plays a pass.")]
        [Min(0f)] public float HitterSetterGap=2f;
        [Tooltip("RECEIVE: the setter also runs this much further (m) than an EXCELLENT pass allows, and sets it as GOOD.")]
        [Min(0f)] public float SetterRushRange=1.2f;
        [Tooltip("EXCELLENT: the setter plays it moving at most this far (m), and it was in the air at least this long since the pass (s).")]
        [Min(0f)] public float ExcellentPassMove=.8f;
        [Min(0f)] public float ExcellentPassHang=.9f;
        [Tooltip("Every second the pass hangs longer than that lets the setter move this much further (m) for EXCELLENT.")]
        [Min(0f)] public float ExcellentMovePerSecond=1.5f;
        [Tooltip("Their attack is touched by one of our jumping blockers when it passes within this of their hands' line (m): it slows and pops up.")]
        [Min(0f)] public float SoftBlockWidth=.45f;
        [Tooltip("GOOD: it rose clearly over the tape and comes down in this band off the net (inside the 3 m line), where a front-row player can set it.")]
        public Vector2 GoodPassOffNet=new Vector2(.5f,3f);
        [Tooltip("Height above the floor where the serve reaches the player's forearms.")]
        [Min(.5f)] public float ReceiveHeight=1.3f;
        [Tooltip("Random spread of the serve around that point: sideways, forward / back, height (m). The player need not step.")]
        public Vector3 ReceiveSpread=new Vector3(.45f,.25f,.1f);
        [Tooltip("Kill chance of our attack after the worst and the best pass.")]
        [Range(0f,1f)] public float StationKillMin=.3f;
        [Range(0f,1f)] public float StationKillMax=.9f;
        [Tooltip("RECEIVE: an EXCELLENT pass's spike is this much faster than their usual attack (m/s)...")]
        [Min(0f)] public float ExcellentSpikeExtraSpeed=14f;
        [Tooltip("...and comes down this far into their court (m from the net): short, so it goes steeply down.")]
        public Vector2 ExcellentKillDepth=new Vector2(2.2f,4.5f);
        [Tooltip("RECEIVE: chance one of our jumping blockers gets a touch on their counter-attack (only after a GOOD rep; after a BAD pass they attack freely).")]
        [Range(0f,1f)] public float OurBlockTouchChance=.5f;
        [Tooltip("RECEIVE serve flight times (s): hard and flat, normal, soft and loopy. One is picked per serve; the landing area stays the same.")]
        public Vector3 ServeFlightSeconds=new Vector3(1.05f,1.5f,2.2f);

        float _stationQuality=-1f, _setArrival, _setClock;
        bool _setPlanned, _passTooLow, _passHitNet, _counterStrong;
        /// <summary>RECEIVE: 3 EXCELLENT, 2 GOOD (a front-row player sets), 1 BAD (someone plays it over), 0 nobody can play it.</summary>
        int _passClass;
        MatchActor _setHitter;
        Vector3 _setContact;
        readonly Vector3[] _hitterSpot=new Vector3[2];
        readonly Renderer[] _targets=new Renderer[5];
        MaterialPropertyBlock _targetBlock;
        int _setBlockers;
        static readonly Color[] TargetColours={new Color(.2f,.9f,1f,.32f),new Color(1f,.85f,.2f,.32f),new Color(1f,1f,1f,.6f)};

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
        /// <summary>EXCELLENT / GOOD / BAD of the player's last set or pass, shown small in the view (<see cref="GradeSerial"/> counts them).</summary>
        public string LastGrade { get; private set; }
        public int GradeSerial { get; private set; }

        static string QualityWord(float q)=>q>=.75f ? "PERFECT" : q>=.4f ? "GOOD" : "LOOSE";
        static string GradeWord(float q)=>q>=.75f ? "EXCELLENT" : q>=.4f ? "GOOD" : "BAD";
        void ShowGrade(string grade){LastGrade=grade;LastPassTaker=PassTaker.None;GradeSerial++;}
        public enum PassTaker { None, Setter, Other }
        /// <summary>RECEIVE: who sets the graded pass - the setter (EXCELLENT, or GOOD when it had to move), another player (GOOD), or nobody (BAD).</summary>
        public PassTaker LastPassTaker { get; private set; }
        Vector3 SetterSpot=>SetHead(TeamPoint(Team.Us,SetterLateral,.8f));

        void StartStationRep()
        {
            _stationQuality=-1f;_setHitter=null;_setPlanned=false;_passTooLow=false;_passHitNet=false;_counterStrong=false;_passClass=0;_setBlockers=0;
            var me=Me;
            if(me==null)return;
            // A fixed spot (no formation jitter), so the player is not moved between reps.
            me.Home=me.Target=Mode==RallyMode.Set ? SetterSpot : TeamPoint(Team.Us,.5f,6.2f);
            _serving=Team.Them;
            var server=Server(Team.Them);
            // The service zone is the whole 9 m behind their end line: serve from anywhere along it.
            float serveFrom=PracticeServes ? (_practiceServe++%2==0 ? -1.8f : 1.8f) : Random.Range(-CourtHalfWidth+.8f,CourtHalfWidth-.8f);
            if(server!=null)server.Home=server.Target=TeamPoint(Team.Them,serveFrom,CourtDepth+.7f);
            if(Mode==RallyMode.Set){RandomizeHitters();StackTheirBlockers();}
            else RandomizeFrontRow();
            EmbodyRequest(me.Home,Mode==RallyMode.Set ? PlayerTouch.Set : PlayerTouch.Receive,true);
            ShowRings(Mode==RallyMode.Set);ShowCrossing(false,Vector3.zero,0f);
            Cue=Mode==RallyMode.Set ? "TOSS: NEAR OR FAR HITTER? WATCH THEIR BLOCKERS, AIM FOR A GHOST BALL"
                : "RECEIVE: PASS HIGH TO THE SETTER (GREEN)";
            var setter=ActorAt(Team.Us,0);
            if(Mode==RallyMode.Pass && setter!=null && !MarkOtherPassers)PlaceTarget(3,new Vector3(setter.Home.x,FloorY+.01f,setter.Home.z),.55f,new Color(.3f,1f,.45f,.3f),.02f);
            else HideTarget(3);
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
        /// <summary>RECEIVE: our setter somewhere new inside the 3 m line, the other two front-row players a little off their spots.</summary>
        void RandomizeFrontRow()
        {
            var setter=ActorAt(Team.Us,0);
            if(PracticeServes)
            {
                // The tutorial: one setter spot in front of the player, the hitters well to the sides.
                if(setter!=null)setter.Home=TeamPoint(Team.Us,.5f,1.6f);
                var near=ActorAt(Team.Us,1);if(near!=null)near.Home=TeamPoint(Team.Us,-2.2f,1.2f);
                var far=ActorAt(Team.Us,2);if(far!=null)far.Home=TeamPoint(Team.Us,2.8f,1.2f);
                return;
            }
            if(setter!=null)setter.Home=TeamPoint(Team.Us,Random.Range(SetterLateralRange.x,SetterLateralRange.y),Random.Range(SetterOffNetRange.x,SetterOffNetRange.y));
            for(int slot=1;slot<3;slot++)
            {
                var actor=ActorAt(Team.Us,slot);
                if(actor==null)continue;
                var other=slot==2 ? ActorAt(Team.Us,1) : null;
                for(int attempt=0;attempt<40;attempt++)
                {
                    actor.Home=TeamPoint(Team.Us,Random.Range(-FrontRowLateral,FrontRowLateral),1f+Random.Range(-.2f,.4f));
                    bool clearOfSetter=setter==null || Mathf.Abs(Lateral(actor.Home)-Lateral(setter.Home))>=HitterSetterGap;
                    bool clearOfOther=other==null || Mathf.Abs(Lateral(actor.Home)-Lateral(other.Home))>=HitterSetterGap*.8f;
                    if(clearOfSetter && clearOfOther)break;
                }
            }
        }
        /// <summary>
        /// RECEIVE, re-planned every physics step after the pass (graded by what the team can do with it, the 3-2-1 idea):
        /// EXCELLENT - it comes to the setter (little movement) with time to spare; GOOD - it rose clearly over the tape and
        /// comes down inside the 3 m line where a front-row player can set it; BAD - too low to set, or behind / beside the
        /// player: whoever can reach it plays it over as a free ball, and they attack hard; nobody can reach it - lost.
        /// A pass that touched the net is never played.
        /// </summary>
        void PlanStationPass(Vector3 p,float t,float top,bool atNet=false)
        {
            if(_passHitNet){_passClass=0;ClearIntercept();return;}
            float hang=_sinceTouch+t,off=-Vector3.Dot(p-NetCentre,Forward);
            _passTooLow=top<NetTop+PassMinClearance;
            MatchActor best=null;float bestDistance=float.MaxValue;
            bool settable=!_passTooLow && off>=GoodPassOffNet.x && off<=GoodPassOffNet.y;
            float moving=t-Mathf.Max(0f,.1f-_sinceTouch);
            // A lob dying into the net is played above the hands (a jump set) just before it gets there.
            float hands=atNet ? Mathf.Max(SetterHandsHeight,p.y) : SetterHandsHeight;
            if(settable)
            {
                // The setter (green) takes every settable ball that comes down around its spot and it can get to; a ball
                // further off goes to the nearest other front-row player (a hitter), who sets it: GOOD at best.
                var setter=ActorAt(Team.Us,0);
                float allowed=ExcellentPassMove+ExcellentMovePerSecond*Mathf.Max(0f,hang-ExcellentPassHang);
                if(setter!=null && setter!=_embodied && setter.A.gameObject.activeSelf
                   && Flat(p-SetWindowAt(setter)).magnitude<=allowed+SetterRushRange && NearerThanTheHitters(setter,p)
                   && Mathf.Max(0f,Flat(p-setter.A.transform.position).magnitude-SetterReach)/SetterSpeed<=moving)best=setter;
                else
                    for(int slot=1;slot<3;slot++)
                    {
                        var actor=ActorAt(Team.Us,slot);
                        if(actor==null || actor==_embodied || !actor.A.gameObject.activeSelf)continue;
                        float distance=Flat(p-actor.A.transform.position).magnitude;
                        if(Mathf.Max(0f,distance-SetterReach)/SetterSpeed<=moving && distance<bestDistance){bestDistance=distance;best=actor;}
                    }
            }
            if(best!=null)
            {
                // The higher the pass (the longer it hangs), the further the setter may move and still have every option.
                float allowed=ExcellentPassMove+ExcellentMovePerSecond*Mathf.Max(0f,hang-ExcellentPassHang);
                bool excellent=best==ActorAt(Team.Us,0) && Flat(p-SetWindowAt(best)).magnitude<=allowed && hang>=ExcellentPassHang;
                _passClass=excellent ? 3 : 2;
                _icActor=best;_icSuccess=true;_icTeam=Team.Us;_icPoint=new Vector3(p.x,FloorY,p.z);
                _icHeight=hands;_icReach=SetterReach;_icReaction=.1f;_icSpeed=SetterSpeed;
                // The setter stands where its hands meet the ball and its set pose grows as it arrives.
                best.Facing=TeamFacing(Team.Us);
                best.A.Role=VolleyOpponentPrototype.MotionRole.Set;best.Anim=Anim.Set;best.Clock=-1f;
                best.Phase=VolleyOpponentPrototype.SetContactPhase*Mathf.Clamp01(1f-t/.8f);
                _icRootOffset=-Flat(best.A.ReleasePositionAt(Vector3.zero,best.Facing));
                if(_plannedAttacker==null)_plannedAttacker=ChooseAttacker(Team.Us,best,Vector3.zero);
                return;
            }
            // BAD: anyone who can get there bumps it over.
            float dig=t-Mathf.Max(0f,OurDigReaction-_sinceTouch);
            for(int slot=0;slot<6;slot++)
            {
                var actor=ActorAt(Team.Us,slot);
                if(actor==null || actor==_embodied || !actor.A.gameObject.activeSelf)continue;
                float distance=Flat(p-actor.A.transform.position).magnitude;
                if(Mathf.Max(0f,distance-OurDigReach)/OurDigSpeed<=dig && distance<bestDistance){bestDistance=distance;best=actor;}
            }
            if(best==null){_passClass=0;ClearIntercept();return;}
            if(best!=_icActor && _icActor!=null && _icActor.Anim==Anim.Set){_icActor.Anim=Anim.Ready;_icActor.Clock=-1f;}
            _passClass=1;
            _icActor=best;_icSuccess=true;_icTeam=Team.Us;_icPoint=new Vector3(p.x,FloorY,p.z);
            _icHeight=hands;_icReach=OurDigReach;_icReaction=OurDigReaction;_icSpeed=OurDigSpeed;_icRootOffset=Vector3.zero;
        }
        /// <summary>RECEIVE: the setter plays a pass only when it is no further from it than from either hitter (no ambiguity).</summary>
        bool NearerThanTheHitters(MatchActor setter,Vector3 p)
        {
            float own=Flat(p-setter.A.transform.position).magnitude;
            for(int slot=1;slot<3;slot++)
            {
                var hitter=ActorAt(Team.Us,slot);
                if(hitter!=null && hitter!=_embodied && hitter.A.gameObject.activeSelf && Flat(p-hitter.A.transform.position).magnitude<own)return false;
            }
            return true;
        }
        /// <summary>RECEIVE, a BAD pass: the second touch just sends it over; they will attack hard.</summary>
        void StationFreeBall(MatchActor actor,Vector3 position)
        {
            actor.A.Role=VolleyOpponentPrototype.MotionRole.Dig;actor.Anim=Anim.Dig;actor.Clock=0f;
            _touches[0]=2;_lastTouchTeam=Team.Us;_counterStrong=true;
            PlaySpikeAudio(position);
            var why=Cue; // the grade's reason stays on the banner
            FreeBall(Team.Us,position);
            Cue=why;
        }
        /// <summary>Whichever of our players (not the player) a pass comes down nearest: a front-row player near the net, anyone behind the 3 m line.</summary>
        MatchActor NearestTeammate(Vector3 point)
        {
            MatchActor best=null;float bestDistance=float.MaxValue;
            for(int slot=0;slot<6;slot++)
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

        Renderer Target(int index)
        {
            if(_targets[index]!=null)return _targets[index];
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name=index<2 ? "TOSS target "+(index+1) : index==2 ? "TOSS set crossing" : "RECEIVE target";go.hideFlags=HideFlags.DontSave;
            if(Application.isPlaying)Destroy(go.GetComponent<Collider>());else DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(transform,false);
            var renderer=go.GetComponent<Renderer>();
            if(TargetMaterial!=null)renderer.sharedMaterial=TargetMaterial;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            return _targets[index]=renderer;
        }
        void PlaceTarget(int index,Vector3 at,float radius,Color colour,float height=-1f)
        {
            var target=Target(index);
            target.gameObject.SetActive(true);
            target.transform.SetPositionAndRotation(at,Quaternion.identity);
            target.transform.localScale=new Vector3(radius*2f,height>0f ? height : radius*2f,radius*2f);
            if(_targetBlock==null)_targetBlock=new MaterialPropertyBlock();
            target.GetPropertyBlock(_targetBlock);_targetBlock.SetColor("_BaseColor",colour);target.SetPropertyBlock(_targetBlock);
        }
        void HideTarget(int index){if(_targets[index]!=null)_targets[index].gameObject.SetActive(false);}
        /// <summary>RECEIVE tutorial: the targets are the two other front-row players (GOOD, yellow labels), not the setter.</summary>
        [System.NonSerialized] public bool MarkOtherPassers;
        [Tooltip("RECEIVE: our setter's jersey (green, like its disc and label), so it stands out from the others.")]
        public Color SetterJersey=new Color(.16f,.72f,.3f);
        VolleyOpponentPrototype _tintedSetter;
        /// <summary>RECEIVE: our setter in its own colour; whoever wore it before back in the team's.</summary>
        void TintSetter(VolleyOpponentPrototype setter)
        {
            if(_tintedSetter==setter)return;
            if(_tintedSetter!=null){_tintedSetter.JerseyColour=AllyJersey;_tintedSetter.ApplyKit();}
            _tintedSetter=setter;
            if(setter!=null){setter.JerseyColour=SetterJersey;setter.ApplyKit();}
        }
        /// <summary>RECEIVE: the setter's green disc stays under it as it moves (hidden while the others are the targets).</summary>
        void FollowSetterDisc()
        {
            var setter=ActorAt(Team.Us,0);
            TintSetter(setter?.A);
            if(MarkOtherPassers){HideTarget(3);return;}
            if(_targets[3]==null || !_targets[3].gameObject.activeSelf || setter==null)return;
            var feet=setter.A.transform.position;feet.y=FloorY+.01f;
            _targets[3].transform.position=feet;
        }
        /// <summary>RECEIVE: above the setter's head, for its label (the disc on the floor is easy to lose).</summary>
        public bool TryGetSetterMarker(out Vector3 point)
        {
            point=default;
            if(!RallyActive || Mode!=RallyMode.Pass || MarkOtherPassers)return false;
            var setter=ActorAt(Team.Us,0);
            if(setter==null || !setter.A.gameObject.activeInHierarchy)return false;
            point=setter.A.transform.position;point.y=FloorY+SetWindow.y*setter.A.transform.lossyScale.y+.55f;
            return true;
        }
        /// <summary>RECEIVE tutorial's GOOD step: above the head of the other front-row player <paramref name="index"/> (0, 1).</summary>
        public bool TryGetPasserMarker(int index,out Vector3 point)
        {
            point=default;
            if(!RallyActive || Mode!=RallyMode.Pass || !MarkOtherPassers)return false;
            var actor=ActorAt(Team.Us,1+index);
            if(actor==null || actor==_embodied || !actor.A.gameObject.activeInHierarchy)return false;
            point=actor.A.transform.position;point.y=FloorY+SetWindow.y*actor.A.transform.lossyScale.y+.55f;
            return true;
        }
        /// <summary>Ghost balls where a perfect set meets the near (cyan) and far (yellow) hitter.</summary>
        void ShowRings(bool show)
        {
            for(int i=0;i<2;i++)
                if(show)PlaceTarget(i,_hitterSpot[i],TargetRadius,TargetColours[i]);else HideTarget(i);
        }
        /// <summary>Where the player's set came down to hitting height, coloured by its grade (stays until the next rep).</summary>
        void ShowCrossing(bool show,Vector3 at,float q)
        {
            if(!show){HideTarget(2);return;}
            var colour=q>=PerfectSetQuality ? new Color(.3f,1f,.4f,.7f) : q>=GoodSetQuality ? new Color(1f,.85f,.2f,.7f) : new Color(1f,.35f,.25f,.7f);
            PlaceTarget(2,at,.12f,colour);
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
            handVelocity=Vector3.ClampMagnitude(handVelocity,SetMaxPushSpeed);
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
            physical*=PassReboundScale;
            // The assist pulls toward our setter's hands (the green disc), not toward whoever is near.
            var setter=ActorAt(Team.Us,0);
            if(setter==null)return physical;
            return Vector3.Lerp(physical,ArcVelocity(ball,SetWindowAt(setter),FloorY+PassApexHeight),PassAssist);
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
            var p=position;var v=velocity;float t=0f;const float dt=.02f;bool found=false;float apex=position.y;
            for(int i=0;i<250;i++)
            {
                if(v.y<0f && p.y<=contactY){found=true;break;}
                if(p.y<FloorY)break;
                v+=Physics.gravity*dt;v*=Mathf.Clamp01(1f-damping*dt);p+=v*dt;t+=dt;
                apex=Mathf.Max(apex,p.y);
            }
            if(!found){_stationQuality=0f;ShowGrade("BAD");Cue="BAD SET — TOO LOW: PUSH UP HARDER";return;}
            if(SideOf(p)!=Team.Us){_stationQuality=0f;ShowGrade("BAD");Cue="BAD SET — OVER THE NET: PUSH ALONG THE NET";return;}
            var hitter=NearestHitter(p);
            float distance=hitter!=null ? Flat(p-HitterContact(hitter)).magnitude : float.MaxValue;
            // Graded by what the hitter can do with it, not by the spot alone: how far the hitter must adjust, how high it
            // peaks over the tape (time to approach - better too high than too low), how far off the net it comes down.
            float rise=apex-NetTop,off=-Vector3.Dot(p-NetCentre,Forward);
            float q;
            if(distance>HitterReach)q=0f;
            else if(distance<=ExcellentSetAdjust && rise>=ExcellentSetRise && off>=ExcellentSetOffNet.x && off<=ExcellentSetOffNet.y)q=1f;
            else if(rise>=GoodSetRise && off>=GoodSetOffNet.x && off<=GoodSetOffNet.y)q=.6f;
            else q=.2f;
            _stationQuality=q;
            ShowCrossing(true,p,q);ShowGrade(GradeWord(q));
            string advice=rise<ExcellentSetRise ? "TOO LOW: PUSH HIGHER, GIVE THE HITTER TIME"
                : off<ExcellentSetOffNet.x ? "TOO TIGHT: KEEP IT OFF THE NET"
                : off>GoodSetOffNet.y ? "BEHIND THE 3 M LINE: PUSH TOWARD THE NET"
                : off>ExcellentSetOffNet.y ? "TOO FAR OFF THE NET: PUSH TOWARD THE NET"
                : hitter!=null ? SetAdvice(position,p,HitterContact(hitter)) : "";
            Cue=GradeWord(q)+" SET"+(q<PerfectSetQuality && advice.Length>0 ? " — "+advice : "!");
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

        /// <summary>
        /// RECEIVE, graded like the common 3-2-1 passing scale by what the setter can still do with it:
        /// EXCELLENT (3) near the passing target after rising well over the tape - every option, a hard spike;
        /// GOOD (2) elsewhere inside the 3 m line - fewer options, a normal spike;
        /// BAD (1) behind the 3 m line or tight to the net - only a high ball outside, a soft attack.
        /// (0 - an ace, a low pass into or under the net, an out - never gets here.)
        /// </summary>
        void JudgeStationPass(Vector3 position,MatchActor player)
        {
            float q;string why;
            if(_passClass>=3){q=1f;why="EXCELLENT PASS — RIGHT TO THE SETTER";}
            else if(_passClass==2){q=.6f;why=player==ActorAt(Team.Us,0) ? "GOOD PASS — THE SETTER HAD TO MOVE" : "GOOD PASS — ANOTHER PLAYER SETS";}
            else{q=.2f;why=_passTooLow ? "BAD PASS — TOO LOW TO SET: FREE BALL OVER" : "BAD PASS — OFF THE TARGET: FREE BALL OVER";}
            _stationQuality=q;ShowGrade(GradeWord(q));
            LastPassTaker=_passClass<2 ? PassTaker.None : player==ActorAt(Team.Us,0) ? PassTaker.Setter : PassTaker.Other;
            Cue=why;
        }

        /// <summary>
        /// Our attack ends the rep. TOSS: the set's quality decides the attack (hard spike, spike, or an off-balance roll
        /// shot) and how likely it kills; their blockers in front of the hitter cut that and may stuff it. RECEIVE: the
        /// pass's quality raises the kill chance. A kill is our point, a stuff block theirs; a dig carries the rally on
        /// (no point): they attack back and the next rep starts from that.
        /// </summary>
        void StationAttack(Vector3 hit)
        {
            float q=Mathf.Clamp01(_stationQuality);
            bool toss=Mode==RallyMode.Set;
            float speed, kill, clearance=.2f; // a GOOD pass's spike is flatter: it clears the tape higher
            string shot;
            if(toss)
            {
                // An EXCELLENT set is put away almost always; their block only matters on the others.
                if(q>=PerfectSetQuality){speed=NpcAttackSpeed+7f;kill=_setBlockers>1 ? .88f : .97f;shot="HARD SPIKE";}
                else
                {
                    if(q>=GoodSetQuality){speed=NpcAttackSpeed+2f;kill=.55f;shot="SPIKE";}
                    else{speed=7f;kill=.15f;shot="OFF BALANCE — ROLL SHOT";}
                    kill*=Mathf.Pow(BlockerKillFactor,_setBlockers);
                }
                if(_setBlockers>0)shot+=_setBlockers>1 ? " INTO A DOUBLE BLOCK" : " PAST ONE BLOCKER";
            }
            // RECEIVE: only an EXCELLENT pass is put away; a GOOD one is attacked but they (almost always) dig it and counter.
            else if(q>=PerfectSetQuality){speed=NpcAttackSpeed+ExcellentSpikeExtraSpeed;kill=StationKillMax;shot="HARD SPIKE";}
            else if(q>=GoodSetQuality){speed=NpcAttackSpeed+1f;kill=StationKillMin;shot="SPIKE";clearance=.7f;}
            else{speed=8f;kill=0f;shot="HIGH BALL — A SOFT ATTACK";}
            string touch=GradeWord(q)+(toss ? " SET" : " PASS");
            var them=Team.Them;
            bool killed=Random.value<kill;
            if(toss && !killed && q<PerfectSetQuality && _setBlockers>0 && Random.value<.35f*_setBlockers)
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
                // The spot in their court farthest from every one of their players; after an EXCELLENT pass a short one
                // (steeply down from the top of the jump: a hammered kill).
                bool hammer=!toss && q>=PerfectSetQuality;
                var depths=hammer ? ExcellentKillDepth : new Vector2(1.5f,8.2f);
                Vector3 best=TeamPoint(them,0f,(depths.x+depths.y)*.5f);float bestGap=-1f;
                for(int i=0;i<16;i++)
                {
                    var spot=TeamPoint(them,Random.Range(-3.6f,3.6f),Random.Range(depths.x,depths.y));
                    float gap=float.MaxValue;
                    foreach(var actor in _cast)if(actor.Team==them && actor.A.gameObject.activeSelf)gap=Mathf.Min(gap,Flat(spot-actor.A.transform.position).magnitude);
                    if(gap>bestGap){bestGap=gap;best=spot;}
                }
                best.y=FloorY;
                float seconds=Mathf.Max(Flat(best-hit).magnitude/speed,ClearNetSeconds(hit,best,clearance));
                StartSegment(hit,best,seconds,()=>{if(hammer)PlaySpikeAudio(best);Award(true,"KILL! — "+touch,best);});
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
            if(digger==null){StartSegment(hit,aim,.6f,()=>Award(true,"KILL! — "+touch,aim));return;}
            var dig=digger.A.transform.position-TeamForward(them)*.3f;dig.y=FloorY+DigHeight;
            float flight=Mathf.Max(Flat(dig-hit).magnitude/speed,ClearNetSeconds(hit,dig,clearance));
            // Dug: no point. RECEIVE flows on (they pass, set and attack the player); TOSS just ends the rep there.
            if(toss){StartSegment(hit,dig,flight,()=>{digger.Anim=Anim.Dig;digger.Clock=0f;PlaySpikeAudio(dig);SetOutcome("DUG — "+touch,true);});return;}
            StartSegment(hit,dig,flight,()=>{Cue="DUG — THEY COUNTER";NpcTouch(them,digger,dig);});
            Cue=shot;
        }

        /// <summary>A physical ball to the player's forearms (serve or attack), a little different each time.</summary>
        /// <summary>RECEIVE tutorial: the serves alternate between two fixed spots, straight to the forearms, to a fixed setter.</summary>
        public bool PracticeServes { get=>_practiceServes; set{_practiceServes=value;_practiceServe=0;} }
        [System.NonSerialized] bool _practiceServes;
        int _practiceServe;
        void LaunchToForearms(Vector3 from,float minimumSeconds,float speed,float heightOffset=0f,bool exact=false)
        {
            RequestLaunch(()=>{
                // Spread so every ball is a little different, never far enough to make the player step.
                var spread=exact ? Vector3.zero : ReceiveSpread;
                var to=GroundedEye+Forward*(.45f+Random.Range(-spread.y,spread.y))+Right*Random.Range(-spread.x,spread.x);
                to.y=FloorY+ReceiveHeight+heightOffset+Random.Range(-spread.z,spread.z);
                float seconds=speed>0f ? Flat(to-from).magnitude/speed : 0f;
                return (from,to,Mathf.Max(Mathf.Max(minimumSeconds,seconds),ClearNetSeconds(from,to,.3f)));
            },RallyPhase.PlayerReceive,PlayerTouch.Receive);
        }
    }
}
