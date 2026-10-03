using System.Collections.Generic;
using Hapbeat.DemoSwitch;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// Attaches the Demo Session host to each loaded scene at runtime, so the checked-in scenes stay
    /// untouched: Volley scenes get <see cref="VolleySessionHost"/>, Demo.unity gets <see cref="GloveBallSessionHost"/>.
    /// </summary>
    internal static class GloveBallSessionBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded -= Attach;
            SceneManager.sceneLoaded += Attach;
        }

        private static void Attach(Scene scene, LoadSceneMode mode)
        {
            var drill = Object.FindAnyObjectByType<VolleyDrillController>();
            if (drill != null)
            {
                if (drill.GetComponent<VolleySessionHost>() == null) drill.gameObject.AddComponent<VolleySessionHost>();
                return;
            }
            var game = Object.FindAnyObjectByType<DemoGameController>();
            if (game != null && game.GetComponent<GloveBallSessionHost>() == null) game.gameObject.AddComponent<GloveBallSessionHost>();
        }
    }

    /// <summary>
    /// Volley Demo Session adapter. `scene` picks Block or Receive once per process; Block plays a
    /// match to `points` and holds the final score; Receive serves `balls` balls (hit or missed).
    /// 視線をリセット (button and CONTROL `recenter`) is the menu's REPOSITION TO START.
    /// </summary>
    public sealed class VolleySessionHost : MonoBehaviour, IDemoSessionHost, IDemoSessionRecenter
    {
        public const string BlockScene = "VolleyBlock-codex";
        public const string ReceiveScene = "VolleyReceive-codex";

        // The session's scene choice is applied once; later operator scene CONTROLs are not reverted.
        private static bool _sceneOptionApplied;
        private VolleyDrillController _drill;
        private bool _completionWired;

        public static void ResetSceneChoiceForTests() => _sceneOptionApplied = false;

        private void Start()
        {
            _drill = GetComponent<VolleyDrillController>();
            DemoSession.RegisterHost(this);
        }

        private void OnDestroy()
        {
            DemoSession.UnregisterHost(this);
            if (_drill == null) return;
            if (_drill.Aerial != null) _drill.Aerial.MatchEnded -= OnMatchEnded;
            _drill.ServesCompleted -= OnServesCompleted;
        }

        public void ApplyOptions(IReadOnlyDictionary<string, string> options)
        {
            if (_drill == null) _drill = GetComponent<VolleyDrillController>();
            options.TryGetValue("scene", out var scene);
            var target = scene == "receive" ? ReceiveScene : BlockScene;
            if (!_sceneOptionApplied)
            {
                _sceneOptionApplied = true;
                if (SceneManager.GetActiveScene().name != target)
                {
                    LoadScene(target);
                    return;
                }
            }

            if (_drill.Drill == VolleyDrill.Block && _drill.Aerial != null)
            {
                if (options.TryGetValue("points", out var points) && int.TryParse(points, out var value) && value > 0)
                    _drill.Aerial.MatchPoints = value;
                if (_drill.Aerial.Mode != VolleyAerialSequence.RallyMode.Match)
                    _drill.Aerial.SetMode(VolleyAerialSequence.RallyMode.Match);
                _drill.Aerial.HoldAtMatchEnd = true;
                if (!_completionWired) _drill.Aerial.MatchEnded += OnMatchEnded;
            }
            else if (_drill.Drill == VolleyDrill.Receive)
            {
                _drill.ServeLimit = options.TryGetValue("balls", out var balls) && int.TryParse(balls, out var count) && count > 0 ? count : 10;
                if (!_completionWired) _drill.ServesCompleted += OnServesCompleted;
            }
            _completionWired = true;
        }

        private void LoadScene(string scene)
        {
            var menu = FindAnyObjectByType<VolleyHandMenu>();
            if (menu != null) menu.LoadDrill(scene);
            else SceneManager.LoadScene(scene);
        }

        private void OnMatchEnded(bool playerWon) => DemoSession.ShowCompletion();
        private void OnServesCompleted() => DemoSession.ShowCompletion();

        public void Restart()
        {
            if (_drill.Drill == VolleyDrill.Block && _drill.Aerial != null) _drill.Aerial.RestartMatch();
            else _drill.RestartServes();
        }

        public void SetHapticsEnabled(bool enabled) => HapticEventRelay.SetHapticsEnabled(enabled);

        public void RecenterToStart()
        {
            var menu = FindAnyObjectByType<VolleyHandMenu>();
            if (menu != null) menu.RepositionToStart();
            else
            {
                var floor = FindAnyObjectByType<VolleyFloorTracking>();
                if (floor != null) floor.Recenter();
            }
        }

        public void SetGameplayPaused(bool paused)
        {
            _drill.SetMenuPaused(paused);
            GameInputGate.SetBlocked(paused);
        }
    }

    /// <summary>
    /// GloveBall (Demo.unity) Demo Session adapter. The wave run (3 rounds) is the natural end; the
    /// result is held instead of starting a new run, and retry starts a fresh wave run.
    /// 視線をリセット uses the package default: the scene's XrStartAlignment aligns again.
    /// </summary>
    public sealed class GloveBallSessionHost : MonoBehaviour, IDemoSessionHost
    {
        private DemoGameController _game;
        private bool _completionWired;

        private void Start()
        {
            _game = GetComponent<DemoGameController>();
            DemoSession.RegisterHost(this);
        }

        private void OnDestroy()
        {
            DemoSession.UnregisterHost(this);
            if (_game != null) _game.RunCompleted -= OnRunCompleted;
        }

        public void ApplyOptions(IReadOnlyDictionary<string, string> options)
        {
            if (_game == null) _game = GetComponent<DemoGameController>();
            if (_game.PlayMode != DemoPlayMode.Waves) _game.SetPlayMode(DemoPlayMode.Waves);
            _game.HoldAtResult = true;
            if (!_completionWired) _game.RunCompleted += OnRunCompleted;
            _completionWired = true;
        }

        private void OnRunCompleted() => DemoSession.ShowCompletion();

        public void Restart() => _game.RestartRun();

        public void SetHapticsEnabled(bool enabled) => HapticEventRelay.SetHapticsEnabled(enabled);

        public void SetGameplayPaused(bool paused) => GameInputGate.SetBlocked(paused);
    }
}
