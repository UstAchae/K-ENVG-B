using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore;

namespace EnvgFusion;

// The ENVG-B's symbology, as its overlay display draws it into the thermal rectangle: light added to the eyepiece's
// picture (so it glows, cyan-white, and can't darken anything). Top left the position (grid) and the range along the
// eyepiece's axis, top centre the heading on a compass tape, top right the time (Zulu) and the image source, bottom
// left the battery, bottom centre the display mode.
//
// Text from one of the game's own TextMeshPro fonts: its signed-distance atlas gives sharp glyphs at any size and the
// glow straight from the distance. One mesh, rebuilt each frame from reused lists (the heading and range change as the
// head moves); nothing allocates once it runs.
internal static class EnvgHud
{
    private static TMP_FontAsset? _font;
    private static bool _fontTried;
    private static Mesh? _mesh;
    private static readonly List<Vector3> _pos = new List<Vector3>(2048);
    private static readonly List<Vector2> _uv = new List<Vector2>(2048), _aux = new List<Vector2>(2048);
    private static readonly List<int> _tris = new List<int>(3072);
    private static readonly List<Color32> _tint = new List<Color32>(2048);
    // the symbology's own colour (alpha 0); the battery's cells green, as the device shows its charge
    private static readonly Color32 Own = new Color32(0, 0, 0, 0), Charge = new Color32(70, 255, 60, 255);
    private static Color32 _paint = Own;
    // an italic face drawn upright: each glyph's top shifted back by its slant
    private static float _unslant;
    private static readonly char[] _buf = new char[48];
    private static int _layerMask = -1;
    private static float _range = -1f;
    private static int _rangeFrame = -1;
    private static bool _logged;

    private static readonly int FontId = Shader.PropertyToID("_EnvgHudFont");
    private static readonly int SdfId = Shader.PropertyToID("_EnvgHudSdf");
    private static readonly int CoreId = Shader.PropertyToID("_EnvgHudCore");
    private static readonly int GlowColourId = Shader.PropertyToID("_EnvgHudGlow");
    internal const int Pass = 35;

