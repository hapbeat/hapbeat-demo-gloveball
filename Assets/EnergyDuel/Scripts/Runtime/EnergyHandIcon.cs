using UnityEngine;
using UnityEngine.UI;

namespace EnergyDuel.Runtime
{
    public enum EnergyIconPose { None, Fist, Open, Guard }

    /// <summary>
    /// A flat hand pictogram built from UI rectangles: palm, four fingers and a thumb. The rect it
    /// lives in never changes size, so switching pose never moves the text around it.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class EnergyHandIcon : MonoBehaviour
    {
        public Color Color = new Color(.85f, .95f, 1f, 1f);
        public Color GuardColor = new Color(.3f, .9f, 1f, 1f);

        private RectTransform _palm;
        private readonly RectTransform[] _fingers = new RectTransform[4];
        private RectTransform _thumb;
        private Image[] _images;
        private RectTransform _arrow;
        private EnergyIconPose _pose = (EnergyIconPose)(-1);

        private void Awake() => Build();

        private void Build()
        {
            if (_palm != null) return;
            _palm = Part("Palm", new Vector2(0f, -20f), new Vector2(90f, 80f));
            for (var i = 0; i < 4; i++) _fingers[i] = Part("Finger " + i, new Vector2(-34f + (i * 23f), 20f), new Vector2(18f, 70f));
            _thumb = Part("Thumb", new Vector2(-60f, -10f), new Vector2(18f, 50f));
            _arrow = Part("Up Arrow", new Vector2(80f, 10f), new Vector2(12f, 90f));
            _images = GetComponentsInChildren<Image>(true);
        }

        private RectTransform Part(string partName, Vector2 position, Vector2 size)
        {
            var go = new GameObject(partName, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, 0f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            go.GetComponent<Image>().raycastTarget = false;
            return rect;
        }

        public void Show(EnergyIconPose pose)
        {
            Build();
            if (pose == _pose) return;
            _pose = pose;
            var visible = pose != EnergyIconPose.None;
            foreach (var image in _images)
            {
                image.enabled = visible;
                image.color = pose == EnergyIconPose.Guard ? GuardColor : Color;
            }

            if (!visible) return;
            var fingerLength = pose == EnergyIconPose.Fist ? 18f : 70f;
            foreach (var finger in _fingers) finger.sizeDelta = new Vector2(18f, fingerLength);
            _thumb.localRotation = Quaternion.Euler(0f, 0f, pose == EnergyIconPose.Fist ? -70f : 30f);
            _arrow.GetComponent<Image>().enabled = pose == EnergyIconPose.Guard;
        }
    }
}
