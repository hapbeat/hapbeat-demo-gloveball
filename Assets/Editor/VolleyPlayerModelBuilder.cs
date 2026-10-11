using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GloveBallDemo.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Original low-poly volleyball player for <see cref="VolleyOpponentPrototype"/>: procedural, flat-shaded meshes on the
/// existing parts (so every pose keeps working), plus hands, shoes and a jersey number. All parts share one material
/// (GloveBall/VolleyPlayer); vertex colours carry the fixed colours and the kit regions that ApplyKit tints.
/// Each part mesh keeps the bounds of the primitive it replaces, so colliders a blocker adds still fit as before.
/// </summary>
public static class VolleyPlayerModelBuilder
{
    const string Folder="Assets/GloveBallDemo/Art/VolleyPlayer";
    const string MeshFolder=Folder+"/Meshes";
    const string MaterialPath=Folder+"/VolleyPlayer.mat";
    const string ShaderName="GloveBall/VolleyPlayer";
    const string SceneFolder="Assets/GloveBallDemo/Scenes";
    const string NumberName="Jersey number";
    const byte Fixed=0,Skin=1,Jersey=2,Shorts=3;
    // Numbers handed out per team, in hierarchy order.
    static readonly int[] TeamNumbers={7,3,10,12,5,1,9,14,4,11,8,2,6,13,15,16,17,18,19,20};

