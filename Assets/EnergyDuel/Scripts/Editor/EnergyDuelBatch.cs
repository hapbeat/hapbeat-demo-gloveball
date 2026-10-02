using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EnergyDuel.Editor
{
    /// <summary>
    /// Batch-mode entry points for Energy Duel (run through tools/run-unity.ps1). Every batch entry
    /// mutes the editor first and hands the mute back when it ends; nothing here sends haptics.
    /// </summary>
    public static class EnergyDuelBatch
    {
        // EditorUtility.audioMasterMute is shared by every project on the machine (EditorPrefs), so
        // the developer's setting is parked here and restored on exit. Keys are Energy Duel's own.
        private const string AudioMuteOwedKey = "EnergyDuel.Batch.AudioMuteOwed";
        private const string AudioMutePreviousKey = "EnergyDuel.Batch.AudioMutePrevious";
        private static bool _quitHookInstalled;

        public static void MuteEditorAudio()
        {
            if (!Application.isBatchMode) return;
            if (!EditorPrefs.GetBool(AudioMuteOwedKey, false))
            {
                EditorPrefs.SetBool(AudioMutePreviousKey, EditorUtility.audioMasterMute);
                EditorPrefs.SetBool(AudioMuteOwedKey, true);
            }

            EditorUtility.audioMasterMute = true;
            if (_quitHookInstalled) return;
            _quitHookInstalled = true;
            EditorApplication.quitting += RestoreEditorAudio;
        }

        public static void RestoreEditorAudio()
        {
            if (!EditorPrefs.GetBool(AudioMuteOwedKey, false)) return;
            var previous = EditorPrefs.GetBool(AudioMutePreviousKey, false);
            EditorUtility.audioMasterMute = previous;
            EditorPrefs.DeleteKey(AudioMuteOwedKey);
            EditorPrefs.DeleteKey(AudioMutePreviousKey);
            Debug.Log($"[EnergyDuelBatch] restored editor audioMasterMute = {previous}");
        }

        internal static string GetArg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return fallback;
        }

        internal static bool HasFlag(string name) => Environment.GetCommandLineArgs().Contains(name);

        // ------------------------------------------------------------------ setup

        /// <summary>Creates the Energy Duel EventMap (additive) and the scene copy (once).</summary>
        public static void Setup()
        {
            MuteEditorAudio();
            EnergyDuelHaptics.EnsureEventMap();
            EnergyDuelSceneSetup.CreateSceneCopy();
        }

        // ---------------------------------------------------------------- reports

        /// <summary>Hierarchy, components and positions of a scene (-edScene), for inspection.</summary>
        public static void DumpScene()
        {
            MuteEditorAudio();
            var path = GetArg("-edScene", EnergyDuelSceneSetup.ScenePath);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var text = new StringBuilder();
            text.AppendLine($"[EnergyDuelBatch] dump {path}");
            foreach (var root in scene.GetRootGameObjects()) Dump(root.transform, 0, text);
            Debug.Log(text.ToString());
        }

        private static void Dump(Transform t, int depth, StringBuilder text)
        {
            if (depth > 4) return;
            var components = t.GetComponents<Component>()
                .Select(c => c == null ? "<MISSING>" : c.GetType().Name)
                .Where(n => n != "Transform" && n != "RectTransform");
            text.AppendLine($"{new string(' ', depth * 2)}{t.name} [{(t.gameObject.activeSelf ? "on" : "off")}] pos={t.position:F2} :: {string.Join(", ", components)}");
            if (t.childCount > 40)
            {
                text.AppendLine($"{new string(' ', (depth + 1) * 2)}({t.childCount} children)");
                return;
            }

            foreach (Transform child in t) Dump(child, depth + 1, text);
        }

        /// <summary>
        /// Extraction manifest: every script and asset the Energy Duel scene still takes from outside
        /// Assets/EnergyDuel, plus missing scripts. Exits non-zero when a script is missing.
        /// </summary>
        public static void ReportDependencies()
        {
            MuteEditorAudio();
            var path = GetArg("-edScene", EnergyDuelSceneSetup.ScenePath);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var missing = 0;
            var scripts = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    foreach (var behaviour in t.GetComponents<MonoBehaviour>())
                    {
                        if (behaviour == null) continue;
                        var script = MonoScript.FromMonoBehaviour(behaviour);
                        var scriptPath = script != null ? AssetDatabase.GetAssetPath(script) : "<no script asset>";
                        if (scriptPath.StartsWith("Assets/EnergyDuel/", StringComparison.Ordinal)) continue;
                        var key = $"{behaviour.GetType().FullName}  ({scriptPath})";
                        if (!scripts.TryGetValue(key, out var users)) scripts[key] = users = new SortedSet<string>();
                        users.Add(PathOf(t));
                    }
                }
            }

            var text = new StringBuilder();
            text.AppendLine($"[EnergyDuelBatch] dependencies of {path}");
            text.AppendLine($"missingScripts={missing}");
            text.AppendLine("-- scripts outside Assets/EnergyDuel --");
            foreach (var pair in scripts)
                text.AppendLine($"{pair.Key}  x{pair.Value.Count}: {string.Join(" | ", pair.Value.Take(4))}{(pair.Value.Count > 4 ? " | ..." : "")}");

            var assets = AssetDatabase.GetDependencies(new[] { path }.Concat(EnergyDuelAssets()).ToArray(), true)
                .Where(p => !p.StartsWith("Assets/EnergyDuel/", StringComparison.Ordinal) && p != path)
                .ToArray();
            text.AppendLine($"-- assets outside Assets/EnergyDuel ({assets.Length}) by folder --");
            foreach (var group in assets.GroupBy(Folder).OrderBy(g => g.Key))
                text.AppendLine($"{group.Key}: {group.Count()}");
            text.AppendLine("-- Assets/ files (not packages) --");
            foreach (var asset in assets.Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !p.StartsWith("Assets/UltimateGloveBall/", StringComparison.Ordinal)).OrderBy(p => p))
                text.AppendLine(asset);
            Debug.Log(text.ToString());
            if (missing > 0) Debug.LogError($"[EnergyDuelBatch] {missing} missing script(s) in {path}");
        }

        private static IEnumerable<string> EnergyDuelAssets() =>
            AssetDatabase.FindAssets(string.Empty, new[] { "Assets/EnergyDuel" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !AssetDatabase.IsValidFolder(p));

        private static string Folder(string assetPath)
        {
            var parts = assetPath.Split('/');
            return parts.Length >= 3 ? $"{parts[0]}/{parts[1]}" : parts[0];
        }

        internal static string PathOf(Transform t)
        {
            var path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return path;
        }
    }
}
