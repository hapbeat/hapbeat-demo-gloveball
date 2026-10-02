using System;
using UnityEngine;

namespace EnergyDuel.Runtime
{
    /// <summary>A still target for the first tutorial step. Shown only while that step runs.</summary>
    [RequireComponent(typeof(SphereCollider))]
    public sealed class EnergyTutorialTarget : MonoBehaviour, IEnergyShotReceiver
    {
        public Renderer Body;
        public Color IdleColor = new Color(1f, .8f, .2f, 1f);
        public Color HitColor = Color.white;
        [Min(0f)] public float FlashSeconds = .25f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _block;
        private float _flash;

        public event Action<EnergyTutorialTarget, EnergyShot> Hit;

        private void Awake()
        {
            GetComponent<SphereCollider>().isTrigger = true;
            _block = new MaterialPropertyBlock();
        }

        private void OnEnable() => _flash = 0f;

        private void Update()
        {
            _flash = Mathf.Max(0f, _flash - Time.deltaTime);
            if (Body == null) return;
            var color = Color.Lerp(IdleColor, HitColor, FlashSeconds > 0f ? _flash / FlashSeconds : 0f);
            Body.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            Body.SetPropertyBlock(_block);
        }

        public bool ReceiveShot(EnergyShot shot, Vector3 point)
        {
            if (shot.Owner != EnergyShotOwner.Player) return false;
            _flash = FlashSeconds;
            Hit?.Invoke(this, shot);
            return true;
        }
    }
}
