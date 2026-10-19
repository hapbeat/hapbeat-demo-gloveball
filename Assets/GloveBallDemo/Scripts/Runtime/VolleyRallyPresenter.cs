using UnityEngine;
using UnityEngine.UI;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// Runtime-only rally presentation: head-locked blink, head-locked comfort vignette (match glides), court banner
    /// (turn, match score, outcome), landing marker and chimes.
    /// </summary>
    public sealed class VolleyRallyPresenter : MonoBehaviour
    {
        public VolleyAerialSequence Rally;
        [Tooltip("Banner centre from the net: forward (into the opponent court) and height.")]
        public float BannerForward=3f;
        public float BannerHeight=4.3f;
        [Range(0f,1f)] public float ChimeVolume=.6f;
        [Min(.2f)] public float OutcomeSeconds=2f;
        [Min(.2f)] public float MarkerSeconds=2.5f;
        [Tooltip("Comfort vignette: clear field half-angle (deg) and the width of its soft edge (deg).")]
        [Range(10f,60f)] public float VignetteClearDegrees=24f;
        [Range(1f,30f)] public float VignetteEdgeDegrees=10f;
        [Header("Match ambience")]
        [Tooltip("A volleyball match's hall sound under the play (looped, 2D). Empty: none.")]
        public AudioClip Ambience;
        [Tooltip("Quiet enough that voices and the ball's sounds are never buried.")]
        [Range(0f,1f)] public float AmbienceVolume=.3f;
        [Header("Tips after a rep that did not go well")]
        [Min(.5f)] public float TipSeconds=2.8f;
        static readonly Color SpikeColour=new Color(.1f,.35f,.9f,.88f);
        static readonly Color BlockColour=new Color(.85f,.2f,.1f,.88f);
        static readonly Color WinColour=new Color(.35f,1f,.45f);
        static readonly Color LoseColour=new Color(1f,.55f,.3f);
        Image _fade, _vignette, _banner;
        Text _grade;
        Texture2D _triangleTexture;
        Text _tip; float _tipAge=-1f;
        AudioSource _ambience;
        Transform _setterLabel;
        float _gradeAge=-1f;
        int _gradeSerial;
        Texture2D _vignetteTexture;
        Text _title, _score, _subtitle;
        AudioSource _audio;
        AudioClip _spikeChime, _blockChime, _pointWon, _pointLost, _matchWon, _matchLost;
        Transform _marker; Material _markerMaterial;
        float _pulse=-1f, _outcomeAge=-1f, _markerAge=-1f;
        int _outcomeSerial;
        Font _font;

        void Start()
        {
            if(Rally==null)Rally=GetComponent<VolleyAerialSequence>();
            _font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildFade();BuildVignette();BuildGrade();BuildTip();BuildSetterLabel();BuildBanner();BuildMarker();BuildAmbience();
            _gradeSerial=Rally.GradeSerial;
            _audio=gameObject.AddComponent<AudioSource>();_audio.playOnAwake=false;_audio.spatialBlend=0f;
            _spikeChime=Chime("Spike turn chime",new[]{523.25f,659.25f,783.99f},.14f);
            _blockChime=Chime("Block turn chime",new[]{783.99f,523.25f},.14f);
            _pointWon=Chime("Point won",new[]{880f,1174.66f},.09f);
            _pointLost=Chime("Point lost",new[]{246.94f,196f},.12f);
            _matchWon=Chime("Match won",new[]{523.25f,659.25f,783.99f,1046.5f},.16f);
            _matchLost=Chime("Match lost",new[]{392f,329.63f,261.63f},.2f);
            Rally.TurnAnnounced+=OnTurn;Rally.PointScored+=OnPoint;Rally.MatchEnded+=OnMatch;Rally.TurnRepResolved+=OnRep;
            _outcomeSerial=Rally.OutcomeSerial;
            Refresh();
        }
        void OnDestroy()
        {
            if(Rally!=null){Rally.TurnAnnounced-=OnTurn;Rally.PointScored-=OnPoint;Rally.MatchEnded-=OnMatch;Rally.TurnRepResolved-=OnRep;}
            foreach(var clip in new[]{_spikeChime,_blockChime,_pointWon,_pointLost,_matchWon,_matchLost})if(clip!=null)Destroy(clip);
            if(_markerMaterial!=null)Destroy(_markerMaterial);
            if(_vignetteTexture!=null)Destroy(_vignetteTexture);
            if(_triangleTexture!=null)Destroy(_triangleTexture);
        }

        void BuildFade()
        {
            var go=new GameObject("Rally blink",typeof(RectTransform),typeof(Canvas));
            go.transform.SetParent(Rally.Drill.Head,false);
            go.transform.localPosition=new Vector3(0f,0f,.3f);go.transform.localRotation=Quaternion.identity;
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.sortingOrder=1000;
            var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(3000,3000);rect.localScale=Vector3.one*.0005f;
            _fade=go.AddComponent<Image>();_fade.color=new Color(0f,0f,0f,0f);_fade.raycastTarget=false;
        }
        /// <summary>Black outside a soft-edged circle around the gaze, on a 1.5 m quad 0.3 m in front of the eyes.</summary>
        void BuildVignette()
        {
            const float Distance=.3f,Half=.75f;const int Size=256;
            var go=new GameObject("Rally comfort vignette",typeof(RectTransform),typeof(Canvas));
            go.transform.SetParent(Rally.Drill.Head,false);
            go.transform.localPosition=new Vector3(0f,0f,Distance);go.transform.localRotation=Quaternion.identity;
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.sortingOrder=999;
            var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(3000,3000);rect.localScale=Vector3.one*.0005f;
            float clear=Distance*Mathf.Tan(VignetteClearDegrees*Mathf.Deg2Rad)/Half;
            float edge=Distance*Mathf.Tan((VignetteClearDegrees+VignetteEdgeDegrees)*Mathf.Deg2Rad)/Half;
            _vignetteTexture=new Texture2D(Size,Size,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp,name="Rally vignette"};
            var pixels=new Color32[Size*Size];
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
            {
                float u=(x+.5f)/Size*2f-1f,v=(y+.5f)/Size*2f-1f;
                float a=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(clear,edge,Mathf.Sqrt(u*u+v*v)));
                pixels[y*Size+x]=new Color32(0,0,0,(byte)(a*255f));
            }
            _vignetteTexture.SetPixels32(pixels);_vignetteTexture.Apply(false,true);
            _vignette=go.AddComponent<Image>();_vignette.raycastTarget=false;
            _vignette.sprite=Sprite.Create(_vignetteTexture,new Rect(0,0,Size,Size),new Vector2(.5f,.5f));
            _vignette.color=new Color(1f,1f,1f,0f);_vignette.enabled=false;
        }
        /// <summary>EXCELLENT / GOOD / BAD of a set or pass: small, head-locked, a little above the centre of view.</summary>
        void BuildGrade()
        {
            var go=new GameObject("Rally grade",typeof(RectTransform),typeof(Canvas));
            go.transform.SetParent(Rally.Drill.Head,false);
            go.transform.localPosition=new Vector3(0f,.07f,.7f);go.transform.localRotation=Quaternion.identity;
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.sortingOrder=998;
            var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(600,120);rect.localScale=Vector3.one*.0006f;
            _grade=new GameObject("Grade",typeof(RectTransform),typeof(Text)).GetComponent<Text>();
            _grade.transform.SetParent(rect,false);_grade.rectTransform.sizeDelta=rect.sizeDelta;
            _grade.font=_font;_grade.fontSize=64;_grade.alignment=TextAnchor.MiddleCenter;_grade.raycastTarget=false;
            _grade.enabled=false;
        }
        /// <summary>A short tip under the centre of view after a rep that did not go well (Japanese).</summary>
        void BuildTip()
        {
            var go=new GameObject("Rally tip",typeof(RectTransform),typeof(Canvas));
            go.transform.SetParent(Rally.Drill.Head,false);
            go.transform.localPosition=new Vector3(0f,-.11f,.7f);go.transform.localRotation=Quaternion.identity;
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.sortingOrder=998;
            var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(900,90);rect.localScale=Vector3.one*.0005f;
            var font=Resources.Load<Font>("HapbeatDemoSession/NotoSansCJKjp-Regular");
            _tip=new GameObject("Tip",typeof(RectTransform),typeof(Text)).GetComponent<Text>();
            _tip.transform.SetParent(rect,false);_tip.rectTransform.sizeDelta=rect.sizeDelta;
            _tip.font=font!=null ? font : _font;_tip.fontSize=44;_tip.alignment=TextAnchor.MiddleCenter;_tip.raycastTarget=false;
            _tip.color=new Color(1f,.85f,.3f);_tip.enabled=false;
        }
        /// <summary>
        /// SPIKE / BLOCK, outside the tutorial (it says its own): after a lost rep or a weak spike, what to change - the
        /// jump's timing with AUTO JUMP off, else where to meet the ball.
        /// </summary>
        public static string TipFor(VolleyAerialSequence rally,bool block,bool success)
        {
            bool weakSpike=!block && success && !rally.LastSpikeStrong;
            if(success && !weakSpike)return null;
            if(!rally.BeginnerBlock)
                switch(rally.LastJumpTiming)
                {
                    case VolleyAerialSequence.JumpTiming.Early:return "ジャンプが早い：もう少し引きつけて";
                    case VolleyAerialSequence.JumpTiming.Late:return "ジャンプが遅い：もう少し早く跳ぼう";
                    case VolleyAerialSequence.JumpTiming.NoJump:return "両手を振り上げてジャンプしよう";
                }
            return block ? "打つ相手の正面に、両手を前へ出そう" : "いちばん高いところで、上から下へたたこう";
        }
        void OnRep(bool block,bool success)
        {
            var tutorial=Rally.Drill!=null ? Rally.Drill.GetComponent<VolleyTutorial>() : null;
            if(tutorial!=null && tutorial.Running!=VolleyTutorial.Course.None)return;
            var tip=TipFor(Rally,block,success);
            if(tip==null || _tip==null)return;
            _tip.text=tip;_tipAge=0f;
        }
        /// <summary>The match's hall sound, looped quietly (the shared pause holds it with all audio).</summary>
        void BuildAmbience()
        {
            if(Ambience==null)return;
            _ambience=gameObject.AddComponent<AudioSource>();
            _ambience.clip=Ambience;_ambience.loop=true;_ambience.spatialBlend=0f;_ambience.volume=AmbienceVolume;_ambience.playOnAwake=false;
            if(Application.isPlaying)_ambience.Play();
        }
        /// <summary>A white triangle pointing to +x (the setter label's arrow, turned down).</summary>
        Sprite Triangle()
        {
            const int Size=64;
            _triangleTexture=new Texture2D(Size,Size,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp,name="Rally triangle"};
            var pixels=new Color32[Size*Size];
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
            {
                // The tip at the right edge, the base at the left.
                float u=(x+.5f)/Size,v=Mathf.Abs((y+.5f)/Size-.5f)*2f;
                float a=Mathf.Clamp01((1f-u-v)*Size*.5f);
                pixels[y*Size+x]=new Color32(255,255,255,(byte)(a*255f));
            }
            _triangleTexture.SetPixels32(pixels);_triangleTexture.Apply(false,true);
            return Sprite.Create(_triangleTexture,new Rect(0,0,Size,Size),new Vector2(.5f,.5f));
        }
        /// <summary>RECEIVE: "SETTER" and a down arrow over our setter's head, turned to the player.</summary>
        void BuildSetterLabel()
        {
            var go=new GameObject("Rally setter label",typeof(RectTransform),typeof(Canvas));
            go.transform.SetParent(transform,false);
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
            var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(400,200);rect.localScale=Vector3.one*.0035f;
            var text=new GameObject("Text",typeof(RectTransform),typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(rect,false);text.rectTransform.sizeDelta=new Vector2(400,90);text.rectTransform.anchoredPosition=new Vector2(0f,45f);
            text.font=_font;text.fontSize=72;text.fontStyle=FontStyle.Bold;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;
            text.text="SETTER";text.color=new Color(.35f,1f,.45f);
            var arrow=new GameObject("Arrow",typeof(RectTransform),typeof(Image)).GetComponent<Image>();
            arrow.transform.SetParent(rect,false);arrow.rectTransform.sizeDelta=new Vector2(70,70);arrow.rectTransform.anchoredPosition=new Vector2(0f,-35f);
            arrow.rectTransform.localRotation=Quaternion.Euler(0f,0f,-90f);
            arrow.sprite=Triangle();
            arrow.color=new Color(.35f,1f,.45f);arrow.raycastTarget=false;
            _setterLabel=go.transform;go.SetActive(false);
        }
        void UpdateSetterLabel()
        {
            if(_setterLabel==null)return;
            var head=Rally.Drill.Head;
            var point=Vector3.zero;
            bool show=head!=null && !GameInputGate.IsBlocked && Rally.TryGetSetterMarker(out point);
            if(show)
            {
                // A little bob, and always facing the player (upright).
                point.y+=.05f*Mathf.Sin(Time.time*Mathf.PI*1.6f);
                var toward=Vector3.ProjectOnPlane(point-head.position,Vector3.up);
                _setterLabel.SetPositionAndRotation(point,toward.sqrMagnitude>1e-4f ? Quaternion.LookRotation(toward,Vector3.up) : Quaternion.identity);
            }
            if(_setterLabel.gameObject.activeSelf!=show)_setterLabel.gameObject.SetActive(show);
        }
        void BuildBanner()
        {
            var go=new GameObject("Rally turn banner",typeof(RectTransform),typeof(Canvas));
            go.transform.SetParent(transform,false);
            var forward=Vector3.ProjectOnPlane(Rally.Drill.CourtFrame.forward,Vector3.up).normalized;
            var net=Rally.Drill.ReceiveNet!=null ? Rally.Drill.ReceiveNet.transform.position : Rally.Drill.CourtFrame.position;
            net.y=Rally.Drill.CourtFrame.position.y;
            go.transform.SetPositionAndRotation(net+forward*BannerForward+Vector3.up*BannerHeight,Quaternion.LookRotation(forward,Vector3.up));
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.sortingOrder=50;
            var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(760,320);rect.localScale=Vector3.one*.0035f;
            _banner=go.AddComponent<Image>();_banner.raycastTarget=false;
            _title=Label(rect,new Vector2(0,100),70);
            _score=Label(rect,new Vector2(0,5),92);
            _subtitle=Label(rect,new Vector2(0,-100),46);
        }
        void BuildMarker()
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);go.name="Rally landing marker";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform,false);go.transform.localScale=new Vector3(.55f,.005f,.55f);
            var shader=Shader.Find("Universal Render Pipeline/Unlit");
            _markerMaterial=new Material(shader);go.GetComponent<Renderer>().sharedMaterial=_markerMaterial;
            _marker=go.transform;go.SetActive(false);
        }
        Text Label(RectTransform parent,Vector2 position,int size)
        {
            var text=new GameObject("Label",typeof(RectTransform),typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent,false);
            text.rectTransform.sizeDelta=new Vector2(740,110);text.rectTransform.anchoredPosition=position;
            text.font=_font;text.fontSize=size;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.raycastTarget=false;
            return text;
        }

        void Play(AudioClip clip){if(Application.isPlaying && _audio!=null && clip!=null)_audio.PlayOneShot(clip,ChimeVolume);}
        void OnTurn(VolleyAerialSequence.RallyTurn turn)
        {
            _pulse=0f;_outcomeAge=-1f;Refresh();
            Play(turn==VolleyAerialSequence.RallyTurn.Spike ? _spikeChime : _blockChime);
        }
        void OnPoint(bool player,Vector3 position)
        {
            Play(player ? _pointWon : _pointLost);
            if(_marker==null)return;
            // Where the ball ended, coloured by who won the point.
            var floor=Rally.Drill.CourtFrame.position.y;
            _marker.position=new Vector3(position.x,floor+.012f,position.z);
            _markerMaterial.SetColor("_BaseColor",player ? WinColour : LoseColour);
            _marker.gameObject.SetActive(true);_markerAge=0f;
        }
        void OnMatch(bool won){_pulse=0f;Play(won ? _matchWon : _matchLost);}

        void LateUpdate()
        {
            if(Rally==null)return;
            if(_fade!=null)
            {
                // The menu sits behind this head-locked blink; never cover it while the menu is open.
                float alpha=GameInputGate.IsBlocked ? 0f : Rally.FadeAlpha;
                _fade.color=new Color(0f,0f,0f,alpha);
                _fade.enabled=alpha>.001f;
            }
            if(_vignette!=null)
            {
                float alpha=GameInputGate.IsBlocked ? 0f : Rally.VignetteAlpha;
                _vignette.color=new Color(1f,1f,1f,alpha);
                _vignette.enabled=alpha>.001f;
            }
            if(_grade!=null)
            {
                if(Rally.GradeSerial!=_gradeSerial){_gradeSerial=Rally.GradeSerial;_gradeAge=0f;_grade.text=Rally.LastGrade;}
                if(_gradeAge>=0f)
                {
                    _gradeAge+=Time.deltaTime;
                    var colour=Rally.LastGrade=="EXCELLENT" ? new Color(.35f,1f,.45f) : Rally.LastGrade=="GOOD" ? new Color(1f,.88f,.3f) : new Color(1f,.45f,.35f);
                    colour.a=Mathf.Clamp01(1.6f-_gradeAge); // shown about a second, then fades
                    _grade.color=colour;
                    if(_gradeAge>1.6f)_gradeAge=-1f;
                }
                _grade.enabled=_gradeAge>=0f && !GameInputGate.IsBlocked;
            }
            UpdateSetterLabel();
            if(_tip!=null)
            {
                if(_tipAge>=0f){_tipAge+=Time.deltaTime;if(_tipAge>TipSeconds)_tipAge=-1f;}
                _tip.enabled=_tipAge>=0f && !GameInputGate.IsBlocked;
            }
            if(_ambience!=null)_ambience.volume=AmbienceVolume;
            if(Rally.OutcomeSerial!=_outcomeSerial){_outcomeSerial=Rally.OutcomeSerial;_outcomeAge=0f;}
            float dt=Time.deltaTime;
            if(_outcomeAge>=0f){_outcomeAge+=dt;if(_outcomeAge>OutcomeSeconds)_outcomeAge=-1f;}
            if(_pulse>=0f){_pulse+=dt;if(_pulse>.6f)_pulse=-1f;}
            if(_markerAge>=0f)
            {
                _markerAge+=dt;
                float grow=1f+.5f*Mathf.Clamp01(_markerAge/.3f);
                _marker.localScale=new Vector3(.55f*grow,.005f,.55f*grow);
                if(_markerAge>MarkerSeconds){_marker.gameObject.SetActive(false);_markerAge=-1f;}
            }
            Refresh();
        }

        void Refresh()
        {
            if(_banner==null)return;
            bool visible=Rally.TurnStarted || Rally.Phase==VolleyAerialSequence.RallyPhase.MatchOver;
            _banner.enabled=_title.enabled=_score.enabled=_subtitle.enabled=visible;
            if(!visible)return;
            _score.text=$"YOU {Rally.PlayerScore}  -  {Rally.OpponentScore} OPP";
            if(Rally.Phase==VolleyAerialSequence.RallyPhase.MatchOver)
            {
                _banner.color=Rally.PlayerWonMatch ? new Color(.1f,.55f,.25f,.9f) : new Color(.35f,.35f,.38f,.9f);
                _title.text=Rally.PlayerWonMatch ? "YOU WIN!" : "YOU LOSE";
                _subtitle.color=Color.white;_subtitle.text="NEW MATCH SOON";
            }
            else
            {
                bool spike=Rally.CurrentTurn==VolleyAerialSequence.RallyTurn.Spike;
                _banner.color=spike ? SpikeColour : BlockColour;
                _title.text=Rally.BannerTitle;
                if(_outcomeAge>=0f)
                {
                    _subtitle.text=(Rally.OutcomeGood ? "+1 YOU: " : "+1 OPP: ")+Rally.Outcome;
                    _subtitle.color=Rally.OutcomeGood ? WinColour : LoseColour;
                }
                else
                {
                    _subtitle.color=Color.white;
                    _subtitle.text=Rally.ModeLabel;
                }
            }
            float scale=_pulse>=0f ? 1f+.35f*Mathf.Sin(Mathf.Clamp01(_pulse/.6f)*Mathf.PI) : 1f;
            _banner.transform.localScale=Vector3.one*.0035f*scale;
        }

        /// <summary>Short sine chime so the cues need no audio asset.</summary>
        static AudioClip Chime(string name,float[] notes,float step)
        {
            const int rate=44100;const float tail=.35f;
            int length=Mathf.CeilToInt(rate*(step*(notes.Length-1)+tail));
            var data=new float[length];
            for(int n=0;n<notes.Length;n++)
            {
                int offset=Mathf.RoundToInt(n*step*rate);
                for(int i=0;offset+i<length;i++)
                {
                    float t=(float)i/rate;
                    float envelope=Mathf.Min(1f,t/.006f)*Mathf.Exp(-t*9f);
                    float w=2f*Mathf.PI*notes[n]*t;
                    data[offset+i]+=.3f*envelope*(Mathf.Sin(w)+.25f*Mathf.Sin(2f*w));
                }
            }
            var clip=AudioClip.Create(name,length,1,rate,false);clip.SetData(data,0);
            return clip;
        }
    }
}
