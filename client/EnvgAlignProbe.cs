using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnvgFusion;

// TEMPORARY (a measurement, to be removed): how well the thermal picture's history is lined up with the present frame.
// About once a second, while the view moves, the thermal depth of two frames running is read back (asynchronously, no
// stall) with the camera as the history's following used it. Off the main thread, each depth edge of the present frame
// is followed to where that following puts it last frame, and last frame's depth is compared there, and at small
// offsets around it (up/down, left/right, in the thermal picture's own pixels). The offset with the least mismatch is
// how far the following is off; logged with the camera's turn between the frames and both frames' sub-pixel offsets.
internal static class EnvgAlignProbe
{
    private struct Frame
    {
        public Matrix4x4 CamToWorld, PrevWorldToCam;
        public Vector2 Tan, PrevTan, Jitter, PrevJitter;
        public Vector4 Crop, CropPrev;
        public int Width, Height, FrameCount;
        public float TurnDeg;
    }

    private static Frame _now;
    private static Vector2 _lastJitter;
    private static Quaternion _lastTurn;
    private static int _lastFrame = -10, _chainFrame = -10;
    private static float _nextAt;
    private static bool _pending;
    private static RenderTexture? _prevDepth;
    private static NativeArray<Vector2> _a, _b;
    private static bool _gotA, _gotB;
    private static Frame _measured;

    // in RenderThermal, with what the history's following uses this frame
    internal static void Note(Matrix4x4 camToWorld, Matrix4x4 prevWorldToCam, Vector2 tan, Vector2 prevTan, Vector2 jitter, Vector4 crop, Vector4 cropPrev, int width, int height, Quaternion turn)
    {
        _now = new Frame
        {
            CamToWorld = camToWorld, PrevWorldToCam = prevWorldToCam, Tan = tan, PrevTan = prevTan, Jitter = jitter, PrevJitter = _lastJitter,
            Crop = crop, CropPrev = cropPrev, Width = width, Height = height, FrameCount = Time.frameCount,
            TurnDeg = Quaternion.Angle(turn, _lastTurn),
        };
        _lastJitter = jitter;
        _lastTurn = turn;
    }

    // at the end of the chain's commands: the readback asked for (when due), then this frame's depth kept for the next
    internal static void Append(CommandBuffer cb, RenderTexture depth)
    {
        if (_prevDepth == null || _prevDepth.width != depth.width || _prevDepth.height != depth.height)
        {
            if (_prevDepth != null)
            {
                _prevDepth.Release();
                Object.Destroy(_prevDepth);
            }
            _prevDepth = new RenderTexture(depth.width, depth.height, 0, RenderTextureFormat.RGFloat) { name = "Justachae ENVG alignment probe, last depth", filterMode = FilterMode.Point };
            _prevDepth.Create();
            _chainFrame = -10;
        }
        bool consecutive = _chainFrame == Time.frameCount - 1;
        if (!_pending && consecutive && Time.unscaledTime >= _nextAt && _now.FrameCount == Time.frameCount && _now.TurnDeg > 0.01f
            && _now.Width == depth.width && _now.Height == depth.height)
        {
            _pending = true;
            _gotA = _gotB = false;
            _measured = _now;
            _nextAt = Time.unscaledTime + 1f;
            cb.RequestAsyncReadback(depth, r => Got(r, true));
            cb.RequestAsyncReadback(_prevDepth, r => Got(r, false));
        }
        cb.Blit(depth, _prevDepth);
        _chainFrame = Time.frameCount;
    }

    private static void Got(AsyncGPUReadbackRequest r, bool present)
    {
        if (r.hasError)
        {
            _pending = false;
            return;
        }
        NativeArray<Vector2> data = r.GetData<Vector2>();
        if (present)
        {
            if (_a.IsCreated) _a.Dispose();
            _a = new NativeArray<Vector2>(data, Allocator.Persistent);
            _gotA = true;
        }
        else
        {
            if (_b.IsCreated) _b.Dispose();
            _b = new NativeArray<Vector2>(data, Allocator.Persistent);
            _gotB = true;
        }
        if (_gotA && _gotB)
        {
            Vector2[] now = _a.ToArray(), then = _b.ToArray();
            Frame f = _measured;
            Task.Run(() => Measure(now, then, f)).ContinueWith(_ => _pending = false);
        }
    }

