using System.Collections.Generic;
using UnityEngine;

namespace EnvgFusion;

// The ENVG-B's two eyepiece pods swing up on their own hinges (as the real one's do), each by its key. The model has a
// node per pod (envgb_pod_L / envgb_pod_R under the device, envgb_eyecup_pod_L / _R in the eyecup): identity is down,
// the swing is about the hinge's own axis, 90 degrees outward and up (the item's joints.json, "pods"). We turn those
// nodes; K-3PS-C reads them for the eyepieces. A pod's tube goes off once it leaves its place, as on real articulating
// goggles: the right pod carries the thermal module, so the thermal picture and the symbology need it down.
internal static class EnvgPods
{
    private const float Swing = 90f;       // degrees, up and out (farther, the eyecups reach the helmet's brim)
    private const float Travel = 0.4f;     // seconds for a pod to swing all the way
    private const float Down = 0.02f;      // still in its place below this share of the swing

    // the pods' targets (true: up) and where they are (0 down, 1 up), for the device worn now; they stay through the
    // whole device's own flip, and start down on another device
    private static bool _wantLeft, _wantRight;
    private static float _left, _right;
    private static object? _device;

    // the nodes, looked for only when something changes: another device, something else in its eyecup slot, or a node
    // gone (the model made anew); a few frames running after such a change, as the game builds the model a frame or
    // two later. Never on a timer.
    private static Transform? _podL, _podR, _cupL, _cupR;
    private static EFT.InventoryLogic.Slot? _cupSlot;
    private static object? _cupItem;
    private static int _lookFrames;
    private static bool _logged;
    private const int LookFor = 30;   // (kept for the count below: a change starts the looking)
    // after a change, looked for a while longer, a few times a second: an eyecup fitted or taken off makes the goggle's
    // model anew, and that model can take longer than half a second to come (the objective's marker was lost then, the
    // thermal camera left 4 cm above the eye)
    private const float LookSeconds = 10f, LookEvery = 0.25f;
    private static float _lookUntil, _nextLook;

