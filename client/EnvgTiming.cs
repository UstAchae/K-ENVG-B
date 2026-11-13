using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace EnvgFusion;

// Temporary (to find where the thermal channel's ~13 ms go; remove once it is fast). Once a session, after the picture
// has been shown for 3 s, while the device stays down:
//  - 5 s of the frame as it is (the baseline);
//  - then each piece of the thermal work in turn, 1.5 s each, run Extra more times every frame. Every piece writes the
//    same targets from the same inputs each time, so the picture does not change; the frame grows by Extra times the
//    piece's own GPU time, so (its frame - the baseline) / Extra is what the piece costs;
//  - the baseline again at the end, to see the drift.
// Besides: the GPU recorder for each piece where this build of the game supports it, our CPU time in each hook, the
// memory we allocate, every texture we keep (size, format) and how many we created during the window. One summary in
// the log; nothing every frame.
internal static class EnvgTiming
{
    internal const int Extra = 5;
    private const float Warm = 3f, BaseTime = 5f, ItemTime = 1.5f, Settle = 0.3f, EndBase = 3f;

    private enum Phase { Waiting, Warm, Base, Items, EndBase, Done }
    private static Phase _phase = Phase.Waiting;
    private static float _phaseStart;
    private static int _shownFrame = -1;
    internal static void Shown() => _shownFrame = Time.frameCount;

    // the pieces: the chain's groups (as EnvgChain names them), then those outside it
    internal const string FuseItem = "eyepiece draw (Fuse)", CopyItem = "scene copy + bodies (camera)", MotionItem = "motion copy (camera)", GrassItem = "grass mask (camera)";
    private static readonly List<string> _items = new List<string>();
    private static int _item = -1;
    internal static string? Current => _phase == Phase.Items && _item >= 0 && _item < _items.Count && Time.unscaledTime - _phaseStart >= 0f ? _items[_item] : null;

    // frame times
    private struct Acc { public double Sum; public double Max; public int N; public void Add(double v) { Sum += v; N++; if (v > Max) Max = v; } public double Avg => N > 0 ? Sum / N : 0; }
    private static Acc _base, _endBase;
    private static readonly Dictionary<string, Acc> _itemFrames = new Dictionary<string, Acc>();
    private static readonly Dictionary<string, Acc> _gpu = new Dictionary<string, Acc>();
    private static readonly Dictionary<string, Acc> _cpu = new Dictionary<string, Acc>();
    private static readonly Dictionary<string, Acc> _alloc = new Dictionary<string, Acc>();
    internal static int PluginCreated;   // textures created by the plugin's Ensure (the chain counts its own)
    private static int Created => PluginCreated + EnvgChain.Created;
    private static int _createdAtStart;
    private static string _mode = "";

    internal static bool Measuring => _phase == Phase.Base || _phase == Phase.Items || _phase == Phase.EndBase;
    internal static bool Splitting => _phase != Phase.Waiting && _phase != Phase.Done;

    // once a frame, from the plugin's Update
    internal static void Tick()
    {
        if (_phase == Phase.Done)
            return;
        float now = Time.unscaledTime;
        bool shown = _shownFrame >= Time.frameCount - 1;
        if (_phase == Phase.Waiting)
        {
            if (shown) { _phase = Phase.Warm; _phaseStart = now; }
            return;
        }
        if (!shown)
        {
            // the device went up (or the picture stopped): start over next time it shows
            Restore();
            _phase = Phase.Waiting;
            return;
        }
        double dt = Time.unscaledDeltaTime * 1000.0;
        switch (_phase)
        {
            case Phase.Warm:
                if (now - _phaseStart >= Warm) { _phase = Phase.Base; _phaseStart = now; _base = default; _createdAtStart = Created; _mode = EnvgFusionPlugin.Show?.Value ?? ""; }
                break;
            case Phase.Base:
                _base.Add(dt);
                SampleRecorders();
                if (now - _phaseStart >= BaseTime) StartItems(now);
                break;
            case Phase.Items:
                if (now - _phaseStart >= Settle)
                {
                    string name = _items[_item];
                    Acc a = _itemFrames.TryGetValue(name, out Acc x) ? x : default;
                    a.Add(dt);
                    _itemFrames[name] = a;
                }
                if (now - _phaseStart >= ItemTime + Settle) NextItem(now);
                break;
            case Phase.EndBase:
                _endBase.Add(dt);
                if (now - _phaseStart >= EndBase) Finish();
                break;
        }
    }

    private static void StartItems(float now)
    {
        _items.Clear();
        _items.AddRange(EnvgChain.GroupNames);
        if (EyepieceLink.ClaimedNow) _items.Add(FuseItem);
        _items.Add(CopyItem);
        _items.Add(MotionItem);
        if (EnvgFusion.GrassMaskWanted) _items.Add(GrassItem);
        _item = -1;
        _phase = Phase.Items;
        NextItem(now);
    }

