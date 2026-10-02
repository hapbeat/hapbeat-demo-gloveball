using System;
using EnergyDuel.Core;
using UnityEngine;

namespace EnergyDuel.Runtime
{
    public enum EnergyOpponentMode { Idle, Tutorial, Duel }

    /// <summary>
    /// A floating core that roams its court, telegraphs each shot with a glow, then fires straight at
    /// the player's chest or head (no lead, so it can be dodged). Player shots damage it.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public sealed class EnergyOpponent : MonoBehaviour, IEnergyShotReceiver
    {
        public EnergyShotPool Pool;
        public Transform PlayerHead;
        [Tooltip("Chest aim point below the head.")]
        public float ChestDrop = .35f;
        [Range(0f, 1f)] public float HeadAimChance = .4f;

        [Header("Court (world space)")]
        public Vector3 CourtCenter;
        public Vector3 CourtSize = new Vector3(4.5f, .4f, 3f);
        public OpponentMoveSettings Move = new OpponentMoveSettings();
        public int Seed = 1234;

        [Header("Firing")]
        [Min(0f)] public float TelegraphSeconds = .6f;
        [Tooltip("Shots leave from this far in front of the core, toward the player.")]
        [Min(0f)] public float MuzzleOffset = .35f;

        [Header("Look")]
        public Renderer Core;
        public Renderer Ring;
        public Transform HpFill;
        public Color IdleColor = new Color(.9f, .2f, .15f, 1f);
        public Color TelegraphColor = new Color(1f, .85f, .5f, 1f);
        public Color HitColor = Color.white;
        [Min(0f)] public float HitFlashSeconds = .15f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private OpponentMovePlanner _planner;
        private System.Random _random;
        private OpponentMoveStep _step;
        private bool _haveStep;
        private float _pause;
        private float _fireTimer;
        private float _telegraph = -1f;
        private float _hitFlash;
        private MaterialPropertyBlock _block;
        private Vector3 _ringBaseScale = Vector3.one;

        public EnergyOpponentMode Mode { get; set; }
        /// <summary>Min / max seconds between shots.</summary>
        public Vector2 FireInterval { get; set; } = new Vector2(1.8f, 2.5f);
        public float ShotSpeed { get; set; } = 6f;
        /// <summary>Stops firing (not moving) while the player's tracking is gone.</summary>
        public bool HoldFire { get; set; }
        public int ShotsFired { get; private set; }
        public bool Telegraphing => _telegraph >= 0f;

        public event Action<EnergyOpponent, EnergyShot> Hit;
        public event Action<EnergyOpponent, EnergyShot> Fired;

        public Bounds Court => new Bounds(CourtCenter, CourtSize);

        private void Awake()
        {
            GetComponent<SphereCollider>().isTrigger = true;
            _planner = new OpponentMovePlanner(Seed, Move);
            _random = new System.Random(Seed ^ 0x5bd1);
            _block = new MaterialPropertyBlock();
            if (Ring != null) _ringBaseScale = Ring.transform.localScale;
            ResetFireTimer();
        }

        public void SetHp01(float hp01)
        {
            if (HpFill == null) return;
            var scale = HpFill.localScale;
            scale.x = Mathf.Clamp01(hp01);
            HpFill.localScale = scale;
        }

        public void ResetFireTimer()
        {
            _fireTimer = Mathf.Lerp(FireInterval.x, FireInterval.y, (float)_random.NextDouble());
            _telegraph = -1f;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (Mode == EnergyOpponentMode.Duel) Roam(dt);
            Face();
            TickFire(dt);
            _hitFlash = Mathf.Max(0f, _hitFlash - dt);
            UpdateLook();
        }

        private void Roam(float dt)
        {
            if (!_haveStep)
            {
                _step = _planner.Next(transform.position, Court);
                _haveStep = true;
            }

            if (_pause > 0f)
            {
                _pause -= dt;
                return;
            }

            transform.position = Vector3.MoveTowards(transform.position, _step.Target, _step.Speed * dt);
            if ((transform.position - _step.Target).sqrMagnitude < 1e-4f)
            {
                _pause = _step.PauseSeconds;
                _haveStep = false;
            }
        }

        private void Face()
        {
            if (PlayerHead == null) return;
            var to = PlayerHead.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        }

        private void TickFire(float dt)
        {
            if (Mode == EnergyOpponentMode.Idle || HoldFire || Pool == null || PlayerHead == null)
            {
                _telegraph = -1f;
                return;
            }

            if (_telegraph >= 0f)
            {
                _telegraph += dt;
                if (_telegraph >= TelegraphSeconds)
                {
                    Shoot();
                    ResetFireTimer();
                }

                return;
            }

            _fireTimer -= dt;
            if (_fireTimer <= 0f) _telegraph = 0f;
        }

        private void Shoot()
        {
            var aim = PlayerHead.position;
            if (_random.NextDouble() >= HeadAimChance) aim -= Vector3.up * ChestDrop;
            var direction = (aim - transform.position).normalized;
            var shot = Pool.SpawnOpponent(transform.position + (direction * MuzzleOffset), direction, ShotSpeed);
            ShotsFired++;
            Fired?.Invoke(this, shot);
        }

        private void UpdateLook()
        {
            var glow = Telegraphing && TelegraphSeconds > 0f ? Mathf.Clamp01(_telegraph / TelegraphSeconds) : 0f;
            var color = Color.Lerp(IdleColor, TelegraphColor, glow);
            if (_hitFlash > 0f) color = HitColor;
            SetColor(Core, color);
            SetColor(Ring, color);
            if (Ring != null)
            {
                Ring.transform.localScale = _ringBaseScale * (1f + (.35f * glow));
                Ring.transform.Rotate(Vector3.up, (90f + (360f * glow)) * Time.deltaTime, Space.Self);
            }
        }

        private void SetColor(Renderer target, Color color)
        {
            if (target == null) return;
            target.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            target.SetPropertyBlock(_block);
        }

        public bool ReceiveShot(EnergyShot shot, Vector3 point)
        {
            if (shot.Owner != EnergyShotOwner.Player) return false;
            _hitFlash = HitFlashSeconds;
            Hit?.Invoke(this, shot);
            return true;
        }
    }
}
