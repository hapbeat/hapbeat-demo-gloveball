using System;
using System.Linq;
using GloveBallDemo.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Focused configuration for the spike/block turn rally. It only edits VolleyBlock-codex; it never rebuilds a scene.</summary>
public static class VolleyRallySetup
{
    const string ScenePath="Assets/GloveBallDemo/Scenes/VolleyBlock-codex.unity";
    const string AllyName="Rally friendly setter";
    const string BlockerPrefix="Opponent blocker ";
    const string DefenderPrefix="Opponent defender ";
    const string TeammatePrefix="Rally teammate ";

    [MenuItem("Hapbeat/Volley/Apply spike + block turn rally")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before applying the rally setup.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save manual edits first.");
        var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        var drill=UnityEngine.Object.FindFirstObjectByType<VolleyDrillController>();
        if(drill==null || drill.Aerial==null || drill.ReceiveNet==null)throw new InvalidOperationException("VolleyBlock scene is missing its drill, aerial sequence, or net.");
        var aerial=drill.Aerial;
        if(aerial.Opponent==null)throw new InvalidOperationException("Block opponent is missing.");
        if(drill.FeedLaunchers==null || drill.FeedLaunchers.Length<1)throw new InvalidOperationException("Rally requires the opponent toss launcher.");

        aerial.RallyEnabled=true;
        aerial.TossLauncher=drill.FeedLaunchers[0];
        aerial.TossLauncher.gameObject.SetActive(true);
        aerial.TossLauncher.enabled=false;
        // The friendly pass now drops from above; the former second feeder stays hidden.
        for(int i=1;i<drill.FeedLaunchers.Length;i++)drill.FeedLaunchers[i].gameObject.SetActive(false);

        var opponent=aerial.Opponent;
        opponent.OverrideKit=true;opponent.JerseyColour=aerial.OpponentJersey;opponent.ShortsColour=aerial.OpponentShorts;opponent.ApplyKit();

        var actors=UnityEngine.Object.FindObjectsByType<VolleyOpponentPrototype>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        // Idempotent: existing cast members keep their scene identity and are only reconfigured.
        var ally=actors.FirstOrDefault(a=>a.gameObject.name==AllyName);
        if(ally==null)ally=VolleyAerialSequence.CreateAlly(opponent,aerial.AllyJersey,aerial.AllyShorts);
        else VolleyAerialSequence.ConfigureActor(ally,VolleyOpponentPrototype.MotionRole.Set,aerial.AllyJersey,aerial.AllyShorts);
        var forward=Vector3.ProjectOnPlane(drill.CourtFrame.forward,Vector3.up).normalized;
        var right=Vector3.Cross(Vector3.up,forward);
        var net=drill.ReceiveNet.transform.position;net.y=drill.CourtFrame.position.y;
        ally.transform.SetPositionAndRotation(net+right*aerial.AllyNetOffset.x+forward*aerial.AllyNetOffset.y,Quaternion.LookRotation(-right,Vector3.up));
        aerial.Ally=ally;

        aerial.Blockers=new VolleyOpponentPrototype[3];
        for(int i=0;i<aerial.Blockers.Length;i++)
        {
            var blocker=actors.FirstOrDefault(a=>a.gameObject.name==BlockerPrefix+(i+1));
            if(blocker==null)blocker=VolleyAerialSequence.CreateBlocker(opponent,i,aerial,aerial.OpponentJersey,aerial.OpponentShorts);
            else
            {
                VolleyAerialSequence.ConfigureActor(blocker,VolleyOpponentPrototype.MotionRole.Block,aerial.OpponentJersey,aerial.OpponentShorts);
                blocker.GetComponent<VolleyBlockerContact>().Rally=aerial;
            }
            blocker.transform.SetPositionAndRotation(net+forward*aerial.BlockerStandbyDistance+right*((i-1)*1.8f),Quaternion.LookRotation(-forward,Vector3.up));
            aerial.Blockers[i]=blocker;
        }

        aerial.Defenders=new VolleyOpponentPrototype[3];
        for(int i=0;i<aerial.Defenders.Length;i++)
        {
            var defender=actors.FirstOrDefault(a=>a.gameObject.name==DefenderPrefix+(i+1));
            if(defender==null)defender=VolleyAerialSequence.CreateDefender(opponent,i,aerial.OpponentJersey,aerial.OpponentShorts);
            else VolleyAerialSequence.ConfigureActor(defender,VolleyOpponentPrototype.MotionRole.Dig,aerial.OpponentJersey,aerial.OpponentShorts);
            defender.transform.SetPositionAndRotation(net+forward*aerial.DefenderDepth+right*((i-1)*aerial.DefenderSpacing),Quaternion.LookRotation(-forward,Vector3.up));
            aerial.Defenders[i]=defender;
        }
        // Match: five more blue players (middle, outside, three back row), inactive outside the match.
        aerial.Teammates=new VolleyOpponentPrototype[5];
        for(int i=0;i<aerial.Teammates.Length;i++)
        {
            var mate=actors.FirstOrDefault(a=>a.gameObject.name==TeammatePrefix+(i+1));
            if(mate==null)mate=VolleyAerialSequence.CreateTeammate(opponent,i,aerial.AllyJersey,aerial.AllyShorts);
            else VolleyAerialSequence.ConfigureActor(mate,VolleyOpponentPrototype.MotionRole.Dig,aerial.AllyJersey,aerial.AllyShorts);
            mate.transform.SetPositionAndRotation(net-forward*(i<2 ? 1.3f : 6f)+right*((i%3-1)*2.6f),Quaternion.LookRotation(forward,Vector3.up));
            aerial.Teammates[i]=mate;
        }
        // The forearm platform comes back only while the player receives a serve (level 2+).
        aerial.ReceiveVolume=UnityEngine.Object.FindFirstObjectByType<VolleyJoinedHands>(FindObjectsInactive.Include);
        if(aerial.ReceiveVolume==null)throw new InvalidOperationException("The joined-hands receive platform is missing from the scene.");
        aerial.Mode=VolleyAerialSequence.RallyMode.Alternate; // default board: spike / block turns; set / pass stations and the 6 v 6 match are menu options
        aerial.PlayerInvolvement=VolleyAerialSequence.Involvement.SpikeBlock;
        // Serialized scene values win over code defaults, so the tuned rally values are written explicitly.
        aerial.AllyFlightSeconds=1.5f;       // high set, time to read it
        aerial.RallyResetSeconds=2f;         // landing ball and marker stay visible
        aerial.SpikeJumpHeight=1.5f;aerial.SpikeJumpSeconds=1.4f;   // more hang time to strike down
        aerial.BlockJumpHeight=.85f;aerial.BlockJumpSeconds=1f;
        aerial.MatchPoints=7;
        aerial.MinBlockers=aerial.MaxBlockers=3;
        aerial.HeightSpread=.15f;aerial.BlockReachAboveEye=.45f;aerial.BlockSpeed=10.5f; // readable from the body turn, always reachable
        aerial.BlockerJumpSeconds=1.3f;aerial.BlockerReactionSeconds=.05f;aerial.BlockerTimingJitter=.05f;
        aerial.ReceiveHeight=1.3f; // forearm height: the serve came in too low to pass upward
        aerial.SetLiftBase=1f;aerial.SetLiftGain=2.6f;aerial.SetAimGain=2.2f; // the held push, not a bounce, lifts the set
        // Block spot: eyes 0.6 m from the net (0.3 m put raised arms through it); the spike stance stays 1.5 m from the net.
        aerial.BlockStandDistance=.6f;aerial.SpikeStandBackDistance=.9f;
        PlaceBlockSpot(aerial,net,forward);
        aerial.TargetMaterial=TargetMaterial();
        // The rally is tuned on the volleyball first; the other balls come back once its feel is right.
        var pool=new SerializedObject(drill.Pool);var kinds=pool.FindProperty("_launchBallKinds");
        kinds.arraySize=1;kinds.GetArrayElementAtIndex(0).enumValueIndex=(int)BallKind.Volleyball;pool.ApplyModifiedPropertiesWithoutUndo();
        aerial.BlockerNetDistance=.35f;
        // Fast overhead swings exceed 8 m/s in tracking space; the old limit flagged them as tracking jumps.
        drill.Left.MaximumTrackedSpeed=drill.Right.MaximumTrackedSpeed=30f;
        drill.Left.MaximumHandSpeed=drill.Right.MaximumHandSpeed=25f;
        drill.MaximumReturnSpeed=18f;
        // Hit area = the visible hand: the box is refitted to all hand joints every tracked frame.
        drill.Left.FitContactToHand=drill.Right.FitContactToHand=true;
        // Hard swings lose hand tracking mid-stroke: carry the hand along its swing for up to 0.22 s / 0.6 m.
        foreach(var hand in new[]{drill.Left,drill.Right}){hand.FastSwingSpeed=3f;hand.FastSwingLossSeconds=.22f;hand.FastSwingPredictionDistance=.6f;}
        drill.SpikeTouchAssist=.03f;
        drill.MaximumBallAge=6f;
        // Points are decided by landing position; the floor targets are not part of the rally.
        drill.Targets.RandomizeOnStart=false;
        drill.Targets.gameObject.SetActive(false);
        ApplySkinHands(drill);
        ApplyStanceAnchor(aerial.Jump.Floor);
        EditorUtility.SetDirty(aerial);
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Failed to save VolleyBlock-codex.");
    }

