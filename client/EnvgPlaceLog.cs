using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace EnvgFusion;

// Temporary (to find why the ENVG-B floats in front of the face, and why the thermal rectangle in the eyepieces is
// not where it should be; remove once known). The device's place: once with the device down and once up, each 1.5 s
// after the state is first seen, from every device in the loaded scenes (one search at each of those two moments).
// The eyepieces: one picture of what we hand them, with the numbers it was drawn with.
internal static class EnvgPlaceLog
{
    private static readonly string[] Prefixes = { "nvg_envgb_psq42_", "nvg_l3_gpnvg-18_anvis" };
    private static bool _loggedDown, _loggedUp;
    private static float _at = -1f;
    private static bool _pendingDown;

    // from EnvgbDown, once a frame while the ENVG-B is in the headwear slot
    internal static void Seen(EFT.Player player, bool down)
    {
        if (down ? _loggedDown : _loggedUp)
            return;
        if (_at < 0f || _pendingDown != down)
        {
            _pendingDown = down;
            _at = Time.unscaledTime + 1.5f;
            return;
        }
        if (Time.unscaledTime < _at)
            return;
        _at = -1f;
        if (down) _loggedDown = true; else _loggedUp = true;
        try
        {
            Write(player, down);
        }
        catch (System.Exception e)
        {
            EnvgFusionPlugin.Log?.LogWarning("ENVG place: failed: " + e);
        }
    }

    private static bool Named(Transform t)
    {
        foreach (string p in Prefixes)
            if (t.name.StartsWith(p))
                return true;
        return false;
    }

