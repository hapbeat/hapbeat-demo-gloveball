using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace EnergyDuel.Editor
{
    /// <summary>
    /// Quest sideload APK of the Energy Duel scene only. Self-contained (no reference to the GloveBall
    /// editor scripts), so it moves with Assets/EnergyDuel. The Quest settings mirror the proven
    /// GloveBall / Volley builds: IL2CPP + ARM64, Vulkan, minSdk 32, OpenXR with the Meta Quest
    /// feature (Quest Pro off, so no required eye_tracking), the three Touch profiles and the XR
    /// Hands HandTracking feature, which is what puts oculus.software.handtracking and
    /// com.oculus.permission.HAND_TRACKING into the manifest.
    ///
    /// The per-build identity (product name, package id) and runInBackground are set for this build
    /// and handed back afterwards, so other builders in the same project see what they left.
    /// </summary>
    public static class EnergyDuelAndroidBuilder
    {
        public const string ProductName = "Energy Duel";
        public const string ApplicationIdentifier = "jp.hapbeat.energyduel";
        public const string DefaultOutput = "Builds/EnergyDuel.apk";

        private const string OpenXrLoaderTypeName = "UnityEngine.XR.OpenXR.OpenXRLoader";
        private const string HandTrackingFeatureTypeName = "HandTracking";
        private const AndroidSdkVersions MinSdk = AndroidSdkVersions.AndroidApiLevel32;

        /// <summary>
        /// Quest models listed in com.oculus.supportedDevices. "cambria" (Quest Pro) stays off: the
        /// Meta Quest feature answers it with a required eye_tracking feature, which blocks install
        /// on Quest 2 / 3 / 3S.
        /// </summary>
        private static readonly HashSet<string> TargetDevices = new HashSet<string> { "quest", "quest2", "eureka", "quest3s" };

        /// <summary>Batch entry. Output override: -edApk &lt;path&gt;.</summary>
        public static void BuildApk()
        {
            EnergyDuelBatch.MuteEditorAudio();
            var output = Path.GetFullPath(EnergyDuelBatch.GetArg("-edApk", DefaultOutput));
            if (!Build(output)) EditorApplication.Exit(1);
        }

        public static bool Build(string outputPath)
        {
            if (!CheckInputBackends()) return false;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(EnergyDuelSceneSetup.ScenePath) == null)
            {
                Debug.LogError($"[EnergyDuelAndroid] scene not found: {EnergyDuelSceneSetup.ScenePath}");
                return false;
            }

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[EnergyDuelAndroid] could not switch the active build target to Android");
                return false;
            }

            if (!ConfigureXr()) return false;
            ConfigurePlayer();

            var previousProduct = PlayerSettings.productName;
            var previousIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            var previousRunInBackground = PlayerSettings.runInBackground;
            try
            {
                PlayerSettings.productName = ProductName;
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationIdentifier);
                // The build must pause when the headset comes off.
                PlayerSettings.runInBackground = false;
                AssetDatabase.SaveAssets();

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
                LogValidationIssues();
                Debug.Log($"[EnergyDuelAndroid] building {outputPath} from {EnergyDuelSceneSetup.ScenePath}");

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { EnergyDuelSceneSetup.ScenePath },
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None,
                });
                var summary = report.summary;
                Debug.Log($"[EnergyDuelAndroid] BuildReport: result={summary.result} totalTime={summary.totalTime} " +
                          $"errors={summary.totalErrors} warnings={summary.totalWarnings} output={summary.outputPath}");
                if (summary.result != BuildResult.Succeeded)
                {
                    foreach (var step in report.steps)
                    foreach (var msg in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                        Debug.LogError($"[EnergyDuelAndroid] {step.name}: {msg.content}");
                    return false;
                }

                var info = new FileInfo(outputPath);
                Debug.Log($"[EnergyDuelAndroid] APK written: {info.FullName} ({info.Length / (1024f * 1024f):F1} MB)");
                return true;
            }
            finally
            {
                PlayerSettings.productName = previousProduct;
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, previousIdentifier);
                PlayerSettings.runInBackground = previousRunInBackground;
                AssetDatabase.SaveAssets();
            }
        }

        // ------------------------------------------------------------------ XR

        private static bool ConfigureXr()
        {
            EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.k_SettingsKey, out var perBuildTarget);
            if (perBuildTarget == null)
            {
                Debug.LogError("[EnergyDuelAndroid] no XRGeneralSettingsPerBuildTarget in EditorBuildSettings");
                return false;
            }

            if (!perBuildTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
                perBuildTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!perBuildTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

            var general = perBuildTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            general.InitManagerOnStart = true;
            var manager = perBuildTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!XRPackageMetadataStore.IsLoaderAssigned(OpenXrLoaderTypeName, BuildTargetGroup.Android) &&
                !XRPackageMetadataStore.AssignLoader(manager, OpenXrLoaderTypeName, BuildTargetGroup.Android))
            {
                Debug.LogError("[EnergyDuelAndroid] failed to assign the OpenXR loader for Android");
                return false;
            }

            EditorUtility.SetDirty(perBuildTarget);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);

            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (openXr == null)
            {
                Debug.LogError("[EnergyDuelAndroid] no OpenXRSettings for Android");
                return false;
            }

            var wanted = new[]
            {
                typeof(MetaQuestFeature),
                typeof(OculusTouchControllerProfile),
                typeof(MetaQuestTouchProControllerProfile),
                typeof(MetaQuestTouchPlusControllerProfile),
            };
            foreach (var type in wanted)
            {
                var feature = openXr.GetFeature(type);
                if (feature == null)
                {
                    Debug.LogError($"[EnergyDuelAndroid] OpenXR feature not found: {type.Name}");
                    return false;
                }

                feature.enabled = true;
                EditorUtility.SetDirty(feature);
            }

            // XR Hands' feature lives in its own assembly; matched by type name as the Volley setup does.
            var hands = openXr.GetFeatures().FirstOrDefault(f => f.GetType().Name == HandTrackingFeatureTypeName);
            if (hands == null)
            {
                Debug.LogError("[EnergyDuelAndroid] XR Hands HandTracking feature not found");
                return false;
            }

            hands.enabled = true;
            EditorUtility.SetDirty(hands);

            var quest = (MetaQuestFeature)openXr.GetFeature(typeof(MetaQuestFeature));
            // Haptics go to Hapbeat devices over UDP, so the manifest keeps INTERNET.
            quest.ForceRemoveInternetPermission = false;
            EditorUtility.SetDirty(quest);
            if (!ConfigureQuestTargetDevices(quest)) return false;

            EditorUtility.SetDirty(openXr);
            AssetDatabase.SaveAssets();
            Debug.Log("[EnergyDuelAndroid] enabled OpenXR features: " +
                      string.Join(", ", openXr.GetFeatures().Where(f => f.enabled).Select(f => f.name)));
            return true;
        }

        /// <summary>MetaQuestFeature.targetDevices is internal, so it is edited through SerializedObject.</summary>
        private static bool ConfigureQuestTargetDevices(MetaQuestFeature quest)
        {
            var so = new SerializedObject(quest);
            var devices = so.FindProperty("targetDevices");
            if (devices == null || !devices.isArray)
            {
                Debug.LogError("[EnergyDuelAndroid] MetaQuestFeature.targetDevices not found");
                return false;
            }

            var summary = new List<string>();
            for (var i = 0; i < devices.arraySize; i++)
            {
                var device = devices.GetArrayElementAtIndex(i);
                var manifestName = device.FindPropertyRelative("manifestName").stringValue;
                var enabled = device.FindPropertyRelative("enabled");
                enabled.boolValue = TargetDevices.Contains(manifestName);
                summary.Add($"{manifestName}={(enabled.boolValue ? "on" : "off")}");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[EnergyDuelAndroid] Quest target devices: {string.Join(" ", summary)}");
            return true;
        }

        // -------------------------------------------------------------- player

        /// <summary>Android player settings shared by every Quest build of the project. Idempotent.</summary>
        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Hapbeat";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = MinSdk;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceInternetPermission = true;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.MTRendering = true;
            PlayerSettings.SetMobileMTRendering(NamedBuildTarget.Android, true);

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;

            // Sideload build: plain APK, debug key.
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.useCustomKeystore = false;
        }

        /// <summary>
        /// Hand tracking and the XR rig read input through the Input System only; activeInputHandler
        /// must be 1 (Input System only) or the player gets no devices. Read as a serialized property
        /// because PlayerSettings has no public accessor for it.
        /// </summary>
        private static bool CheckInputBackends()
        {
            var raw = -1;
            foreach (var settings in Resources.FindObjectsOfTypeAll<PlayerSettings>())
            {
                var property = new SerializedObject(settings).FindProperty("activeInputHandler");
                if (property != null) raw = property.intValue;
                break;
            }

            var ok = raw == 1;
#if !ENABLE_INPUT_SYSTEM || ENABLE_LEGACY_INPUT_MANAGER
            ok = false;
#endif
            if (!ok)
            {
                Debug.LogError($"[EnergyDuelAndroid] input backends not build-ready: activeInputHandler={raw}, expected 1 (Input System only)");
                return false;
            }

            Debug.Log("[EnergyDuelAndroid] input backends ok (activeInputHandler=1)");
            return true;
        }

        private static void LogValidationIssues()
        {
            var issues = new List<OpenXRFeature.ValidationRule>();
            OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);
            if (issues.Count == 0)
            {
                Debug.Log("[EnergyDuelAndroid] OpenXR validation: no issues");
                return;
            }

            foreach (var issue in issues)
            {
                var line = $"[EnergyDuelAndroid] OpenXR validation ({(issue.error ? "error" : "warning")}): {issue.message}";
                if (issue.error) Debug.LogError(line);
                else Debug.LogWarning(line);
            }
        }
    }
}
