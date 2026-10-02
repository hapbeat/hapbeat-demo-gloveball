using UnityEngine;

namespace EnergyDuel.Runtime
{
    public enum EnergyShotOwner { Player, Opponent }

    /// <summary>Anything a shot can strike. Returns true when the shot is spent on it.</summary>
    public interface IEnergyShotReceiver
    {
        bool ReceiveShot(EnergyShot shot, Vector3 point);
    }

    /// <summary>A straight-flying energy ball. Moved and hit-tested by <see cref="EnergyShotPool"/>.</summary>
    public sealed class EnergyShot : MonoBehaviour
    {
        public EnergyShotOwner Owner { get; private set; }
        public bool FromLeftHand { get; private set; }
        public float Charge01 { get; private set; }
        public float Radius { get; private set; }
        public Vector3 Velocity { get; private set; }
        public float Age { get; set; }
        public bool Live { get; private set; }
        public Vector3 PreviousPosition { get; set; }

        private MeshRenderer _renderer;
        private TrailRenderer _trail;
        private SphereCollider _collider;

        public void Build(Material material)
        {
            _renderer = GetComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _collider = GetComponent<SphereCollider>();
            _collider.isTrigger = true;
            _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.sharedMaterial = material;
            _trail.time = .12f;
            _trail.minVertexDistance = .02f;
            _trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gameObject.SetActive(false);
        }

        public void Launch(EnergyShotOwner owner, bool fromLeftHand, Vector3 origin, Vector3 direction, float speed, float radius, float charge01)
        {
            Owner = owner;
            FromLeftHand = fromLeftHand;
            Charge01 = Mathf.Clamp01(charge01);
            Radius = radius;
            Velocity = direction.normalized * speed;
            Age = 0f;
            transform.position = origin;
            PreviousPosition = origin;
            // The sphere primitive's collider radius is 0.5 in local units, so scale = diameter.
            transform.localScale = Vector3.one * (radius * 2f);
            _trail.widthMultiplier = radius * 1.6f;
            _trail.Clear();
            Live = true;
            gameObject.SetActive(true);
        }

        public void Retire()
        {
            Live = false;
            gameObject.SetActive(false);
        }
    }
}
