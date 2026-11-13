using System;
using System.IO;
using System.Linq;
using EFT.Visual;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
// Builds one bundle per colour from what item/prepare.py wrote to Assets/Source/<colour>. The prefab copies the
// GPNVG-18's layout and components (nvg_l3_gpnvg-18_anvis): root with LODGroup, Dress, NightVisionDevice,
// CurveRotator, NightVisionMount and PreviewPivot; the hinge "axis" (the GPNVG-18's place, deployed rotation)
// carrying the device node nvg_envgb_psq42_LOD0 (with its glass and the thermal lens marker as children) and the
// face light; the shroud adapter stays on the root. Run: -executeMethod EnvgbBuilder.Build
public static class EnvgbBuilder {
 const string Src="Assets/Source",Gen="Assets/Generated";
 static readonly string Out=Path.GetFullPath(Path.Combine(Application.dataPath,"../../built"));
 public const string DeviceNode="nvg_envgb_psq42_LOD0",ThermalLens="envgb_thermal_lens";
 [Serializable] public class Info {public string colour;public float[] thermalLens,boundsMin,boundsMax;}

 // The GPNVG-18's hinge and flip (from its bundle).
 static readonly Vector3 AxisPos=new Vector3(-0.000388f,-0.0322f,0.0456f);
 static readonly Quaternion OnRotation=new Quaternion(0.47860321f,-5.5665769e-08f,1.9227921e-08f,-0.87803131f);
 static readonly Quaternion OffRotation=new Quaternion(0.98575121f,-3.6230801e-08f,-4.6429719e-08f,0.16821051f);
 const float RotationSpeed=0.75187969f;
 static AnimationCurve Curve(){
  var c=new AnimationCurve(
   new Keyframe(0f,0f,2.1630912f,2.1630912f,0.33333334f,0.33333334f),
   new Keyframe(0.46189547f,0.99912202f,2.1630912f,0.0016316094f,0.33333334f,0.33333334f),
   new Keyframe(1f,1f,0.0016316094f,0.0016316094f,0.33333334f,0.33333334f));
  c.preWrapMode=WrapMode.ClampForever;c.postWrapMode=WrapMode.ClampForever;return c;
 }

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
  var device=ReadMesh(src+"/device.bin",DeviceNode,gen);
  var glassMesh=ReadMesh(src+"/glass.bin","nvg_envgb_psq42_glass_LOD0",gen);
  var mountMesh=ReadMesh(src+"/mount.bin","nvg_envgb_psq42_mount_LOD0",gen);

  // Material names carry the role for finalize.py: _body (textured p0) and _glass.
  var body=new Material(stub){name=name+"_body"};
  body.SetTexture("_MainTex",Import(src+"/albedo.png",TextureImporterType.Default,true));
  body.SetTexture("_SpecMap",Import(src+"/spec.png",TextureImporterType.Default,false));
  body.SetTexture("_BumpMap",Import(src+"/normal.png",TextureImporterType.NormalMap,false));
  AssetDatabase.CreateAsset(body,gen+"/mat_body.asset");
  var glass=new Material(stub){name=name+"_glass"};AssetDatabase.CreateAsset(glass,gen+"/mat_glass.asset");

  var root=new GameObject(name);
  var axis=new GameObject("axis");axis.transform.SetParent(root.transform,false);
  axis.transform.localPosition=AxisPos;axis.transform.localRotation=new Quaternion(-OnRotation.x,-OnRotation.y,-OnRotation.z,-OnRotation.w);

  var lod=new GameObject(DeviceNode);lod.transform.SetParent(axis.transform,false);
  lod.AddComponent<MeshFilter>().sharedMesh=device;
  var devR=lod.AddComponent<MeshRenderer>();devR.sharedMaterial=body;devR.shadowCastingMode=ShadowCastingMode.On;devR.receiveShadows=true;
  var box=lod.AddComponent<BoxCollider>();box.center=device.bounds.center;box.size=device.bounds.size;box.enabled=false;  // disabled like native items

  var gl=new GameObject("nvg_envgb_psq42_glass_LOD0");gl.transform.SetParent(lod.transform,false);
  gl.AddComponent<MeshFilter>().sharedMesh=glassMesh;
  var glR=gl.AddComponent<MeshRenderer>();glR.sharedMaterial=glass;glR.shadowCastingMode=ShadowCastingMode.Off;glR.receiveShadows=true;

  // Thermal objective: centre of its window, +Z out of the front (the device looks along -Y), +Y toward the mount.
  var lens=new GameObject(ThermalLens);lens.transform.SetParent(lod.transform,false);
  lens.transform.localPosition=new Vector3(info.thermalLens[0],info.thermalLens[1],info.thermalLens[2]);
  lens.transform.localRotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);

  // Face light, as the GPNVG-18's "Point light" (off; the game switches it).
  var lightGo=new GameObject("Point light");lightGo.transform.SetParent(axis.transform,false);
  lightGo.transform.localPosition=new Vector3(0.0004f,0.0516f,-0.075f);lightGo.transform.localRotation=new Quaternion(0.479f,0,0,0.878f);
  var light=lightGo.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(0.28275859f,1f,0f);light.intensity=3.14f;light.range=0.05f;light.enabled=false;

  var mount=new GameObject("nvg_envgb_psq42_mount_LOD0");mount.transform.SetParent(root.transform,false);
  mount.AddComponent<MeshFilter>().sharedMesh=mountMesh;
  var mR=mount.AddComponent<MeshRenderer>();mR.sharedMaterial=body;mR.shadowCastingMode=ShadowCastingMode.On;mR.receiveShadows=true;
  var mbox=mount.AddComponent<BoxCollider>();mbox.center=mountMesh.bounds.center;mbox.size=mountMesh.bounds.size;mbox.enabled=false;

  var renderers=new Renderer[]{devR,glR,mR};
  var lodGroup=root.AddComponent<LODGroup>();lodGroup.SetLODs(new[]{new LOD(0.02f,renderers)});lodGroup.size=0.5f;
  var dress=root.AddComponent<Dress>();dress.DecalTextureType=1;dress.Renderers=renderers;
  root.AddComponent<NightVisionDevice>().Set(light);
  var rot=root.AddComponent<CurveRotator>();rot.RotatedTransform=axis.transform;rot.RotationSpeed=RotationSpeed;rot.OffRotation=OffRotation;rot.OnRotation=OnRotation;rot.AnimationCurve=Curve();
  root.AddComponent<NightVisionMount>().Set(rot);

  // Preview: the GPNVG-18's view angles, centred on this device's bounds in the root frame.
  var b=new Bounds(mR.bounds.center,mR.bounds.size);b.Encapsulate(devR.bounds);
  var pivot=root.AddComponent<PreviewPivot>();
  pivot.pivotPosition=b.center;pivot.pivotRotation=new Quaternion(0.0074694254f,0.98476535f,0.04375f,-0.16812912f);pivot.scale=Vector3.one;
  pivot.Icon=new PreviewPivot.IconSettings{rotation=new Quaternion(-0.049385615f,-0.94817758f,-0.25407037f,0.1843058f),boundsScale=1.8080130f,perspective=15,orthographicSize=10};

  string prefab=gen+"/"+name+".prefab";PrefabUtility.SaveAsPrefabAsset(root,prefab);UnityEngine.Object.DestroyImmediate(root);
  return new AssetBundleBuild{assetBundleName=BundleKey(colour),assetNames=new[]{prefab}};
 }
}