    static void PlaceBlockSpot(VolleyAerialSequence aerial,Vector3 net,Vector3 forward)
    {
        var floor=aerial.Jump.Floor;
        var spot=floor.RepositionTarget;
        var position=net-forward*aerial.BlockStandDistance;position.y=spot.position.y;
        spot.position=new Vector3(spot.position.x,position.y,position.z);
        // The XR Origin starts there too (the start alignment then has nothing to correct in the editor).
        var origin=floor.Origin.transform;
        origin.position=new Vector3(origin.position.x,origin.position.y,position.z);
        EditorUtility.SetDirty(spot);EditorUtility.SetDirty(origin);
    }
    const string TargetMaterialPath="Assets/GloveBallDemo/Art/VolleyTargetGhost.mat";
    /// <summary>Transparent unlit material for the TOSS ghost targets (an asset, so the transparent variant ships).</summary>
    static Material TargetMaterial()
    {
        var material=AssetDatabase.LoadAssetAtPath<Material>(TargetMaterialPath);
        if(material==null)
        {
            var shader=Shader.Find("Universal Render Pipeline/Unlit");
            if(shader==null)throw new InvalidOperationException("URP Unlit shader is missing.");
            material=new Material(shader);AssetDatabase.CreateAsset(material,TargetMaterialPath);
        }
        material.SetFloat("_Surface",1f);material.SetFloat("_Blend",0f);
        material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite",0f);material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue=(int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.SetColor("_BaseColor",new Color(1f,1f,1f,.35f));
        EditorUtility.SetDirty(material);
        return material;
    }
    /// <summary>The shared start alignment aligns the head to an anchor that follows the stance (see VolleyFloorTracking.StanceAnchor).</summary>
    static void ApplyStanceAnchor(VolleyFloorTracking floor)
    {
        if(floor==null || floor.RepositionTarget==null)throw new InvalidOperationException("Floor tracking needs its RepositionTarget.");
        const string Name="Volley stance anchor";
        var anchor=floor.StanceAnchor;
        if(anchor==null)
        {
            var existing=GameObject.Find(Name);
            anchor=existing!=null ? existing.transform : new GameObject(Name).transform;
        }
        anchor.SetParent(floor.RepositionTarget.parent,false);
        anchor.SetPositionAndRotation(floor.RepositionTarget.position,floor.RepositionTarget.rotation);
        floor.StanceAnchor=anchor;EditorUtility.SetDirty(floor);
        var alignment=UnityEngine.Object.FindFirstObjectByType<Hapbeat.DemoSwitch.XrStartAlignment>(FindObjectsInactive.Include);
        if(alignment==null)throw new InvalidOperationException("The scene has no XrStartAlignment.");
        var serialized=new SerializedObject(alignment);
        serialized.FindProperty("_anchor").objectReferenceValue=anchor;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    const string SkinHandPath="Assets/GloveBallDemo/Art/UnityGhostHands/VolleySkinHand.mat";
    const string GhostHandPath="Assets/GloveBallDemo/Art/UnityGhostHands/Materials/Unity_Hand_Medium.mat";
    const string DepthOnlyPath="Assets/GloveBallDemo/Art/UnityGhostHands/Materials/DepthOnly.mat";
    const string SmoothHandShaderPath="Assets/GloveBallDemo/Art/UnityGhostHands/Shaders/Unity_Hand.shadergraph";
    /// <summary>
    /// Skin-tone hands for the rally scene only. Built from the ghost-hand material so the wrist keeps its gradual fade
    /// (no hard cut edge); the receive scene keeps the grey ghost hands.
    /// </summary>
    static void ApplySkinHands(VolleyDrillController drill)
    {
        var source=AssetDatabase.LoadAssetAtPath<Material>(GhostHandPath);
        if(source==null)throw new InvalidOperationException("Ghost hand material is missing: "+GhostHandPath);
        var material=AssetDatabase.LoadAssetAtPath<Material>(SkinHandPath);
        if(material==null){material=new Material(source);AssetDatabase.CreateAsset(material,SkinHandPath);}
        // Smooth (non-dithered) ghost shader: the wrist fades as a gradient instead of speckle.
        var smooth=AssetDatabase.LoadAssetAtPath<Shader>(SmoothHandShaderPath);
        if(smooth==null)throw new InvalidOperationException("Hand shader is missing: "+SmoothHandShaderPath);
        material.shader=smooth;material.CopyPropertiesFromMaterial(source);
        var skin=new Color(.87f,.67f,.53f,1f);
        material.SetColor("_MainColor",skin);
        material.SetColor("_ColorTop",skin);material.SetColor("_ColorBottom",skin); // the grey gradient darkened the fade band
        material.SetColor("_EdgeColor",new Color(.87f,.67f,.53f,0f)); // no rim light: it read as an outline
        material.SetFloat("_FadeStart",.06f);material.SetFloat("_FadeSize",.11f); // long gradual fade toward the wrist, opaque fingers
        EditorUtility.SetDirty(material);
        // Keep the ghost setup's depth-only second pass: without it the transparent hand shows its own fingers
        // through the palm and UI canvases draw over it.
        var depth=AssetDatabase.LoadAssetAtPath<Material>(DepthOnlyPath);
        if(depth==null)throw new InvalidOperationException("Depth-only material is missing: "+DepthOnlyPath);
        int count=0;
        // The hand mesh is instantiated at runtime (private or placeholder model); the resolver applies these materials.
        foreach(var resolver in UnityEngine.Object.FindObjectsByType<VolleyHandModelResolver>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            resolver.Materials=new[]{material,depth};EditorUtility.SetDirty(resolver);count++;
        }
        if(count!=2)throw new InvalidOperationException("Expected two ghost hands, found "+count);
    }
}