    // what glyphs the symbology needs
    private const string Chars = "0123456789.:-° ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private static bool EnsureFont()
    {
        if (_font != null)
            return true;
        if (_fontTried)
            return false;
        _fontTried = true;
        try
        {
            // the game's own fonts (loaded with its UI): one that has every glyph the symbology needs, a plain one first
            TMP_FontAsset? best = null;
            int bestScore = -1;
            foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (f == null || f.atlasTexture == null)
                    continue;
                if (f.atlasPopulationMode == AtlasPopulationMode.Dynamic)
                    f.TryAddCharacters(Chars, out string _);
                int have = 0;
                foreach (char c in Chars)
                    if (f.characterLookupTable.ContainsKey(c))
                        have++;
                string n = f.name.ToLowerInvariant();
                // never one the game marks as not for use, an upright face before an italic one, a regular weight
                if (n.Contains("don't use") || n.Contains("dont use") || n.Contains("do not use"))
                    continue;
                int score = have * 10 + (n.Contains("bender") ? 5 : 0) + (n.Contains("italic") || n.Contains("oblique") ? 0 : 4)
                    + (n.Contains("bold") || n.Contains("black") ? 0 : 2);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = f;
                }
            }
            _font = best;
            // an italic face (when the game has no upright one): about 12 degrees of slant taken back
            _unslant = _font != null && (_font.name.ToLowerInvariant().Contains("italic") || _font.name.ToLowerInvariant().Contains("oblique")) ? 0.21f : 0f;
            EnvgFusionPlugin.Log?.LogInfo(_font != null
                ? "ENVG symbology font: " + _font.name + " (" + bestScore / 10 + " of " + Chars.Length + " glyphs, padding " + _font.atlasPadding + ", " + _font.atlasRenderMode + ", point size " + _font.faceInfo.pointSize + (_unslant > 0f ? ", italic drawn upright" : "") + ")"
                : "ENVG symbology: no TextMeshPro font found, no symbology");
        }
        catch (System.Exception e)
        {
            EnvgFusionPlugin.Log?.LogWarning("ENVG symbology font failed: " + e.Message);
        }
        return _font != null;
    }

    // ---- the readings ----

    // the range along the eyepiece's axis: the first solid surface (high-poly colliders and terrain), once a frame
    private static float Range(Camera main, Vector2 axisUv)
    {
        if (_rangeFrame == Time.frameCount)
            return _range;
        _rangeFrame = Time.frameCount;
        if (_layerMask == -1)
        {
            _layerMask = LayerMask.GetMask("HighPolyCollider", "Terrain");
            if (_layerMask == 0)
                _layerMask = Physics.DefaultRaycastLayers;
        }
        Ray ray = main.ViewportPointToRay(new Vector3(axisUv.x, axisUv.y, 0f));
        _range = Physics.Raycast(ray.origin + ray.direction * 0.3f, ray.direction, out RaycastHit hit, 3000f, _layerMask, QueryTriggerInteraction.Ignore)
            ? hit.distance + 0.3f : -1f;
        return _range;
    }

    // the time on the device: Zulu (the game's clock is Moscow time, UTC+3)
    private static System.DateTime Zulu()
    {
        try
        {
            if (Comfort.Common.Singleton<EFT.GameWorld>.Instantiated && Comfort.Common.Singleton<EFT.GameWorld>.Instance.GameDateTime != null)
                return Comfort.Common.Singleton<EFT.GameWorld>.Instance.GameDateTime.Calculate().AddHours(-3);
        }
        catch (System.Exception)
        {
        }
        return System.DateTime.UtcNow;
    }

    // ---- drawing ----

    // Adds the symbology to the eyepiece picture: within the thermal rectangle (centre and half sizes in dest pixels).
    internal static void Draw(CommandBuffer cb, Material m, Camera main, RenderTexture dest, Vector2 centre, Vector2 half, Vector2 axisUv, string mode, float strength)
    {
        if (!EnsureFont() || _font == null || strength <= 0f)
            return;
        _mesh ??= new Mesh { name = "Justachae ENVG symbology" };
        _mesh.MarkDynamic();
        _pos.Clear(); _uv.Clear(); _aux.Clear(); _tris.Clear(); _tint.Clear();
        _paint = Own;
        float w = dest.width, h = dest.height;
        float ts = Mathf.Max(10f, 0.052f * 2f * half.y);   // the text's em, in pixels
        float left = centre.x - half.x + 0.8f * ts, right = centre.x + half.x - 0.8f * ts;
        float top1 = centre.y + half.y - 1.25f * ts, top2 = top1 - 1.25f * ts;

        // top left: grid and range
        Vector3 p = main.transform.position;
        int n = 0;
        Put("36V UF ", ref n);
        PutInt(Mod((int)Mathf.Floor((p.x + 50000f) / 10f), 10000), 4, ref n);
        _buf[n++] = ' ';
        PutInt(Mod((int)Mathf.Floor((p.z + 50000f) / 10f), 10000), 4, ref n);
        Text(_buf, n, left, top1, ts, 0f);
        float range = Range(main, axisUv);
        n = 0;
        if (range > 0f)
        {
            int dm = Mathf.RoundToInt(range * 10f);
            PutInt(dm / 10, 1, ref n);
            _buf[n++] = '.';
            PutInt(dm % 10, 1, ref n);
        }
        else
            Put("---.-", ref n);
        Put(" M", ref n);
        Text(_buf, n, left, top2, ts, 0f);

        // top right: time (Zulu) and the image source
        System.DateTime z = Zulu();
        n = 0;
        PutInt(z.Hour, 2, ref n); _buf[n++] = ':'; PutInt(z.Minute, 2, ref n); _buf[n++] = ':'; PutInt(z.Second, 2, ref n);
        Put(" Z", ref n);
        Text(_buf, n, right, top1, ts, 1f);
        n = 0;
        Put("FUSED", ref n);
        Text(_buf, n, right, top2, ts, 1f);

        // top centre: heading over the compass tape (north along the world's +z, as the game's compass)
        Vector3 f = main.transform.forward;
        float heading = Mathf.Repeat(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 360f);
        n = 0;
        PutInt(Mathf.RoundToInt(heading) % 360, 3, ref n);
        Text(_buf, n, centre.x, top1 + 0.15f * ts, ts, 0.5f);
        float tapeY = top2 + 0.1f * ts, tapeHalf = Mathf.Min(0.36f * half.x, 7f * ts), perDeg = tapeHalf / 30f;
        float line = Mathf.Max(1f, 0.07f * ts);
        Box(centre.x, tapeY + 0.55f * ts, 0.18f * ts, 0.18f * ts, 0.45f * ts);              // the pointer
        Box(centre.x, tapeY, tapeHalf, line * 0.5f, 0.35f * ts);                               // the base line
        int first = Mathf.CeilToInt((heading - 30f) / 5f) * 5;
        for (int deg = first; deg <= heading + 30f; deg += 5)
        {
            float x = centre.x + (deg - heading) * perDeg;
            bool major = Mod(deg, 10) == 0;
            float tick = major ? 0.42f * ts : 0.22f * ts;
            Box(x, tapeY + tick * 0.5f, line * 0.5f, tick * 0.5f, 0.35f * ts);
            if (major && Mathf.Abs(deg - heading) < 27f)
            {
                n = 0;
                PutInt(Mod(deg, 360), 3, ref n);
                if (_font.characterLookupTable.ContainsKey('°'))
                    _buf[n++] = '°';
                Text(_buf, n, x, tapeY - 1.0f * ts, 0.62f * ts, 0.5f);
            }
            if (Mod(deg, 90) == 0)
            {
                char c = "NESW"[Mod(deg, 360) / 90];
                _buf[0] = c;
                Text(_buf, 1, x, tapeY + 0.6f * ts, 0.62f * ts, 0.5f);
            }
        }

        // bottom centre: the display mode
        float bottom = centre.y - half.y + 0.75f * ts;
        n = 0;
        Put(mode == EnvgFusionPlugin.ShowOutline ? "OUTLINE" : mode == EnvgFusionPlugin.ShowFusion ? "WHITE HOT" : mode == EnvgFusionPlugin.ShowBlackHot ? "BLACK HOT" : "THERMAL", ref n);
        Text(_buf, n, centre.x, bottom, ts, 0.5f);

        // bottom left: the battery (full)
        float bx = left + 1.1f * ts, by = bottom + 0.35f * ts, bw = 1.1f * ts, bh = 0.45f * ts;
        Box(bx, by + bh, bw, line * 0.5f, 0.3f * ts);
        Box(bx, by - bh, bw, line * 0.5f, 0.3f * ts);
        Box(bx - bw, by, line * 0.5f, bh, 0.3f * ts);
        Box(bx + bw, by, line * 0.5f, bh, 0.3f * ts);
        Box(bx - bw - 0.12f * ts, by, 0.12f * ts, 0.2f * ts, 0.3f * ts);
        _paint = Charge;
        for (int k = 0; k < 4; k++)
            Box(bx - bw + (k + 0.5f) * (2f * bw / 4f), by, 0.18f * ts, bh - 0.16f * ts, 0.3f * ts);
        _paint = Own;

        // into clip space (upside down where a texture's first row is its top)
        float flip = SystemInfo.graphicsUVStartsAtTop ? -1f : 1f;
        for (int i = 0; i < _pos.Count; i++)
        {
            Vector3 v = _pos[i];
            _pos[i] = new Vector3(v.x / w * 2f - 1f, flip * (v.y / h * 2f - 1f), v.z);
        }
        _mesh.Clear();
        _mesh.SetVertices(_pos);
        _mesh.SetUVs(0, _uv);
        _mesh.SetUVs(1, _aux);
        _mesh.SetColors(_tint);
        _mesh.SetTriangles(_tris, 0, false);
        _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);

        m.SetTexture(FontId, _font.atlasTexture);
        // atlas distance: alpha 0.5 on the outline, changing by 0.5 over the gradient scale (padding + 1) atlas texels
        m.SetVector(SdfId, new Vector4(2f * (_font.atlasPadding + 1f), 0f, 0f, 0f));
        // the overlay's cyan-white: a near-white core, a cyan glow a few pixels wide
        m.SetVector(CoreId, new Vector4(0.80f * strength, 1.00f * strength, 1.00f * strength, 0.22f * ts));
        m.SetVector(GlowColourId, new Vector4(0.10f * strength, 0.75f * strength, 1.00f * strength, 0.55f));
        cb.DrawMesh(_mesh, Matrix4x4.identity, m, 0, Pass);
        if (!_logged)
        {
            _logged = true;
            EnvgFusionPlugin.Log?.LogInfo("ENVG symbology drawn into the eyepieces (" + _pos.Count / 4 + " quads, text " + ts.ToString("F0") + " px)");
        }
    }

    // a line of text: x is its left, right or centre edge by align 0, 1, 0.5; y its baseline (pixels)
    private static void Text(char[] s, int count, float x, float y, float size, float align)
    {
        TMP_FontAsset font = _font!;
        FaceInfo face = font.faceInfo;
        float scale = size / Mathf.Max(face.pointSize, 1f);
        float pad = font.atlasPadding;
        float aw = font.atlasWidth, ah = font.atlasHeight;
        float width = 0f;
        for (int i = 0; i < count; i++)
            if (font.characterLookupTable.TryGetValue(s[i], out TMP_Character ch) && ch.glyph != null)
                width += ch.glyph.metrics.horizontalAdvance * scale;
        float pen = x - width * align;
        for (int i = 0; i < count; i++)
        {
            if (!font.characterLookupTable.TryGetValue(s[i], out TMP_Character ch) || ch.glyph == null)
                continue;
            Glyph g = ch.glyph;
            GlyphMetrics gm = g.metrics;
            GlyphRect r = g.glyphRect;
            if (r.width > 0 && r.height > 0)
            {
                float x0 = pen + (gm.horizontalBearingX - pad) * scale;
                float y1 = y + (gm.horizontalBearingY + pad) * scale;
                float x1 = x0 + (r.width + 2f * pad) * scale;
                float y0 = y1 - (r.height + 2f * pad) * scale;
                float u0 = (r.x - pad) / aw, v0 = (r.y - pad) / ah, u1 = (r.x + r.width + pad) / aw, v1 = (r.y + r.height + pad) / ah;
                int b = _pos.Count;
                float s0 = (y0 - y) * _unslant, s1 = (y1 - y) * _unslant;
                _pos.Add(new Vector3(x0 - s0, y0, 0f)); _uv.Add(new Vector2(u0, v0)); _aux.Add(new Vector2(scale, 0f));
                _pos.Add(new Vector3(x0 - s1, y1, 0f)); _uv.Add(new Vector2(u0, v1)); _aux.Add(new Vector2(scale, 0f));
                _pos.Add(new Vector3(x1 - s1, y1, 0f)); _uv.Add(new Vector2(u1, v1)); _aux.Add(new Vector2(scale, 0f));
                _pos.Add(new Vector3(x1 - s0, y0, 0f)); _uv.Add(new Vector2(u1, v0)); _aux.Add(new Vector2(scale, 0f));
                _tint.Add(_paint); _tint.Add(_paint); _tint.Add(_paint); _tint.Add(_paint);
                _tris.Add(b); _tris.Add(b + 1); _tris.Add(b + 2); _tris.Add(b); _tris.Add(b + 2); _tris.Add(b + 3);
            }
            pen += gm.horizontalAdvance * scale;
        }
    }

    // a solid bar (centre, half sizes in pixels) with room around it for its glow
    private static void Box(float cx, float cy, float hx, float hy, float glow)
    {
        float ex = hx + glow, ey = hy + glow;
        int b = _pos.Count;
        _pos.Add(new Vector3(cx - ex, cy - ey, 1f + glow)); _uv.Add(new Vector2(-ex, -ey)); _aux.Add(new Vector2(hx, hy));
        _pos.Add(new Vector3(cx - ex, cy + ey, 1f + glow)); _uv.Add(new Vector2(-ex, ey)); _aux.Add(new Vector2(hx, hy));
        _pos.Add(new Vector3(cx + ex, cy + ey, 1f + glow)); _uv.Add(new Vector2(ex, ey)); _aux.Add(new Vector2(hx, hy));
        _pos.Add(new Vector3(cx + ex, cy - ey, 1f + glow)); _uv.Add(new Vector2(ex, -ey)); _aux.Add(new Vector2(hx, hy));
        _tint.Add(_paint); _tint.Add(_paint); _tint.Add(_paint); _tint.Add(_paint);
        _tris.Add(b); _tris.Add(b + 1); _tris.Add(b + 2); _tris.Add(b); _tris.Add(b + 2); _tris.Add(b + 3);
    }

    private static int Mod(int a, int m) => ((a % m) + m) % m;

    private static void Put(string s, ref int n)
    {
        foreach (char c in s)
            if (n < _buf.Length)
                _buf[n++] = c;
    }

    // a number with at least so many digits (leading zeros), no allocation
    private static void PutInt(int v, int digits, ref int n)
    {
        if (v < 0) { _buf[n++] = '-'; v = -v; }
        int len = 1;
        for (int t = v; t >= 10; t /= 10) len++;
        len = Mathf.Max(len, digits);
        for (int k = len - 1; k >= 0; k--)
        {
            if (n + k < _buf.Length)
                _buf[n + k] = (char)('0' + v % 10);
            v /= 10;
        }
        n = Mathf.Min(n + len, _buf.Length);
    }
}
