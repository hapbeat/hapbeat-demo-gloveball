using System.Collections.Generic;
using System.Text;
using EnergyDuel.Core;
using EnergyDuel.Runtime;
using Hapbeat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EnergyDuel.Editor
{
    /// <summary>
    /// Batch play-through of the Energy Duel scene with <see cref="EnergyAutoPilot"/>: tutorial,
    /// one round, result, and the restart that skips the tutorial. Fails (non-zero exit) on any
    /// logged error or if a gameplay path was never exercised. Haptics stay off the network: the
    /// relay's event stream is counted, nothing is sent.
    ///
    /// Run: tools/run-unity.ps1 -Method EnergyDuel.Editor.EnergyDuelAutoPlay.Run -NoQuit
    /// (-NoQuit: the editor loop must keep ticking; this exits the editor itself).
    /// </summary>
    [InitializeOnLoad]
    public static class EnergyDuelAutoPlay
    {
        private const string ActiveKey = "EnergyDuel.AutoPlay.Active";
        private const string ErrorsKey = "EnergyDuel.AutoPlay.Errors";
        private const string DurationKey = "EnergyDuel.AutoPlay.Duration";
        private const float DefaultDuration = 80f;
        private const float RealtimeCap = 480f;

        private static readonly List<string> ErrorSamples = new List<string>();
        private static readonly Dictionary<EnergyHapticEvent, int> HapticTally = new Dictionary<EnergyHapticEvent, int>();
        private static readonly List<string> Phases = new List<string>();
        private static bool _hooked;
        private static bool _started;
        private static float _startRealtime;
        private static float _startGameTime;
        private static EnergyDuelController _duel;

        static EnergyDuelAutoPlay()
        {
            // Entering play mode reloads the domain; re-attach.
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        public static void Run()
        {
            EnergyDuelBatch.MuteEditorAudio();
            var seconds = float.TryParse(EnergyDuelBatch.GetArg("-edSeconds", ""), out var parsed) && parsed > 0f ? parsed : DefaultDuration;
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetInt(ErrorsKey, 0);
            SessionState.SetFloat(DurationKey, seconds);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(EnergyDuelSceneSetup.ScenePath) == null)
            {
                Debug.LogError($"[EnergyAutoPlay] scene not found: {EnergyDuelSceneSetup.ScenePath}");
                Finish(20);
                return;
            }

            EditorSceneManager.OpenScene(EnergyDuelSceneSetup.ScenePath, OpenSceneMode.Single);
            Hook();
            Debug.Log($"[EnergyAutoPlay] entering play mode for {seconds:0}s of game time");
            EditorApplication.isPlaying = true;
        }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            _started = false;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += OnUpdate;
        }

        private static void Unhook()
        {
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= OnUpdate;
            _hooked = false;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            SessionState.SetInt(ErrorsKey, SessionState.GetInt(ErrorsKey, 0) + 1);
            if (ErrorSamples.Count < 20) ErrorSamples.Add($"{type}: {condition}");
        }

        private static void OnUpdate()
        {
            if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying) return;
            // The Console's Error Pause preference is shared with the developer's editor; an
            // unattended run must keep playing (every error is still counted below).
            if (EditorApplication.isPaused)
            {
                EditorApplication.isPaused = false;
                Debug.Log("[EnergyAutoPlay] play mode was paused (Console Error Pause); resumed");
            }

            // Batch mode does not render, so step the player loop explicitly.
            EditorApplication.QueuePlayerLoopUpdate();
            if (!_started)
            {
                _started = true;
                _startRealtime = Time.realtimeSinceStartup;
                _startGameTime = Time.time;
                AudioListener.volume = 0f;
                AudioListener.pause = true;
                Begin();
                return;
            }

            var elapsed = Time.time - _startGameTime;
            var wall = Time.realtimeSinceStartup - _startRealtime;
            if (elapsed < SessionState.GetFloat(DurationKey, DefaultDuration) && wall < RealtimeCap) return;
            if (wall >= RealtimeCap) Debug.LogError($"[EnergyAutoPlay] hit the {RealtimeCap:0}s wall-clock cap after {elapsed:0.0}s of game time");
            Report(elapsed);
            EditorApplication.isPlaying = false;
            Finish(SessionState.GetInt(ErrorsKey, 0) == 0 ? 0 : 21);
        }

        private static void Begin()
        {
            Debug.Log($"[EnergyAutoPlay] haptics live send: {(EnergyHapticsGate.LiveSendEnabled ? "ON" : "OFF")}");
            _duel = Object.FindFirstObjectByType<EnergyDuelController>();
            if (_duel == null)
            {
                Debug.LogError("[EnergyAutoPlay] no EnergyDuelController in the scene");
                return;
            }

            HapticTally.Clear();
            Phases.Clear();
            if (EnergyHapticRelay.Instance == null) Debug.LogError("[EnergyAutoPlay] no EnergyHapticRelay");
            else EnergyHapticRelay.Instance.EventReported += (evt, _, __) => HapticTally[evt] = HapticTally.TryGetValue(evt, out var n) ? n + 1 : 1;
            _duel.PhaseChanged += (phase, step) => Phases.Add(phase == EnergyDuelPhase.Tutorial ? $"Tutorial:{step}" : phase.ToString());

            // No headset in batch: lift the head to standing eye height so shots fly at body level.
            var head = _duel.Head;
            if (head.position.y < 1f) head.position += Vector3.up * (1.6f - head.position.y);
            _duel.gameObject.AddComponent<EnergyAutoPilot>().Duel = _duel;
            Debug.Log("[EnergyAutoPlay] autopilot attached");
        }

        private static void Report(float elapsed)
        {
            if (_duel == null)
            {
                Debug.LogError("[EnergyAutoPlay] nothing to report: no duel controller");
                return;
            }

            var opponent = _duel.Opponent;
            var text = new StringBuilder();
            text.AppendLine($"[EnergyAutoPlay] ran {elapsed:0.0}s of game time over {Time.frameCount} frames");
            text.AppendLine($"phases: {string.Join(" > ", Phases)}");
            text.AppendLine($"opponentShots={(opponent != null ? opponent.ShotsFired : -1)} shieldBlocks={_duel.ShieldBlocks} shieldBreaks={_duel.ShieldBreaks} " +
                            $"bodyHits={_duel.BodyHits} armHits={_duel.ArmHits} playerShots={_duel.PlayerShots} opponentHits={_duel.OpponentHits} targetHits={_duel.TargetHits} " +
                            $"cancelledPairs={(_duel.Pool != null ? _duel.Pool.CancelledPairs : -1)}");
            text.AppendLine($"tutorialsCompleted={_duel.TutorialsCompleted} roundsStarted={_duel.RoundsStarted} roundsWithoutTutorial={_duel.RoundsStartedWithoutTutorial} results={_duel.ResultsShown} " +
                            $"phaseNow={_duel.Rules.Phase} hp={_duel.Rules.OpponentHp:0} matchesPlayed={_duel.Rules.MatchesPlayed}");
            var tally = new StringBuilder();
            foreach (EnergyHapticEvent evt in System.Enum.GetValues(typeof(EnergyHapticEvent)))
            {
                HapticTally.TryGetValue(evt, out var count);
                tally.Append($"{evt}={count} ");
                var relay = EnergyHapticRelay.Instance;
                if (relay == null) continue;
                var trigger = (HapbeatTriggerBase)relay.GetLoopTrigger(evt) ?? relay.GetTrigger(evt);
                if (trigger == null) Debug.LogError($"[EnergyAutoPlay] {evt} has no Hapbeat trigger bound");
                else if (trigger.ResolveEntry() == null) Debug.LogError($"[EnergyAutoPlay] {evt}'s trigger resolves to no EventMap entry");
            }

            text.AppendLine($"haptic events (reported, not sent): {tally.ToString().TrimEnd()}");
            Debug.Log(text.ToString());

            Require(opponent != null ? opponent.ShotsFired : 0, "opponent shots");
            Require(_duel.ShieldBlocks, "shield blocks");
            Require(_duel.ShieldBreaks, "shield breaks");
            Require(_duel.BodyHits + _duel.ArmHits, "hits on the player");
            Require(_duel.PlayerShots, "player shots");
            Require(_duel.OpponentHits, "hits on the opponent");
            Require(_duel.TargetHits, "tutorial target hits");
            Require(_duel.TutorialsCompleted, "completed tutorials");
            Require(_duel.ResultsShown, "results");
            Require(_duel.RoundsStartedWithoutTutorial, "restarts straight into a round");
            Require(Count(EnergyHapticEvent.ShieldBreakNeck), "shield_break_neck haptics");
            Require(Count(EnergyHapticEvent.RightChargeFull), "r_charge_full haptics");
            Require(Count(EnergyHapticEvent.RightRelease), "r_release haptics");
        }

        private static int Count(EnergyHapticEvent evt) => HapticTally.TryGetValue(evt, out var n) ? n : 0;

        private static void Require(int actual, string label)
        {
            if (actual < 1) Debug.LogError($"[EnergyAutoPlay] no {label} happened");
        }

        private static void Finish(int exitCode)
        {
            Debug.Log($"[EnergyAutoPlay] finished: errorLogs={SessionState.GetInt(ErrorsKey, 0)} exitCode={exitCode}");
            foreach (var sample in ErrorSamples) Debug.Log($"[EnergyAutoPlay] LOGGED {sample}");
            SessionState.SetBool(ActiveKey, false);
            Unhook();
            EnergyDuelBatch.RestoreEditorAudio();
            EditorApplication.Exit(exitCode);
        }
    }
}
