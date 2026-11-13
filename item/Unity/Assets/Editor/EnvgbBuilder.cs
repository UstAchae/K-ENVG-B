using System;
using System.IO;
using System.Linq;
using EFT.Visual;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
// Builds one bundle per colour from what item/prepare.py wrote to Assets/Source/<colour>. The prefab copies the
// GPNVG-18's layout and components (nvg_l3_gpnvg-18_anvis): root with LODGroup, Dress, NightVisionDevice,
// CurveRotator, NightVisionMount and PreviewPivot; the hinge "axis" (the ENVG-B's own mount knuckle, deployed rotation; see prepare.py)
// carrying the device node nvg_envgb_psq42_LOD0 (with its glass and the thermal lens marker as children) and the
// face light; the shroud adapter stays on the root. Run: -executeMethod EnvgbBuilder.Build
public static class EnvgbBuilder {
 const string Src="Assets/Source",Gen="Assets/Generated";
 static readonly string Out=Path.GetFullPath(Path.Combine(Application.dataPath,"../../built"));
 public const string DeviceNode="nvg_envgb_psq42_LOD0",ThermalLens="envgb_thermal_lens";
 [Serializable] public class Node {public string name,parent,mesh;public float[] pos,rot;}
 [Serializable] public class Info {public string colour;public float[] thermalLens,onRotation,offRotation;public Node[] nodes;}

 const float RotationSpeed=0.75187969f;
 static AnimationCurve Curve(){
  var c=new AnimationCurve(
   new Keyframe(0f,0f,2.1630912f,2.1630912f,0.33333334f,0.33333334f),
   new Keyframe(0.46189547f,0.99912202f,2.1630912f,0.0016316094f,0.33333334f,0.33333334f),
   new Keyframe(1f,1f,0.0016316094f,0.0016316094f,0.33333334f,0.33333334f));
  c.preWrapMode=WrapMode.ClampForever;c.postWrapMode=WrapMode.ClampForever;return c;
 }

