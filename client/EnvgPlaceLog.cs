using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace EnvgFusion;

// Temporary (to find why the ENVG-B floats in front of the face; remove once known): where the game actually hangs the
// device. Logged once with the device down and once up, each 1.5 s after the state is first seen (the flip settled),
// from the local player's own hierarchy (one walk at those two moments, nothing per frame).
internal static class EnvgPlaceLog
{
    private const string Envgb = "nvg_envgb_psq42_", Gpnvg = "nvg_l3_gpnvg-18_anvis";
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

    private static void Write(EFT.Player player, bool down)
    {
        Transform[] all = ((Component)player).transform.GetComponentsInChildren<Transform>(true);
        Transform? head = null;
        var roots = new List<Transform>();
        foreach (Transform t in all)
        {
            if (head == null && t.name == "Base HumanHead")
                head = t;
            bool envgb = t.name.StartsWith(Envgb), gpnvg = t.name.StartsWith(Gpnvg);
            if ((envgb || gpnvg) && (t.parent == null || !(t.parent.name.StartsWith(Envgb) || t.parent.name.StartsWith(Gpnvg))))
                roots.Add(t);
        }
        var sb = new StringBuilder();
        sb.Append("ENVG place: device ").Append(down ? "DOWN" : "UP").Append(", player ").Append(player.name)
          .Append(", ").Append(roots.Count).Append(" device root(s), head bone ").Append(head != null ? P(head.position) : "not found");
        EnvgFusionPlugin.Log?.LogInfo(sb.ToString());
        int n = 0;
        foreach (Transform root in roots)
        {
            n++;
            sb.Length = 0;
            sb.Append("ENVG place: [").Append(n).Append("] ").Append(root.name).Append(" (active ").Append(root.gameObject.activeInHierarchy).Append(")\n  chain: ");
            Transform? helmet = null;
            for (Transform? p = root; p != null; p = p.parent)
            {
                sb.Append(p.name);
                if (p.parent != null) sb.Append(" <- ");
                if (helmet == null && p != root && p.name.ToLowerInvariant().Contains("helmet"))
                    helmet = p;
            }
            Transform? parent = root.parent;
            sb.Append("\n  root local to parent ").Append(parent != null ? parent.name : "-").Append(": pos ").Append(P(root.localPosition))
              .Append(" rot ").Append(Q(root.localRotation));
            sb.Append("\n  helmet root: ").Append(helmet != null ? helmet.name : "none named *helmet* in the chain");
            Line(sb, "root", root, null, helmet, head);
            Transform? axis = Find(root, "axis");
            Line(sb, "axis", axis, null, helmet, head);
            if (axis != null)
                sb.Append("\n  axis localRotation ").Append(Q(axis.localRotation)).Append(" localEuler ").Append(P(axis.localEulerAngles));
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string lower = t.name.ToLowerInvariant();
                if (lower.EndsWith("_lod0") || t.name == "envgb_thermal_lens")
                    Line(sb, t.name, t, t.GetComponent<Renderer>(), helmet, head);
            }
            Line(sb, "helmet", helmet, null, helmet, head);
            Line(sb, "head bone", head, null, helmet, head);
            Rotators(sb, root);
            EnvgFusionPlugin.Log?.LogInfo(sb.ToString());
        }
    }

    private static Transform? Find(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name)
                return t;
        return null;
    }

    private static void Line(StringBuilder sb, string label, Transform? t, Renderer? r, Transform? helmet, Transform? head)
    {
        sb.Append("\n  ").Append(label).Append(": ");
        if (t == null) { sb.Append("not found"); return; }
        sb.Append("world ").Append(P(t.position));
        if (helmet != null) sb.Append(" in helmet ").Append(P(helmet.InverseTransformPoint(t.position)));
        if (head != null) sb.Append(" in head ").Append(P(head.InverseTransformPoint(t.position)));
        if (r != null)
        {
            Vector3 c = r.bounds.center;
            sb.Append(" | bounds centre world ").Append(P(c));
            if (helmet != null) sb.Append(" in helmet ").Append(P(helmet.InverseTransformPoint(c)));
            sb.Append(" (renderer enabled ").Append(r.enabled).Append(")");
        }
    }

    // the game's flip animation, whatever it is called on this device: every component named *Rotator* in the device,
    // with its number and bool fields
    private static void Rotators(StringBuilder sb, Transform root)
    {
        foreach (Component c in root.GetComponentsInChildren<Component>(true))
        {
            if (c == null || !c.GetType().Name.Contains("Rotator"))
                continue;
            sb.Append("\n  ").Append(c.GetType().FullName).Append(" on ").Append(c.name).Append(":");
            for (System.Type? type = c.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                foreach (FieldInfo f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (f.FieldType == typeof(float) || f.FieldType == typeof(bool) || f.FieldType == typeof(int))
                        sb.Append(' ').Append(f.Name).Append('=').Append(f.GetValue(c));
        }
    }

    private static string P(Vector3 v) => "(" + v.x.ToString("F4") + ", " + v.y.ToString("F4") + ", " + v.z.ToString("F4") + ")";
    private static string Q(Quaternion q) => "(" + q.x.ToString("F4") + ", " + q.y.ToString("F4") + ", " + q.z.ToString("F4") + ", " + q.w.ToString("F4") + ")";
}
