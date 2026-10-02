using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnergyDuel.Runtime;
using Hapbeat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.OpenXR;
using Object = UnityEngine.Object;

namespace EnergyDuel.Editor
{
    /// <summary>
    /// One-time, non-destructive creation of the Energy Duel scene from a copy of
    /// VolleyReceive-codex.unity (for its hand-tracking rig and arena). Only the copy is edited:
    /// GloveBall gameplay is removed, GloveBall rig scripts are swapped for their Energy Duel
    /// counterparts, and the duel is added. An existing scene is never overwritten.
    /// </summary>
    public static class EnergyDuelSceneSetup
    {
        public const string SourceScenePath = "Assets/GloveBallDemo/Scenes/VolleyReceive-codex.unity";
        public const string ScenePath = "Assets/EnergyDuel/Scenes/EnergyDuel.unity";
        public const string MaterialsDir = "Assets/EnergyDuel/Materials";
        /// <summary>The rig's input actions; Energy Duel reads trigger / grip / menu / B / Y from it.</summary>
        public const string InputActionsPath = "Assets/GloveBallDemo/Input/DemoControls.inputactions";

        public const string DuelRootName = "Energy Duel";
        public const string RigRootName = "Energy Duel Rig";

        /// <summary>GloveBall gameplay objects removed from the copy, by path from a root.</summary>
        public static readonly string[] DroppedObjects =
        {
            "Volley Drill - Receive",
            "Volley Torso",
            "Volley Joined Hands",
            "Volley Net",
            "GloveBallDemo/HapticEventRelay",
            "GloveBallDemo/BallPool",
            "GloveBallDemo/Launchers",
            "GloveBallDemo/PlayerHitZone",
            "GloveBallDemo/Targets",
            "GloveBallDemo/DemoGameController",
            "GloveBallDemo/XR Device Simulator (EditorOnly)",
            "GloveBallDemo/XR Origin/Camera Offset/Main Camera/QuestMenu",
            "GloveBallDemo/XR Origin/Camera Offset/Main Camera/Volley Tracking Warning",
            "GloveBallDemo/XR Origin/Camera Offset/LeftHand",
            "GloveBallDemo/XR Origin/Camera Offset/RightHand",
        };

        [MenuItem("GloveBall Demo/Energy Duel/Open")]
        public static void Open()
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogWarning($"[EnergyDuel] {ScenePath} does not exist yet; use Create Scene Copy (once).");
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("GloveBall Demo/Energy Duel/Create Scene Copy (once)")]
        public static void CreateSceneCopyMenu() => CreateSceneCopy();

        /// <summary>Creates the scene if it does not exist. Returns false (and changes nothing) if it does.</summary>
        public static bool CreateSceneCopy()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            if (File.Exists(ScenePath))
            {
                Debug.LogWarning($"[EnergyDuel] {ScenePath} already exists. Open and adjust it; it is never regenerated.");
                return false;
            }

            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your edited scene first; setup will not save or overwrite it.");

