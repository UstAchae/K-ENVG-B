using System.IO;
using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine;

namespace EnvgFusion;

// The ENVG-B's eyecup slot ("mod_eyecup") is a slot the game has no picture for: ModSlotView.Show finds no
// "Slots/mod_eyecup" sprite, leaves the slot's back image white and its name unreadable on it. Our own silhouette
// (item/ui/slot_mod_eyecup.png, built into this DLL) is put there instead, once Show has run. Made once, kept.
internal static class EyecupSlotIcon
{
    private const string SlotName = "mod_eyecup";
    private static Sprite? _sprite;
    private static bool _tried;

    internal static void Patch(Harmony harmony)
    {
        MethodInfo? show = AccessTools.Method(typeof(ModSlotView), nameof(ModSlotView.Show),
            new[] { typeof(Slot), typeof(ItemContext), typeof(ItemController), typeof(ItemUiContext) });
        if (show != null)
            harmony.Patch(show, postfix: new HarmonyMethod(typeof(EyecupSlotIcon), nameof(AfterShow)));
        else
            EnvgFusionPlugin.Log?.LogWarning("ModSlotView.Show not found; the eyecup slot keeps the game's blank picture");
    }

    private static void AfterShow(ModSlotView __instance, Slot slot)
    {
        if (slot == null || slot.Name != SlotName)
            return;
        Sprite? sprite = Sprite();
        if (sprite != null)
            __instance.SetSlotBackImage(sprite);
    }

    private static Sprite? Sprite()
    {
        if (_sprite != null || _tried)
            return _sprite;
        _tried = true;
        try
        {
            using Stream? stream = typeof(EyecupSlotIcon).Assembly.GetManifestResourceStream("slot_mod_eyecup.png");
            if (stream == null)
            {
                EnvgFusionPlugin.Log?.LogWarning("The eyecup slot picture is missing from the DLL");
                return null;
            }
            var bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Justachae ENVG eyecup slot", hideFlags = HideFlags.HideAndDontSave };
            if (!texture.LoadImage(bytes))
            {
                EnvgFusionPlugin.Log?.LogWarning("The eyecup slot picture could not be read");
                return null;
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            _sprite = UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            _sprite.name = "Slots/" + SlotName;
            _sprite.hideFlags = HideFlags.HideAndDontSave;
            EnvgFusionPlugin.Log?.LogInfo("Eyecup slot picture made (" + texture.width + "x" + texture.height + "), shown in empty eyecup slots");
        }
        catch (System.Exception e)
        {
            EnvgFusionPlugin.Log?.LogWarning("The eyecup slot picture failed: " + e.Message);
        }
        return _sprite;
    }
}
