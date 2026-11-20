using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnvgFusion;

// TEMPORARY (asked for to check the thermal lens's view; to be removed): the gun in hand as the ENVG-B's thermal lens
// sees it, from the device's envgb_thermal_lens with the camera's own turn and field, shown small in the screen's
// lower left. Its body grey, the sights' glass cyan over it, its outline copper. Drawn once a frame after the scene's
// update, outside the cameras' own rendering; the gun's parts collected once per gun.
internal static class EnvgProbe
{
    private const int Width = 512, Height = 288;
    private static Material? _material;
    private static RenderTexture? _scene, _picture;
    private static CommandBuffer? _draw;
    private static GameObject? _gun;
    private struct Part { public Renderer Renderer; public int Sub; public bool Glass; }
    private static readonly List<Part> _parts = new List<Part>();
    private static bool _shown;
    private static int _glassParts;

    internal static Texture? Picture => _shown ? _picture : null;
    // the lens's place against the camera's (its right, up, forward; cm) and what is drawn, for the label under the probe
    internal static Vector3 LensOffsetCm;
    internal static string What = "";

    internal static void SetShader(Shader shader)
    {
        if (shader != null && shader.isSupported)
            _material = new Material(shader) { hideFlags = HideFlags.DontSave };
    }

    internal static void Render()
    {
        _shown = false;
        if (EnvgFusionPlugin.Probe?.Value != true || _material == null)
            return;
        Transform? lens = EnvgPods.ThermalLens;
        Camera? main = Camera.main;
        EFT.Player? wearer = EnvgFusion.Wearer;
        if (lens == null || main == null || wearer == null)
            return;
        GameObject? gun = null;
        try { gun = wearer.HandsController?.ControllerGameObject; } catch (System.Exception) { }
        if (gun == null)
            return;
        if (gun != _gun)
        {
            _gun = gun;
            _parts.Clear();
            foreach (Renderer r in gun.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer)
                    continue;
                Material[] materials = r.sharedMaterials;
                for (int sub = 0; sub < materials.Length; sub++)
                    if (materials[sub] != null)
                        _parts.Add(new Part { Renderer = r, Sub = sub, Glass = materials[sub].renderQueue > 2500 });
            }
            _glassParts = 0;
            foreach (Part part in _parts)
                if (part.Glass)
                    _glassParts++;
            What = gun.name.Replace("(Clone)", "") + "  " + (_parts.Count - _glassParts) + " parts, " + _glassParts + " glass";
            EnvgFusionPlugin.Log?.LogInfo("ENVG probe (temporary): " + gun.name + " from the thermal lens, " + (_parts.Count - _glassParts) + " solid parts, " + _glassParts + " glass parts");
        }
        if (_scene == null)
        {
            _scene = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBHalf) { name = "Justachae ENVG probe" };
            _picture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32) { name = "Justachae ENVG probe, shown" };
            _scene.Create();
            _picture.Create();
        }
        Matrix4x4 view = main.worldToCameraMatrix * Matrix4x4.Translate(main.transform.position - lens.position);
        Vector3 d = main.transform.InverseTransformVector(lens.position - main.transform.position) * 100f;
        LensOffsetCm = d;
        Matrix4x4 projection = Matrix4x4.Perspective(main.fieldOfView, (float)Width / Height, 0.01f, 20f);
        _draw ??= new CommandBuffer { name = "Justachae ENVG probe" };
        _draw.Clear();
        _draw.SetRenderTarget(_scene);
        _draw.ClearRenderTarget(true, true, new Color(0.05f, 0.05f, 0.05f, 100f));
        _draw.SetViewProjectionMatrices(view, projection);
        foreach (Part part in _parts)
            if (part.Renderer != null && !part.Glass && part.Renderer.gameObject.activeInHierarchy)
                _draw.DrawRenderer(part.Renderer, _material, part.Sub, 0);
        foreach (Part part in _parts)
            if (part.Renderer != null && part.Glass && part.Renderer.gameObject.activeInHierarchy)
                _draw.DrawRenderer(part.Renderer, _material, part.Sub, 1);
        _draw.Blit(_scene, _picture, _material, 2);
        Graphics.ExecuteCommandBuffer(_draw);
        _shown = true;
    }
}
