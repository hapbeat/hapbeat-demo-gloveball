using UnityEngine;
using UnityEngine.UI;

namespace GloveBallDemo.Runtime
{
    /// <summary>Runtime-only rally presentation: head-locked blink, court banner for the current turn, outcome text and turn chimes.</summary>
    public sealed class VolleyRallyPresenter : MonoBehaviour
    {
        public VolleyAerialSequence Rally;
        [Tooltip("Banner centre from the net: forward (into the opponent court) and height.")]
        public float BannerForward=3f;
        public float BannerHeight=4.3f;
        [Range(0f,1f)] public float ChimeVolume=.6f;
        [Min(.2f)] public float OutcomeSeconds=1.6f;
        static readonly Color SpikeColour=new Color(.1f,.35f,.9f,.88f);
        static readonly Color BlockColour=new Color(.85f,.2f,.1f,.88f);
        Image _fade, _banner;
        Text _title, _subtitle;
        AudioSource _audio;
        AudioClip _spikeChime, _blockChime;
        float _pulse=-1f, _outcomeAge=-1f;
        int _outcomeSerial;
        Font _font;

        void Start()
        {
            if(Rally==null)Rally=GetComponent<VolleyAerialSequence>();
            _font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildFade();BuildBanner();
            _audio=gameObject.AddComponent<AudioSource>();_audio.playOnAwake=false;_audio.spatialBlend=0f;
            _spikeChime=Chime("Spike turn chime",new[]{523.25f,659.25f,783.99f});
            _blockChime=Chime("Block turn chime",new[]{783.99f,523.25f});
            Rally.TurnAnnounced+=OnTurn;
            _outcomeSerial=Rally.OutcomeSerial;
            Refresh();
        }
        void OnDestroy()
        {
            if(Rally!=null)Rally.TurnAnnounced-=OnTurn;
            if(_spikeChime!=null)Destroy(_spikeChime);
            if(_blockChime!=null)Destroy(_blockChime);
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
            var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(700,230);rect.localScale=Vector3.one*.0035f;
            _banner=go.AddComponent<Image>();_banner.raycastTarget=false;
            _title=Label(rect,new Vector2(0,32),86);
            _subtitle=Label(rect,new Vector2(0,-62),44);
        }
        Text Label(RectTransform parent,Vector2 position,int size)
        {
            var text=new GameObject("Label",typeof(RectTransform),typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent,false);
            text.rectTransform.sizeDelta=new Vector2(680,110);text.rectTransform.anchoredPosition=position;
            text.font=_font;text.fontSize=size;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.raycastTarget=false;
            return text;
        }

        void OnTurn(VolleyAerialSequence.RallyTurn turn)
        {
            _pulse=0f;_outcomeAge=-1f;Refresh();
            if(Application.isPlaying && _audio!=null)
                _audio.PlayOneShot(turn==VolleyAerialSequence.RallyTurn.Spike ? _spikeChime : _blockChime,ChimeVolume);
        }

        void LateUpdate()
        {
            if(Rally==null)return;
            if(_fade!=null)
            {
                _fade.color=new Color(0f,0f,0f,Rally.FadeAlpha);
                _fade.enabled=Rally.FadeAlpha>.001f;
            }
            if(Rally.OutcomeSerial!=_outcomeSerial){_outcomeSerial=Rally.OutcomeSerial;_outcomeAge=0f;}
            float dt=Time.deltaTime;
            if(_outcomeAge>=0f){_outcomeAge+=dt;if(_outcomeAge>OutcomeSeconds)_outcomeAge=-1f;}
            if(_pulse>=0f){_pulse+=dt;if(_pulse>.6f)_pulse=-1f;}
            Refresh();
        }

        void Refresh()
        {
            if(_banner==null)return;
            bool visible=Rally.TurnStarted;
            _banner.enabled=_title.enabled=_subtitle.enabled=visible;
            if(!visible)return;
            bool spike=Rally.CurrentTurn==VolleyAerialSequence.RallyTurn.Spike;
            _banner.color=spike ? SpikeColour : BlockColour;
            _title.text=spike ? "SPIKE TURN" : "BLOCK TURN";
            if(_outcomeAge>=0f)
            {
                _subtitle.text=Rally.Outcome;
                _subtitle.color=Rally.OutcomeGood ? new Color(.55f,1f,.55f) : new Color(1f,.85f,.4f);
            }
            else
            {
                _subtitle.color=Color.white;
                _subtitle.text=Rally.Mode==VolleyAerialSequence.RallyMode.Alternate ? "ALTERNATING SPIKE / BLOCK"
                    : Rally.Mode==VolleyAerialSequence.RallyMode.SpikeOnly ? "SPIKE ONLY" : "BLOCK ONLY";
            }
            float scale=_pulse>=0f ? 1f+.35f*Mathf.Sin(Mathf.Clamp01(_pulse/.6f)*Mathf.PI) : 1f;
            _banner.transform.localScale=Vector3.one*.0035f*scale;
        }

        /// <summary>Short sine chime so the turn cue needs no audio asset. Ascending = our attack, descending = defend.</summary>
        static AudioClip Chime(string name,float[] notes)
        {
            const int rate=44100;const float step=.14f;const float tail=.35f;
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
