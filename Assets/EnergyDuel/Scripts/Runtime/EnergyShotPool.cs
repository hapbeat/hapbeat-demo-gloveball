using System.Collections.Generic;
using UnityEngine;

namespace EnergyDuel.Runtime
{
    /// <summary>
    /// Owns every shot: pooling, straight-line motion, swept hit tests against receivers and
    /// shot-versus-shot cancellation. A swept capsule per frame means a fast, small shot never
    /// tunnels through a thin shield.
    /// </summary>
    public sealed class EnergyShotPool : MonoBehaviour
    {
        public Material PlayerMaterial;
        public Material OpponentMaterial;
        [Min(1)] public int Capacity = 24;
        [Min(.1f)] public float Lifetime = 4f;

        [Header("Player shot, charge 0 -> 1")]
        public Vector2 PlayerSpeed = new Vector2(8f, 18f);
        public Vector2 PlayerRadius = new Vector2(.06f, .20f);

        [Header("Opponent shot")]
        [Min(.01f)] public float OpponentRadius = .12f;

        [Tooltip("A player shot and an opponent shot that meet cancel each other.")]
        public bool CancelOnContact = true;

        private readonly List<EnergyShot> _shots = new List<EnergyShot>();
        private readonly Collider[] _overlaps = new Collider[32];
        private readonly List<(float distance, IEnergyShotReceiver receiver, Vector3 point)> _hits = new List<(float, IEnergyShotReceiver, Vector3)>();

        public int ActiveCount { get; private set; }
        public int CancelledPairs { get; private set; }

        public event System.Action<EnergyShot, EnergyShot> Cancelled;

        public float PlayerSpeedFor(float charge01) => Mathf.Lerp(PlayerSpeed.x, PlayerSpeed.y, Mathf.Clamp01(charge01));
        public float PlayerRadiusFor(float charge01) => Mathf.Lerp(PlayerRadius.x, PlayerRadius.y, Mathf.Clamp01(charge01));

        public EnergyShot SpawnPlayer(bool fromLeftHand, Vector3 origin, Vector3 direction, float charge01) =>
            Spawn(EnergyShotOwner.Player, fromLeftHand, origin, direction, PlayerSpeedFor(charge01), PlayerRadiusFor(charge01), charge01, PlayerMaterial);

        public EnergyShot SpawnOpponent(Vector3 origin, Vector3 direction, float speed) =>
            Spawn(EnergyShotOwner.Opponent, false, origin, direction, speed, OpponentRadius, 1f, OpponentMaterial);

        public void RetireAll()
        {
            foreach (var shot in _shots) if (shot.Live) shot.Retire();
        }

        private EnergyShot Spawn(EnergyShotOwner owner, bool left, Vector3 origin, Vector3 direction, float speed, float radius, float charge, Material material)
        {
            EnergyShot free = null;
            foreach (var shot in _shots)
            {
                if (!shot.Live && shot.GetComponent<MeshRenderer>().sharedMaterial == material)
                {
                    free = shot;
                    break;
                }
            }

            if (free == null)
            {
                if (_shots.Count >= Capacity)
                {
                    // Recycle the oldest live shot rather than refuse to fire.
                    var oldest = _shots[0];
                    foreach (var shot in _shots) if (shot.Age > oldest.Age) oldest = shot;
                    oldest.Retire();
                    _shots.Remove(oldest);
                    Destroy(oldest.gameObject);
                }

                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = owner + " Shot";
                go.transform.SetParent(transform, false);
                free = go.AddComponent<EnergyShot>();
                free.Build(material);
                _shots.Add(free);
            }

            free.Launch(owner, left, origin, direction, speed, radius, charge);
            return free;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            // Receivers follow tracked poses set this frame; queries must see where they are now.
            Physics.SyncTransforms();
            var active = 0;
            foreach (var shot in _shots)
            {
                if (!shot.Live) continue;
                shot.PreviousPosition = shot.transform.position;
                shot.Age += dt;
                if (shot.Age >= Lifetime)
                {
                    shot.Retire();
                    continue;
                }

                var from = shot.PreviousPosition;
                var to = from + (shot.Velocity * dt);
                shot.transform.position = to;
                if (Strike(shot, from, to)) shot.Retire();
                else active++;
            }

            if (CancelOnContact) CancelMeetingShots(dt);
            ActiveCount = active;
        }

        private bool Strike(EnergyShot shot, Vector3 from, Vector3 to)
        {
            var count = Physics.OverlapCapsuleNonAlloc(from, to, shot.Radius, _overlaps, ~0, QueryTriggerInteraction.Collide);
            if (count == 0) return false;
            _hits.Clear();
            for (var i = 0; i < count; i++)
            {
                var collider = _overlaps[i];
                if (collider == null || collider.GetComponent<EnergyShot>() != null) continue;
                var receiver = collider.GetComponentInParent<IEnergyShotReceiver>();
                if (receiver == null) continue;
                var point = collider.ClosestPoint(from);
                _hits.Add((Vector3.Distance(from, point), receiver, point));
            }

            _hits.Sort((a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in _hits)
            {
                if (hit.receiver.ReceiveShot(shot, hit.point)) return true;
            }

            return false;
        }

        private void CancelMeetingShots(float dt)
        {
            for (var i = 0; i < _shots.Count; i++)
            {
                var a = _shots[i];
                if (!a.Live || a.Owner != EnergyShotOwner.Player) continue;
                for (var j = 0; j < _shots.Count; j++)
                {
                    var b = _shots[j];
                    if (!b.Live || b.Owner != EnergyShotOwner.Opponent) continue;
                    if (ClosestApproach(a.PreviousPosition, a.Velocity, b.PreviousPosition, b.Velocity, dt) > a.Radius + b.Radius) continue;
                    a.Retire();
                    b.Retire();
                    CancelledPairs++;
                    Cancelled?.Invoke(a, b);
                    break;
                }
            }
        }

        /// <summary>Smallest distance between two points moving linearly over [0, dt].</summary>
        public static float ClosestApproach(Vector3 a, Vector3 va, Vector3 b, Vector3 vb, float dt)
        {
            var offset = a - b;
            var relative = va - vb;
            var speed2 = relative.sqrMagnitude;
            var t = speed2 > 1e-8f ? Mathf.Clamp(-Vector3.Dot(offset, relative) / speed2, 0f, dt) : 0f;
            return (offset + (relative * t)).magnitude;
        }
    }
}
