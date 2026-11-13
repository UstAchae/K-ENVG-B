using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Utils;

namespace UstAchae.Envgb;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.ustachae.envgb";
    public string Name { get; init; } = "Justachae's ENVG-B";
    public string Author { get; init; } = "UstAchae";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("0.54.0");  // one version for the whole K-ENVG-B mod (client and server); bump on every change
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.6");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "All rights reserved";
}

// The ENVG-B items (one per colour, db/items.json) are the GPNVG-18's template with their own bundle, a round
// binocular mask and white phosphor, and go everywhere the GPNVG-18 goes (helmet and mount slots, conflicts).
// Runs after the database and other content mods, so modded helmets and mounts that take the GPNVG-18 take these too.
[Injectable(InjectionType.Transient, TypePriority = OnLoadOrder.Preload + 50000)]
public class EnvgbMod(
    ISptLogger<EnvgbMod> logger, ModHelper modHelper, CustomItemService customItemService,
    TemplateTable templateTable, TradersTable tradersTable, LocaleTable localeTable
) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        string modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var options = JsonUtil.JsonSerializerOptionsNoIndent ?? new JsonSerializerOptions();
        var db = JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(modPath, "db", "items.json")))!.AsObject();
        var clone = new MongoId(db["clone"]!.GetValue<string>());
        if (!templateTable.Items.TryGetValue(clone, out var source))
        {
            logger.Error("ENVG-B not loaded: the GPNVG-18 template it is cloned from is missing");
            return Task.CompletedTask;
        }
        string sourceJson = JsonSerializer.Serialize(source, options);
        // The eyecup slot's name in the item screens (slot names are global locale keys in upper case, as MOD_NVG).
        foreach (var (lang, table) in localeTable.Global)
        {
            string label = lang == "ch" ? "眼罩" : "EYECUP";
            table.AddTransformer(d => { if (d is not null) d["MOD_EYECUP"] = label; return d; });
        }
        // The removable eyecup pairs first (db "eyecups"), so the ENVG-B's mod_eyecup slot can take them.
        var eyecups = new List<MongoId>();
        var eyecupDb = db["eyecups"]!.AsObject();
        if (templateTable.Items.TryGetValue(new MongoId(eyecupDb["clone"]!.GetValue<string>()), out var cupSource))
        {
            string cupJson = JsonSerializer.Serialize(cupSource, options);
            foreach (var entry in eyecupDb["items"]!.AsArray())
            {
                var e = entry!.AsObject();
                string bundle = e["bundle"]!.GetValue<string>();
                if (!File.Exists(System.IO.Path.Combine(modPath, "bundles", bundle))) { logger.Error($"ENVG-B eyecups {e["name"]}: bundles/{bundle} is missing"); continue; }
                var node = JsonNode.Parse(cupJson)!.AsObject();
                node["_id"] = e["id"]!.GetValue<string>();
                node["_name"] = e["name"]!.GetValue<string>();
                var props = node["_props"]!.AsObject();
                props["Prefab"] = new JsonObject { ["path"] = bundle, ["rcid"] = "" };
                props["Weight"] = 0.06;
                props["Ergonomics"] = 0;
                props["ConflictingItems"] = new JsonArray();
                var id = Create(node, e, options, eyecupDb["handbookParentId"]!.GetValue<string>(), eyecupDb["price"]!.GetValue<double>());
                if (id is MongoId made)
                {
                    eyecups.Add(made);
                    AddListing(made, new MongoId(e["assortId"]!.GetValue<string>()), eyecupDb["listing"]!.AsObject(), null);
                }
            }
        }
        else logger.Error("ENVG-B eyecups not loaded: their clone template is missing");

        var added = new List<MongoId>();
        foreach (var entry in db["items"]!.AsArray())
        {
            var e = entry!.AsObject();
            string bundle = e["bundle"]!.GetValue<string>();
            if (!File.Exists(System.IO.Path.Combine(modPath, "bundles", bundle)))
            {
                logger.Error($"ENVG-B {e["name"]} not loaded: bundles/{bundle} is missing from {modPath}. Reinstall the mod.");
                continue;
            }
            var node = JsonNode.Parse(sourceJson)!.AsObject();
            node["_id"] = e["id"]!.GetValue<string>();
            node["_name"] = e["name"]!.GetValue<string>();
            var props = node["_props"]!.AsObject();
            props["Prefab"] = new JsonObject { ["path"] = bundle, ["rcid"] = "" };
            // The game's own night vision when nothing else draws it: round binocular field, white phosphor.
            props["Mask"] = "Binocular";
            props["MaskSize"] = 0.96;
            // White phosphor (P45), blue-white. The look the user picked as the target (a white-phosphor tube in another mod)
            // keeps one hue from shadows to highlights, R:G:B 0.61:0.85:1, at a mean brightness 0.41x ours: with the
            // GPNVG-18's gain (2.27) our highlights clipped to white (0.91:0.98:1) and the tint was lost. Hue and gain set to
            // match; both are only measured from screenshots.
            props["Color"] = new JsonObject { ["r"] = 156, ["g"] = 217, ["b"] = 255, ["a"] = 254 };
            props["Intensity"] = 1.4;
            // The eyecups come off: slot mod_eyecup (the prefab's node of that name under the device), either colour fits.
            var filter = new JsonArray(); foreach (var c in eyecups) filter.Add(c.ToString());
            props["Slots"] = new JsonArray(new JsonObject
            {
                ["_id"] = e["eyecupSlotId"]!.GetValue<string>(), ["_name"] = "mod_eyecup", ["_parent"] = e["id"]!.GetValue<string>(),
                ["_props"] = new JsonObject { ["filters"] = new JsonArray(new JsonObject { ["Shift"] = 0, ["Filter"] = filter }) },
                ["_required"] = false, ["_mergeSlotWithChildren"] = false, ["_proto"] = "55d30c4c4bdc2db4468b457e"
            });
            if (Create(node, e, options, db["handbookParentId"]!.GetValue<string>(), db["price"]!.GetValue<double>()) is not MongoId envgb) continue;
            // Sold with its own colour's eyecups on.
            var cup = new MongoId(e["eyecup"]!.GetValue<string>());
            AddListing(envgb, new MongoId(e["assortId"]!.GetValue<string>()), db["listing"]!.AsObject(),
                eyecups.Contains(cup) ? (cup, new MongoId(e["eyecupAssortId"]!.GetValue<string>())) : null);
            added.Add(envgb);
        }
        int slots = GoWhereItGoes(clone, added);
        if (added.Count > 0) logger.Success($"ENVG-B loaded: {added.Count} colours, {eyecups.Count} eyecup pairs, fits {slots} slots");
        return Task.CompletedTask;
    }

    // Every slot that takes the GPNVG-18 takes the ENVG-B; every item that conflicts with it conflicts with these.
    int GoWhereItGoes(MongoId clone, List<MongoId> added)
    {
        int slots = 0;
        foreach (var item in templateTable.Items.Values)
        {
            if (added.Contains(item.Id) || item.Properties is null) continue;
            foreach (var slot in item.Properties.Slots ?? [])
                foreach (var f in slot.Properties?.Filters ?? [])
                    if (f.Filter is not null && f.Filter.Contains(clone))
                    {
                        foreach (var id in added) f.Filter.Add(id);
                        slots++;
                    }
            if (item.Properties.ConflictingItems is { } conflicts && conflicts.Contains(clone))
                foreach (var id in added) conflicts.Add(id);
        }
        return slots;
    }

    MongoId? Create(JsonObject node, JsonObject e, JsonSerializerOptions options, string handbookParent, double price)
    {
        var template = JsonSerializer.Deserialize<TemplateItem>(node, options)
            ?? throw new InvalidOperationException($"{e["name"]}: template did not deserialize");
        var locales = new Dictionary<string, LocaleDetails>();
        foreach (var pair in e["locales"]!.AsObject())
            locales[pair.Key] = new LocaleDetails
            {
                Name = pair.Value!["name"]!.GetValue<string>(),
                ShortName = pair.Value!["shortName"]!.GetValue<string>(),
                Description = pair.Value!["description"]!.GetValue<string>()
            };
        var result = customItemService.CreateItem(new NewItemDetails
        {
            NewItem = template, AddToHandbook = true, HandbookParentId = handbookParent,
            HandbookPriceRoubles = price, FleaPriceRoubles = price, AddToFleaPriceDb = true, Locales = locales
        }, Assembly.GetExecutingAssembly());
        if (result.Success) return template.Id;
        logger.Error($"ENVG-B {e["name"]} was not created: " + string.Join("; ", result.Errors ?? []));
        return null;
    }

    void AddListing(MongoId item, MongoId assortId, JsonObject listing, (MongoId tpl, MongoId id)? child)
    {
        if (!tradersTable.TryGetValue(new MongoId(listing["traderId"]!.GetValue<string>()), out var trader) || trader.Assort is null)
        {
            logger.Warning("ENVG-B trader missing; item is only on the flea market");
            return;
        }
        if (trader.Assort.Items.Any(i => i.Id == assortId)) return;
        trader.Assort.Items.Add(new Item
        {
            Id = assortId,
            Template = item,
            ParentId = "hideout",
            SlotId = "hideout",
            Upd = new Upd { UnlimitedCount = true, StackObjectsCount = 999999, BuyRestrictionMax = listing["limit"]?.GetValue<int>() ?? 1, BuyRestrictionCurrent = 0 }
        });
        if (child is { } c)
            trader.Assort.Items.Add(new Item { Id = c.id, Template = c.tpl, ParentId = assortId.ToString(), SlotId = "mod_eyecup" });
        trader.Assort.BarterScheme[assortId] =
        [
            [new BarterScheme { Count = listing["price"]!.GetValue<double>(), Template = new MongoId(listing["currency"]!.GetValue<string>()) }]
        ];
        trader.Assort.LoyalLevelItems[assortId] = listing["loyalty"]!.GetValue<int>();
    }
}
