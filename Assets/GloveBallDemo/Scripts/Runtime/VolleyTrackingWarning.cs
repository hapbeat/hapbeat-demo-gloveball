using UnityEngine;
using UnityEngine.UI;
namespace GloveBallDemo.Runtime
{
    public sealed class VolleyTrackingWarning : MonoBehaviour
    {
        public VolleyTrackedHand Left;
        public VolleyTrackedHand Right;
        public Graphic[] Graphics;
        private string _normalText;
        private void LateUpdate()
        {
            bool show=!Left.Ready && !Right.Ready;
            foreach(var graphic in Graphics)
            {
                graphic.enabled=show;
                if(graphic is Text text)
                {
                    if(_normalText==null)_normalText=text.text;
                    text.text=Left.IsEstimated||Right.IsEstimated ? "WMM ESTIMATED HANDS\nShow a hand to resume" : _normalText;
                }
            }
        }
    }
}
