using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.SceneManagement;
using Hapbeat.DemoSwitch;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// Volley-only paused menu. Meta hand menu gesture opens it; aim rays and pinch edges select rows.
    /// With the shared pause (Demo Switch Pause Menu, on in the Volley APK) the shared input owns the same
    /// gesture and the ≡ button: this menu then opens and closes with the 一時停止 panel, beside it, and its
    /// own open / close (RESUME, row actions, CONTROL menu_open / menu_close) go through <see cref="DemoPause"/>.
    /// </summary>
    public sealed class VolleyHandMenu : MonoBehaviour, Hapbeat.DemoSwitch.IDemoAppControls
    {
        public VolleyDrillController Drill;
        public VolleyFloorTracking Floor;
        public bool IsOpen { get; private set; }
        readonly List<XRHandSubsystem> _hands=new List<XRHandSubsystem>();
        readonly List<RectTransform> _rows=new List<RectTransform>();
        readonly List<Text> _labels=new List<Text>();
        readonly bool[] _pinched=new bool[2];
        readonly LineRenderer[] _rays=new LineRenderer[2];
        readonly HandRaySmoother[] _handRays={new HandRaySmoother(),new HandRaySmoother()};

        // Like XRI's pinch visual, separate the pinch origin from the aiming pose and smooth both.
        // In the joint-only fallback, wrist position drives aim: curling the index cannot steer it.
        public sealed class HandRaySmoother
        {
            bool _valid; int _source; Vector3 _origin, _direction;
            public void Reset()=>_valid=false;
            public Ray Sample(Vector3 origin,Vector3 direction,float deltaTime,int source)
            {
                if(!_valid || _source!=source){_origin=origin;_direction=direction.normalized;_valid=true;_source=source;}
                else
                {
                    float alpha=1f-Mathf.Exp(-12f*Mathf.Max(0f,deltaTime));
                    _origin=Vector3.Lerp(_origin,origin,alpha);
                    _direction=Vector3.Slerp(_direction,direction.normalized,alpha).normalized;
                }
                return new Ray(_origin,_direction);
            }
        }
        public static Ray JointRay(Pose wrist,Pose index,Pose thumb,Vector3 shoulder)
            =>new Ray((index.position+thumb.position)*.5f,(wrist.position-shoulder).normalized);
        Canvas _canvas; Text _heightText; Material _rayMaterial; Font _font;
        float _lastToggle=-10f, _gestureStart=-1f;
        bool _menuPressed, _gestureUsed;
        /// <summary>Yaw right of the head's heading for this menu beside the shared pause panel (0.55 m ahead, about 0.44 m wide).</summary>
        public const float BesidePauseYaw=48f;
        /// <summary>Settings on the left (rows 0..6), the five ways to play on the right: picking one starts it at once.</summary>
        public const int AutoJumpRow=6, FirstModeRow=7, TutorialReceiveRow=12, TutorialSpikeBlockRow=13, RowCount=14;
        public static readonly VolleyAerialSequence.RallyMode[] MenuModes={VolleyAerialSequence.RallyMode.Pass,VolleyAerialSequence.RallyMode.Set,
            VolleyAerialSequence.RallyMode.SpikeOnly,VolleyAerialSequence.RallyMode.BlockOnly,VolleyAerialSequence.RallyMode.Alternate};
        static readonly string[] MenuModeNames={"RECEIVE\npass high to a front-row player","TOSS\nset the ball to a hitter","SPIKE","BLOCK","SPIKE + BLOCK\ntake turns"};
        static readonly Color RowColour=new Color(.1f,.18f,.24f), CurrentColour=new Color(.12f,.38f,.2f), HoverColour=new Color(.12f,.48f,.6f);
        static bool SharedPause=>DemoPause.IsEnabled;

        /// <summary>Rally mode a CONTROL scene change asks for; applied once the reloaded Block scene has started.</summary>
        static VolleyAerialSequence.RallyMode? s_pendingMode;

        void Start()
        {
            if(_canvas==null)Build();
            if(s_pendingMode.HasValue || s_pendingTutorial!=VolleyTutorial.Course.None)StartCoroutine(ApplyPendingMode());
            if(Application.isPlaying && !s_launchRepositioned)StartCoroutine(RepositionAtLaunch());
        }
        System.Collections.IEnumerator ApplyPendingMode()
        {
            yield return null; // let the drill and aerial sequence finish their own Start first
            var mode=s_pendingMode;s_pendingMode=null;
            if(mode.HasValue && Drill!=null && Drill.Drill==VolleyDrill.Block && Drill.Aerial!=null && Drill.Aerial.Mode!=mode.Value)
                Drill.Aerial.SetMode(mode.Value);
            var course=s_pendingTutorial;s_pendingTutorial=VolleyTutorial.Course.None;
            if(course!=VolleyTutorial.Course.None && Drill!=null && Drill.Drill==VolleyDrill.Block)Drill.GetComponent<VolleyTutorial>()?.Begin(course);
        }
        void OnEnable()=>DemoPause.PausedChanged+=OnSharedPauseChanged;
        void OnSharedPauseChanged(bool paused)=>Show(paused,true);
        void Build()
        {
            _font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go=new GameObject("Volley hand menu",typeof(RectTransform),typeof(Canvas));go.transform.SetParent(transform,false);
            _canvas=go.GetComponent<Canvas>();_canvas.renderMode=RenderMode.WorldSpace;_canvas.sortingOrder=100;
            // Two columns: settings (7 rows) left, the five modes (taller buttons) right.
            const float Width=1180f, Column=555f, Left=-292f, Right=292f;
            float height=250+(FirstModeRow)*72, top=height*.5f;
            var rect=go.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(Width,height);rect.localScale=Vector3.one*.0012f;
            go.AddComponent<Image>().color=new Color(.025f,.04f,.065f,.97f);
            Label("VOLLEY MENU",rect,new Vector2(0,top-50),34);
            _heightText=Label("",rect,new Vector2(Left,top-110),21);
            Label("PLAY  (starts at once)",rect,new Vector2(Right,top-110),26);
            float modeHeight=(FirstModeRow*72f-12f-(RowCount-FirstModeRow-1)*12f)/(RowCount-FirstModeRow);
            for(int i=0;i<RowCount;i++)
            {
                bool mode=i>=FirstModeRow;
                var row=new GameObject("Menu row "+i,typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();
                row.SetParent(rect,false);
                if(mode){int m=i-FirstModeRow;row.sizeDelta=new Vector2(Column,modeHeight);row.anchoredPosition=new Vector2(Right,top-160-modeHeight*.5f-m*(modeHeight+12f));}
                else{row.sizeDelta=new Vector2(Column,60);row.anchoredPosition=new Vector2(Left,top-190-i*72);}
                row.GetComponent<Image>().color=RowColour;
                var label=Label("",row,Vector2.zero,mode ? 28 : 25);label.rectTransform.sizeDelta=row.sizeDelta;
                _rows.Add(row);_labels.Add(label);
            }
            _rayMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));_rayMaterial.SetColor("_BaseColor",Color.white);
            for(int i=0;i<2;i++)
            {
                var ray=new GameObject(i==0?"Left menu ray":"Right menu ray").AddComponent<LineRenderer>();ray.transform.SetParent(transform,false);
                ray.sharedMaterial=_rayMaterial;ray.positionCount=2;ray.startWidth=.003f;ray.endWidth=.0015f;ray.enabled=false;_rays[i]=ray;
            }
            // Always in front: the net and the players must never hide the menu (no depth test, drawn last).
            var onTop=new Material(Shader.Find("UI/Default")){name="Volley menu on top"};
            onTop.SetInt("unity_GUIZTestMode",(int)UnityEngine.Rendering.CompareFunction.Always);
            onTop.renderQueue=(int)UnityEngine.Rendering.RenderQueue.Overlay;
            foreach(var graphic in go.GetComponentsInChildren<Graphic>(true))graphic.material=onTop;
            _canvas.gameObject.SetActive(false);Refresh();
        }
        Text Label(string value,RectTransform parent,Vector2 position,int size)
        {
            var t=new GameObject("Label",typeof(RectTransform),typeof(Text)).GetComponent<Text>();t.transform.SetParent(parent,false);
            t.rectTransform.sizeDelta=new Vector2(555,62);t.rectTransform.anchoredPosition=position;
            t.font=_font;t.fontSize=size;t.alignment=TextAnchor.MiddleCenter;t.text=value;t.color=Color.white;t.raycastTarget=false;return t;
        }
        void Update()
        {
            var left=MetaAimHand.left;
            var flags=left==null?MetaAimFlags.None:(MetaAimFlags)left.aimFlags.ReadValue();
            var controller=InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            controller.TryGetFeatureValue(CommonUsages.menuButton,out bool controllerMenu);
            bool pressed=(flags&MetaAimFlags.MenuPressed)!=0 || controllerMenu;
            bool fallback=LeftPalmPinch(flags);
            // The shared pause input reads the same gesture and button; a second toggle here would close it again.
            if(!SharedPause)ProcessMenuGesture(Time.unscaledTime,pressed,fallback);
            UpdatePointers();
        }
        public void ProcessMenuGesture(float now,bool pressed,bool fallback)
        {
            if(fallback){if(_gestureStart<0)_gestureStart=now;}
            else{_gestureStart=-1;_gestureUsed=false;}
            bool held=fallback && !_gestureUsed && now-_gestureStart>.5f;
            if(((pressed&&!_menuPressed)||held) && now-_lastToggle>.5f)
            {SetOpen(!IsOpen);_lastToggle=now;_gestureUsed=true;}
            _menuPressed=pressed;
        }
        void UpdatePointers()
        {
            if(!IsOpen)return;
            for(int i=0;i<_rows.Count;i++)_rows[i].GetComponent<Image>().color=BaseColour(i);
            for(int side=0;side<2;side++)
            {
                bool valid=TryRay(side,out var ray,out bool pinch);
                ProcessPointer(side,valid,ray,pinch);
                if(!IsOpen)break;
            }
            if(IsOpen)Refresh();
        }
        public void ProcessPointer(int side,bool valid,Ray ray,bool pinch)
        {
            if(!IsOpen)return;
            _rays[side].enabled=valid;
            if(!valid){_pinched[side]=true;return;}
            int hit=HitRow(ray,out var point);_rays[side].SetPosition(0,ray.origin);_rays[side].SetPosition(1,point);
            if(hit>=0)_rows[hit].GetComponent<Image>().color=HoverColour;
            if(pinch&&!_pinched[side] && Time.unscaledTime-_lastToggle>.35f && hit>=0)Activate(hit);
            _pinched[side]=pinch;
        }
        bool LeftPalmPinch(MetaAimFlags flags)
        {
            if((flags&MetaAimFlags.SystemGesture)!=0 && MetaAimHand.left!=null && MetaAimHand.left.indexPressed.isPressed)return true;
            if(!TryJoints(0,out var wrist,out var index,out var thumb))return false;
            var space=Floor.Origin.CameraFloorOffsetObject.transform;
            return IsPalmPinch(wrist,index,thumb,space.InverseTransformPoint(Drill.Head.position));
        }
        public static bool IsPalmPinch(Pose wrist,Pose index,Pose thumb,Vector3 headInTrackingSpace)
            // Same palm axis as XR Hands' XRHandOrientationUtility: local -Y, not the back-of-hand +Y.
            => Vector3.Distance(index.position,thumb.position)<.025f && Vector3.Dot(wrist.rotation*Vector3.down,(headInTrackingSpace-wrist.position).normalized)>.6f;
        bool TryJoints(int side,out Pose wrist,out Pose index,out Pose thumb)
        {
            wrist=index=thumb=default;SubsystemManager.GetSubsystems(_hands);
            foreach(var sub in _hands){if(!sub.running)continue;var h=side==0?sub.leftHand:sub.rightHand;
                if(h.isTracked && h.GetJoint(XRHandJointID.Wrist).TryGetPose(out wrist) && h.GetJoint(XRHandJointID.IndexTip).TryGetPose(out index) && h.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out thumb))return true;}
            return false;
        }
        bool TryRay(int side,out Ray ray,out bool pinch)
        {
            var aim=side==0?MetaAimHand.left:MetaAimHand.right;var space=Floor.Origin.CameraFloorOffsetObject.transform;
            if(aim!=null && (((MetaAimFlags)aim.aimFlags.ReadValue())&MetaAimFlags.Valid)!=0)
            {
                var origin=aim.devicePosition.ReadValue();
                if(TryJoints(side,out var w,out var i,out var t))origin=(i.position+t.position)*.5f;
                var local=_handRays[side].Sample(origin,aim.deviceRotation.ReadValue()*Vector3.forward,Time.unscaledDeltaTime,1);
                ray=new Ray(space.TransformPoint(local.origin),space.TransformDirection(local.direction));
                pinch=aim.pinchStrengthIndex.ReadValue()>(_pinched[side]?.55f:.8f);return true;
            }
            if(TryJoints(side,out var wrist,out var index,out var thumb))
            {
                var shoulder=Drill.Head.position-Vector3.up*.25f+Drill.Head.right*(side==0?-.18f:.18f);
                var raw=JointRay(wrist,index,thumb,space.InverseTransformPoint(shoulder));
                var local=_handRays[side].Sample(raw.origin,raw.direction,Time.unscaledDeltaTime,2);
                ray=new Ray(space.TransformPoint(local.origin),space.TransformDirection(local.direction));
                pinch=Vector3.Distance(index.position,thumb.position)<(_pinched[side]?.04f:.025f);return true;
            }
            _handRays[side].Reset();
            var device=InputDevices.GetDeviceAtXRNode(side==0?XRNode.LeftHand:XRNode.RightHand);
            if(device.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked)&&tracked && device.TryGetFeatureValue(CommonUsages.devicePosition,out Vector3 p)&&device.TryGetFeatureValue(CommonUsages.deviceRotation,out Quaternion q))
            {device.TryGetFeatureValue(CommonUsages.triggerButton,out pinch);ray=new Ray(space.TransformPoint(p),space.rotation*q*Vector3.forward);return true;}
            ray=default;pinch=false;return false;
        }
        int HitRow(Ray ray,out Vector3 point)
        {
            point=ray.GetPoint(2f);var plane=new Plane(_canvas.transform.forward,_canvas.transform.position);
            if(!plane.Raycast(ray,out var distance)||distance>4f)return -1;point=ray.GetPoint(distance);
            for(int i=0;i<_rows.Count;i++)if(_rows[i].rect.Contains(_rows[i].InverseTransformPoint(point)))return i;return -1;
        }
        public void SetOpen(bool open)
        {
            if(SharedPause)
            {
                // The shared pause owns pause, audio and haptics; PausedChanged shows or hides this menu beside it.
                if(open)DemoPause.Pause();else DemoPause.Resume();
                Show(open && DemoPause.IsPaused,true);
                return;
            }
            Show(open,false);
        }
        void Show(bool open,bool besidePause)
        {
            if(_canvas==null)Build();if(IsOpen==open)return;IsOpen=open;
            foreach(var smoother in _handRays)smoother.Reset();
            if(open){var jump=Floor.GetComponent<VolleyArmJump>();if(jump!=null && !jump.AutomaticJump)jump.ResetJump();var forward=Vector3.ProjectOnPlane(Drill.Head.forward,Vector3.up).normalized;
                if(besidePause)forward=Quaternion.AngleAxis(BesidePauseYaw,Vector3.up)*forward;
                _canvas.transform.position=Drill.Head.position+forward*1.25f;_canvas.transform.rotation=Quaternion.LookRotation(forward);_pinched[0]=_pinched[1]=true;}
            Drill.SetMenuPaused(open);
            _canvas.gameObject.SetActive(open);GameInputGate.SetBlocked(open);foreach(var ray in _rays)ray.enabled=false;
        }
        public void Activate(int row)
        {
            if(!IsOpen)return;
            switch(row)
            {
                case 0:SetOpen(false);break;
                case 1:var mode=Drill.Left.InputMode==VolleyInputMode.HandsOnly?VolleyInputMode.Automatic:VolleyInputMode.HandsOnly;Drill.Left.InputMode=Drill.Right.InputMode=mode;break;
                case 2:ResetAutomaticAttempt();Floor.CalibrateStandingHeight();SetOpen(false);break;
                case 3:ResetAutomaticAttempt();Floor.ClearCalibration();SetOpen(false);break;
                case 4:
                    SetOpen(false);
#if UNITY_EDITOR
                    UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(SceneManager.GetActiveScene().path,new LoadSceneParameters(LoadSceneMode.Single));
#else
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
#endif
                    break;
                case 5:RepositionToStart();break;
                case AutoJumpRow:if(Drill.Aerial!=null)Drill.Aerial.SetBeginnerBlock(!Drill.Aerial.BeginnerBlock);break;
                case TutorialSpikeBlockRow:StartTutorial(VolleyTutorial.Course.SpikeBlock);return;
                case TutorialReceiveRow:StartTutorial(VolleyTutorial.Course.Receive);return;
                default:if(row>=FirstModeRow && row<FirstModeRow+MenuModes.Length){Play(MenuModes[row-FirstModeRow]);return;}break;
            }
            Refresh();
        }
        /// <summary>Starts a mode right away: closes a shown completion panel and this menu (the Receive scene loads the Block scene first).</summary>
        public void Play(VolleyAerialSequence.RallyMode mode)
        {
            if(DemoSession.IsCompletionShown)DemoSession.DismissCompletion();
            var tutorial=Drill.GetComponent<VolleyTutorial>();
            if(tutorial!=null)tutorial.Stop(false);
            if(Drill.Drill==VolleyDrill.Block && Drill.Aerial!=null){Drill.Aerial.SetMode(mode);SetOpen(false);return;}
            s_pendingMode=mode;LoadDrill("VolleyBlock-codex");
        }
        /// <summary>Starts a tutorial from its first step (the Receive scene loads the Block scene first).</summary>
        public void StartTutorial(VolleyTutorial.Course course)
        {
            if(DemoSession.IsCompletionShown)DemoSession.DismissCompletion();
            var tutorial=Drill.GetComponent<VolleyTutorial>();
            if(Drill.Drill==VolleyDrill.Block && tutorial!=null){SetOpen(false);tutorial.Begin(course);return;}
            s_pendingTutorial=course;LoadDrill("VolleyBlock-codex");
        }
        static VolleyTutorial.Course s_pendingTutorial;
        bool IsCurrent(int row)=>row>=FirstModeRow && row<FirstModeRow+MenuModes.Length && Drill.Drill==VolleyDrill.Block && Drill.Aerial!=null
            && Drill.Aerial.Mode==MenuModes[row-FirstModeRow];
        Color BaseColour(int row)=>IsCurrent(row) ? CurrentColour : RowColour;
        void Refresh()
        {
            if(_heightText==null)return;_heightText.text=$"Eye {Floor.EyeHeight:F2} m  /  correction {Floor.HeightCorrection:+0.00;-0.00;0.00} m\n"+(GloveBallWideMotionFeature.Active?.Status??"WMM: unavailable (Quest APK required)");
            var names=new List<string>{"RESUME",$"HANDS ONLY: {(Drill.Left.InputMode==VolleyInputMode.HandsOnly?"ON":"OFF")}",$"STAND UPRIGHT: SET EYE {Floor.StandingEyeHeight:F2} m","USE RUNTIME FLOOR","RESTART","REPOSITION TO START",
                Drill.Aerial==null ? "AUTO JUMP: (SPIKE / BLOCK)" : Drill.Aerial.BeginnerBlock ? "AUTO JUMP: ON" : "AUTO JUMP: OFF (RAISE HANDS TO JUMP)"};
            for(int i=0;i<MenuModeNames.Length;i++)names.Add((IsCurrent(FirstModeRow+i) ? "NOW: " : "")+MenuModeNames[i]);
            // In the order of the modes above: RECEIVE first, then SPIKE + BLOCK.
            names.Add("TUTORIAL: RECEIVE\nlearn to pass to the setter");names.Add("TUTORIAL: SPIKE + BLOCK\nlearn to spike and block");
            for(int i=0;i<names.Count;i++)_labels[i].text=names[i];
        }
        void ResetAutomaticAttempt(){if(Drill.Aerial!=null && Drill.Aerial.BeginnerBlock)Drill.Aerial.SetBeginnerBlock(true);}
        /// <summary>REPOSITION TO START, also the shared 視線をリセット (VolleySessionHost) and once at the app's start.</summary>
        public void RepositionToStart(){ResetAutomaticAttempt();Floor.Recenter();Floor.CalibrateStandingHeight();SetOpen(false);Repositioned?.Invoke();}
        /// <summary>The stance and eye height were set again (panels placed for the player follow).</summary>
        public static event System.Action Repositioned;
        static bool s_launchRepositioned;
        [Tooltip("At the app's first start, 視線をリセット once the headset is worn and the hands have been tracked this long (s): the eye height is then set without a button.")]
        [Min(0f)] public float LaunchRepositionSeconds=1f;
        /// <summary>The app's first start: 視線をリセット from where the player stands, once the headset is on and tracking has settled.</summary>
        System.Collections.IEnumerator RepositionAtLaunch()
        {
            float settled=0f;
            while(settled<LaunchRepositionSeconds)
            {
                settled=Drill!=null && Drill.TrackingReady && HeadsetWorn() ? settled+Time.unscaledDeltaTime : 0f;
                yield return null;
            }
            if(IsOpen)yield break; // the player is already in the menu
            s_launchRepositioned=true;
            RepositionToStart();
        }
        /// <summary>The headset's proximity sensor says it is on a head (a runtime without the feature counts as worn).</summary>
        static bool HeadsetWorn()
        {
            var head=InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return !head.isValid || !head.TryGetFeatureValue(CommonUsages.userPresence,out bool present) || present;
        }
        void OnDisable(){DemoPause.PausedChanged-=OnSharedPauseChanged;if(IsOpen)SetOpen(false);}
        public bool CanExecuteControl(string action,string sceneId) =>
            action=="menu_open" || action=="menu_close" || action=="restart"
            || (action=="scene" && SceneName(sceneId)!=null);
        // Scene IDs match the descriptor's `scene` option (block / receive / match, VolleySessionHost.SceneMode), plus set
        // (TOSS); all are modes of the Block scene.
        static string SceneName(string id) => id=="block" || id=="receive" || id=="match" || id=="set" ? VolleySessionHost.BlockScene : null;
        static VolleyAerialSequence.RallyMode? SceneMode(string id) => SceneName(id)==null ? (VolleyAerialSequence.RallyMode?)null
            : id=="set" ? VolleyAerialSequence.RallyMode.Set : VolleySessionHost.SceneMode(id);
        public System.Collections.IEnumerator ExecuteControl(string action,string sceneId)
        {
            if(!CanExecuteControl(action,sceneId))throw new System.InvalidOperationException("Unsupported control.");
            if(action=="menu_open"){SetOpen(true);yield break;}
            if(action=="menu_close"){SetOpen(false);yield break;}
            string scene=action=="restart" ? SceneManager.GetActiveScene().name : SceneName(sceneId);
            if(action=="scene")s_pendingMode=SceneMode(sceneId);
            LoadDrill(scene);
            yield return null;
            if(SceneManager.GetActiveScene().name!=scene)throw new System.InvalidOperationException("Scene transition did not complete.");
        }
        public void LoadDrill(string scene)
        {
            if(scene!="VolleyReceive-codex" && scene!="VolleyBlock-codex")return;
            SetOpen(false);
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/GloveBallDemo/Scenes/"+scene+".unity",new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(scene);
#endif
        }
        void OnDestroy()
        {
            Dispose(_rayMaterial);
            if(_canvas!=null)Dispose(_canvas.gameObject);
            foreach(var ray in _rays)if(ray!=null)Dispose(ray.gameObject);
        }
        static void Dispose(Object obj){if(obj==null)return;if(Application.isPlaying)Destroy(obj);else DestroyImmediate(obj);}
    }
}
