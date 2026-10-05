using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using GloveBallDemo.Editor;

public static class VolleyQuestBuilder
{
    /// <summary>Build order; the first scene is what the APK opens on launch (Spike + Block rally).</summary>
    public static readonly string[] SceneNames={"VolleyBlock-codex","VolleyReceive-codex"};
    /// <summary>
    /// XR Hands' Meta Hand Tracking Aim (XR_FB_hand_tracking_aim): the shared pause reads Quest's left-hand
    /// system menu gesture from its aim flags (Hapbeat Demo Switch, DemoPauseInput).
    /// </summary>
    const string HandTrackingAimFeatureTypeName="MetaHandTrackingAim";

    public static void Build()
    {
        DemoAndroidBuilder.ConfigureAndroidXr();
        DemoAndroidBuilder.ConfigureAndroidPlayer();
        EnableHandTrackingAim();
        string product=PlayerSettings.productName;
        string identifier=PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        var settings=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Resources/HapbeatDemoSwitchSettings.asset");
        var serialized=new SerializedObject(settings);var id=serialized.FindProperty("_currentDemoId");string oldId=id.stringValue;
        // The shared pause is Volley's only: GloveBall (same settings asset) keeps its own menu.
        var pause=serialized.FindProperty("_pauseMenu");var gesture=serialized.FindProperty("_pauseGesture");bool oldPause=pause.boolValue;int oldGesture=gesture.intValue;
        // The checked-in StreamingAssets descriptor is Volley's (demo_id volley); verify it before building.
        using(DemoSessionDescriptors.Use(null,DemoSessionDescriptors.VolleyDemoId))
        try
        {
            PlayerSettings.productName="Volley";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"jp.hapbeat.volley");
            id.stringValue="volley";pause.boolValue=true;gesture.intValue=0; // SystemMenu: left-hand menu gesture + controller ≡
            serialized.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
            var scenes=SceneNames.Select(n=>"Assets/GloveBallDemo/Scenes/"+n+".unity").ToArray();
            string output=Path.GetFullPath("Builds/Android/hapbeat-volley.apk");Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=scenes,locationPathName=output,target=BuildTarget.Android,options=BuildOptions.None});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Volley build failed: "+report.summary.result);
            Debug.Log("[VolleyBuild] SUCCESS "+output);
        }
        finally
        {
            PlayerSettings.productName=product;PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,identifier);
            serialized.Update();serialized.FindProperty("_currentDemoId").stringValue=oldId;
            serialized.FindProperty("_pauseMenu").boolValue=oldPause;serialized.FindProperty("_pauseGesture").intValue=oldGesture;
            serialized.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
        }
    }

    static void EnableHandTrackingAim()
    {
        var openXr=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        var aim=openXr==null?null:openXr.GetFeatures().FirstOrDefault(f=>f.GetType().Name==HandTrackingAimFeatureTypeName);
        if(aim==null)throw new Exception("XR Hands Meta Hand Tracking Aim feature not found for Android.");
        aim.enabled=true;EditorUtility.SetDirty(aim);AssetDatabase.SaveAssets();
        Debug.Log("[VolleyBuild] OpenXR feature enabled: "+aim.name);
    }
}