    internal static bool RightDown => _right < Down;
    // the thermal picture's share as the right pod leaves its place: full while down, faded out over the first third
    // of the swing (not gone at once), back in the same way
    private const float FadeBy = 0.35f;
    internal static float RightShown => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, FadeBy, _right));
    internal static bool LeftDown => _left < Down;

    internal static void Toggle(bool right)
    {
        if (right)
            _wantRight = !_wantRight;
        else
            _wantLeft = !_wantLeft;
    }

    // once a frame: the swing, and the nodes turned to it
    internal static void Update(EFT.Player? wearer, EFT.InventoryLogic.Item? device)
    {
        SyncCups();
        if (!ReferenceEquals(device, _device))
        {
            _device = device;
            _wantLeft = _wantRight = false;
            _left = _right = 0f;
            _podL = _podR = _cupL = _cupR = null;
            _cupSlot = null;
            _cupItem = null;
            if (device is EFT.InventoryLogic.CompoundItem compound && compound.Slots != null)
                foreach (EFT.InventoryLogic.Slot slot in compound.Slots)
                    if (slot != null && slot.ID == "mod_eyecup")
                        _cupSlot = slot;
            _lookFrames = LookFor;
        }
        if (device == null || wearer == null)
            return;
        float step = Time.deltaTime / Travel;
        _left = Mathf.MoveTowards(_left, _wantLeft ? 1f : 0f, step);
        _right = Mathf.MoveTowards(_right, _wantRight ? 1f : 0f, step);

        // an eyecup fitted or taken off, or a node gone: look again (for a few frames)
        object? cup = _cupSlot?.ContainedItem;
        if (!ReferenceEquals(cup, _cupItem))
        {
            // (the goggle's model is made anew: every node looked for again, the objective's marker too; kept, the old
            // model's marker stood where the game laid the old model aside, and the thermal camera with it: the user's,
            // the eyecups taken off)
            _cupItem = cup;
            _cupL = _cupR = null;
            _podL = _podR = null;
            _lens = null;
            _lookFrames = LookFor;
        }
        if (Gone(_podL) || Gone(_podR) || Gone(_lens))
        {
            _podL = _podR = null;
            _lens = null;
            _lookFrames = LookFor;
        }
        bool missing = _podL == null || _podR == null || _lens == null || (cup != null && (_cupL == null || _cupR == null));
        if (_lookFrames > 0)
        {
            _lookUntil = Time.unscaledTime + LookSeconds;
            _nextLook = 0f;
            _lookFrames = 0;
        }
        if (missing && Time.unscaledTime < _lookUntil && Time.unscaledTime >= _nextLook)
        {
            _nextLook = Time.unscaledTime + LookEvery;
            Look(((Component)wearer).transform);
        }
        float l = Mathf.SmoothStep(0f, 1f, _left) * Swing, r = Mathf.SmoothStep(0f, 1f, _right) * Swing;
        Quaternion left = Quaternion.AngleAxis(l, Vector3.up), right = Quaternion.AngleAxis(r, Vector3.down);
        if (_podL != null) _podL.localRotation = left;
        if (_podR != null) _podR.localRotation = right;
        if (_cupL != null) _cupL.localRotation = left;
        if (_cupR != null) _cupR.localRotation = right;
        CupsAsDevice();
    }

    // (the lists for SyncCups: made when the nodes are known)
    // Without K-3PS-C's eyepieces (not installed, switched off, or not showing the device) the game hides the goggle on its own wearer's head in first person, and the eyecups
    // fitted to it stood in the picture (the user's: "眼罩本来就是夜视仪的子分支"): each eyecup renderer is given, every
    // frame, what the game gives the device's own (shown or not, its shadows, its layer). With the eyepieces, they show
    // the device and its cups themselves, and nothing is done here.
    private static readonly List<Renderer> _cupRenderers = new List<Renderer>();
    private static Renderer? _deviceRenderer;
    private static Transform? _cupsOf;
    private static bool _loggedCups;
    private static int _cupSyncLogs;
    private static void CupsAsDevice()
    {
        if (_podL == null || _cupL == null)
            return;
        if (_cupsOf != _cupL || _deviceRenderer == null)
        {
            _cupsOf = _cupL;
            _cupRenderers.Clear();
            _cupL.GetComponentsInChildren(true, _cupRenderers);
            int left = _cupRenderers.Count;
            if (_cupR != null)
            {
                var right = new List<Renderer>();
                _cupR.GetComponentsInChildren(true, right);
                _cupRenderers.AddRange(right);
            }
            _deviceRenderer = _podL.GetComponentInChildren<Renderer>(true);
            if (!_loggedCups && _deviceRenderer != null && _cupRenderers.Count > 0)
            {
                _loggedCups = true;
                Renderer c = _cupRenderers[0];
                EnvgFusionPlugin.Log?.LogInfo("ENVG-B eyecups without K-3PS-C: " + _cupRenderers.Count + " renderer(s) given the device's state (device: shown "
                    + _deviceRenderer.enabled + ", shadows " + _deviceRenderer.shadowCastingMode + ", layer " + _deviceRenderer.gameObject.layer + ", off " + _deviceRenderer.forceRenderingOff
                    + "; eyecup had: shown " + c.enabled + ", shadows " + c.shadowCastingMode + ", layer " + c.gameObject.layer + ", off " + c.forceRenderingOff + ")");
            }
        }
    }

    // The eyecups' renderers kept as the device's own, every frame, whoever wears it or looks: also where no wearer or
    // worn device is known (the hideout's overview before the range: the game hid the goggle there, its night vision
    // still on, and the cups, left as they were, hung in the picture: the user's, 0.73.41)
    private static void SyncCups()
    {
        if (EyepieceLink.ClaimedRecently || _cupRenderers.Count == 0)
            return;
        Renderer? d = _deviceRenderer;
        if (d == null)
        {
            _cupRenderers.Clear();
            _cupsOf = null;
            return;
        }
        foreach (Renderer c in _cupRenderers)
        {
            if (c == null)
                continue;
            if (c.enabled != d.enabled || c.shadowCastingMode != d.shadowCastingMode || c.forceRenderingOff != d.forceRenderingOff)
            {
                if (_cupSyncLogs < 4)
                {
                    _cupSyncLogs++;
                    EnvgFusionPlugin.Log?.LogInfo("ENVG-B eyecups follow the device: now shown " + d.enabled + ", shadows " + d.shadowCastingMode + ", off " + d.forceRenderingOff + " (were shown " + c.enabled + ", shadows " + c.shadowCastingMode + ", off " + c.forceRenderingOff + ")");
                }
            }
            if (c.enabled != d.enabled) c.enabled = d.enabled;
            if (c.shadowCastingMode != d.shadowCastingMode) c.shadowCastingMode = d.shadowCastingMode;
            if (c.forceRenderingOff != d.forceRenderingOff) c.forceRenderingOff = d.forceRenderingOff;
            if (c.gameObject.layer != d.gameObject.layer) c.gameObject.layer = d.gameObject.layer;
        }
    }

    // the thermal objective's marker on the worn device (the lens camera stands there)
    private static Transform? _lens;
    internal static Transform? Lens => _lens;

    // a node destroyed, or on a model the game has put aside (not shown: a model made anew leaves the old one inactive)
    private static bool Gone(Transform? t) => t is not null && (t == null || !t.gameObject.activeInHierarchy);

    // the device's and the eyecup's pod nodes on the wearer (on the shown model only)
    private static readonly List<Transform> _found = new List<Transform>();
    private static void Look(Transform wearer)
    {
        wearer.GetComponentsInChildren(true, _found);
        foreach (Transform t in _found)
        {
            if (!t.gameObject.activeInHierarchy)
                continue;
            switch (t.name)
            {
                case "envgb_pod_L": _podL = t; break;
                case "envgb_thermal_lens": _lens = t; break;
                case "envgb_pod_R": _podR = t; break;
                case "envgb_eyecup_pod_L": _cupL = t; break;
                case "envgb_eyecup_pod_R": _cupR = t; break;
            }
        }
        _found.Clear();
        if (!_logged && (_podL != null || _podR != null))
        {
            _logged = true;
            EnvgFusionPlugin.Log?.LogInfo("ENVG-B pods: device " + (_podL != null && _podR != null ? "both" : "one") + " found, eyecup "
                + (_cupL != null && _cupR != null ? "both" : _cupL != null || _cupR != null ? "one" : "none (not fitted)") + "; Alt+left / Alt+right swing them up");
        }
    }
}
