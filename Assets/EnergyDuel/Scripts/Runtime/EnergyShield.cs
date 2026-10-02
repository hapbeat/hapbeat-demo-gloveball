using System;
using EnergyDuel.Core;
using UnityEngine;

namespace EnergyDuel.Runtime
{
    /// <summary>
    /// The translucent disc in front of a guarding hand. Blocks opponent shots until its durability
    /// runs out, then shatters and stays down for the cooldown. Durability and cooldown live in
    /// <see cref="EnergyShieldState"/> (owned by the rules); this is the view and the hit surface.
    /// </summary>
    public sealed class EnergyShield : MonoBehaviour, IEnergyShotReceiver
    {
        public EnergyHandInput Input;
        public Material DiscMaterial;
        [Min(.05f)] public float Diameter = .34f;
        [Tooltip("Disc centre in front of the palm, along the palm normal.")]
        public float PalmOffset = .06f;
        public Color FullColor = new Color(.3f, .9f, 1f, .45f);
        public Color WornColor = new Color(1f, .85f, .3f, .45f);
        public Color CrackedColor = new Color(1f, .45f, .2f, .5f);
        [Tooltip("Shown in the same place while the player guards during the cooldown; it blocks nothing.")]
        public Color CooldownColor = new Color(.6f, .2f, .2f, .18f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private EnergyShieldState _state;
        private Transform _disc;
        private Renderer _renderer;
        private Collider _collider;
        private ParticleSystem _burst;
        private MaterialPropertyBlock _block;
        private bool _wasUp;
        private int _maxDurability = 3;

        /// <summary>Whether this shield may be raised at all (off in Ready / Result).</summary>
        public bool Allowed { get; set; } = true;
        public bool Up { get; private set; }
        public EnergyShieldState State => _state;

        public event Action<EnergyShield, EnergyShieldBlock, EnergyShot> Struck;

        public void Bind(EnergyShieldState state, int maxDurability)
        {
            _state = state;
            _maxDurability = maxDurability;
        }

        private void Awake()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Shield Disc";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            // Cylinder mesh: radius 0.5, height 2. A thin box collider carries the hits.
            go.transform.localScale = new Vector3(Diameter, .01f, Diameter);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1f, 6f, 1f);
            _disc = go.transform;
            _renderer = go.GetComponent<MeshRenderer>();
            if (DiscMaterial != null) _renderer.sharedMaterial = DiscMaterial;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _collider = box;
            _block = new MaterialPropertyBlock();
            _burst = BuildBurst(transform, DiscMaterial);
            _disc.gameObject.SetActive(false);
        }

        private static ParticleSystem BuildBurst(Transform parent, Material material)
        {
            var go = new GameObject("Shield Shatter");
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = .3f;
            main.startLifetime = .5f;
            main.startSpeed = 2.5f;
            main.startSize = .03f;
            main.maxParticles = 64;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = .6f;
            var emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = .15f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (material != null) renderer.sharedMaterial = material;
            return system;
        }

        private void LateUpdate()
        {
            var wants = Allowed && Input != null && Input.Present && Input.Guarding;
            var canBlock = _state == null || _state.CanGuard;
            Up = wants && canBlock;
            _disc.gameObject.SetActive(wants);
            _collider.enabled = Up;
            if (wants)
            {
                var normal = Input.PalmNormal.sqrMagnitude > 1e-6f ? Input.PalmNormal.normalized : Vector3.forward;
                _disc.SetPositionAndRotation(Input.PalmPosition + (normal * PalmOffset), Quaternion.FromToRotation(Vector3.up, normal));
                SetColor(!canBlock ? CooldownColor : DurabilityColor());
            }

            if (Up && !_wasUp)
            {
                EnergyHapticRelay.Report(EnergyHapticRelay.Sided(Input.IsLeft, EnergyHapticEvent.LeftShieldUp), _disc.position);
            }

            _wasUp = Up;
        }

        private Color DurabilityColor()
        {
            if (_state == null) return FullColor;
            var full = _state.Durability >= Mathf.Max(1, _maxDurability);
            return full ? FullColor : _state.Durability >= 2 ? WornColor : CrackedColor;
        }

        private void SetColor(Color color)
        {
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            _renderer.SetPropertyBlock(_block);
        }

        public bool ReceiveShot(EnergyShot shot, Vector3 point)
        {
            if (!Up || shot.Owner != EnergyShotOwner.Opponent) return false;
            var result = _state != null ? _state.Block() : EnergyShieldBlock.Blocked;
            if (result == EnergyShieldBlock.Ignored) return false;
            var left = Input.IsLeft;
            if (result == EnergyShieldBlock.Broken)
            {
                EnergyHapticRelay.Report(EnergyHapticRelay.Sided(left, EnergyHapticEvent.LeftShieldBreak), point);
                EnergyHapticRelay.Report(EnergyHapticEvent.ShieldBreakNeck, point);
                _burst.transform.SetPositionAndRotation(_disc.position, _disc.rotation);
                _burst.Play();
                Up = false;
                _collider.enabled = false;
            }
            else
            {
                EnergyHapticRelay.Report(EnergyHapticRelay.Sided(left, EnergyHapticEvent.LeftShieldBlock), point);
            }

            Struck?.Invoke(this, result, shot);
            return true;
        }
    }
}
