using UnityEngine;
using UnityEngine.Rendering;

// Offline check (not shipped): a front object hidden from a deferred camera's culling and drawn back into its G-buffer
// after the G-buffer is copied. Does the copy see what is behind it, and does the drawn-back object come out exactly
// as when the camera draws it itself (lit by a point light, so the lighting's depth matters)?
public static class EnvgHideTest
{
    static Renderer front;
    static Camera cam;
    static bool hide;

    public static void Run()
    {
        var root = new GameObject("test");
        cam = new GameObject("cam").AddComponent<Camera>();
        cam.transform.SetParent(root.transform);
        cam.renderingPath = RenderingPath.DeferredShading;
        cam.allowHDR = true;
        cam.allowMSAA = false;
        cam.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
        cam.backgroundColor = Color.black;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.nearClipPlane = 0.05f;
        var target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGBHalf);
        cam.targetTexture = target;
        var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
        back.transform.position = new Vector3(0, 0, 5);
        back.transform.localScale = Vector3.one * 20;
        back.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = Color.green };
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = new Vector3(0, 0, 2);
        cube.transform.rotation = Quaternion.Euler(20, 30, 0);
        var mat = new Material(Shader.Find("Standard")) { color = Color.red };
        front = cube.GetComponent<Renderer>();
        front.sharedMaterial = mat;
        var light = new GameObject("light").AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 10;
        light.intensity = 3;
        light.transform.position = new Vector3(1, 1, 0.5f);

        var peek = new Material(Shader.Find("Hidden/EnvgDepthPeek"));
        var albedoCopy = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGB32);
        var depthEarly = new RenderTexture(128, 128, 0, RenderTextureFormat.RFloat);
        var depthLate = new RenderTexture(128, 128, 0, RenderTextureFormat.RFloat);
        var depthSub = new RenderTexture(128, 128, 0, RenderTextureFormat.RFloat);
        var depthResolved = new RenderTexture(128, 128, 0, RenderTextureFormat.RFloat);

        var early = new CommandBuffer { name = "copy, then the front drawn back" };
        early.Blit(BuiltinRenderTextureType.GBuffer0, albedoCopy);
        early.Blit(BuiltinRenderTextureType.None, depthEarly, peek, 0);
        early.SetGlobalTexture("_EnvgDepthBuffer", new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget), RenderTextureSubElement.Depth);
        early.Blit(BuiltinRenderTextureType.None, depthSub, peek, 1);
        early.SetGlobalTexture("_EnvgDepthBuffer", BuiltinRenderTextureType.ResolvedDepth);
        early.Blit(BuiltinRenderTextureType.None, depthResolved, peek, 1);
        early.SetRenderTarget(new RenderTargetIdentifier[] { BuiltinRenderTextureType.GBuffer0, BuiltinRenderTextureType.GBuffer1, BuiltinRenderTextureType.GBuffer2, BuiltinRenderTextureType.CameraTarget }, BuiltinRenderTextureType.CameraTarget);
        early.DrawRenderer(front, mat, 0, mat.FindPass("DEFERRED"));
        var late = new CommandBuffer { name = "late depth" };
        late.Blit(BuiltinRenderTextureType.None, depthLate, peek, 0);
        var mvCopy = new RenderTexture(128, 128, 0, RenderTextureFormat.RGHalf);
        var mv = new CommandBuffer { name = "motion" };
        mv.Blit(BuiltinRenderTextureType.MotionVectors, mvCopy);

        Camera.onPreCull += c => { if (c == cam && hide) front.forceRenderingOff = true; };
        Camera.onPreRender += c => { if (c == cam && hide) front.forceRenderingOff = false; };

        // reference: drawn by the camera itself
        hide = false;
        cam.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, late);
        cam.AddCommandBuffer(CameraEvent.BeforeImageEffects, mv);
        cube.GetComponent<Renderer>().motionVectorGenerationMode = MotionVectorGenerationMode.Object;
        cam.Render();
        cube.transform.position += new Vector3(0.05f, 0, 0);
        cam.Render();
        Color mvRef = Read(mvCopy)[64 * 128 + 64];
        cube.transform.position -= new Vector3(0.05f, 0, 0);
        cam.Render();
        Color[] refPix = Read(target);
        float refLate = Read(depthLate)[64 * 128 + 64].r;

        // hidden, copied, drawn back
        hide = true;
        cam.AddCommandBuffer(CameraEvent.AfterGBuffer, early);
        var mvMat = new Material(Shader.Find("Justachae/ENVG/GunMotion"));
        var mvBack = new CommandBuffer { name = "motion drawn back" };
        cam.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, mvBack);
        System.Action<Matrix4x4> setMotion = prevM =>
        {
            Matrix4x4 vp = cam.nonJitteredProjectionMatrix * cam.worldToCameraMatrix;
            mvBack.Clear();
            mvBack.SetRenderTarget(BuiltinRenderTextureType.MotionVectors, BuiltinRenderTextureType.CameraTarget);
            mvBack.SetGlobalMatrix("_EnvgNowVP", vp);
            mvBack.SetGlobalMatrix("_EnvgPrevVP", vp);
            mvBack.SetGlobalMatrix("_EnvgPrevM", prevM);
            mvBack.DrawRenderer(front, mvMat, 0, 0);
        };
        setMotion(cube.transform.localToWorldMatrix);
        cam.Render();
        Matrix4x4 before = cube.transform.localToWorldMatrix;
        cube.transform.position += new Vector3(0.05f, 0.05f, 0);
        setMotion(before);
        cam.Render();
        Color mvHidden = Read(mvCopy)[64 * 128 + 64];
        cube.transform.position -= new Vector3(0.05f, 0.05f, 0);
        setMotion(cube.transform.localToWorldMatrix);
        cam.Render();
        Debug.Log("ENVG_HIDE_TEST motion at the centre: hidden and drawn back " + mvHidden + ", expected about (0.031, 0.031) for the cube moved 5 cm right and up at 1.385 m (camera's own in this offline run, unreliable: " + mvRef + ")");
        Color[] pix = Read(target);
        float maxDiff = 0;
        for (int i = 0; i < pix.Length; i++)
            maxDiff = Mathf.Max(maxDiff, Mathf.Max(Mathf.Abs(pix[i].r - refPix[i].r), Mathf.Max(Mathf.Abs(pix[i].g - refPix[i].g), Mathf.Abs(pix[i].b - refPix[i].b))));
        Color albedo = Read(albedoCopy)[64 * 128 + 64];
        float dEarly = Read(depthEarly)[64 * 128 + 64].r, dLate = Read(depthLate)[64 * 128 + 64].r;
        Debug.Log("ENVG_HIDE_TEST centre: copied albedo " + albedo + " (green = the back seen), depth at the copy " + dEarly.ToString("F3")
            + ", depth later " + dLate.ToString("F3") + " (camera's own " + refLate.ToString("F3") + "), final centre " + pix[64 * 128 + 64]
            + "; depth buffer as a texture " + Read(depthSub)[64 * 128 + 64].r.ToString("F3") + ", resolved depth " + Read(depthResolved)[64 * 128 + 64].r.ToString("F3")
            + " vs camera's own " + refPix[64 * 128 + 64] + ", largest difference over the frame " + maxDiff.ToString("F5"));
    }

    static Color[] Read(RenderTexture rt)
    {
        var was = RenderTexture.active;
        RenderTexture.active = rt;
        var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = was;
        return t.GetPixels();
    }
}
