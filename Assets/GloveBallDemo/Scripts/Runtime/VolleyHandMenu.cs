using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.SceneManagement;

namespace GloveBallDemo.Runtime
{
    /// <summary>Volley-only paused menu. Meta hand menu gesture opens it; aim rays and pinch edges select rows.</summary>
    public sealed class VolleyHandMenu : MonoBehaviour
    {
        public VolleyDrillController Drill;
        public VolleyFloorTracking Floor;
        public bool IsOpen { get; private set; }
        readonly List<XRHandSubsystem> _hands=new List<XRHandSubsystem>();
        readonly List<RectTransform> _rows=new List<RectTransform>();
        readonly List<Text> _labels=new List<Text>();
        readonly bool[] _pinched=new bool[2];
        readonly LineRenderer[] _rays=new LineRenderer[2];
        Canvas _canvas; Text _heightText; Material _rayMaterial; Font _font;
        float _savedTimeScale, _lastToggle=-10f, _gestureStart=-1f;
        bool _menuPressed, _gestureUsed;

        void Start(){if(_canvas==null)Build();}
        void Build()
        {
            _font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go=new GameObject("Volley hand menu",typeof(RectTransform),typeof(Canvas));go.transform.SetParent(transform,false);
            _canvas=go.GetComponent<Canvas>();_canvas.renderMode=RenderMode.WorldSpace;_canvas.sortingOrder=100;
            var rect=go.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(620,570);rect.localScale=Vector3.one*.0016f;
            go.AddComponent<Image>().color=new Color(.025f,.04f,.065f,.97f);
            Label("VOLLEY MENU",rect,new Vector2(0,235),34);
            _heightText=Label("",rect,new Vector2(0,185),23);
            for(int i=0;i<5;i++)
            {
                var row=new GameObject("Menu row "+i,typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();
                row.SetParent(rect,false);row.sizeDelta=new Vector2(555,64);row.anchoredPosition=new Vector2(0,110-i*77);
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
            if(fallback){if(_gestureStart<0)_gestureStart=Time.unscaledTime;}
            else{_gestureStart=-1;_gestureUsed=false;}
            bool held=fallback && !_gestureUsed && Time.unscaledTime-_gestureStart>.5f;
            if(((pressed&&!_menuPressed)||held) && Time.unscaledTime-_lastToggle>.5f)
            {SetOpen(!IsOpen);_lastToggle=Time.unscaledTime;_gestureUsed=true;}
            _menuPressed=pressed;
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
            if((flags&MetaAimFlags.SystemGesture)!=0 && MetaAimHand.left!=null)return MetaAimHand.left.indexPressed.isPressed;
            if(!TryJoints(0,out var wrist,out var index,out var thumb))return false;
            var space=Floor.Origin.CameraFloorOffsetObject.transform;
            var normal=space.rotation*wrist.rotation*Vector3.up;
            return Vector3.Distance(index.position,thumb.position)<.025f && Vector3.Dot(normal,(Drill.Head.position-space.TransformPoint(wrist.position)).normalized)>.6f;
        }
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
            {ray=new Ray(space.TransformPoint(aim.devicePosition.ReadValue()),space.rotation*aim.deviceRotation.ReadValue()*Vector3.forward);pinch=aim.pinchStrengthIndex.ReadValue()>(_pinched[side]?.55f:.8f);return true;}
            if(TryJoints(side,out var wrist,out var index,out var thumb))
            {var origin=space.TransformPoint(index.position);var shoulder=Drill.Head.position-Vector3.up*.25f+Drill.Head.right*(side==0?-.18f:.18f);ray=new Ray(origin,(origin-shoulder).normalized);pinch=Vector3.Distance(index.position,thumb.position)<(_pinched[side]?.04f:.025f);return true;}
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
            if(open){_savedTimeScale=Time.timeScale;Time.timeScale=0f;var forward=Vector3.ProjectOnPlane(Drill.Head.forward,Vector3.up).normalized;
                _canvas.transform.position=Drill.Head.position+forward*1.25f;_canvas.transform.rotation=Quaternion.LookRotation(forward);_pinched[0]=_pinched[1]=true;}
            else Time.timeScale=_savedTimeScale;
            _canvas.gameObject.SetActive(open);GameInputGate.SetBlocked(open);foreach(var ray in _rays)ray.enabled=false;
        }
        public void Activate(int row)
        {
            if(!IsOpen)return;
            switch(row)
            {
                case 0:SetOpen(false);break;
                case 1:var mode=Drill.Left.InputMode==VolleyInputMode.HandsOnly?VolleyInputMode.Automatic:VolleyInputMode.HandsOnly;Drill.Left.InputMode=Drill.Right.InputMode=mode;break;
                case 2:Floor.CalibrateStandingHeight();SetOpen(false);break;
                case 3:Floor.ClearCalibration();SetOpen(false);break;
                case 4:
                    SetOpen(false);
#if UNITY_EDITOR
                    UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(SceneManager.GetActiveScene().path,new LoadSceneParameters(LoadSceneMode.Single));
#else
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
#endif
                    break;
            }
            Refresh();
        }
        void Refresh()
        {
            if(_heightText==null)return;_heightText.text=$"Eye {Floor.EyeHeight:F2} m  /  correction {Floor.HeightCorrection:+0.00;-0.00;0.00} m";
            var names=new[]{"RESUME",$"HANDS ONLY: {(Drill.Left.InputMode==VolleyInputMode.HandsOnly?"ON":"OFF")}",$"STAND UPRIGHT: SET EYE {Floor.StandingEyeHeight:F2} m","USE RUNTIME FLOOR","RESTART"};
            for(int i=0;i<names.Length;i++)_labels[i].text=names[i];
        }
        void OnDisable(){if(IsOpen)SetOpen(false);}
        void OnDestroy()
        {
            Dispose(_rayMaterial);
            if(_canvas!=null)Dispose(_canvas.gameObject);
            foreach(var ray in _rays)if(ray!=null)Dispose(ray.gameObject);
        }
        static void Dispose(Object obj){if(obj==null)return;if(Application.isPlaying)Destroy(obj);else DestroyImmediate(obj);}
    }
}
