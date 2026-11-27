using System;
using System.Reflection;
using UnityEngine;

namespace EnvgFusion;

// K-3PS-C's eyepieces (EyepieceFusion v1), found by name so neither mod needs the other: when they show the ENVG-B,
// they claim the frame and call our Fuse with the tube's picture; we then draw nothing over the screen and leave the
// tube's colour to them. Read once a frame through getters bound at load, so nothing allocates.
internal static class EyepieceLink
{
    private const string TypeName = "EFTNewCamera.Interop.EyepieceFusion";
    private const string AssemblyName = "JustachaeThirdPersonCamera";
    private static Func<int>? _claimedFrame;
    private static Func<Vector4>? _field;
    private static Func<float>? _deployed;
    // optional (not in every v1): the tubes' gain the eyepieces apply to both tubes (1 full, 0 dark)
    private static FieldInfo? _i2Gain;
    internal static bool TakesGain => _i2Gain != null;
    private static float _sentGain = -1f;
    internal static void SetGain(float gain)
    {
        if (_i2Gain == null || Mathf.Approximately(gain, _sentGain))
            return;
        _sentGain = gain;
        try { _i2Gain.SetValue(null, gain); } catch (Exception) { _i2Gain = null; }
    }
    private static bool _loggedClaim;

    internal static void Bind()
    {
        try
        {
            Type? type = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == AssemblyName)
                {
                    type = assembly.GetType(TypeName, false);
                    BindPods(assembly);
                    break;
                }
            if (type == null)
            {
                EnvgFusionPlugin.Log?.LogInfo("ENVG: no K-3PS-C eyepieces (EyepieceFusion), the picture goes over the screen");
                return;
            }
            FieldInfo? version = type.GetField("Version", BindingFlags.Public | BindingFlags.Static);
            int v = version != null ? (int)version.GetValue(null) : 0;
            if (v != 1)
            {
                EnvgFusionPlugin.Log?.LogWarning("ENVG: K-3PS-C's EyepieceFusion is version " + v + ", this mod knows 1: the picture goes over the screen");
                return;
            }
            _claimedFrame = Getter<int>(type, "ClaimedFrame");
            _field = Getter<Vector4>(type, "FieldInMainUv");
            _deployed = Getter<float>(type, "Deployed");
            FieldInfo? fuse = type.GetField("Fuse", BindingFlags.Public | BindingFlags.Static);
            if (_claimedFrame == null || _field == null || _deployed == null || fuse == null)
            {
                _claimedFrame = null;
                EnvgFusionPlugin.Log?.LogWarning("ENVG: K-3PS-C's EyepieceFusion is missing a member: the picture goes over the screen");
                return;
            }
            fuse.SetValue(null, new Func<RenderTexture, RenderTexture, bool>(EnvgFusion.FuseInto));
            FieldInfo? gain = type.GetField("I2Gain", BindingFlags.Public | BindingFlags.Static);
            _i2Gain = gain != null && gain.FieldType == typeof(float) ? gain : null;
            EnvgFusionPlugin.Log?.LogInfo("ENVG: K-3PS-C eyepieces found (EyepieceFusion v1), the picture goes into them when they show the device"
                + (_i2Gain != null ? "; they take the tubes' gain for both eyes" : "; the tubes' gain set in the right eye's picture only"));
        }
        catch (Exception e)
        {
            _claimedFrame = null;
            EnvgFusionPlugin.Log?.LogWarning("ENVG: binding K-3PS-C's EyepieceFusion failed, the picture goes over the screen: " + e.Message);
        }
    }

    // ---- the pods' sideways swing kept by K-3PS-C (EyepiecePods v1, optional) ----
    // K-3PS-C keeps which pod is up as a state of the goggle (remembered by item, drawn on every model of the player:
    // the loading screen, the inventory, the overview), with its own keys; the user's, 2026-11-27. Handed over once
    // here; from then on it turns the pod nodes and we only read where the worn goggle's pods are. Without it (not
    // installed, or an older one) our own swing stays as it was.
    private const string PodsTypeName = "EFTNewCamera.Interop.EyepiecePods";
    private const string DeviceNode = "nvg_envgb_psq42_LOD0";
    private static Func<string, bool>? _podsTurn;
    private static Func<int, float>? _podUp;
    internal static bool TurnsPods => _podsTurn != null && _podsTurn(DeviceNode);
    internal static float PodUp(int pod) => _podUp != null ? _podUp(pod) : 0f;

    private static void BindPods(Assembly assembly)
    {
        try
        {
            Type? type = assembly.GetType(PodsTypeName, false);
            if (type == null)
            {
                EnvgFusionPlugin.Log?.LogInfo("ENVG-B pods: this K-3PS-C does not keep them (no EyepiecePods), they swing by this mod's own keys");
                return;
            }
            FieldInfo? version = type.GetField("Version", BindingFlags.Public | BindingFlags.Static);
            int v = version != null ? (int)version.GetValue(null) : 0;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            MethodInfo? hand = type.GetMethod("HandOver", flags, null, new[] { typeof(string) }, null);
            MethodInfo? turns = type.GetMethod("Turns", flags, null, new[] { typeof(string) }, null);
            MethodInfo? up = type.GetMethod("Up", flags, null, new[] { typeof(int) }, null);
            if (v != 1 || hand == null || turns == null || up == null || turns.ReturnType != typeof(bool) || up.ReturnType != typeof(float))
            {
                EnvgFusionPlugin.Log?.LogWarning("ENVG-B pods: K-3PS-C's EyepiecePods is version " + v + " or missing a member, they swing by this mod's own keys");
                return;
            }
            var turnsFn = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), turns);
            var upFn = (Func<int, float>)Delegate.CreateDelegate(typeof(Func<int, float>), up);
            hand.Invoke(null, new object[] { DeviceNode });
            _podUp = upFn;
            _podsTurn = turnsFn;
            EnvgFusionPlugin.Log?.LogInfo("ENVG-B pods: handed over to K-3PS-C (EyepiecePods v1), taken: " + turnsFn(DeviceNode) + "; it turns and keeps them, this mod's pod keys do nothing");
        }
        catch (Exception e)
        {
            _podsTurn = null;
            _podUp = null;
            EnvgFusionPlugin.Log?.LogWarning("ENVG-B pods: handing them over to K-3PS-C failed, they swing by this mod's own keys: " + e.Message);
        }
    }

    private static Func<T>? Getter<T>(Type type, string name)
    {
        MethodInfo? get = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
        return get != null && get.ReturnType == typeof(T) ? (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), get) : null;
    }

    private static int Claimed => _claimedFrame != null ? _claimedFrame() : -1;

    // the eyepieces show the device this frame (they claim it at the main camera's pre-cull)
    internal static bool ClaimedNow
    {
        get
        {
            bool now = Claimed == Time.frameCount;
            if (now && !_loggedClaim)
            {
                _loggedClaim = true;
                EnvgFusionPlugin.Log?.LogInfo("ENVG: K-3PS-C's eyepieces claimed the device, nothing drawn over the screen while they show it");
            }
            return now;
        }
    }

    // this frame or the last: the tube's colour stays theirs between the claim and our late update
    internal static bool ClaimedRecently
    {
        get
        {
            int c = Claimed;
            return c >= 0 && Time.frameCount - c <= 1;
        }
    }

    internal static Vector4 FieldInMainUv => _field != null ? _field() : new Vector4(0.5f, 0.5f, 0.5f, 16f / 9f);
    internal static float Deployed => _deployed != null ? _deployed() : 1f;
}