    private static void Write(EFT.Player player, bool down)
    {
        Transform me = ((Component)player).transform;
        int roots = 0;
        foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (!t.gameObject.scene.IsValid() || !Named(t))
                continue;
            bool child = false;
            for (Transform? p = t.parent; p != null; p = p.parent)
                if (Named(p)) { child = true; break; }
            if (child)
                continue;
            roots++;
            Device(t, me, down, roots);
        }
        EnvgFusionPlugin.Log?.LogInfo("ENVG place: device " + (down ? "DOWN" : "UP") + ", local player " + me.name + " at " + P(me.position) + ", " + roots + " device root(s) in the scene (each logged above)");
    }

    private static void Device(Transform root, Transform me, bool down, int n)
    {
        var sb = new StringBuilder();
        sb.Append("ENVG place: ").Append(down ? "DOWN" : "UP").Append(" [").Append(n).Append("] ").Append(root.name)
          .Append(" active ").Append(root.gameObject.activeInHierarchy).Append(", on the local player ").Append(root.IsChildOf(me))
          .Append(", scene ").Append(root.gameObject.scene.name).Append(", parent ").Append(root.parent != null ? root.parent.name : "none");
        Transform? helmet = null, head = null;
        sb.Append("\nENVG place:   chain (name local pos / local rot):");
        for (Transform? p = root; p != null; p = p.parent)
        {
            sb.Append("\nENVG place:     ").Append(p.name).Append(' ').Append(P(p.localPosition)).Append(" / ").Append(Q(p.localRotation));
            if (helmet == null && p != root && p.name.ToLowerInvariant().Contains("helmet"))
                helmet = p;
            if (head == null && p.name == "Base HumanHead")
                head = p;
        }
        Line(sb, "root", root, null, helmet, head);
        Transform? axis = null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "axis")
                axis = t;
            string lower = t.name.ToLowerInvariant();
            if (lower.EndsWith("_lod0") || lower.Contains("mount") || t.name == "envgb_thermal_lens")
                Line(sb, t.name, t, t.GetComponent<Renderer>(), helmet, head);
        }
        Line(sb, "axis", axis, null, helmet, head);
        if (axis != null)
            sb.Append("\nENVG place:   axis localRotation ").Append(Q(axis.localRotation)).Append(" localEuler ").Append(P(axis.localEulerAngles));
        Line(sb, "helmet " + (helmet != null ? helmet.name : "(none named *helmet* in the chain)"), helmet, null, helmet, head);
        Line(sb, "head bone of this device's player", head, null, helmet, head);
        foreach (Component c in root.GetComponentsInChildren<Component>(true))
        {
            if (c == null || !c.GetType().Name.Contains("Rotator"))
                continue;
            sb.Append("\nENVG place:   ").Append(c.GetType().FullName).Append(" on ").Append(c.name).Append(':');
            for (System.Type? type = c.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                foreach (FieldInfo f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (f.FieldType == typeof(float) || f.FieldType == typeof(bool) || f.FieldType == typeof(int))
                        sb.Append(' ').Append(f.Name).Append('=').Append(f.GetValue(c));
        }
        EnvgFusionPlugin.Log?.LogInfo(sb.ToString());
    }

    private static void Line(StringBuilder sb, string label, Transform? t, Renderer? r, Transform? helmet, Transform? head)
    {
        sb.Append("\nENVG place:   ").Append(label).Append(": ");
        if (t == null) { sb.Append("not found"); return; }
        sb.Append("world ").Append(P(t.position));
        if (helmet != null) sb.Append(" in helmet ").Append(P(helmet.InverseTransformPoint(t.position)));
        if (head != null) sb.Append(" in head ").Append(P(head.InverseTransformPoint(t.position)));
        if (r != null)
        {
            Vector3 c = r.bounds.center;
            sb.Append(" | bounds centre world ").Append(P(c));
            if (helmet != null) sb.Append(" in helmet ").Append(P(helmet.InverseTransformPoint(c)));
            if (head != null) sb.Append(" in head ").Append(P(head.InverseTransformPoint(c)));
            sb.Append(" (renderer enabled ").Append(r.enabled).Append(')');
        }
    }

    // ---- the eyepieces: what we hand them and the screen of the same frame, 2 s after a fused mode is switched to
    // (each switch once, at most 5 a session) ----
    private static float _fuseAt = -1f;
    private static bool _fuseDumped;
    private static int _fuseDumps;
    internal static void Rearm()
    {
        if (_fuseDumps < 5)
        {
            _fuseDumped = false;
            _fuseAt = -1f;
        }
    }
    internal static void AfterFuse(RenderTexture i2, RenderTexture dest, Vector4 at, Vector4 field)
    {
        if (_fuseDumped)
            return;
        string show = EnvgFusionPlugin.Show?.Value ?? "";
        if (show != EnvgFusionPlugin.ShowFusion && show != EnvgFusionPlugin.ShowBlackHot)
            return;
        if (_fuseAt < 0f)
        {
            _fuseAt = Time.unscaledTime + 2f;
            return;
        }
        if (Time.unscaledTime < _fuseAt)
            return;
        _fuseDumped = true;
        _fuseDumps++;
        try
        {
            string dir = Path.Combine(Path.GetDirectoryName(typeof(EnvgPlaceLog).Assembly.Location) ?? "", "dump");
            Directory.CreateDirectory(dir);
            string stamp = System.DateTime.Now.ToString("HHmmss");
            Save(dest, Path.Combine(dir, "fuse-" + stamp + "-dest.png"));
            Save(i2, Path.Combine(dir, "fuse-" + stamp + "-i2.png"));
            // the screen as this frame ends: what the eyes see, to set against dest
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "fuse-" + stamp + "-screen.png"));
            int w = dest.width, h = dest.height;
            EnvgFusionPlugin.Log?.LogInfo("ENVG place: eyepiece picture saved (dump/fuse-" + stamp + "-dest.png, -i2.png and -screen.png, " + show + ", " + w + "x" + h + "), FieldInMainUv "
                + V(at) + ", _EnvgField " + V(field) + ": thermal rectangle x " + ((at.x - field.x) * w).ToString("F0") + "-" + ((at.x + field.x) * w).ToString("F0")
                + ", y from the top " + ((1f - at.y - field.y) * h).ToString("F0") + "-" + ((1f - at.y + field.y) * h).ToString("F0")
                + " px (" + (2f * field.x * w).ToString("F0") + "x" + (2f * field.y * h).ToString("F0") + "), screen " + Screen.width + "x" + Screen.height
                + ", camera aspect " + (Camera.main != null ? Camera.main.aspect.ToString("F4") : "-") + ", ThermalField " + (EnvgFusionPlugin.ThermalField?.Value ?? 1f));
        }
        catch (System.Exception e)
        {
            EnvgFusionPlugin.Log?.LogWarning("ENVG place: eyepiece picture failed: " + e);
        }
    }

    private static void Save(RenderTexture rt, string path)
    {
        RenderTexture? was = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = was;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.Destroy(tex);
    }

    private static string P(Vector3 v) => "(" + v.x.ToString("F4") + ", " + v.y.ToString("F4") + ", " + v.z.ToString("F4") + ")";
    private static string Q(Quaternion q) => "(" + q.x.ToString("F4") + ", " + q.y.ToString("F4") + ", " + q.z.ToString("F4") + ", " + q.w.ToString("F4") + ")";
    private static string V(Vector4 v) => "(" + v.x.ToString("F4") + ", " + v.y.ToString("F4") + ", " + v.z.ToString("F4") + ", " + v.w.ToString("F4") + ")";
}
