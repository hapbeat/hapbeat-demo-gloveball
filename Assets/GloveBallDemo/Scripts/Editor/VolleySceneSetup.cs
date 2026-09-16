using System;
using System.IO;
using System.Linq;
using GloveBallDemo.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.OpenXR;
using Object = UnityEngine.Object;

namespace GloveBallDemo.Editor
{
    /// <summary>One-time non-destructive fork. Existing drills are never regenerated.</summary>
    public static class VolleySceneSetup
    {
        public const string ReceivePath = DemoAssetPaths.ScenesDir + "/VolleyReceive-codex.unity";
        public const string SpikePath = DemoAssetPaths.ScenesDir + "/VolleySpike-codex.unity";

        [MenuItem("GloveBall Demo/Volley/Open Receive")]
        public static void OpenReceive()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ReceivePath);
        }
        [MenuItem("GloveBall Demo/Volley/Open Spike")]
        public static void OpenSpike()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(SpikePath);
        }
        public static void OpenReceiveForVerification()
        {
            OpenReceive();
            LocalMcpConnection.Connect();
        }

        [MenuItem("GloveBall Demo/Volley/Create Receive and Spike Copies (once)")]
        public static void CreateDrills()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your edited scene first; setup will not save or overwrite it.");
            if (File.Exists(ReceivePath) || File.Exists(SpikePath)) throw new InvalidOperationException("Volley scenes already exist. Open and adjust them; do not regenerate.");
            byte[] source = File.ReadAllBytes(DemoAssetPaths.DemoScene);
            CreateCopy(ReceivePath, VolleyDrill.Receive);
            CreateCopy(SpikePath, VolleyDrill.Spike);
            if (!source.SequenceEqual(File.ReadAllBytes(DemoAssetPaths.DemoScene))) throw new Exception("Original scene changed unexpectedly");
            ConfigureHands();
            EditorSceneManager.OpenScene(ReceivePath);
            Debug.Log("[Volley] Both scene copies saved; original Demo.unity byte-identical.");
        }

        [MenuItem("GloveBall Demo/Volley/Configure Air Link and Hands")]
        public static void ConfigureHands()
        {
            DemoAirLinkBuilder.ConfigureAirLinkPlayMode();
            foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                FeatureHelpers.RefreshFeatures(group);
                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                if (settings == null) throw new InvalidOperationException("OpenXR settings missing: " + group);
                var feature = settings.GetFeatures().FirstOrDefault(f => f.GetType().Name == "HandTracking");
                if (feature == null) throw new InvalidOperationException("XR Hands HandTracking feature missing: " + group);
                feature.enabled = true;
                EditorUtility.SetDirty(feature); EditorUtility.SetDirty(settings);
            }
            AssetDatabase.SaveAssets();
        }

        private static void CreateCopy(string path, VolleyDrill mode)
        {
            if (!AssetDatabase.CopyAsset(DemoAssetPaths.DemoScene, path)) throw new IOException("Cannot copy scene to " + path);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var head = all.First(t => t.name == "Main Camera");
            var rig = all.First(t => t.name == "XR Origin");
            var pool = Find<BallPool>(scene).Single();
            var layout = Find<TargetLayoutField>(scene).Single();
            var board = Find<ScoreboardPresenter>(scene).Single();
            var boardData = new SerializedObject(board);
            var status = (Text)boardData.FindProperty("_messageText").objectReferenceValue;
            var score = (Text)boardData.FindProperty("_scoreText").objectReferenceValue;
            foreach (var field in new[] { "_comboText", "_waveText" })
            {
                var text = (Text)boardData.FindProperty(field).objectReferenceValue;
                if (text != null) text.text = "";
            }
            Object.DestroyImmediate(board);
            foreach (var c in Find<DemoGameController>(scene)) Object.DestroyImmediate(c);
            foreach (var c in Find<XrLocomotionController>(scene)) Object.DestroyImmediate(c);
            foreach (var c in Find<QuestMenuController>(scene)) c.gameObject.SetActive(false);
            foreach (var c in Find<RealHmdSimulatorGate>(scene)) Object.DestroyImmediate(c);
            foreach (var c in Find<PlayerHitZone>(scene)) c.gameObject.SetActive(false);
            foreach (var c in Find<BallLauncher>(scene)) c.gameObject.SetActive(false);
            foreach (var t in all)
                if (t.name == "LeftHand" || t.name == "RightHand" || t.name.Contains("XR Device Simulator")) t.gameObject.SetActive(false);

            // Change only the copy's pool instance, never the shared ball prefab or feel profile.
            var poolData = new SerializedObject(pool);
            var kinds = poolData.FindProperty("_launchBallKinds");
            kinds.arraySize = 1; kinds.GetArrayElementAtIndex(0).enumValueIndex = (int)BallKind.Volleyball;
            poolData.ApplyModifiedPropertiesWithoutUndo();

            var game = new GameObject("Volley Drill - " + mode).AddComponent<VolleyDrillController>();
            game.Drill = mode; game.Head = head; game.CourtFrame = rig;
            game.Pool = pool; game.Targets = layout;
            game.StatusText = status; game.ScoreText = score;
            game.Panels = Find<TargetPanel>(scene);
            game.Left = CreateHand(GloveSide.Left, head.parent);
            game.Right = CreateHand(GloveSide.Right, head.parent);
            game.ReadySeconds = 2f;
            if (mode == VolleyDrill.Spike)
            {
                game.FeedDistance = 2.8f; game.FeedHeightAboveHead = .9f;
                game.ContactHeightFromHead = .2f; game.ContactForwardDistance = .6f;
                game.FlightSeconds = .85f; game.LateralSpread = .25f;
                game.SwingGain = 1.5f;
            }
            var layoutData = new SerializedObject(layout);
            Set(layoutData, "_minX", -2.4f); Set(layoutData, "_maxX", 2.4f);
            Set(layoutData, "_minZ", -1.5f); Set(layoutData, "_maxZ", 1.5f);
            Set(layoutData, "_minHeight", mode == VolleyDrill.Spike ? .45f : 1f);
            Set(layoutData, "_maxHeight", mode == VolleyDrill.Spike ? 1.1f : 2f);
            Set(layoutData, "_minimumSpacing", 1.3f);
            layoutData.ApplyModifiedPropertiesWithoutUndo();
            var body = new GameObject("Volley Torso").AddComponent<VolleyBodySurface>();
            body.Head = head; body.Drill = game;
            status.text = mode + " - No buttons. Show hands or pick up controllers.";
            score.text = mode.ToString().ToUpperInvariant();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
        }

        private static VolleyTrackedHand CreateHand(GloveSide side, Transform trackingSpace)
        {
            var hand = new GameObject("Volley " + side + " Palm").AddComponent<VolleyTrackedHand>();
            hand.Side = side; hand.TrackingSpace = trackingSpace;
            var visual = new GameObject("Contact Face").transform;
            visual.SetParent(hand.transform, false); hand.Visual = visual;
            // Simple open-hand proxy makes the contact plane visible for both input sources.
            AddShape(visual, PrimitiveType.Cube, "Palm", Vector3.zero, new Vector3(.12f, .025f, .13f));
            for (int finger = 0; finger < 4; finger++)
                AddShape(visual, PrimitiveType.Capsule, "Finger " + finger,
                    new Vector3((finger - 1.5f) * .031f, 0, .095f), new Vector3(.023f, .06f, .023f), Quaternion.Euler(90,0,0));
            AddShape(visual, PrimitiveType.Capsule, "Thumb", new Vector3(side == GloveSide.Left ? .085f : -.085f, 0, .025f),
                new Vector3(.028f,.045f,.028f), Quaternion.Euler(90,0,side == GloveSide.Left ? -40 : 40));
            visual.gameObject.SetActive(false);
            return hand;
        }
        private static void AddShape(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            go.transform.SetParent(parent, false); go.transform.localPosition = position;
            go.transform.localRotation = rotation ?? Quaternion.identity; go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        private static T[] Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
        private static void Set(SerializedObject obj, string field, float value) => obj.FindProperty(field).floatValue = value;
    }
}