    [MenuItem("Hapbeat/Volley/Apply low-poly player model")]
    public static void ApplyToVolleyScenes()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before applying the player model.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save manual edits first.");
        var previous=SceneManager.GetActiveScene().path;
        BuildAssets();
        foreach(var path in AssetDatabase.FindAssets("t:Scene",new[]{SceneFolder}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>Path.GetFileName(p).StartsWith("Volley")).OrderBy(p=>p))
        {
            var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
            var actors=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<VolleyOpponentPrototype>(true)).ToList();
            if(actors.Count==0)continue;
            var nextNumber=new Dictionary<string,int>();
            foreach(var actor in actors)
            {
                var team=ColorUtility.ToHtmlStringRGB(actor.OverrideKit ? actor.JerseyColour : Color.clear);
                nextNumber.TryGetValue(team,out int index);nextNumber[team]=index+1;
                Dress(actor,TeamNumbers[index%TeamNumbers.Length]);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Could not save "+path);
            Debug.Log($"[VolleyPlayerModel] {path}: dressed {actors.Count} players");
        }
        AssetDatabase.SaveAssets();
        if(!string.IsNullOrEmpty(previous) && SceneManager.GetActiveScene().path!=previous)EditorSceneManager.OpenScene(previous,OpenSceneMode.Single);
    }

    /// <summary>Puts the player body on one actor. Idempotent: reuses the child objects and mesh assets it created before.</summary>
    public static void Dress(VolleyOpponentPrototype actor,int number)
    {
        var material=PlayerMaterial();
        Wear(actor.Torso,PartMesh("Torso"),material);
        Wear(actor.Head,PartMesh("Head"),material);
        if(actor.Head.localScale!=Vector3.one*.25f)throw new InvalidOperationException(actor.name+": the head must stay at scale 0.25 (blocker head colliders depend on it).");
        Wear(actor.LeftUpperArm,PartMesh("Upper arm"),material);Wear(actor.RightUpperArm,PartMesh("Upper arm"),material);
        Wear(actor.LeftForearm,PartMesh("Forearm"),material);Wear(actor.RightForearm,PartMesh("Forearm"),material);
        Wear(actor.LeftThigh,PartMesh("Thigh"),material);Wear(actor.RightThigh,PartMesh("Thigh"),material);
        Wear(actor.LeftShin,PartMesh("Shin"),material);Wear(actor.RightShin,PartMesh("Shin"),material);
        Wear(Child(actor.Torso,NumberName),NumberMesh(number),material);
        actor.LeftHandModel=Wear(Child(actor.transform,"Left hand model"),PartMesh("Hand L"),material);
        actor.RightHandModel=Wear(Child(actor.transform,"Right hand model"),PartMesh("Hand R"),material);
        actor.LeftShoe=Wear(Child(actor.transform,"Left shoe"),PartMesh("Shoe"),material);
        actor.RightShoe=Wear(Child(actor.transform,"Right shoe"),PartMesh("Shoe"),material);
        foreach(var shoe in new[]{actor.LeftShoe,actor.RightShoe}){shoe.localRotation=Quaternion.identity;shoe.localScale=Vector3.one;}
        EditorUtility.SetDirty(actor);
        // A spike preview needs its preview ball and target; everything else poses without them.
        if(!actor.PreviewOnly || actor.Role!=VolleyOpponentPrototype.MotionRole.Spike || (actor.PreviewBall!=null && actor.Target!=null))actor.Pose(actor.PreviewPhase);
        actor.ApplyKit();
    }

    static Transform Child(Transform parent,string name)
    {
        var child=parent.Find(name);
        if(child==null){child=new GameObject(name).transform;child.SetParent(parent,false);}
        return child;
    }
    static Transform Wear(Transform part,Mesh mesh,Material material)
    {
        if(!part.TryGetComponent<MeshFilter>(out var filter))filter=part.gameObject.AddComponent<MeshFilter>();
        if(!part.TryGetComponent<MeshRenderer>(out var renderer))renderer=part.gameObject.AddComponent<MeshRenderer>();
        filter.sharedMesh=mesh;renderer.sharedMaterials=new[]{material};
        renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
        EditorUtility.SetDirty(filter);EditorUtility.SetDirty(renderer);
        return part;
    }

    // ------------------------------------------------------------------ assets

    static Material PlayerMaterial()
    {
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(material!=null)return material;
        var shader=Shader.Find(ShaderName);
        if(shader==null)throw new InvalidOperationException(ShaderName+" is not imported.");
        EnsureFolder();
        material=new Material(shader){name="VolleyPlayer",enableInstancing=true};
        AssetDatabase.CreateAsset(material,MaterialPath);
        return material;
    }
    static void EnsureFolder(){if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/GloveBallDemo/Art","VolleyPlayer");}

    /// <summary>Rebuilds every part mesh in place (same sub-asset, same references) from the code below.</summary>
    public static void BuildAssets()
    {
        PlayerMaterial();
        Fill("Torso",BuildTorso,new Bounds(Vector3.zero,Vector3.one));
        Fill("Head",BuildHead,new Bounds(Vector3.zero,Vector3.one));
        var capsule=new Bounds(Vector3.zero,new Vector3(1f,2f,1f));
        Fill("Upper arm",BuildUpperArm,capsule);Fill("Forearm",BuildForearm,capsule);
        Fill("Thigh",BuildThigh,capsule);Fill("Shin",BuildShin,capsule);
        Fill("Hand L",b=>BuildHand(b,-1f),null);Fill("Hand R",b=>BuildHand(b,1f),null);
        Fill("Shoe",BuildShoe,null);
        AssetDatabase.SaveAssets();
    }
    static string MeshPath(string name)=>MeshFolder+"/"+name+".asset";
    static Mesh PartMesh(string name)
    {
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(name));
        if(mesh==null)throw new InvalidOperationException($"Mesh '{name}' is missing; run BuildAssets first.");
        return mesh;
    }
    static Mesh NumberMesh(int number)=>Fill("Number "+number,b=>BuildNumber(b,number),null);
    /// <summary>Refills the mesh asset in place so scene references survive a rebuild.</summary>
    static Mesh Fill(string name,Action<Builder> build,Bounds? bounds)
    {
        EnsureFolder();
        if(!AssetDatabase.IsValidFolder(MeshFolder))AssetDatabase.CreateFolder(Folder,"Meshes");
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(name));
        bool created=mesh==null;
        if(created)mesh=new Mesh{name=name};
        var builder=new Builder();build(builder);builder.Write(mesh,bounds);
        if(created)AssetDatabase.CreateAsset(mesh,MeshPath(name));
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    // ------------------------------------------------------------------ colours

    /// <summary>A kit region; <paramref name="shade"/> darkens the per-instance colour (trims, side panels).</summary>
    static Color32 Kit(byte region,float shade=1f){var v=(byte)Mathf.RoundToInt(Mathf.Clamp01(shade)*255f);return new Color32(v,v,v,(byte)(region*85));}
    /// <summary>A fixed colour given in sRGB; the shader works in linear space and does not convert vertex colours.</summary>
    static Color32 Paint(float r,float g,float b)
    {
        var c=new Color(r,g,b).linear;
        return new Color32((byte)Mathf.RoundToInt(c.r*255f),(byte)Mathf.RoundToInt(c.g*255f),(byte)Mathf.RoundToInt(c.b*255f),Fixed);
    }
    static readonly Color32 White=Paint(.95f,.95f,.94f),Hair=Paint(.13f,.09f,.07f),Pad=Paint(.14f,.14f,.16f),Sole=Paint(.86f,.86f,.83f),
        Eye=Paint(.07f,.05f,.05f),Lip=Paint(.6f,.36f,.33f);

    // ------------------------------------------------------------------ geometry

    sealed class Builder
    {
        readonly List<Vector3> _positions=new List<Vector3>();
        readonly List<Vector3> _normals=new List<Vector3>();
        readonly List<Color32> _colours=new List<Color32>();
        readonly List<int> _triangles=new List<int>();
        /// <summary>Flat-shaded triangle wound so it faces away from <paramref name="inside"/>.</summary>
        public void Tri(Vector3 a,Vector3 b,Vector3 c,Color32 colour,Vector3 inside)
        {
            var normal=Vector3.Cross(b-a,c-a);
            if(normal.sqrMagnitude<1e-18f)return;
            if(Vector3.Dot(normal,(a+b+c)/3f-inside)<0f){(b,c)=(c,b);normal=-normal;}
            normal.Normalize();
            int i=_positions.Count;
            _positions.Add(a);_positions.Add(b);_positions.Add(c);
            for(int k=0;k<3;k++){_normals.Add(normal);_colours.Add(colour);}
            _triangles.Add(i);_triangles.Add(i+1);_triangles.Add(i+2);
        }
        public void Write(Mesh mesh,Bounds? bounds)
        {
            mesh.Clear();
            mesh.indexFormat=_positions.Count>65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_positions);mesh.SetNormals(_normals);mesh.SetColors(_colours);mesh.SetTriangles(_triangles,0);
            mesh.RecalculateBounds();
            if(bounds.HasValue)mesh.bounds=bounds.Value;
        }
    }

    /// <summary>Cross-section at height <see cref="Y"/>: superellipse half-widths Rx (x) and Rz (z) centred at z = Oz. Colour paints the band up to the next ring.</summary>
    struct Ring
    {
        public float Y,Rx,Rz,Oz;public Color32 Colour;
        public Ring(float y,float rx,float rz,float oz,Color32 colour){Y=y;Rx=rx;Rz=rz;Oz=oz;Colour=colour;}
        public static Ring Lerp(Ring a,Ring b,float t)=>new Ring(Mathf.Lerp(a.Y,b.Y,t),Mathf.Lerp(a.Rx,b.Rx,t),Mathf.Lerp(a.Rz,b.Rz,t),Mathf.Lerp(a.Oz,b.Oz,t),a.Colour);
    }
    static Vector3 OnRing(Ring ring,float angle,float exponent)
    {
        float c=Mathf.Cos(angle),s=Mathf.Sin(angle);
        return new Vector3(ring.Rx*Mathf.Sign(c)*Mathf.Pow(Mathf.Abs(c),2f/exponent),ring.Y,ring.Oz+ring.Rz*Mathf.Sign(s)*Mathf.Pow(Mathf.Abs(s),2f/exponent));
    }
    /// <summary>Front (+z) or back surface z of a loft at (x, y), for decals that sit on it.</summary>
    static float SurfaceZ(IList<Ring> rings,float x,float y,float exponent,bool front)
    {
        int i=1;while(i<rings.Count-1 && rings[i].Y<y)i++;
        var ring=Ring.Lerp(rings[i-1],rings[i],Mathf.InverseLerp(rings[i-1].Y,rings[i].Y,y));
        float across=Mathf.Pow(Mathf.Max(0f,1f-Mathf.Pow(Mathf.Abs(x/ring.Rx),exponent)),1f/exponent);
        return ring.Oz+(front?1f:-1f)*ring.Rz*across;
    }

    /// <summary>
    /// Closed loft along +y through <paramref name="rings"/> (ascending y; a zero-size ring closes an end). One facet is centred
    /// on +z. <paramref name="paint"/> may override a facet colour from its ring band, side index and local centroid.
    /// </summary>
    static void Loft(Builder b,IList<Ring> rings,int sides,float exponent,Matrix4x4 m,Func<int,int,Vector3,Color32,Color32> paint=null)
    {
        var points=new Vector3[rings.Count][];
        for(int r=0;r<rings.Count;r++)
        {
            points[r]=new Vector3[sides];
            for(int k=0;k<sides;k++)points[r][k]=OnRing(rings[r],Mathf.PI*.5f+(2*k+1)*Mathf.PI/sides,exponent);
        }
        for(int r=0;r<rings.Count-1;r++)
        for(int k=0;k<sides;k++)
        {
            var a=points[r][k];var b2=points[r][(k+1)%sides];var c=points[r+1][(k+1)%sides];var d=points[r+1][k];
            var inside=new Vector3(0f,(rings[r].Y+rings[r+1].Y)*.5f,(rings[r].Oz+rings[r+1].Oz)*.5f);
            var colour=paint==null ? rings[r].Colour : paint(r,k,(a+b2+c+d)*.25f,rings[r].Colour);
            var ma=m.MultiplyPoint3x4(a);var mc=m.MultiplyPoint3x4(c);var mInside=m.MultiplyPoint3x4(inside);
            b.Tri(ma,m.MultiplyPoint3x4(d),mc,colour,mInside);
            b.Tri(ma,mc,m.MultiplyPoint3x4(b2),colour,mInside);
        }
    }
    /// <summary>Axially symmetric limb in capsule space (y = -1 at the near joint, +1 at the far joint, radius 0.5 = the pose width).</summary>
    static void Limb(Builder b,params (float y,float r,Color32 colour)[] profile)
        =>Loft(b,profile.Select(p=>new Ring(p.y,p.r,p.r,0f,p.colour)).ToList(),10,2f,Matrix4x4.identity);
    static void Box(Builder b,Matrix4x4 m,Vector3 centre,Vector3 size,Color32 colour)
    {
        var h=size*.5f;var corners=new Vector3[8];
        for(int i=0;i<8;i++)corners[i]=m.MultiplyPoint3x4(centre+Vector3.Scale(h,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
        var inside=m.MultiplyPoint3x4(centre);
        int[][] faces={new[]{0,1,3,2},new[]{4,5,7,6},new[]{0,1,5,4},new[]{2,3,7,6},new[]{0,2,6,4},new[]{1,3,7,5}};
        foreach(var f in faces){b.Tri(corners[f[0]],corners[f[1]],corners[f[2]],colour,inside);b.Tri(corners[f[0]],corners[f[2]],corners[f[3]],colour,inside);}
    }

    // Torso: unit cube scaled (.42, .53, .25); y = -0.5 is the hip line, +0.5 the shoulder line. Authored in metres.
    static readonly Vector3 TorsoScale=new Vector3(.42f,.53f,.25f);
    static readonly Ring[] TorsoRings={
        new Ring(-.345f,0f,0f,0f,Kit(Shorts)),
        new Ring(-.33f,.06f,.05f,0f,Kit(Shorts)),
        new Ring(-.30f,.15f,.10f,-.005f,Kit(Shorts)),
        new Ring(-.24f,.19f,.115f,-.008f,Kit(Shorts)),
        new Ring(-.165f,.185f,.108f,-.004f,Kit(Jersey,.5f)),   // waistband
        new Ring(-.14f,.178f,.104f,-.002f,Kit(Jersey)),        // jersey hem lip
        new Ring(-.135f,.192f,.117f,0f,Kit(Jersey)),
        new Ring(-.05f,.176f,.108f,.003f,Kit(Jersey)),
        new Ring(.06f,.19f,.122f,.012f,Kit(Jersey)),
        new Ring(.16f,.215f,.13f,.014f,Kit(Jersey)),
        new Ring(.235f,.225f,.12f,.006f,Kit(Jersey)),
        new Ring(.27f,.2f,.105f,.003f,Kit(Jersey)),          // shoulder top, sloping to the neck
        new Ring(.305f,.12f,.08f,0f,White),                  // collar
        new Ring(.335f,.065f,.06f,0f,White),
        new Ring(.34f,0f,0f,0f,White)};
    const float TorsoExponent=3f;
    static void BuildTorso(Builder b)
    {
        var m=Matrix4x4.Scale(new Vector3(1f/TorsoScale.x,1f/TorsoScale.y,1f/TorsoScale.z));
        Loft(b,TorsoRings,16,TorsoExponent,m,(band,side,centroid,colour)=>
        {
            // Side panels down the jersey and a side stripe on the shorts.
            bool flank=Mathf.Abs(centroid.z)<.045f && Mathf.Abs(centroid.x)>.15f;
            if(!flank)return colour;
            if(band>=6 && band<=9)return Kit(Jersey,.35f);
            if(band>=1 && band<=3)return Kit(Jersey);
            return colour;
        });
    }
    /// <summary>Seven-segment jersey number on the chest (small) and the back (large), lying on the torso surface.</summary>
    static void BuildNumber(Builder b,int number)
    {
        var digits=number.ToString();
        foreach(bool front in new[]{true,false})
        {
            float height=front?.085f:.19f,width=height*.56f,stroke=height*.16f,gap=height*.14f,centreY=front?.1f:.045f;
            float total=digits.Length*width+(digits.Length-1)*gap;
            for(int i=0;i<digits.Length;i++)
            {
                float left=-total*.5f+i*(width+gap);
                foreach(var rect in Segments(digits[i]-'0',width,height,stroke))
                    Decal(b,new Rect(rect.x+left,rect.y+centreY-height*.5f,rect.width,rect.height),front);
            }
        }
    }
    static IEnumerable<Rect> Segments(int digit,float w,float h,float s)
    {
        // a b c d e f g
        int[] masks={0x3F,0x06,0x5B,0x4F,0x66,0x6D,0x7D,0x07,0x7F,0x6F};
        int mask=masks[digit];
        if((mask&1)!=0)yield return new Rect(0f,h-s,w,s);
        if((mask&2)!=0)yield return new Rect(w-s,h*.5f,s,h*.5f);
        if((mask&4)!=0)yield return new Rect(w-s,0f,s,h*.5f);
        if((mask&8)!=0)yield return new Rect(0f,0f,w,s);
        if((mask&16)!=0)yield return new Rect(0f,0f,s,h*.5f);
        if((mask&32)!=0)yield return new Rect(0f,h*.5f,s,h*.5f);
        if((mask&64)!=0)yield return new Rect(0f,(h-s)*.5f,w,s);
    }
    /// <summary>A flat white patch following the torso surface; <paramref name="rect"/> is in metres as seen from that side.</summary>
    static void Decal(Builder b,Rect rect,bool front)
    {
        const float lift=.006f;
        var m=Matrix4x4.Scale(new Vector3(1f/TorsoScale.x,1f/TorsoScale.y,1f/TorsoScale.z));
        int nx=Mathf.Max(1,Mathf.CeilToInt(rect.width/.03f)),ny=Mathf.Max(1,Mathf.CeilToInt(rect.height/.03f));
        Vector3 At(float u,float v)
        {
            float x=front?-u:u; // the actor faces +z, so its +x is the viewer's left from the front
            float z=SurfaceZ(TorsoRings,x,v,TorsoExponent,front);
            return new Vector3(x,v,z+(front?lift:-lift));
        }
        for(int i=0;i<nx;i++)
        for(int j=0;j<ny;j++)
        {
            float u0=rect.x+rect.width*i/nx,u1=rect.x+rect.width*(i+1)/nx,v0=rect.y+rect.height*j/ny,v1=rect.y+rect.height*(j+1)/ny;
            var p00=At(u0,v0);var p10=At(u1,v0);var p11=At(u1,v1);var p01=At(u0,v1);
            var inside=m.MultiplyPoint3x4(new Vector3((p00.x+p11.x)*.5f,(v0+v1)*.5f,0f));
            b.Tri(m.MultiplyPoint3x4(p00),m.MultiplyPoint3x4(p10),m.MultiplyPoint3x4(p11),White,inside);
            b.Tri(m.MultiplyPoint3x4(p00),m.MultiplyPoint3x4(p11),m.MultiplyPoint3x4(p01),White,inside);
        }
    }

    // Head: unit sphere at scale 0.25, centred 0.24 m above the shoulder line. Authored in metres.
    static readonly Ring[] HeadRings={
        new Ring(-.112f,0f,0f,.05f,Kit(Skin)),
        new Ring(-.106f,.03f,.025f,.052f,Kit(Skin)),
        new Ring(-.087f,.052f,.06f,.03f,Kit(Skin)),
        new Ring(-.047f,.066f,.085f,.01f,Kit(Skin)),
        new Ring(-.005f,.074f,.096f,0f,Kit(Skin)),
        new Ring(.035f,.077f,.099f,-.004f,Kit(Skin)),
        new Ring(.072f,.071f,.092f,-.008f,Kit(Skin)),
        new Ring(.1f,.056f,.074f,-.011f,Kit(Skin)),
        new Ring(.118f,.03f,.042f,-.013f,Kit(Skin)),
        new Ring(.124f,0f,0f,-.013f,Kit(Skin))};
    const float HeadExponent=2.2f;
    // Authored at life size; drawn a little larger so the head reads next to the wide pose shoulders (arms hang at x = ±0.25 m).
    const float HeadGrowth=1.08f;
    static void BuildHead(Builder b)
    {
        var m=Matrix4x4.Scale(Vector3.one*4f*HeadGrowth);
        Loft(b,HeadRings,12,HeadExponent,m,(band,side,centroid,colour)=>
        {
            // Hairline: high on the forehead, down to the nape at the back.
            float fromFront=Vector2.Angle(Vector2.up,new Vector2(centroid.x,centroid.z+.01f))/180f;
            return centroid.y>.062f-.125f*Mathf.Pow(fromFront,1.4f) ? Hair : colour;
        });
        // Short hair volume over the crown and the back of the head; it stays inside the face.
        var cap=new List<Ring>();
        foreach(float lat in new[]{-50f,-25f,0f,20f,40f,60f,80f,90f})
        {
            float c=Mathf.Cos(lat*Mathf.Deg2Rad);
            cap.Add(new Ring(.03f+.1f*Mathf.Sin(lat*Mathf.Deg2Rad),.081f*c,.098f*c,-.016f,Hair));
        }
        cap.Insert(0,new Ring(.03f-.1f*Mathf.Sin(50f*Mathf.Deg2Rad)-.002f,0f,0f,-.016f,Hair));
        Loft(b,cap,12,2f,m);
        // Neck, from inside the collar up into the jaw.
        Loft(b,new[]{
            new Ring(-.24f,0f,0f,-.012f,Kit(Skin)),new Ring(-.235f,.057f,.056f,-.012f,Kit(Skin)),new Ring(-.17f,.054f,.054f,-.012f,Kit(Skin)),
            new Ring(-.09f,.05f,.05f,-.01f,Kit(Skin)),new Ring(-.06f,.03f,.03f,-.005f,Kit(Skin)),new Ring(-.05f,0f,0f,-.005f,Kit(Skin))},10,2f,m);
        foreach(float side in new[]{-1f,1f})
        {
            Box(b,m,new Vector3(side*.075f,.004f,-.008f),new Vector3(.018f,.045f,.026f),Kit(Skin));               // ear
            Box(b,m,Face(side*.03f,.008f,.002f),new Vector3(.018f,.011f,.006f),Eye);                              // eye
            Box(b,m,Face(side*.031f,.025f,.003f),new Vector3(.026f,.006f,.008f),Hair);                            // brow
        }
        Box(b,m,Face(0f,-.053f,.001f),new Vector3(.028f,.006f,.006f),Lip);
        // Nose: a small wedge on the face.
        var top=new Vector3(0f,.012f,Face(0f,.012f,0f).z-.003f);var lowL=new Vector3(-.014f,-.026f,Face(-.014f,-.026f,0f).z-.003f);
        var lowR=new Vector3(.014f,-.026f,lowL.z);var tip=new Vector3(0f,-.022f,Face(0f,-.022f,0f).z+.02f);
        var core=m.MultiplyPoint3x4(new Vector3(0f,-.014f,Face(0f,-.014f,0f).z-.01f));
        b.Tri(m.MultiplyPoint3x4(top),m.MultiplyPoint3x4(lowL),m.MultiplyPoint3x4(tip),Kit(Skin),core);
        b.Tri(m.MultiplyPoint3x4(top),m.MultiplyPoint3x4(tip),m.MultiplyPoint3x4(lowR),Kit(Skin),core);
        b.Tri(m.MultiplyPoint3x4(lowL),m.MultiplyPoint3x4(lowR),m.MultiplyPoint3x4(tip),Kit(Skin,.85f),core);
    }
    static Vector3 Face(float x,float y,float lift)=>new Vector3(x,y,SurfaceZ(HeadRings,x,y,HeadExponent,true)+lift);

    // Limbs: capsule space, radius 0.5 = the pose width (upper arm .105, forearm .085, thigh .15, shin .11 m).
    static void BuildUpperArm(Builder b)=>Limb(b,
        (-1.13f,0f,Kit(Jersey)),(-1.06f,.34f,Kit(Jersey)),(-.95f,.5f,Kit(Jersey)),(-.7f,.52f,Kit(Jersey)),(-.45f,.52f,White),
        (-.4f,.52f,Kit(Skin)),(-.39f,.44f,Kit(Skin)),(0f,.45f,Kit(Skin)),(.5f,.42f,Kit(Skin)),(.85f,.37f,Kit(Skin)),(1f,.32f,Kit(Skin)),(1.1f,0f,Kit(Skin)));
    static void BuildForearm(Builder b)=>Limb(b,
        (-1.1f,0f,Kit(Skin)),(-1.03f,.36f,Kit(Skin)),(-.85f,.5f,Kit(Skin)),(-.5f,.52f,Kit(Skin)),(0f,.46f,Kit(Skin)),(.55f,.38f,Kit(Skin)),
        (.85f,.33f,Kit(Skin)),(1f,.3f,Kit(Skin)),(1.07f,0f,Kit(Skin)));
    static void BuildThigh(Builder b)=>Limb(b,
        (-1.1f,0f,Kit(Shorts)),(-1.02f,.4f,Kit(Shorts)),(-.92f,.56f,Kit(Shorts)),(-.5f,.6f,Kit(Shorts)),(-.18f,.62f,Kit(Skin)),
        (-.16f,.48f,Kit(Skin)),(.3f,.43f,Kit(Skin)),(.7f,.35f,Kit(Skin)),(.95f,.33f,Kit(Skin)),(1.07f,0f,Kit(Skin)));
    static void BuildShin(Builder b)=>Limb(b,
        (-1.12f,0f,Pad),(-1.08f,.4f,Pad),(-1f,.62f,Pad),(-.72f,.68f,Pad),(-.5f,.64f,Kit(Skin)),                       // knee pad
        (-.48f,.5f,Kit(Skin)),(-.2f,.55f,Kit(Skin)),(.15f,.45f,Kit(Skin)),(.35f,.42f,Kit(Jersey)),(.43f,.41f,White),    // calf, sock stripe
        (.8f,.36f,White),(1f,.33f,White),(1.08f,0f,White));

    /// <summary>Mitten hand in metres: origin at the hand point, fingers along +y, palm facing +z; side -1 = left (thumb on +x).</summary>
    static void BuildHand(Builder b,float side)
    {
        Loft(b,new[]{
            new Ring(-.042f,0f,0f,0f,Kit(Skin)),new Ring(-.035f,.03f,.017f,0f,Kit(Skin)),new Ring(-.01f,.04f,.017f,0f,Kit(Skin)),
            new Ring(.04f,.043f,.015f,.002f,Kit(Skin)),new Ring(.075f,.041f,.012f,.006f,Kit(Skin,.9f)),new Ring(.11f,.037f,.011f,.011f,Kit(Skin)),
            new Ring(.132f,.028f,.009f,.016f,Kit(Skin)),new Ring(.138f,0f,0f,.018f,Kit(Skin))},8,4f,Matrix4x4.identity);
        var thumb=Matrix4x4.TRS(new Vector3(-side*.034f,-.014f,.008f),Quaternion.Euler(20f,0f,side*35f),Vector3.one);
        Loft(b,new[]{
            new Ring(-.006f,0f,0f,0f,Kit(Skin)),new Ring(0f,.012f,.011f,0f,Kit(Skin)),new Ring(.035f,.011f,.01f,0f,Kit(Skin)),
            new Ring(.055f,.008f,.008f,0f,Kit(Skin)),new Ring(.062f,0f,0f,0f,Kit(Skin))},8,2f,thumb);
    }

    /// <summary>Shoe in metres: origin 6 cm above the floor and 8 cm in front of the ankle, toe along +z.</summary>
    static void BuildShoe(Builder b)
    {
        Loft(b,new[]{
            new Ring(-.061f,0f,0f,.01f,Sole),new Ring(-.06f,.047f,.138f,.01f,Sole),new Ring(-.04f,.05f,.144f,.01f,Sole),
            new Ring(-.036f,.044f,.13f,.008f,Sole),new Ring(-.035f,0f,0f,.008f,Sole)},12,2.4f,Matrix4x4.identity);
        Loft(b,new[]{
            new Ring(-.042f,0f,0f,.01f,White),new Ring(-.04f,.046f,.136f,.01f,White),new Ring(-.012f,.05f,.138f,.008f,White),
            new Ring(.012f,.047f,.112f,-.016f,White),new Ring(.032f,.043f,.08f,-.048f,White),new Ring(.046f,.04f,.062f,-.064f,Pad),
            new Ring(.05f,.035f,.05f,-.068f,Pad),new Ring(.052f,0f,0f,-.068f,Pad)},12,2.4f,Matrix4x4.identity,
            (band,side,centroid,colour)=>Mathf.Abs(centroid.x)>.036f && centroid.y>-.03f && centroid.y<.03f && centroid.z>-.1f && centroid.z<.06f ? Kit(Jersey) : colour);
    }
}