 static Vector3 V(float[] a)=>new Vector3(a[0],a[1],a[2]);
 static Quaternion Q(float[] a)=>new Quaternion(a[0],a[1],a[2],a[3]);
 static Vector3[] R3(BinaryReader r,int n){var a=new Vector3[n];for(int i=0;i<n;i++)a[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());return a;}
 static Vector2[] R2(BinaryReader r,int n){var a=new Vector2[n];for(int i=0;i<n;i++)a[i]=new Vector2(r.ReadSingle(),r.ReadSingle());return a;}
 static Mesh ReadMesh(string path,string name,string gen){
  var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};
  using(var r=new BinaryReader(File.OpenRead(path))){
   int n=r.ReadInt32();r.ReadInt32();int s=r.ReadInt32();
   mesh.vertices=R3(r,n);mesh.normals=R3(r,n);mesh.uv=R2(r,n);mesh.subMeshCount=s;
   for(int i=0;i<s;i++){var a=new int[r.ReadInt32()];for(int j=0;j<a.Length;j++)a[j]=r.ReadInt32();mesh.SetTriangles(a,i,true);}
   if(r.BaseStream.Position!=r.BaseStream.Length)throw new Exception("Unread mesh bytes in "+path);
  }
  mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
  AssetDatabase.CreateAsset(mesh,gen+"/mesh_"+name+".asset");return mesh;
 }
 static Texture2D Import(string path,TextureImporterType type,bool srgb){
  var im=(TextureImporter)AssetImporter.GetAtPath(path);
  im.textureType=type;im.sRGBTexture=srgb;im.mipmapEnabled=true;im.wrapMode=TextureWrapMode.Repeat;im.anisoLevel=4;im.maxTextureSize=2048;im.SaveAndReimport();
  return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
 }
 // Placeholder shader with the p0 shader's texture slots; finalize.py points the materials at the game's shader.
 static Shader StubShader(){
  string p=Gen+"/EnvgbStub.shader";
  File.WriteAllText(p,"Shader \"UstAchae/ENVGB/Stub\" { Properties { _MainTex (\"m\", 2D) = \"white\" {} _SpecMap (\"s\", 2D) = \"white\" {} _BumpMap (\"b\", 2D) = \"bump\" {} _Color (\"c\", Color) = (1,1,1,1) } SubShader { Pass { Color (1,1,1,1) } } }");
  AssetDatabase.ImportAsset(p,ImportAssetOptions.ForceSynchronousImport);return AssetDatabase.LoadAssetAtPath<Shader>(p);
 }
 public static string BundleKey(string colour)=>"ustachae_envgb/envgb_"+colour+".bundle";

 public static void Build(){
  PlayerSettings.colorSpace=ColorSpace.Linear;
  if(AssetDatabase.IsValidFolder(Gen))AssetDatabase.DeleteAsset(Gen);
  Directory.CreateDirectory(Gen);Directory.CreateDirectory(Out);AssetDatabase.Refresh();
  var stub=StubShader();
  var builds=Directory.GetDirectories(Src).Select(d=>BuildOne(Path.GetFileName(d),stub)).ToArray();
  AssetDatabase.SaveAssets();
  if(BuildPipeline.BuildAssetBundles(Out,builds,BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode,BuildTarget.StandaloneWindows64)==null)throw new Exception("Build failed");
  Debug.Log("ENVGB_BUILD_SUCCESS "+string.Join(",",builds.Select(b=>b.assetBundleName)));
 }

 static AssetBundleBuild BuildOne(string colour,Shader stub){
  string src=Src+"/"+colour,gen=Gen+"/"+colour;Directory.CreateDirectory(gen);AssetDatabase.Refresh();
  var info=JsonUtility.FromJson<Info>(File.ReadAllText(src+"/info.json"));
  string name="nvg_envgb_psq42_"+colour;
  var glassMesh=ReadMesh(src+"/glass.bin","nvg_envgb_psq42_glass_LOD0",gen);

  // Material names carry the role for finalize.py: _body (textured p0) and _glass.
  var body=new Material(stub){name=name+"_body"};
  body.SetTexture("_MainTex",Import(src+"/albedo.png",TextureImporterType.Default,true));
  body.SetTexture("_SpecMap",Import(src+"/spec.png",TextureImporterType.Default,false));
  body.SetTexture("_BumpMap",Import(src+"/normal.png",TextureImporterType.NormalMap,false));
  AssetDatabase.CreateAsset(body,gen+"/mat_body.asset");
  var glass=new Material(stub){name=name+"_glass"};AssetDatabase.CreateAsset(glass,gen+"/mat_glass.asset");

  // The jointed mount from prepare.py (rig): each node at its rest pose in its parent, with its own mesh. The
  // shroud plate stays on the root; height screw and carriage on it; the flip hinge "axis" (CurveRotator) on the
  // carriage; arm, tilt pin, bracket, slide; the device node nvg_envgb_psq42_LOD0 last.
  var root=new GameObject(name);
  var made=new System.Collections.Generic.Dictionary<string,Transform>();
  var renderers=new System.Collections.Generic.List<Renderer>();
  foreach(var n in info.nodes){
   var go=new GameObject(n.name);go.transform.SetParent(n.parent==""?root.transform:made[n.parent],false);
   go.transform.localPosition=V(n.pos);go.transform.localRotation=Q(n.rot);made[n.name]=go.transform;
   if(n.mesh!=""){
    var mesh=ReadMesh(src+"/"+n.mesh+".bin",n.mesh,gen);
    go.AddComponent<MeshFilter>().sharedMesh=mesh;
    var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=body;r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;renderers.Add(r);
    var box=go.AddComponent<BoxCollider>();box.center=mesh.bounds.center;box.size=mesh.bounds.size;box.enabled=false;  // disabled like native items
   }
  }
  var lod=made[DeviceNode];var axis=made["axis"];
  var on=Q(info.onRotation);var off=Q(info.offRotation);
  if(Quaternion.Angle(axis.localRotation,on)>0.01f)throw new Exception("axis rest rotation is not OnRotation");

  var gl=new GameObject("nvg_envgb_psq42_glass_LOD0");gl.transform.SetParent(lod,false);
  gl.AddComponent<MeshFilter>().sharedMesh=glassMesh;
  var glR=gl.AddComponent<MeshRenderer>();glR.sharedMaterial=glass;glR.shadowCastingMode=ShadowCastingMode.Off;glR.receiveShadows=true;renderers.Add(glR);

  // Thermal objective: centre of its window, +Z out of the front (the device looks along -Y), +Y toward the mount.
  var lens=new GameObject(ThermalLens);lens.transform.SetParent(lod,false);
  lens.transform.localPosition=new Vector3(info.thermalLens[0],info.thermalLens[1],info.thermalLens[2]);
  lens.transform.localRotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);

  // Face light, as the GPNVG-18's "Point light" (off; the game switches it).
  var lightGo=new GameObject("Point light");lightGo.transform.SetParent(lod,false);
  lightGo.transform.localPosition=new Vector3(0.0004f,0.0516f,-0.075f);lightGo.transform.localRotation=new Quaternion(0.479f,0,0,0.878f);
  var light=lightGo.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(0.28275859f,1f,0f);light.intensity=3.14f;light.range=0.05f;light.enabled=false;

  var all=renderers.ToArray();
  var lodGroup=root.AddComponent<LODGroup>();lodGroup.SetLODs(new[]{new LOD(0.02f,all)});lodGroup.size=0.5f;
  var dress=root.AddComponent<Dress>();dress.DecalTextureType=1;dress.Renderers=all;
  root.AddComponent<NightVisionDevice>().Set(light);
  var rot=root.AddComponent<CurveRotator>();rot.RotatedTransform=axis;rot.RotationSpeed=RotationSpeed;rot.OffRotation=off;rot.OnRotation=on;rot.AnimationCurve=Curve();
  root.AddComponent<NightVisionMount>().Set(rot);
  var b=all[0].bounds;foreach(var r in all)b.Encapsulate(r.bounds);

  // Preview: the GPNVG-18's view angles, centred on this device's bounds in the root frame.
  var pivot=root.AddComponent<PreviewPivot>();
  pivot.pivotPosition=b.center;pivot.pivotRotation=new Quaternion(0.0074694254f,0.98476535f,0.04375f,-0.16812912f);pivot.scale=Vector3.one;
  pivot.Icon=new PreviewPivot.IconSettings{rotation=new Quaternion(-0.049385615f,-0.94817758f,-0.25407037f,0.1843058f),boundsScale=1.8080130f,perspective=15,orthographicSize=10};

  string prefab=gen+"/"+name+".prefab";PrefabUtility.SaveAsPrefabAsset(root,prefab);UnityEngine.Object.DestroyImmediate(root);
  return new AssetBundleBuild{assetBundleName=BundleKey(colour),assetNames=new[]{prefab}};
 }
}
