using UnityEngine;
using UnityEngine.UI;
namespace GloveBallDemo.Runtime
{
    public sealed class VolleyTrackingWarning : MonoBehaviour
    {
        public VolleyTrackedHand Left;
        public VolleyTrackedHand Right;
        public Graphic[] Graphics;
        private void LateUpdate()
        {
            bool show=!Left.Ready && !Right.Ready;
            foreach(var graphic in Graphics) graphic.enabled=show;
        }
    }
}
