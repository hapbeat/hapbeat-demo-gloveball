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
    /// Volley Demo Session adapter, all in the Block scene: `scene` `block` plays the spike / block turns (Alternate),
    /// `receive` RECEIVE (passes to the green setter, <see cref="VolleyAerialSequence.RallyMode.Pass"/>) and `match`
    /// the 6 v 6 match, each to `points`, holding the final score. `tutorial` off skips the tutorial the app otherwise
    /// starts with (<see cref="VolleyTutorial.StartOnLaunch"/>); a retry never runs it.
    /// 視線をリセット (button and CONTROL `recenter`) is the menu's REPOSITION TO START.
    /// The shared pause (Volley APK) pauses through <see cref="SetGameplayPaused"/> and restarts through
    /// <see cref="Restart"/>; <see cref="VolleyHandMenu"/> opens beside its panel. After SPIKE + BLOCK the completion panel
    /// also offers レシーブへ (RECEIVE in the same step, with its tutorial unless the session's tutorial is off).
    /// </summary>
    public sealed class VolleySessionHost : MonoBehaviour, IDemoSessionHost, IDemoSessionRecenter, IDemoSessionTutorial, IDemoSessionCompletionActions
    {
        public const string BlockScene = "VolleyBlock-codex";

        // The session's scene choice is applied once; later operator scene CONTROLs are not reverted.
        private static bool _sceneOptionApplied;
        private VolleyDrillController _drill;
        private bool _completionWired, _tutorialOn = true;

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
        }

        public void ApplyOptions(IReadOnlyDictionary<string, string> options)
        {
            if (_drill == null) _drill = GetComponent<VolleyDrillController>();
            options.TryGetValue("scene", out var scene);
            _tutorialOn = !(options.TryGetValue("tutorial", out var tutorial) && tutorial == "off");
            if (!_tutorialOn) VolleyTutorial.SkipLaunchTutorial();
            var mode = SceneMode(scene);
            if (!_sceneOptionApplied)
            {
                _sceneOptionApplied = true;
                if (SceneManager.GetActiveScene().name != BlockScene)
                {
                    LoadScene(BlockScene);
                    return;
                }
            }

            if (_drill.Drill == VolleyDrill.Block && _drill.Aerial != null)
            {
                if (options.TryGetValue("points", out var points) && int.TryParse(points, out var value) && value > 0)
                    _drill.Aerial.MatchPoints = value;
                if (_drill.Aerial.Mode != mode) _drill.Aerial.SetMode(mode);
                _drill.Aerial.HoldAtMatchEnd = true;
                if (!_completionWired) _drill.Aerial.MatchEnded += OnMatchEnded;
                _completionWired = true;
            }
        }

        /// <summary>The mode of a descriptor `scene` value (also the CONTROL `scene` IDs, <see cref="VolleyHandMenu"/>).</summary>
        public static VolleyAerialSequence.RallyMode SceneMode(string scene) =>
            scene == "match" ? VolleyAerialSequence.RallyMode.Match
            : scene == "receive" ? VolleyAerialSequence.RallyMode.Pass
            : VolleyAerialSequence.RallyMode.Alternate;

        private void LoadScene(string scene)
        {
            var menu = FindAnyObjectByType<VolleyHandMenu>();
            if (menu != null) menu.LoadDrill(scene);
            else SceneManager.LoadScene(scene);
        }

        private void OnMatchEnded(bool playerWon) => DemoSession.ShowCompletion();

        /// <summary>The completion panel's own buttons: レシーブへ after SPIKE + BLOCK (none after RECEIVE or the match).</summary>
        public IReadOnlyList<DemoSessionCompletionAction> CompletionActions =>
            _drill != null && _drill.Drill == VolleyDrill.Block && _drill.Aerial != null
            && VolleyTutorial.CourseFor(_drill.Aerial.Mode) == VolleyTutorial.Course.SpikeBlock
                ? new[] { new DemoSessionCompletionAction("レシーブへ", PlayReceive) }
                : System.Array.Empty<DemoSessionCompletionAction>();

        /// <summary>RECEIVE, with its tutorial first unless the session's tutorial option is off.</summary>
        private void PlayReceive()
        {
            var menu = FindAnyObjectByType<VolleyHandMenu>();
            if (_tutorialOn)
            {
                if (menu != null) menu.StartTutorial(VolleyTutorial.Course.Receive);
                else GetComponent<VolleyTutorial>()?.Begin(VolleyTutorial.Course.Receive);
            }
            else if (menu != null) menu.Play(VolleyAerialSequence.RallyMode.Pass);
            else _drill.Aerial.SetMode(VolleyAerialSequence.RallyMode.Pass);
        }

        /// <summary>CONTROL tutorial_start: the RECEIVE tutorial in RECEIVE, else the SPIKE + BLOCK one.</summary>
        public void StartTutorial()
        {
            var course = _drill.Drill == VolleyDrill.Block && _drill.Aerial != null && _drill.Aerial.Mode == VolleyAerialSequence.RallyMode.Pass
                ? VolleyTutorial.Course.Receive : VolleyTutorial.Course.SpikeBlock;
            var menu = FindAnyObjectByType<VolleyHandMenu>();
            if (menu != null) menu.StartTutorial(course);
            else GetComponent<VolleyTutorial>()?.Begin(course);
        }

        public void Restart()
        {
            GetComponent<VolleyTutorial>()?.Stop(false);
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
