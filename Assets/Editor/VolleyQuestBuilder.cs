using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using GloveBallDemo.Editor;

public static class VolleyQuestBuilder
{
    public static void Build()
    {
        DemoAndroidBuilder.ConfigureAndroidXr();
        DemoAndroidBuilder.ConfigureAndroidPlayer();
        string product=PlayerSettings.productName;
        string identifier=PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        var settings=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Resources/HapbeatDemoSwitchSettings.asset");
        var serialized=new SerializedObject(settings);var id=serialized.FindProperty("_currentDemoId");string oldId=id.stringValue;
        try
        {
            PlayerSettings.productName="Hapbeat Volley";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"jp.hapbeat.volley");
            id.stringValue="volley";serialized.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
            var scenes=new[]{"VolleyReceive-codex","VolleyBlock-codex","VolleyJumpSpike-codex"}
                .Select(n=>"Assets/GloveBallDemo/Scenes/"+n+".unity").ToArray();
            string output=Path.GetFullPath("Builds/Android/hapbeat-volley.apk");Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=scenes,locationPathName=output,target=BuildTarget.Android,options=BuildOptions.None});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Volley build failed: "+report.summary.result);
            Debug.Log("[VolleyBuild] SUCCESS "+output);
        }
        finally
        {
            PlayerSettings.productName=product;PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,identifier);
            serialized.Update();serialized.FindProperty("_currentDemoId").stringValue=oldId;serialized.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
        }
    }
}
