using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.SceneManagement;

namespace GloveBallDemo.Runtime
{
    /// <summary>Volley-only paused menu. Meta hand menu gesture opens it; aim rays and pinch edges select rows.</summary>
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

        void Start(){if(_canvas==null)Build();}
        void Build()
        {
            _font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go=new GameObject("Volley hand menu",typeof(RectTransform),typeof(Canvas));go.transform.SetParent(transform,false);
            _canvas=go.GetComponent<Canvas>();_canvas.renderMode=RenderMode.WorldSpace;_canvas.sortingOrder=100;
            int rows=Drill.Drill==VolleyDrill.Block?10:8;
            float height=250+rows*72, top=height*.5f;
            var rect=go.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(620,height);rect.localScale=Vector3.one*.0016f;
            go.AddComponent<Image>().color=new Color(.025f,.04f,.065f,.97f);
            Label("VOLLEY MENU",rect,new Vector2(0,top-50),34);
            _heightText=Label("",rect,new Vector2(0,top-105),23);
            for(int i=0;i<rows;i++)
            {
                var row=new GameObject("Menu row "+i,typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();
                row.SetParent(rect,false);row.sizeDelta=new Vector2(555,60);row.anchoredPosition=new Vector2(0,top-190-i*72);
                row.GetComponent<Image>().color=new Color(.1f,.18f,.24f);
                _rows.Add(row);_labels.Add(Label("",row,Vector2.zero,27));
            }
            _rayMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));_rayMaterial.SetColor("_BaseColor",Color.white);
            for(int i=0;i<2;i++)
            {
                var ray=new GameObject(i==0?"Left menu ray":"Right menu ray").AddComponent<LineRenderer>();ray.transform.SetParent(transform,false);
                ray.sharedMaterial=_rayMaterial;ray.positionCount=2;ray.startWidth=.003f;ray.endWidth=.0015f;ray.enabled=false;_rays[i]=ray;
            }
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
            ProcessMenuGesture(Time.unscaledTime,pressed,fallback);
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
            foreach(var row in _rows)row.GetComponent<Image>().color=new Color(.1f,.18f,.24f);
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
            if(hit>=0)_rows[hit].GetComponent<Image>().color=new Color(.12f,.48f,.6f);
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
            if(_canvas==null)Build();if(IsOpen==open)return;IsOpen=open;
            foreach(var smoother in _handRays)smoother.Reset();
            if(open){var jump=Floor.GetComponent<VolleyArmJump>();if(jump!=null && !jump.AutomaticJump)jump.ResetJump();var forward=Vector3.ProjectOnPlane(Drill.Head.forward,Vector3.up).normalized;
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
                case 5:ResetAutomaticAttempt();Floor.Recenter();SetOpen(false);break;
                case 6:LoadDrill("VolleyReceive-codex");break;
                case 7:LoadDrill("VolleyBlock-codex");break;
                case 8:if(Drill.Aerial!=null)Drill.Aerial.SetBeginnerBlock(!Drill.Aerial.BeginnerBlock);break;
                case 9:if(Drill.Aerial!=null)Drill.Aerial.SetMode((VolleyAerialSequence.RallyMode)(((int)Drill.Aerial.Mode+1)%3));break;
            }
            Refresh();
        }
        void Refresh()
        {
            if(_heightText==null)return;_heightText.text=$"Eye {Floor.EyeHeight:F2} m  /  correction {Floor.HeightCorrection:+0.00;-0.00;0.00} m\n"+(GloveBallWideMotionFeature.Active?.Status??"WMM: unavailable (Quest APK required)");
            var names=new List<string>{"RESUME",$"HANDS ONLY: {(Drill.Left.InputMode==VolleyInputMode.HandsOnly?"ON":"OFF")}",$"STAND UPRIGHT: SET EYE {Floor.StandingEyeHeight:F2} m","USE RUNTIME FLOOR","RESTART","REPOSITION TO START","RECEIVE DEMO","SPIKE + BLOCK DEMO"};
            if(Drill.Drill==VolleyDrill.Block)
            {
                names.Add(Drill.Aerial.BeginnerBlock?"JUMP: BEGINNER (AUTO JUMP)":"JUMP: ADVANCED (HAND JUMP)");
                var mode=Drill.Aerial.Mode;
                names.Add("TURNS: "+(mode==VolleyAerialSequence.RallyMode.Alternate?"ALTERNATE SPIKE / BLOCK":mode==VolleyAerialSequence.RallyMode.SpikeOnly?"SPIKE ONLY":"BLOCK ONLY"));
            }
            for(int i=0;i<names.Count;i++)_labels[i].text=names[i];
        }
        void ResetAutomaticAttempt(){if(Drill.Aerial!=null && Drill.Aerial.BeginnerBlock)Drill.Aerial.SetBeginnerBlock(true);}
        void OnDisable(){if(IsOpen)SetOpen(false);}
        public bool CanExecuteControl(string action,string sceneId) =>
            action=="menu_open" || action=="menu_close" || action=="recenter" || action=="restart"
            || (action=="scene" && SceneName(sceneId)!=null);
        static string SceneName(string id) => id=="receive" ? "VolleyReceive-codex"
            : id=="block" ? "VolleyBlock-codex" : null;
        public System.Collections.IEnumerator ExecuteControl(string action,string sceneId)
        {
            if(!CanExecuteControl(action,sceneId))throw new System.InvalidOperationException("Unsupported control.");
            if(action=="menu_open"){SetOpen(true);yield break;}
            if(action=="menu_close"){SetOpen(false);yield break;}
            if(action=="recenter"){ResetAutomaticAttempt();Floor.Recenter();SetOpen(false);yield break;}
            string scene=action=="restart" ? SceneManager.GetActiveScene().name : SceneName(sceneId);
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
