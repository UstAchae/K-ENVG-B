using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnvgFusion;

// TEMPORARY (a measurement, to be removed). About once a second, two frames running of each step of the thermal chain
// are read back (asynchronously, no stall), with the camera as the history's following used it: the depth, the copied
// picture (the input), the heat, the heat followed in time, the edge response, the final lines. Off the main thread,
// each pixel of the present frame is followed to where it was last frame (the same following as the chain's), and the
// step's value there is compared with its value now. A still scene, lined up right, changes nothing: what a step changes
// from frame to frame is its own flicker. Logged per step; so is how far the following is off (by depth edges).
internal static class EnvgAlignProbe
{
    private struct Frame
    {
        public Matrix4x4 CamToWorld, PrevWorldToCam;
        public Vector2 Tan, PrevTan, Jitter, PrevJitter;
        public Vector4 Crop, CropPrev;
        public int Width, Height, FrameCount;
        public float TurnDeg, MoveMm;
    }

    private static readonly string[] Names = { "depth", "heat", "heat followed", "albedo soft", "normals soft", "grass mask", "frames held", "edge response", "lines" };
    private const int Stages = 9;
    private const int CountStage = 6;
    private static Frame _now, _measured;
    private static Vector2 _lastJitter;
    private static Quaternion _lastTurn;
    private static Vector3 _lastPos;
    private static int _chainFrame = -10;
    private static float _nextAt;
    private static bool _pending;
    private static readonly RenderTexture?[] _prev = new RenderTexture?[Stages];
    private static readonly float[]?[] _nowData = new float[Stages][], _thenData = new float[Stages][];
    private static int _got;

    internal static void Note(Matrix4x4 camToWorld, Matrix4x4 prevWorldToCam, Vector2 tan, Vector2 prevTan, Vector2 jitter, Vector4 crop, Vector4 cropPrev, int width, int height, Quaternion turn, Vector3 position)
    {
        _now = new Frame
        {
            CamToWorld = camToWorld, PrevWorldToCam = prevWorldToCam, Tan = tan, PrevTan = prevTan, Jitter = jitter, PrevJitter = _lastJitter,
            Crop = crop, CropPrev = cropPrev, Width = width, Height = height, FrameCount = Time.frameCount,
            TurnDeg = Quaternion.Angle(turn, _lastTurn), MoveMm = (position - _lastPos).magnitude * 1000f,
        };
        _lastJitter = jitter;
        _lastTurn = turn;
        _lastPos = position;
    }

