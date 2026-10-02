using System;
using UnityEngine;

namespace EnergyDuel.Core
{
    [Serializable]
    public sealed class OpponentMoveSettings
    {
        [Min(.1f)] public float MinimumSpeed = 1.5f;
        [Min(.1f)] public float MaximumSpeed = 3f;
        [Tooltip("Chance of a pause after reaching a waypoint.")]
        [Range(0f, 1f)] public float PauseChance = .4f;
        [Min(0f)] public float MinimumPauseSeconds = .3f;
        [Min(0f)] public float MaximumPauseSeconds = 1.2f;
        [Tooltip("A new waypoint is at least this far from the current position (relaxed when the court is smaller).")]
        [Min(0f)] public float MinimumTravel = 1f;
    }

    public readonly struct OpponentMoveStep
    {
        public readonly Vector3 Target;
        public readonly float Speed;
        /// <summary>Seconds to stand still after arriving.</summary>
        public readonly float PauseSeconds;

        public OpponentMoveStep(Vector3 target, float speed, float pauseSeconds)
        {
            Target = target;
            Speed = speed;
            PauseSeconds = pauseSeconds;
        }
    }

    /// <summary>Seeded random waypoints inside the opponent's court (a world-space box).</summary>
    public sealed class OpponentMovePlanner
    {
        private const int AttemptBudget = 16;
        private readonly System.Random _random;
        private readonly OpponentMoveSettings _settings;

        public OpponentMovePlanner(int seed, OpponentMoveSettings settings)
        {
            _random = new System.Random(seed);
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public OpponentMoveStep Next(Vector3 current, Bounds court)
        {
            var best = RandomPoint(court);
            var bestDistance = Distance(best, current);
            for (var attempt = 1; attempt < AttemptBudget && bestDistance < _settings.MinimumTravel; attempt++)
            {
                var candidate = RandomPoint(court);
                var distance = Distance(candidate, current);
                if (distance > bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            var speed = Lerp(Mathf.Min(_settings.MinimumSpeed, _settings.MaximumSpeed), Mathf.Max(_settings.MinimumSpeed, _settings.MaximumSpeed), Next01());
            var pause = Next01() < _settings.PauseChance
                ? Lerp(Mathf.Min(_settings.MinimumPauseSeconds, _settings.MaximumPauseSeconds), Mathf.Max(_settings.MinimumPauseSeconds, _settings.MaximumPauseSeconds), Next01())
                : 0f;
            return new OpponentMoveStep(best, speed, pause);
        }

        private Vector3 RandomPoint(Bounds court) => new Vector3(
            Lerp(court.min.x, court.max.x, Next01()),
            Lerp(court.min.y, court.max.y, Next01()),
            Lerp(court.min.z, court.max.z, Next01()));

        private static float Distance(Vector3 a, Vector3 b) => Vector3.Distance(a, b);
        private float Next01() => (float)_random.NextDouble();
        private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
    }
}
