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

    const string NarrationFolder="Assets/GloveBallDemo/Audio/Narration/ja";
    const string MusicPath="Assets/GloveBallDemo/Audio/Music/energetic_bigbeat_drum_loop.wav";
    const string LeftPlaceholderPath="Assets/GloveBallDemo/Art/FallbackHands/LeftHandPlaceholder.prefab";
    const string RightPlaceholderPath="Assets/GloveBallDemo/Art/FallbackHands/RightHandPlaceholder.prefab";
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
        // The hitter stands so its right (hitting) hand, not its body, is on the block spot's line: the body turn still
        // tells the shot, and the shots spread evenly to both sides of the player (they all went to the player's left).
        {
            var role=opponent.Role;opponent.Role=VolleyOpponentPrototype.MotionRole.Spike;
            var courtRight=Vector3.Cross(Vector3.up,Vector3.ProjectOnPlane(drill.CourtFrame.forward,Vector3.up).normalized);
            var spot=aerial.Jump.Floor.RepositionTarget!=null ? aerial.Jump.Floor.RepositionTarget.position : drill.CourtFrame.position;
            float handOffset=Vector3.Dot(opponent.ReleasePosition-spot,courtRight);
            opponent.transform.position-=courtRight*handOffset;
            opponent.Role=role;EditorUtility.SetDirty(opponent.transform);
        }
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
        aerial.MatchPoints=4; // a demo: first to 4, so a match has a few spikes and blocks (the session's points option may change it)
        aerial.MinBlockers=aerial.MaxBlockers=3;
        aerial.HeightSpread=.15f;aerial.BlockReachAboveEye=.45f;aerial.BlockSpeed=10.5f; // readable from the body turn, always reachable
        aerial.BlockerJumpSeconds=1.3f;aerial.BlockerReactionSeconds=.05f;aerial.BlockerTimingJitter=.05f;
        aerial.TheirAttackSpeed=12f;aerial.TheirAttackDrop=.15f;aerial.TheirStrongAttackSpeed=15f;aerial.TheirStrongAttackDrop=.35f;
        // RECEIVE: only EXCELLENT scores; GOOD is dug and countered (our block touches it half the time); BAD gets a clean hard counter.
        aerial.StationKillMin=.08f;aerial.OurBlockTouchChance=.5f;
        // An EXCELLENT pass is always put away, hammered steeply into their court: the payoff of a good receive.
        aerial.StationKillMax=1f;aerial.ExcellentSpikeExtraSpeed=14f;aerial.ExcellentKillDepth=new Vector2(2.2f,4.5f);
        aerial.ServeFlightSeconds=new Vector3(1.05f,1.5f,2.2f);aerial.NetLobPlayDistance=.35f;
        aerial.SetterLateralRange=new Vector2(-3f,3f);aerial.FrontRowLateral=3.6f;aerial.HitterSetterGap=2f;aerial.SetterRushRange=1.2f; // the setter anywhere along the net, clear of the hitters
        aerial.SetterOffNetRange=new Vector2(1f,2.5f);
        aerial.ExcellentPassMove=.8f;aerial.ExcellentPassHang=.9f;aerial.GoodPassOffNet=new Vector2(.5f,3f);
        // A lob just over the tape is still settable; a little higher serve is kinder (no deep knee bend every time).
        aerial.PassMinClearance=.1f;aerial.GoodPassOffNet=new Vector2(.2f,3f);aerial.ExcellentMovePerSecond=1.5f;aerial.SoftBlockWidth=.45f;
        aerial.ReceiveSpread=new Vector3(.45f,.25f,.1f);aerial.ReceiveHeight=1.3f;
        aerial.SetLiftBase=1f;aerial.SetLiftGain=2.6f;aerial.SetAimGain=2.2f; // the held push, not a bounce, lifts the set
        // Block spot: eyes 0.5 m from the net (0.3 m put raised arms through it); the spike stance stays 1.5 m from the net.
        aerial.BlockStandDistance=.5f;aerial.SpikeStandBackDistance=1f;
        // Their attack: centred on the player, a narrower spread (fingertip blocks only graze, so it must stay reachable).
        aerial.AttackShotDeviation=3f;
        // A palm block mostly keeps the hands' own rebound (palms facing their court already send it back over).
        aerial.BlockAssist=.35f;aerial.BlockMaximumYaw=20f;
        // Spike: a high contact is a good shot; an open hand is not a fingertip tip unless it is the very tips.
        drill.SpikeSwingSpeed=1.8f;drill.SpikeMinimumSpeed=11f;drill.SpikeMaximumSpeed=22f;
        drill.SpikeFingertipZone=.85f;drill.FingertipSpikeScale=.8f; // an open hand is the normal spike: little difference to a fist
        aerial.SpikeHeightWeight=.65f;aerial.SpikeHeightWindow=.8f;aerial.SpikeFullSwing=7f;
        drill.BlockForearms=true;
        // Background music: a quiet CC0 drum loop (Audio/Music/sources.json) under the play.
        aerial.Music=AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
        if(aerial.Music==null)throw new InvalidOperationException("Background music missing: "+MusicPath);
        aerial.MusicVolume=.1f;
        // The player jumps (AUTO JUMP off by default; the menu turns it on). RECEIVE and TOSS never jump either way.
        aerial.Jump.AutomaticJump=false;EditorUtility.SetDirty(aerial.Jump);aerial.JumpTimingTolerance=.18f;
        // Haptics: how hard the hand hit the ball (its change of velocity), up to the trigger's full x2; a receive in both arms.
        drill.ImpactReferenceSpeed=10f;drill.MaximumImpactGain=2f;drill.ReceiveBothArmsDistance=.45f;
        // For now spike and block at the full gain: is the clip itself strong enough (or must it change)? A receive is
        // never weaker than a good one (where on the arms it was met never matters to the feel).
        drill.HapticsAtMaximum=true;drill.ReceiveMinimumGain=1.2f;
        if(drill.JoinedHands!=null){drill.JoinedHands.JoinDistance=.24f;drill.JoinedHands.SeparateDistance=.32f;EditorUtility.SetDirty(drill.JoinedHands);}
        // The tutorials (menu TUTORIAL rows, CONTROL tutorial_start) and their voice.
        var tutorial=drill.GetComponent<VolleyTutorial>();
        if(tutorial==null)tutorial=drill.gameObject.AddComponent<VolleyTutorial>();
        tutorial.Drill=drill;
        tutorial.Clips=VolleyTutorialText.Lines.Select(line=>AssetDatabase.LoadAssetAtPath<AudioClip>(NarrationFolder+"/"+line.Id+".wav")).Where(clip=>clip!=null).ToArray();
        EditorUtility.SetDirty(tutorial);
        // The block forearms are the shared package's tutorial hand (as in Energy Duel): it needs the placeholder skeleton.
        if(drill.BlockArms==null || drill.BlockArms.Length!=2)drill.BlockArms=new Hapbeat.DemoSwitch.DemoTutorialHand[2];
        for(int side=0;side<2;side++)
        {
            string armName=side==0 ? "Left block forearm" : "Right block forearm";
            var armTransform=drill.transform.Find(armName);
            if(armTransform==null){armTransform=new GameObject(armName).transform;armTransform.SetParent(drill.transform,false);}
            var arm=armTransform.GetComponent<Hapbeat.DemoSwitch.DemoTutorialHand>();
            if(arm==null)arm=armTransform.gameObject.AddComponent<Hapbeat.DemoSwitch.DemoTutorialHand>();
            arm.Left=side==0;arm.ShowForearm=true;arm.Scale=1f;
            // Meta's hand mesh measured at its wrist cuff (Energy Duel's setup, DemoTutorialHandCuff.Measure x 1.05): the forearm
            // meets the tracked hand, which stays opaque 2 cm past the joint while it is shown (DemoHands.SetWristFade): a fade
            // right at the joint showed through as dots where the hand's cuff reaches out of the forearm's ellipse.
            arm.ForearmWristRadii=arm.ForearmCuffRadii=new Vector2(.02734f,.01987f);
            arm.HandFadeStart=.02f;arm.HandFadeLength=.01f;arm.SkinUv=new Vector2(.0757f,.5025f);
            arm.PlaceholderSkeleton=AssetDatabase.LoadAssetAtPath<GameObject>(side==0 ? LeftPlaceholderPath : RightPlaceholderPath);
            if(arm.PlaceholderSkeleton==null)throw new InvalidOperationException("Placeholder hand prefab missing for the block forearm.");
            armTransform.gameObject.SetActive(false);
            drill.BlockArms[side]=arm;
            EditorUtility.SetDirty(arm);
        }
        // Their hitter left of / in front of / right of the player in turn; their setter far enough left to stay clear of a
        // hitter on the left, and their idle middle on the other side of the hitter.
        aerial.AttackerShift=1.2f;aerial.AttackerLaneJitter=.25f;aerial.OpponentSetterOffset=new Vector2(-2.2f,.4f);aerial.MiddleBlockerOffset=1.6f;
        // Their attack within reach of a block made standing still: narrower.
        aerial.AttackBodyYaw=7f;aerial.BlockLateralLimit=.3f;
        aerial.BlockerReboundScale=.5f;aerial.FlowRally=true;aerial.FlowGlideSeconds=.45f;
        aerial.CoverMaxSpeed=11f;aerial.DiveChance=.45f;
        // Their defence plays the ball where its hands are: a platform within arm's reach (even a hard ball at the body), a
        // dive with stretched arms after a short lunge; a dive that will not get it falls visibly short.
        aerial.ArmReach=.45f;aerial.DiveSlide=.5f;aerial.DiveFallSeconds=.3f;aerial.DiveMaxTilt=65f;aerial.DiveMissShort=.55f;aerial.HandContactRadius=.3f;
        // Their defence: two deep, one covering short balls; hard-driven balls are often shanked, soft ones rarely.
        aerial.DefenderDepth=6.5f;aerial.CoverDepth=3.3f;
        // Nobody moves for the ball for half a second: a sharp, hard shot is put away (unless at a player's body), a ball in
        // the air longer is run down by whoever is nearest, the front row included.
        aerial.DefenderReactionSeconds=.5f;
        aerial.DigSpeedRange=new Vector2(8f,20f);aerial.DigEasyChance=.95f;aerial.DigHardChance=.3f;
        // One blocker reads the hitter closely.
        aerial.ReadingBlockerRange=.3f;
        PlaceBlockSpot(aerial,net,forward);
        aerial.TargetMaterial=TargetMaterial();
        // For now one of the four balls at random (their haptic clips are tried in play; the volleyball alone comes back after).
        var pool=new SerializedObject(drill.Pool);var kinds=pool.FindProperty("_launchBallKinds");
        var four=new[]{BallKind.Bowling,BallKind.Volleyball,BallKind.Foam,BallKind.Perforated};
        kinds.arraySize=four.Length;
        for(int i=0;i<four.Length;i++)kinds.GetArrayElementAtIndex(i).enumValueIndex=(int)four[i];
        pool.ApplyModifiedPropertiesWithoutUndo();
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
        ApplyAttackLines(net,forward);
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
    const string AttackLinesName="Volley attack lines (3 m)";
    const string LineMaterialPath="Assets/GloveBallDemo/Art/VolleyCourtLine.mat";
    /// <summary>
    /// The stadium court's markings put the attack lines 2.25 m from the net (the outer 9 x 18 m lines and the 2.43 m net
    /// are true to scale). Covers those two lines in the court colour and draws them at 3 m: 5 cm wide, the rear edge
    /// 3 m from the centre line, as the rules mark them.
    /// </summary>
    static void ApplyAttackLines(Vector3 net,Vector3 forward)
    {
        var old=GameObject.Find(AttackLinesName);
        if(old!=null)UnityEngine.Object.DestroyImmediate(old);
        var court=UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(r=>r.name=="court");
        if(court==null)throw new InvalidOperationException("The stadium court renderer is missing.");
        var line=AssetDatabase.LoadAssetAtPath<Material>(LineMaterialPath);
        if(line==null)
        {
            line=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(line,LineMaterialPath);
        }
        line.SetColor("_BaseColor",new Color(.95f,.95f,.95f,1f));EditorUtility.SetDirty(line);
        var root=new GameObject(AttackLinesName).transform;
        var rotation=Quaternion.LookRotation(Vector3.down,forward); // a quad lying on the floor, facing up
        foreach(float side in new[]{-1f,1f})
        {
            Strip(root,"Cover 2.25 m",net+forward*(side*2.25f)+Vector3.up*.002f,rotation,9.12f,.14f,court.sharedMaterial);
            Strip(root,"Attack line 3 m",net+forward*(side*2.975f)+Vector3.up*.003f,rotation,9f,.05f,line);
        }
    }
    static void Strip(Transform root,string name,Vector3 position,Quaternion rotation,float length,float width,Material material)
    {
        var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.name=name;
        UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(root,false);
        quad.transform.SetPositionAndRotation(position,rotation);
        quad.transform.localScale=new Vector3(length,width,1f);
        var renderer=quad.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
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