    // at the end of the chain's commands: the readbacks asked for (when due), then this frame's steps kept for the next
    internal static void Append(CommandBuffer cb, RenderTexture?[] steps)
    {
        RenderTexture? depth = steps[0];
        if (depth == null)
            return;
        for (int s = 0; s < Stages; s++)
        {
            RenderTexture? src = steps[s];
            if (src == null)
                return;
            if (_prev[s] == null || _prev[s]!.width != src.width || _prev[s]!.height != src.height)
            {
                if (_prev[s] != null)
                {
                    _prev[s]!.Release();
                    Object.Destroy(_prev[s]);
                }
                _prev[s] = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.RFloat) { name = "Justachae ENVG probe, last " + Names[s], filterMode = FilterMode.Point };
                _prev[s]!.Create();
                _chainFrame = -10;
            }
        }
        bool consecutive = _chainFrame == Time.frameCount - 1;
        if (!_pending && consecutive && Time.unscaledTime >= _nextAt && _now.FrameCount == Time.frameCount && _now.Width == depth.width && _now.Height == depth.height)
        {
            bool sameSize = true;
            for (int s = 0; s < Stages; s++)
                sameSize &= steps[s]!.width == depth.width && steps[s]!.height == depth.height;
            if (sameSize)
            {
                _pending = true;
                _got = 0;
                _measured = _now;
                _nextAt = Time.unscaledTime + 1f;
                for (int s = 0; s < Stages; s++)
                {
                    int stage = s;
                    cb.RequestAsyncReadback(steps[s]!, 0, TextureFormat.RFloat, r => Got(r, stage, true));
                    cb.RequestAsyncReadback(_prev[s]!, 0, TextureFormat.RFloat, r => Got(r, stage, false));
                }
            }
        }
        for (int s = 0; s < Stages; s++)
            cb.Blit(steps[s]!, _prev[s]!);
        _chainFrame = Time.frameCount;
    }

    private static void Got(AsyncGPUReadbackRequest r, int stage, bool present)
    {
        if (r.hasError)
        {
            _pending = false;
            return;
        }
        float[] data = r.GetData<float>().ToArray();
        if (present) _nowData[stage] = data; else _thenData[stage] = data;
        if (++_got == Stages * 2)
        {
            var now = (float[][])_nowData.Clone();
            var then = (float[][])_thenData.Clone();
            Frame f = _measured;
            Task.Run(() => Measure(now, then, f)).ContinueWith(_ => _pending = false);
        }
    }

    private static float Sample(float[] d, int w, int h, float x, float y)
    {
        x = Mathf.Clamp(x, 0f, w - 1.001f);
        y = Mathf.Clamp(y, 0f, h - 1.001f);
        int x0 = (int)x, y0 = (int)y;
        float fx = x - x0, fy = y - y0;
        return Mathf.Lerp(Mathf.Lerp(d[y0 * w + x0], d[y0 * w + x0 + 1], fx), Mathf.Lerp(d[(y0 + 1) * w + x0], d[(y0 + 1) * w + x0 + 1], fx), fy);
    }

    private const int Offsets = 9;   // the following's error checked at -1 .. +1 thermal pixels in quarters
    private static void Measure(float[][] now, float[][] then, Frame f)
    {
        try
        {
            int w = f.Width, h = f.Height, used = 0, edgeUsed = 0;
            var change = new double[Stages];
            var level = new double[Stages];
            var errY = new double[Offsets];
            var errX = new double[Offsets];
            float[] dNow = now[0], dThen = then[0];
            for (int y = 3; y < h - 3; y += 2)
                for (int x = 3; x < w - 3; x += 2)
                {
                    float d = dNow[y * w + x];
                    if (d <= 0.3f || d > 150f)
                        continue;
                    var su = new Vector2(f.Crop.x + (x + 0.5f) / w * f.Crop.z, f.Crop.y + (y + 0.5f) / h * f.Crop.w);
                    var view = new Vector3((su.x * 2f - 1f) * f.Tan.x, (su.y * 2f - 1f) * f.Tan.y, -1f) * d;
                    Vector3 before = f.PrevWorldToCam.MultiplyPoint3x4(f.CamToWorld.MultiplyPoint3x4(view));
                    if (before.z > -1e-4f)
                        continue;
                    var thenSu = new Vector2(before.x / -before.z / f.PrevTan.x, before.y / -before.z / f.PrevTan.y) * 0.5f + new Vector2(0.5f, 0.5f);
                    var p = new Vector2((thenSu.x - f.CropPrev.x) / f.CropPrev.z * w - 0.5f, (thenSu.y - f.CropPrev.y) / f.CropPrev.w * h - 0.5f);
                    if (p.x < 3f || p.y < 3f || p.x > w - 4f || p.y > h - 4f)
                        continue;
                    // (only where the depth here and there agree: the same surface, not something that came into view)
                    float predicted = -before.z, there = Sample(dThen, w, h, p.x, p.y);
                    if (there <= 0f || Mathf.Abs(Mathf.Log(there / predicted)) > 0.05f)
                        continue;
                    used++;
                    for (int s = 1; s < Stages; s++)
                    {
                        float a = now[s][y * w + x];
                        if (s == CountStage)
                        {
                            change[s] += a;               // the mean of the frames held
                            level[s] += a >= 31.5f ? 1 : 0;   // the share settled all the way
                            continue;
                        }
                        float b = Sample(then[s], w, h, p.x, p.y);
                        change[s] += Mathf.Abs(a - b);
                        level[s] += 0.5f * (Mathf.Abs(a) + Mathf.Abs(b));
                    }
                    // the following's own error, at depth edges
                    float gx = Sample(dThen, w, h, p.x + 1f, p.y) - Sample(dThen, w, h, p.x - 1f, p.y);
                    float gy = Sample(dThen, w, h, p.x, p.y + 1f) - Sample(dThen, w, h, p.x, p.y - 1f);
                    if (Mathf.Max(Mathf.Abs(gx), Mathf.Abs(gy)) < 0.05f * predicted)
                        continue;
                    edgeUsed++;
                    for (int k = 0; k < Offsets; k++)
                    {
                        float o = (k - Offsets / 2) * 0.25f;
                        errY[k] += Mathf.Min(Mathf.Abs(Mathf.Log(Mathf.Max(Sample(dThen, w, h, p.x, p.y + o), 1e-3f) / predicted)), 0.5f);
                        errX[k] += Mathf.Min(Mathf.Abs(Mathf.Log(Mathf.Max(Sample(dThen, w, h, p.x + o, p.y), 1e-3f) / predicted)), 0.5f);
                    }
                }
            if (used < 200)
            {
                EnvgFusionPlugin.Log?.LogInfo("ENVG chain probe (temporary): too few samples (" + used + ")");
                return;
            }
            int by = 0, bx = 0;
            for (int k = 1; k < Offsets; k++)
            {
                if (errY[k] < errY[by]) by = k;
                if (errX[k] < errX[bx]) bx = k;
            }
            var said = new System.Text.StringBuilder("ENVG chain probe (temporary): turn " + f.TurnDeg.ToString("F3") + " deg, move " + f.MoveMm.ToString("F1")
                + " mm; frame-to-frame change where the scene stays (share of the level):");
            for (int s = 1; s < Stages; s++)
            {
                if (s == CountStage)
                {
                    said.Append(" | frames held: mean " + (change[s] / used).ToString("F1") + ", all 32 at " + (level[s] / used * 100.0).ToString("F0") + "% |");
                    continue;
                }
                said.Append(" " + Names[s] + " " + (change[s] / System.Math.Max(level[s], 1e-9) * 100.0).ToString("F1") + "%");
            }
            said.Append("; following off by up " + ((by - Offsets / 2) * 0.25f).ToString("+0.00;-0.00") + " right " + ((bx - Offsets / 2) * 0.25f).ToString("+0.00;-0.00")
                + " px (" + edgeUsed + " edges); " + used + " samples of " + w + "x" + h + "; jitter " + f.Jitter.x.ToString("F5") + "," + f.Jitter.y.ToString("F5")
                + " before " + f.PrevJitter.x.ToString("F5") + "," + f.PrevJitter.y.ToString("F5"));
            EnvgFusionPlugin.Log?.LogInfo(said.ToString());
        }
        catch (System.Exception e)
        {
            EnvgFusionPlugin.Log?.LogWarning("ENVG chain probe failed: " + e.Message);
        }
    }
}
