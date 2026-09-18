using UnityEngine;
using UnityEngine.UI;
namespace GloveBallDemo.Runtime
{
    public sealed class VolleyTrackingWarning : MonoBehaviour
    {
        public VolleyTrackedHand Left;
        public VolleyTrackedHand Right;
        public Graphic[] Graphics;
        public VolleyDrillController Drill;
        float _lostSeconds;
        void Start()
        {
            if(Drill==null)Drill=FindFirstObjectByType<VolleyDrillController>();
            // Keep the warning out of normal play; a sustained loss warrants a central prompt.
            if(transform is RectTransform rect){rect.anchoredPosition=Vector2.zero;rect.localScale=Vector3.one*.0015f;}
        }
        private void LateUpdate()
        {
            _lostSeconds=Left.Ready||Right.Ready ? 0 : _lostSeconds+Time.unscaledDeltaTime;
            bool show=Drill!=null ? Drill.TrackingSuspended : _lostSeconds>=1.5f;
            foreach(var graphic in Graphics)
            {
                graphic.enabled=show;
                if(graphic is Text text)
                {
                    text.text=Left.IsEstimated||Right.IsEstimated ? "HAND TRACKING: ESTIMATED" : "HAND TRACKING: LOST";
                }
            }
        }
    }
}