    private static void NextItem(float now)
    {
        Restore();
        _item++;
        _phaseStart = now;
        if (_item >= _items.Count)
        {
            _phase = Phase.EndBase;
            _endBase = default;
            return;
        }
        string name = _items[_item];
        if (name == CopyItem || name == MotionItem || name == GrassItem)
            EnvgFusion.RepeatCameraBuffer(name, Extra);
    }

    private static void Restore()
    {
        if (_phase == Phase.Items && _item >= 0 && _item < _items.Count)
        {
            string name = _items[_item];
            if (name == CopyItem || name == MotionItem || name == GrassItem)
                EnvgFusion.RepeatCameraBuffer(name, 0);
        }
    }

    // ---- the GPU recorder (each group is bracketed by a sample of its name) ----
    private static readonly Dictionary<string, Recorder> _recorders = new Dictionary<string, Recorder>();
    internal static void Bracket(CommandBuffer cb, string name, bool begin)
    {
        if (begin) cb.BeginSample(name); else cb.EndSample(name);
        if (!_recorders.ContainsKey(name))
        {
            Recorder r = Recorder.Get(name);
            r.enabled = true;
            _recorders[name] = r;
        }
    }
    private static void SampleRecorders()
    {
        foreach (KeyValuePair<string, Recorder> pair in _recorders)
        {
            if (!pair.Value.isValid)
                continue;
            Acc a = _gpu.TryGetValue(pair.Key, out Acc x) ? x : default;
            a.Add(pair.Value.gpuElapsedNanoseconds / 1e6);
            _gpu[pair.Key] = a;
        }
    }

    // ---- our CPU time and allocations per hook ----
    internal static long Begin(out long bytes)
    {
        bytes = Measuring ? Allocated() : 0;
        return Measuring ? Stopwatch.GetTimestamp() : 0;
    }
    internal static void End(string hook, long start, long bytes)
    {
        if (!Measuring || start == 0)
            return;
        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        Acc a = _cpu.TryGetValue(hook, out Acc x) ? x : default;
        a.Add(ms);
        _cpu[hook] = a;
        Acc b = _alloc.TryGetValue(hook, out Acc y) ? y : default;
        b.Add(Allocated() - bytes);
        _alloc[hook] = b;
    }

    private static bool _noAllocCount;
    private static long Allocated()
    {
        if (_noAllocCount)
            return 0;
        try { return System.GC.GetAllocatedBytesForCurrentThread(); }
        catch (System.Exception) { _noAllocCount = true; return 0; }
    }

    private static void Finish()
    {
        _phase = Phase.Done;
        var sb = new StringBuilder();
        double b = _base.Avg, e = _endBase.Avg;
        sb.Append("ENVG timing: mode ").Append(_mode).Append(", eyepieces ").Append(EyepieceLink.ClaimedNow ? "claimed" : "not claimed")
          .Append(", screen ").Append(Screen.width).Append('x').Append(Screen.height)
          .Append(", baseline frame ").Append(b.ToString("F2")).Append(" ms avg (max ").Append(_base.Max.ToString("F1")).Append(", ").Append((1000.0 / Mathf.Max((float)b, 0.01f)).ToString("F0")).Append(" FPS), at the end ")
          .Append(e.ToString("F2")).Append(" ms; GPU recorder supported ").Append(SystemInfo.supportsGpuRecorder);
        sb.Append("\nENVG timing: each piece run ").Append(Extra).Append(" more times a frame: frame avg -> cost of one run = (frame - baseline) / ").Append(Extra);
        double sum = 0;
        foreach (string name in _items)
        {
            if (!_itemFrames.TryGetValue(name, out Acc a))
                continue;
            double cost = (a.Avg - (b + e) * 0.5) / Extra;
            sum += System.Math.Max(cost, 0);
            sb.Append("\nENVG timing:   ").Append(name).Append(": frame ").Append(a.Avg.ToString("F2")).Append(" ms (").Append(a.N).Append(" frames) -> ")
              .Append(cost.ToString("F2")).Append(" ms");
            if (_gpu.TryGetValue(name, out Acc g) && g.N > 0)
                sb.Append(", recorder ").Append(g.Avg.ToString("F2")).Append(" ms");
        }
        sb.Append("\nENVG timing:   sum of the pieces ").Append(sum.ToString("F2")).Append(" ms");
        foreach (KeyValuePair<string, Acc> pair in _cpu)
            sb.Append("\nENVG timing: CPU ").Append(pair.Key).Append(' ').Append(pair.Value.Avg.ToString("F3")).Append(" ms avg, max ").Append(pair.Value.Max.ToString("F2"))
              .Append(", allocated ").Append(_alloc.TryGetValue(pair.Key, out Acc al) ? al.Avg.ToString("F0") + " B avg, max " + al.Max.ToString("F0") : "-");
        sb.Append("\nENVG timing: textures created during the window ").Append(Created - _createdAtStart);
        foreach (string line in EnvgFusion.TextureList())
            sb.Append("\nENVG timing:   ").Append(line);
        EnvgFusionPlugin.Log?.LogInfo(sb.ToString());
    }
}
