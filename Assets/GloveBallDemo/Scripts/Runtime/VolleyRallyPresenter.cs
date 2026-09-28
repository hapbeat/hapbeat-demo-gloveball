using UnityEngine;
using UnityEngine.UI;

namespace GloveBallDemo.Runtime
{
    /// <summary>Runtime-only rally presentation: head-locked blink, court banner (turn, match score, outcome), landing marker and chimes.</summary>
    public sealed class VolleyRallyPresenter : MonoBehaviour
    {
        public VolleyAerialSequence Rally;
        [Tooltip("Banner centre from the net: forward (into the opponent court) and height.")]
        public float BannerForward=3f;
        public float BannerHeight=4.3f;
        [Range(0f,1f)] public float ChimeVolume=.6f;
        [Min(.2f)] public float OutcomeSeconds=2f;
        [Min(.2f)] public float MarkerSeconds=2.5f;
        static readonly Color SpikeColour=new Color(.1f,.35f,.9f,.88f);
        static readonly Color BlockColour=new Color(.85f,.2f,.1f,.88f);
        static readonly Color WinColour=new Color(.35f,1f,.45f);
        static readonly Color LoseColour=new Color(1f,.55f,.3f);
        Image _fade, _banner;
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
            BuildFade();BuildBanner();BuildMarker();
            _audio=gameObject.AddComponent<AudioSource>();_audio.playOnAwake=false;_audio.spatialBlend=0f;
            _spikeChime=Chime("Spike turn chime",new[]{523.25f,659.25f,783.99f},.14f);
            _blockChime=Chime("Block turn chime",new[]{783.99f,523.25f},.14f);
            _pointWon=Chime("Point won",new[]{880f,1174.66f},.09f);
            _pointLost=Chime("Point lost",new[]{246.94f,196f},.12f);
            _matchWon=Chime("Match won",new[]{523.25f,659.25f,783.99f,1046.5f},.16f);
            _matchLost=Chime("Match lost",new[]{392f,329.63f,261.63f},.2f);
            Rally.TurnAnnounced+=OnTurn;Rally.PointScored+=OnPoint;Rally.MatchEnded+=OnMatch;
            _outcomeSerial=Rally.OutcomeSerial;
            Refresh();
        }
        void OnDestroy()
        {
            if(Rally!=null){Rally.TurnAnnounced-=OnTurn;Rally.PointScored-=OnPoint;Rally.MatchEnded-=OnMatch;}
            foreach(var clip in new[]{_spikeChime,_blockChime,_pointWon,_pointLost,_matchWon,_matchLost})if(clip!=null)Destroy(clip);
            if(_markerMaterial!=null)Destroy(_markerMaterial);
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
                _title.text=spike ? "SPIKE TURN" : "BLOCK TURN";
                if(_outcomeAge>=0f)
                {
                    _subtitle.text=(Rally.OutcomeGood ? "+1 YOU: " : "+1 OPP: ")+Rally.Outcome;
                    _subtitle.color=Rally.OutcomeGood ? WinColour : LoseColour;
                }
                else
                {
                    _subtitle.color=Color.white;
                    _subtitle.text="FIRST TO "+Rally.MatchPoints+"  /  "+(Rally.Mode==VolleyAerialSequence.RallyMode.Alternate ? "ALTERNATING"
                        : Rally.Mode==VolleyAerialSequence.RallyMode.SpikeOnly ? "SPIKE ONLY" : "BLOCK ONLY");
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