            var sourceBytes = File.ReadAllBytes(SourceScenePath);
            EnergyDuelHaptics.EnsureEventMap();
            var materials = EnsureMaterials();
            EnsureHandTrackingFeature();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? ".");
            if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath)) throw new IOException("Cannot copy scene to " + ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var dropped = DropGloveBallGameplay(scene);
            var rig = SwapRigScripts(scene);
            BuildDuel(scene, rig, materials);

            var leftovers = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(c => c != null && c.GetType().Namespace != null && c.GetType().Namespace.StartsWith("GloveBallDemo", StringComparison.Ordinal))
                .Select(c => $"{EnergyDuelBatch.PathOf(c.transform)} :: {c.GetType().Name}")
                .ToArray();
            if (leftovers.Length > 0) Debug.LogError("[EnergyDuel] GloveBall scripts left in the scene:\n" + string.Join("\n", leftovers));

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Cannot save " + ScenePath);
            if (!sourceBytes.SequenceEqual(File.ReadAllBytes(SourceScenePath))) throw new Exception("Source scene changed unexpectedly");
            Debug.Log($"[EnergyDuel] created {ScenePath} from {SourceScenePath}; source byte-identical.\nDropped:\n  " + string.Join("\n  ", dropped));
            return true;
        }

        // ------------------------------------------------------------------ dropping

        private static List<string> DropGloveBallGameplay(Scene scene)
        {
            var dropped = new List<string>();
            foreach (var path in DroppedObjects)
            {
                var target = Find(scene, path);
                if (target == null)
                {
                    Debug.LogWarning($"[EnergyDuel] expected object not found (already absent): {path}");
                    continue;
                }

                var components = string.Join(", ", target.GetComponentsInChildren<MonoBehaviour>(true)
                    .Where(c => c != null).Select(c => c.GetType().Name).Distinct());
                dropped.Add($"{path}  [{components}]");
                Object.DestroyImmediate(target.gameObject);
            }

            return dropped;
        }

        // ------------------------------------------------------------------ rig

        private sealed class Rig
        {
            public Transform Root;
            public Transform Head;
            public Transform StartAnchor;
            public EnergyTrackedHand Left;
            public EnergyTrackedHand Right;
        }

        private static Rig SwapRigScripts(Scene scene)
        {
            var rig = new Rig
            {
                Root = Find(scene, "GloveBallDemo"),
                Head = Find(scene, "GloveBallDemo/XR Origin/Camera Offset/Main Camera"),
                StartAnchor = Find(scene, "GloveBallDemo/XR Start Anchor"),
            };
            if (rig.Root == null || rig.Head == null || Find(scene, "GloveBallDemo/HapbeatManager") == null)
                throw new InvalidOperationException("Source rig not found (GloveBallDemo / Main Camera / HapbeatManager).");

            rig.Root.name = RigRootName;
            Swap(rig.Root.gameObject, "DemoHapticsGate", typeof(EnergyHapticsGate), (from, to) => Copy(from, to, "_sdkRoot"));

            var origin = Find(scene, RigRootName + "/XR Origin");
            Swap(origin.gameObject, "XrRigBootstrap", typeof(EnergyRigBootstrap), (from, to) => Copy(from, to, "_actions", "_head"));
            Swap(origin.gameObject, "VolleyFloorTracking", typeof(EnergyFloorTracking), (from, to) =>
                Copy(from, to, "Origin", "RepositionTarget", "RecenterOnFirstTracking", "HeightCorrection"));

            rig.Left = SwapHand(scene, "Volley Left Palm", "Energy Left Hand");
            rig.Right = SwapHand(scene, "Volley Right Palm", "Energy Right Hand");

            foreach (var side in new[] { "Left", "Right" })
            {
                var ghost = Find(scene, $"{RigRootName}/XR Origin/Camera Offset/Volley {side} Ghost Hand");
                if (ghost == null) throw new InvalidOperationException($"Volley {side} Ghost Hand not found");
                ghost.name = $"Energy {side} Ghost Hand";
                var hand = side == "Left" ? rig.Left : rig.Right;
                EnergyGhostHand newGhost = null;
                Swap(ghost.gameObject, "VolleyGhostHand", typeof(EnergyGhostHand), (from, to) =>
                {
                    Copy(from, to, "Skeleton", "Mesh");
                    newGhost = (EnergyGhostHand)to;
                    newGhost.Hand = hand;
                });
                Swap(ghost.gameObject, "VolleyHandModelResolver", typeof(EnergyHandModelResolver), (from, to) =>
                {
                    Copy(from, to, "PrivateResourcePath", "FallbackPrefab", "Materials", "Skeleton");
                    ((EnergyHandModelResolver)to).Ghost = newGhost;
                });
            }

            // The far scoreboard keeps its Hapbeat connection readout; its GloveBall texts are blanked.
            var board = Find(scene, RigRootName + "/Scoreboard");
            if (board != null)
            {
                foreach (var text in board.GetComponentsInChildren<Text>(true))
                {
                    if (text.name == "HapbeatStatus") continue;
                    text.text = text.name == "Message" ? "ENERGY DUEL" : string.Empty;
                }
            }

            return rig;
        }

        private static EnergyTrackedHand SwapHand(Scene scene, string sourceName, string newName)
        {
            var palm = Find(scene, sourceName);
            if (palm == null) throw new InvalidOperationException(sourceName + " not found");
            palm.name = newName;
            EnergyTrackedHand hand = null;
            Swap(palm.gameObject, "VolleyTrackedHand", typeof(EnergyTrackedHand), (from, to) =>
            {
                Copy(from, to,
                    "TrackingSpace", "Visual", "ControllerRotationOffset", "ControllerPositionOffset",
                    "MaximumTrackedSpeed", "MaximumHandSpeed", "ReacquireDelay", "BriefLossSeconds",
                    "MaximumPredictionDistance", "FastSwingSpeed", "FastSwingLossSeconds", "FastSwingPredictionDistance",
                    "VisualHoldSeconds");
                // Enums map by index: Left/Right and Automatic/HandsOnly/ControllersOnly share their order.
                var source = new SerializedObject(from);
                var target = new SerializedObject(to);
                target.FindProperty("Side").enumValueIndex = source.FindProperty("Side").enumValueIndex;
                target.FindProperty("InputMode").enumValueIndex = source.FindProperty("InputMode").enumValueIndex;
                target.ApplyModifiedPropertiesWithoutUndo();
                hand = (EnergyTrackedHand)to;
            });
            // The Volley ball contact volume has no role here.
            foreach (var box in palm.GetComponents<BoxCollider>()) Object.DestroyImmediate(box);
            return hand;
        }

        /// <summary>Adds the replacement, lets the caller copy values across, then removes the GloveBall component.</summary>
        private static void Swap(GameObject go, string oldTypeName, Type newType, Action<Component, Component> copy)
        {
            var old = go.GetComponents<MonoBehaviour>().FirstOrDefault(c => c != null && c.GetType().Name == oldTypeName);
            if (old == null) throw new InvalidOperationException($"{go.name} has no {oldTypeName}");
            var replacement = go.AddComponent(newType);
            copy(old, replacement);
            Object.DestroyImmediate(old);
        }

        /// <summary>Copies same-named serialized fields from a GloveBall component to its Energy Duel counterpart.</summary>
        private static void Copy(Component from, Component to, params string[] fields)
        {
            var source = new SerializedObject(from);
            var target = new SerializedObject(to);
            foreach (var field in fields)
            {
                var property = source.FindProperty(field);
                if (property == null) throw new InvalidOperationException($"{from.GetType().Name}.{field} not found");
                if (target.FindProperty(field) == null) throw new InvalidOperationException($"{to.GetType().Name}.{field} not found");
                target.CopyFromSerializedProperty(property);
            }

            target.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ duel

        private sealed class Materials
        {
            public Material PlayerShot, OpponentShot, Shield, Opponent, Target, HpBack, HpFill;
        }

        private static void BuildDuel(Scene scene, Rig rig, Materials materials)
        {
            var root = new GameObject(DuelRootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            var anchor = rig.StartAnchor != null ? rig.StartAnchor : rig.Root;
            var forward = Vector3.ProjectOnPlane(anchor.forward, Vector3.up);
            forward = forward.sqrMagnitude > .25f ? forward.normalized : Vector3.forward;

            var pool = root.AddComponent<EnergyShotPool>();
            pool.PlayerMaterial = materials.PlayerShot;
            pool.OpponentMaterial = materials.OpponentShot;

            var relay = root.AddComponent<EnergyHapticRelay>();
            WireHaptics(root, relay);

            var actions = AssetDatabase.LoadAllAssetRepresentationsAtPath(InputActionsPath).OfType<InputActionReference>().ToArray();
            InputActionReference Action(string actionName)
            {
                var reference = actions.FirstOrDefault(a => a.action != null && a.action.name == actionName);
                if (reference == null) Debug.LogError($"[EnergyDuel] input action '{actionName}' not found in {InputActionsPath}");
                return reference;
            }

            var left = BuildHand(rig.Left, rig.Head, Action("TriggerLeft"), Action("LeftGrip"), materials);
            var right = BuildHand(rig.Right, rig.Head, Action("TriggerRight"), Action("RightGrip"), materials);

            var hurtboxes = new[]
            {
                BuildHurtbox(root.transform, "Energy Body", EnergyHurtboxKind.Body, rig.Head, null, new Vector3(0f, -.65f, 0f), true, .22f),
                BuildHurtbox(root.transform, "Energy Head", EnergyHurtboxKind.Head, rig.Head, null, Vector3.zero, false, .13f),
                BuildHurtbox(root.transform, "Energy Left Arm", EnergyHurtboxKind.LeftArm, null, left.input, Vector3.zero, false, .09f),
                BuildHurtbox(root.transform, "Energy Right Arm", EnergyHurtboxKind.RightArm, null, right.input, Vector3.zero, false, .09f),
            };

            var opponent = BuildOpponent(root.transform, anchor.position + (forward * 6.5f) + (Vector3.up * 1.4f), forward, pool, rig.Head, materials);
            var target = BuildTarget(root.transform, materials);
            var hud = BuildHud(root.transform, anchor.position + (forward * 2.4f) + (Vector3.up * 2.35f), forward);

            var duel = root.AddComponent<EnergyDuelController>();
            duel.Head = rig.Head;
            duel.LeftHand = left.input;
            duel.RightHand = right.input;
            duel.LeftShield = left.shield;
            duel.RightShield = right.shield;
            duel.Hurtboxes = hurtboxes;
            duel.Pool = pool;
            duel.Opponent = opponent;
            duel.TutorialTarget = target;
            duel.TitleText = hud.Title;
            duel.BodyText = hud.Body;
            duel.HintText = hud.Hint;
            duel.ScoreText = hud.Score;
            duel.StatusText = hud.Status;
            duel.Icon = hud.Icon;
            var skip = new SerializedObject(duel);
            var skipActions = skip.FindProperty("SkipActions");
            var skipNames = new[] { "LeftMenu", "LeftSecondary", "RightSecondary" };
            skipActions.arraySize = skipNames.Length;
            for (var i = 0; i < skipNames.Length; i++) SetActionReference(skipActions.GetArrayElementAtIndex(i), Action(skipNames[i]));
            skip.ApplyModifiedPropertiesWithoutUndo();
        }

        private static (EnergyHandInput input, EnergyShield shield) BuildHand(EnergyTrackedHand hand, Transform head, InputActionReference trigger, InputActionReference grip, Materials materials)
        {
            var go = hand.gameObject;
            var input = go.AddComponent<EnergyHandInput>();
            input.Hand = hand;
            input.Head = head;
            var so = new SerializedObject(input);
            SetActionReference(so.FindProperty("Trigger"), trigger);
            SetActionReference(so.FindProperty("Grip"), grip);
            so.ApplyModifiedPropertiesWithoutUndo();

            var charge = go.AddComponent<EnergyCharge>();
            charge.Input = input;
            charge.OrbMaterial = materials.PlayerShot;

            var shield = go.AddComponent<EnergyShield>();
            shield.Input = input;
            shield.DiscMaterial = materials.Shield;
            return (input, shield);
        }

        private static EnergyHurtbox BuildHurtbox(Transform parent, string name, EnergyHurtboxKind kind, Transform head, EnergyHandInput hand, Vector3 offset, bool capsule, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (capsule)
            {
                var collider = go.AddComponent<CapsuleCollider>();
                collider.radius = radius;
                collider.height = 1f;
                collider.direction = 1;
                collider.isTrigger = true;
            }
            else
            {
                var collider = go.AddComponent<SphereCollider>();
                collider.radius = radius;
                collider.isTrigger = true;
            }

            var hurtbox = go.AddComponent<EnergyHurtbox>();
            hurtbox.Kind = kind;
            hurtbox.Head = head;
            hurtbox.Hand = hand;
            hurtbox.HeadOffset = offset;
            return hurtbox;
        }

        private static EnergyOpponent BuildOpponent(Transform parent, Vector3 center, Vector3 forward, EnergyShotPool pool, Transform head, Materials materials)
        {
            var go = new GameObject("Energy Opponent");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, Quaternion.LookRotation(-forward, Vector3.up));
            go.AddComponent<SphereCollider>().radius = .32f;

            var core = Primitive(PrimitiveType.Sphere, "Core", go.transform, Vector3.zero, Vector3.one * .5f, materials.Opponent);
            var ring = Primitive(PrimitiveType.Cylinder, "Ring", go.transform, Vector3.zero, new Vector3(.95f, .015f, .95f), materials.Opponent);
            ring.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);

            var bar = new GameObject("HP Bar").transform;
            bar.SetParent(go.transform, false);
            bar.localPosition = new Vector3(0f, .62f, 0f);
            Primitive(PrimitiveType.Cube, "Back", bar, Vector3.zero, new Vector3(1f, .08f, .02f), materials.HpBack);
            var pivot = new GameObject("Fill Pivot").transform;
            pivot.SetParent(bar, false);
            pivot.localPosition = new Vector3(-.5f, 0f, .015f);
            Primitive(PrimitiveType.Cube, "Fill", pivot, new Vector3(.5f, 0f, 0f), new Vector3(1f, .06f, .02f), materials.HpFill);

            var opponent = go.AddComponent<EnergyOpponent>();
            opponent.Pool = pool;
            opponent.PlayerHead = head;
            opponent.CourtCenter = center;
            opponent.CourtSize = new Vector3(5f, .4f, 3f);
            opponent.Core = core.GetComponent<Renderer>();
            opponent.Ring = ring.GetComponent<Renderer>();
            opponent.HpFill = pivot;
            return opponent;
        }

        private static EnergyTutorialTarget BuildTarget(Transform parent, Materials materials)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Energy Tutorial Target";
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * .6f;
            go.GetComponent<MeshRenderer>().sharedMaterial = materials.Target;
            var target = go.AddComponent<EnergyTutorialTarget>();
            target.Body = go.GetComponent<MeshRenderer>();
            go.SetActive(false);
            return target;
        }

        private sealed class Hud
        {
            public Text Title, Body, Hint, Score, Status;
            public EnergyHandIcon Icon;
        }

        private static Hud BuildHud(Transform parent, Vector3 position, Vector3 forward)
        {
            var go = new GameObject("Energy Duel HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(1000f, 420f);
            rect.localScale = Vector3.one * .002f;
            rect.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(rect, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(.02f, .04f, .08f, .65f);
            panelImage.raycastTarget = false;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Text Label(string name, float x, float y, float width, float height, int size, Color color)
            {
                var label = new GameObject(name, typeof(RectTransform), typeof(Text));
                var labelRect = (RectTransform)label.transform;
                labelRect.SetParent(rect, false);
                labelRect.anchorMin = labelRect.anchorMax = new Vector2(.5f, .5f);
                labelRect.anchoredPosition = new Vector2(x, y);
                labelRect.sizeDelta = new Vector2(width, height);
                var text = label.GetComponent<Text>();
                text.font = font;
                text.fontSize = size;
                text.color = color;
                text.alignment = TextAnchor.MiddleCenter;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                text.raycastTarget = false;
                text.text = string.Empty;
                return text;
            }

            var hud = new Hud
            {
                Title = Label("Title", 90f, 140f, 780f, 80f, 64, Color.white),
                Body = Label("Body", 90f, 55f, 780f, 90f, 38, new Color(.85f, .95f, 1f)),
                Hint = Label("Hint", 90f, -25f, 780f, 60f, 32, new Color(1f, .85f, .3f)),
                Score = Label("Score", 0f, -95f, 960f, 60f, 44, Color.white),
                Status = Label("Status", 0f, -165f, 960f, 50f, 32, new Color(1f, .4f, .35f)),
            };

            var icon = new GameObject("Hand Icon", typeof(RectTransform));
            var iconRect = (RectTransform)icon.transform;
            iconRect.SetParent(rect, false);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(.5f, .5f);
            iconRect.anchoredPosition = new Vector2(-400f, 60f);
            iconRect.sizeDelta = new Vector2(180f, 180f);
            hud.Icon = icon.AddComponent<EnergyHandIcon>();
            return hud;
        }

        // ------------------------------------------------------------------ haptics

        private static void WireHaptics(GameObject go, EnergyHapticRelay relay)
        {
            var map = AssetDatabase.LoadAssetAtPath<HapbeatEventMap>(EnergyDuelHaptics.EventMapPath);
            if (map == null) throw new InvalidOperationException("EventMap missing: " + EnergyDuelHaptics.EventMapPath);
            var so = new SerializedObject(relay);
            var oneShots = so.FindProperty("_bindings");
            var loops = so.FindProperty("_loopBindings");
            oneShots.arraySize = 0;
            loops.arraySize = 0;
            foreach (var definition in EnergyDuelHaptics.Definitions)
            {
                var entry = map.entries.FirstOrDefault(e => e.eventName == definition.Name);
                if (entry == null) throw new InvalidOperationException("EventMap entry missing: " + definition.Name);
                SerializedProperty element;
                if (definition.Loop)
                {
                    var trigger = go.AddComponent<HapbeatSequenceTrigger>();
                    trigger.EditorSetupEntry(map, entry.id);
                    var triggerSo = new SerializedObject(trigger);
                    triggerSo.FindProperty("_onStartEntryId").stringValue = string.Empty;
                    triggerSo.FindProperty("_onStopEntryId").stringValue = string.Empty;
                    // No one-shot to wait for, so the loop starts immediately.
                    triggerSo.FindProperty("_startShotDelay").floatValue = 0f;
                    triggerSo.ApplyModifiedPropertiesWithoutUndo();
                    loops.arraySize++;
                    element = loops.GetArrayElementAtIndex(loops.arraySize - 1);
                    element.FindPropertyRelative("Trigger").objectReferenceValue = trigger;
                }
                else
                {
                    var trigger = go.AddComponent<HapbeatUnityEventTrigger>();
                    trigger.EditorSetupEntry(map, entry.id);
                    oneShots.arraySize++;
                    element = oneShots.GetArrayElementAtIndex(oneShots.arraySize - 1);
                    element.FindPropertyRelative("Trigger").objectReferenceValue = trigger;
                }

                element.FindPropertyRelative("Event").enumValueIndex = (int)definition.Event;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ assets

        private static Materials EnsureMaterials() => new Materials
        {
            PlayerShot = EnsureMaterial("EnergyPlayerShot", new Color(.35f, .8f, 1f, 1f), false),
            OpponentShot = EnsureMaterial("EnergyOpponentShot", new Color(1f, .3f, .2f, 1f), false),
            Shield = EnsureMaterial("EnergyShield", new Color(.3f, .9f, 1f, .45f), true),
            Opponent = EnsureMaterial("EnergyOpponent", new Color(.9f, .2f, .15f, 1f), false),
            Target = EnsureMaterial("EnergyTarget", new Color(1f, .8f, .2f, 1f), false),
            HpBack = EnsureMaterial("EnergyHpBack", new Color(.1f, .1f, .1f, 1f), false),
            HpFill = EnsureMaterial("EnergyHpFill", new Color(.3f, 1f, .4f, 1f), false),
        };

        /// <summary>URP Unlit, created once; an existing material (possibly hand-tuned) is reused as is.</summary>
        private static Material EnsureMaterial(string name, Color color, bool transparent)
        {
            var path = $"{MaterialsDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            Directory.CreateDirectory(MaterialsDir);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP Unlit shader not found");
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_Cull", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Enables OpenXR's XR Hands HandTracking feature only when it is off (same check as GloveBall's ConfigureHands).</summary>
        private static void EnsureHandTrackingFeature()
        {
            foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                FeatureHelpers.RefreshFeatures(group);
                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                if (settings == null) throw new InvalidOperationException("OpenXR settings missing: " + group);
                var feature = settings.GetFeatures().FirstOrDefault(f => f.GetType().Name == "HandTracking");
                if (feature == null) throw new InvalidOperationException("XR Hands HandTracking feature missing: " + group);
                if (feature.enabled) continue;
                feature.enabled = true;
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                Debug.Log($"[EnergyDuel] enabled OpenXR HandTracking for {group}");
            }
        }

        // ------------------------------------------------------------------ utils

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private static void SetActionReference(SerializedProperty property, InputActionReference action)
        {
            property.FindPropertyRelative("m_UseReference").boolValue = true;
            property.FindPropertyRelative("m_Reference").objectReferenceValue = action;
        }

        /// <summary>Finds a transform by "Root/Child/..." path, inactive objects included.</summary>
        internal static Transform Find(Scene scene, string path)
        {
            var parts = path.Split('/');
            var current = scene.GetRootGameObjects().FirstOrDefault(r => r.name == parts[0])?.transform;
            for (var i = 1; i < parts.Length && current != null; i++)
            {
                Transform next = null;
                foreach (Transform child in current)
                {
                    if (child.name == parts[i])
                    {
                        next = child;
                        break;
                    }
                }

                current = next;
            }

            return current;
        }
    }
}
