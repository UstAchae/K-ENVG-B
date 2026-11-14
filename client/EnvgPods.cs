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
    private const int LookFor = 30;   // frames to keep looking after a change

    internal static bool RightDown => _right < Down;
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
            _cupItem = cup;
            _cupL = _cupR = null;
            _lookFrames = LookFor;
        }
        if ((_podL is not null && _podL == null) || (_podR is not null && _podR == null))
        {
            _podL = _podR = null;
            _lookFrames = LookFor;
        }
        bool missing = _podL == null || _podR == null || (cup != null && (_cupL == null || _cupR == null));
        if (missing && _lookFrames > 0)
        {
            _lookFrames--;
            Look(((Component)wearer).transform);
        }
        float l = Mathf.SmoothStep(0f, 1f, _left) * Swing, r = Mathf.SmoothStep(0f, 1f, _right) * Swing;
        Quaternion left = Quaternion.AngleAxis(l, Vector3.up), right = Quaternion.AngleAxis(r, Vector3.down);
        if (_podL != null) _podL.localRotation = left;
        if (_podR != null) _podR.localRotation = right;
        if (_cupL != null) _cupL.localRotation = left;
        if (_cupR != null) _cupR.localRotation = right;
    }

    // the device's and the eyecup's pod nodes on the wearer
    private static readonly List<Transform> _found = new List<Transform>();
    private static void Look(Transform wearer)
    {
        wearer.GetComponentsInChildren(true, _found);
        foreach (Transform t in _found)
        {
            switch (t.name)
            {
                case "envgb_pod_L": _podL = t; break;
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