    private static float Sample(Vector2[] d, int w, int h, float x, float y)
    {
        x = Mathf.Clamp(x, 0f, w - 1.001f);
        y = Mathf.Clamp(y, 0f, h - 1.001f);
        int x0 = (int)x, y0 = (int)y;
        float fx = x - x0, fy = y - y0;
        float a = d[y0 * w + x0].x, b = d[y0 * w + x0 + 1].x, c = d[(y0 + 1) * w + x0].x, e = d[(y0 + 1) * w + x0 + 1].x;
        if (a <= 0f || b <= 0f || c <= 0f || e <= 0f)
            return -1f;
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, e, fx), fy);
    }

    private const int Steps = 17;   // offsets -2 .. +2 thermal pixels in quarters
    private static void Measure(Vector2[] now, Vector2[] then, Frame f)
    {
        try
        {
            int w = f.Width, h = f.Height, used = 0;
            var errY = new double[Steps];
            var errX = new double[Steps];
            for (int y = 2; y < h - 2; y += 2)
                for (int x = 2; x < w - 2; x += 2)
                {
                    float d = now[y * w + x].x;
                    if (d <= 0.3f || d > 150f)
                        continue;
                    var cuv = new Vector2((x + 0.5f) / w, (y + 0.5f) / h);
                    var su = new Vector2(f.Crop.x + cuv.x * f.Crop.z, f.Crop.y + cuv.y * f.Crop.w);
                    var view = new Vector3((su.x * 2f - 1f) * f.Tan.x, (su.y * 2f - 1f) * f.Tan.y, -1f) * d;
                    Vector3 world = f.CamToWorld.MultiplyPoint3x4(view);
                    Vector3 before = f.PrevWorldToCam.MultiplyPoint3x4(world);
                    if (before.z > -1e-4f)
                        continue;
                    var thenSu = new Vector2(before.x / -before.z / f.PrevTan.x, before.y / -before.z / f.PrevTan.y) * 0.5f + new Vector2(0.5f, 0.5f);
                    var p = new Vector2((thenSu.x - f.CropPrev.x) / f.CropPrev.z * w - 0.5f, (thenSu.y - f.CropPrev.y) / f.CropPrev.w * h - 0.5f);
                    if (p.x < 3f || p.y < 3f || p.x > w - 4f || p.y > h - 4f)
                        continue;
                    float predicted = -before.z;
                    // only where last frame's depth changes (an edge): elsewhere any offset matches
                    float gx = Sample(then, w, h, p.x + 1f, p.y) - Sample(then, w, h, p.x - 1f, p.y);
                    float gy = Sample(then, w, h, p.x, p.y + 1f) - Sample(then, w, h, p.x, p.y - 1f);
                    if (Mathf.Max(Mathf.Abs(gx), Mathf.Abs(gy)) < 0.05f * predicted)
                        continue;
                    bool ok = true;
                    var ey = new float[Steps];
                    var ex = new float[Steps];
                    for (int k = 0; k < Steps && ok; k++)
                    {
                        float o = (k - Steps / 2) * 0.25f;
                        float sy = Sample(then, w, h, p.x, p.y + o), sx = Sample(then, w, h, p.x + o, p.y);
                        if (sy <= 0f || sx <= 0f)
                            ok = false;
                        else
                        {
                            ey[k] = Mathf.Min(Mathf.Abs(Mathf.Log(sy / predicted)), 0.5f);
                            ex[k] = Mathf.Min(Mathf.Abs(Mathf.Log(sx / predicted)), 0.5f);
                        }
                    }
                    if (!ok)
                        continue;
                    for (int k = 0; k < Steps; k++)
                    {
                        errY[k] += ey[k];
                        errX[k] += ex[k];
                    }
                    used++;
                }
            if (used < 50)
            {
                EnvgFusionPlugin.Log?.LogInfo("ENVG alignment probe (temporary): too few edges (" + used + ")");
                return;
            }
            int by = 0, bx = 0;
            for (int k = 1; k < Steps; k++)
            {
                if (errY[k] < errY[by]) by = k;
                if (errX[k] < errX[bx]) bx = k;
            }
            int mid = Steps / 2;
            EnvgFusionPlugin.Log?.LogInfo("ENVG alignment probe (temporary): turn " + f.TurnDeg.ToString("F3") + " deg; history off by up "
                + ((by - mid) * 0.25f).ToString("+0.00;-0.00") + " px, right " + ((bx - mid) * 0.25f).ToString("+0.00;-0.00")
                + " px (thermal pixels, " + w + "x" + h + "); mismatch at 0 " + (errY[mid] / used).ToString("F4") + ", at best " + (errY[by] / used).ToString("F4") + "/" + (errX[bx] / used).ToString("F4")
                + "; " + used + " edge samples; jitter now " + f.Jitter.x.ToString("F5") + "," + f.Jitter.y.ToString("F5") + " before " + f.PrevJitter.x.ToString("F5") + "," + f.PrevJitter.y.ToString("F5")
                + "; crop " + f.Crop.ToString("F3") + " before " + f.CropPrev.ToString("F3"));
        }
        catch (System.Exception e)
        {
            EnvgFusionPlugin.Log?.LogWarning("ENVG alignment probe failed: " + e.Message);
        }
    }
}
