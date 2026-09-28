using HarmonyLib;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ShieldShare
{
    /// <summary>
    ///     What happens when another player has a shield from a pack you don't.
    ///
    ///     The game only sends item *hashes* around (for equipment, chests and dropped items), so a client
    ///     without the pack can't even tell which shield it is. ShieldShare therefore tags its shields:
    ///       - every shield item carries its ID and base in its custom data (saved with the item);
    ///       - a player's ZDO carries "ID|Base" for the shield in hand and on the back;
    ///       - a dropped shield's ZDO carries "ID|Base".
    ///     When a client meets a tagged shield it doesn't have, it registers a stand-in on the spot:
    ///     a copy of the built-in magenta shield for that base, under the shield's REAL name. The item
    ///     keeps its identity (nothing is converted or deleted), and it looks right again as soon as the
    ///     pack is installed. The player gets BannerShare's notice: one centre-screen message, details in chat.
    /// </summary>
    internal static class MissingShields
    {
        public const string ItemIdKey = "ShieldShare.Id";
        public const string ItemBaseKey = "ShieldShare.Base";
        private const string ZdoLeftKey = "ShieldShare.Left";
        private const string ZdoLeftBackKey = "ShieldShare.LeftBack";
        private const string ZdoDropKey = "ShieldShare.Item";

        /// <summary>Base prefab of every shield registered locally (packs, built-ins, stand-ins), by ID.</summary>
        public static readonly Dictionary<string, string> LocalBases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, GameObject> standIns = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        // Private game fields, read by reflection.
        private static readonly FieldInfo LeftItemField = AccessTools.Field(typeof(Humanoid), "m_leftItem");
        private static readonly FieldInfo HiddenLeftItemField = AccessTools.Field(typeof(Humanoid), "m_hiddenLeftItem");
        private static readonly FieldInfo VisNViewField = AccessTools.Field(typeof(VisEquipment), "m_nview");
        private static readonly FieldInfo ItemByHashField = AccessTools.Field(typeof(ObjectDB), "m_itemByHash");
        private static readonly FieldInfo NamedPrefabsField = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");
        private static readonly FieldInfo ItemStandNViewField = AccessTools.Field(typeof(ItemStand), "m_nview");
        private static readonly FieldInfo ArmorStandNViewField = AccessTools.Field(typeof(ArmorStand), "m_nview");

        private static ItemDrop.ItemData LeftItem(Humanoid h) => (ItemDrop.ItemData)LeftItemField.GetValue(h);
        private static ItemDrop.ItemData HiddenLeftItem(Humanoid h) => (ItemDrop.ItemData)HiddenLeftItemField.GetValue(h);
        private static ZNetView VisNView(VisEquipment v) => (ZNetView)VisNViewField.GetValue(v);
        private static Dictionary<int, GameObject> ItemByHash(ObjectDB db) => (Dictionary<int, GameObject>)ItemByHashField.GetValue(db);
        private static Dictionary<int, GameObject> NamedPrefabs(ZNetScene scene) => (Dictionary<int, GameObject>)NamedPrefabsField.GetValue(scene);

        // ------------------------------------------------------------------------------------------
        // Tags
        // ------------------------------------------------------------------------------------------

        public static string PrefabName(string id) => ShieldShare.ItemPrefabPrefix + id;

        /// <summary>
        ///     ZDO key for the item saved at slot <paramref name="index"/> - mirrors ItemDrop.SaveToZDO, which is
        ///     called with index -1 for dropped items and item stands, and the slot number (0, 1, ...) for armour stands.
        /// </summary>
        private const int WholeObjectIndex = -1;
        private static string ItemTagKey(int index) => index < 0 ? ZdoDropKey : index + "_" + ZdoDropKey;

        /// <summary>"ID|Base" for a ShieldShare item, or "" for anything else.</summary>
        public static string TagFor(ItemDrop.ItemData item)
        {
            if (item == null)
                return "";
            string id = null, baseName = null;
            if (item.m_customData != null)
            {
                item.m_customData.TryGetValue(ItemIdKey, out id);
                item.m_customData.TryGetValue(ItemBaseKey, out baseName);
            }
            if (string.IsNullOrEmpty(id) && item.m_dropPrefab != null && item.m_dropPrefab.name.StartsWith(ShieldShare.ItemPrefabPrefix, StringComparison.Ordinal))
                id = item.m_dropPrefab.name.Substring(ShieldShare.ItemPrefabPrefix.Length);
            if (string.IsNullOrEmpty(id))
                return "";
            if (string.IsNullOrEmpty(baseName))
                LocalBases.TryGetValue(id, out baseName);
            return id + "|" + (baseName ?? "");
        }

        private static bool ParseTag(string tag, out string id, out string baseName)
        {
            id = baseName = null;
            if (string.IsNullOrEmpty(tag))
                return false;
            int bar = tag.IndexOf('|');
            id = bar >= 0 ? tag.Substring(0, bar) : tag;
            baseName = bar >= 0 ? tag.Substring(bar + 1) : "";
            return id.Length > 0;
        }

        private static void SetIfChanged(ZDO zdo, string key, string value)
        {
            if (zdo.GetString(key, "") != value)
                zdo.Set(key, value);
        }

        // ------------------------------------------------------------------------------------------
        // Stand-ins
        // ------------------------------------------------------------------------------------------

        /// <summary>
        ///     Makes sure a shield with this ID exists in the running game, registering a magenta stand-in
        ///     if it doesn't. Returns false if nothing could be registered.
        /// </summary>
        public static bool EnsureExists(string id, string baseName, string whereSeen)
        {
            if (ObjectDB.instance == null || ZNetScene.instance == null || string.IsNullOrEmpty(id))
                return false;

            string prefabName = PrefabName(id);
            int hash = prefabName.GetStableHashCode();
            if (ObjectDB.instance.GetItemPrefab(hash) != null)
                return true; // installed (or already stood in for this world)

            GameObject standIn;
            if (!standIns.TryGetValue(id, out standIn) || standIn == null)
            {
                standIn = CreateStandIn(id, baseName);
                if (standIn == null)
                    return false;
                standIns[id] = standIn;
            }

            // Register into this world's ObjectDB and ZNetScene (both are rebuilt for every world).
            if (!ObjectDB.instance.m_items.Contains(standIn))
                ObjectDB.instance.m_items.Add(standIn);
            ItemByHash(ObjectDB.instance)[hash] = standIn;
            if (!ZNetScene.instance.m_prefabs.Contains(standIn))
                ZNetScene.instance.m_prefabs.Add(standIn);
            NamedPrefabs(ZNetScene.instance)[hash] = standIn;

            Notices.Queue(id, whereSeen);
            return true;
        }

        private static GameObject CreateStandIn(string id, string baseName)
        {
            var template = PrefabManager.Instance.GetPrefab(PrefabName(ShieldShare.BuiltInPrefix + baseName))
                           ?? PrefabManager.Instance.GetPrefab(PrefabName(ShieldShare.BuiltInPrefix + ShieldPack.DefaultBasePrefab));
            if (template == null)
            {
                Jotunn.Logger.LogWarning($"[ShieldShare] No built-in missing shield to stand in for '{id}' (base '{baseName}').");
                return null;
            }

            var clone = PrefabManager.Instance.CreateClonedPrefab(PrefabName(id), template);
            var drop = clone != null ? clone.GetComponent<ItemDrop>() : null;
            if (drop == null)
                return null;

            // Own copy of the shared item data so the name doesn't change the built-in shield too.
            var shared = (ItemDrop.ItemData.SharedData)AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(drop.m_itemData.m_shared, null);
            shared.m_name = id + " (missing)";
            shared.m_description = $"Someone on this server has the shield '{id}', but you don't have its shield pack. " +
                                   "Ask them for the zip, put it in Documents\\Valheim Custom Shields and restart the game.";
            drop.m_itemData.m_shared = shared;
            drop.m_itemData.m_dropPrefab = clone;
            drop.m_itemData.m_customData = new Dictionary<string, string>
            {
                { ItemIdKey, id },
                { ItemBaseKey, string.IsNullOrEmpty(baseName) ? ShieldPack.DefaultBasePrefab : baseName },
            };
            LocalBases[id] = drop.m_itemData.m_customData[ItemBaseKey];
            Jotunn.Logger.LogInfo($"[ShieldShare] Missing shield '{id}' isn't installed locally, showing the built-in placeholder.");
            return clone;
        }

        // ------------------------------------------------------------------------------------------
        // Notices - same behaviour as BannerShare: wait for things to settle, then one centre message
        // and one chat line per missing shield.
        // ------------------------------------------------------------------------------------------

        internal static class Notices
        {
            private const float SettleSeconds = 2f;
            private static readonly HashSet<string> noticed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private static readonly List<KeyValuePair<string, string>> pending = new List<KeyValuePair<string, string>>();
            private static float lastQueuedTime;

            public static void Queue(string id, string whereSeen)
            {
                if (!noticed.Add(id))
                    return;
                pending.Add(new KeyValuePair<string, string>(id, whereSeen));
                lastQueuedTime = Time.time;
            }

            public static void Update()
            {
                if (ZNet.instance == null)
                {
                    // Not in a world (main menu) - start fresh for the next one
                    noticed.Clear();
                    pending.Clear();
                    return;
                }

                if (pending.Count == 0 || Time.time - lastQueuedTime < SettleSeconds)
                    return;

                // Things near the spawn point load before the player exists - hold on until then
                if (Player.m_localPlayer == null || Chat.instance == null || MessageHud.instance == null)
                    return;

                string count = pending.Count == 1 ? "1 shield file" : $"{pending.Count} shield files";
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"You are missing {count} - check your chat log");

                foreach (var p in pending)
                    Chat.instance.AddString($"[ShieldShare] Missing shield: {p.Key}{(string.IsNullOrEmpty(p.Value) ? "" : " (" + p.Value + ")")}");
                Chat.instance.AddString("[ShieldShare] Ask around the server for these shield packs, drop the zips in Documents\\Valheim Custom Shields, then restart the game.");

                pending.Clear();
            }
        }

        // ------------------------------------------------------------------------------------------
        // Harmony patches
        // ------------------------------------------------------------------------------------------

        /// <summary>Owner side: publish which ShieldShare shields this player has in hand and on their back.</summary>
        [HarmonyPatch(typeof(Humanoid), "SetupVisEquipment")]
        private static class Humanoid_SetupVisEquipment_Patch
        {
            private static void Postfix(Humanoid __instance)
            {
                if (!(__instance is Player))
                    return;
                var nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid() || !nview.IsOwner())
                    return;
                var zdo = nview.GetZDO();
                ItemDrop.ItemData left = LeftItem(__instance);
                ItemDrop.ItemData leftBack = HiddenLeftItem(__instance);
                SetIfChanged(zdo, ZdoLeftKey, TagFor(left));
                SetIfChanged(zdo, ZdoLeftBackKey, TagFor(leftBack));
            }
        }

        /// <summary>Other players: stand in for shields in their hand or on their back that we don't have.</summary>
        [HarmonyPatch(typeof(VisEquipment), "UpdateEquipmentVisuals")]
        private static class VisEquipment_UpdateEquipmentVisuals_Patch
        {
            private static void Prefix(VisEquipment __instance)
            {
                var nview = VisNView(__instance);
                if (nview == null || !nview.IsValid() || nview.IsOwner())
                    return;
                var zdo = nview.GetZDO();
                CheckSlot(__instance, zdo, ZdoLeftKey);
                CheckSlot(__instance, zdo, ZdoLeftBackKey);
            }

            private static void CheckSlot(VisEquipment vis, ZDO zdo, string key)
            {
                string id, baseName;
                if (!ParseTag(zdo.GetString(key, ""), out id, out baseName))
                    return;
                if (ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(PrefabName(id).GetStableHashCode()) != null)
                    return; // installed, or already stood in
                var player = vis.GetComponent<Player>();
                EnsureExists(id, baseName, player != null ? "carried by " + player.GetPlayerName() : "carried by another player");
            }
        }

        /// <summary>Owner side: tag dropped shields so other clients know what they are.</summary>
        [HarmonyPatch(typeof(ItemDrop), "Start")]
        private static class ItemDrop_Start_Patch
        {
            private static void Postfix(ItemDrop __instance)
            {
                var nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid() || !nview.IsOwner())
                    return;
                string tag = TagFor(__instance.m_itemData);
                if (tag.Length > 0)
                    SetIfChanged(nview.GetZDO(), ZdoDropKey, tag);
            }
        }

        /// <summary>Dropped shields we don't have: register the stand-in before the game looks for the prefab.</summary>
        [HarmonyPatch(typeof(ZNetScene), "CreateObject", new[] { typeof(ZDO) })]
        private static class ZNetScene_CreateObject_Patch
        {
            private static void Prefix(ZDO zdo)
            {
                if (zdo == null || ZNetScene.instance == null || ZNetScene.instance.GetPrefab(zdo.GetPrefab()) != null)
                    return;
                string id, baseName;
                if (ParseTag(zdo.GetString(ZdoDropKey, ""), out id, out baseName) && PrefabName(id).GetStableHashCode() == zdo.GetPrefab())
                    EnsureExists(id, baseName, "on the ground");
            }
        }

        /// <summary>
        ///     Owner side: whenever an item is saved into a ZDO (dropped item, item stand, armour stand slot),
        ///     record which ShieldShare shield it is - or clear the record if something else went in the slot.
        /// </summary>
        [HarmonyPatch(typeof(ItemDrop), "SaveToZDO", new[] { typeof(ItemDrop.ItemData), typeof(ZDO), typeof(int) })]
        private static class ItemDrop_SaveToZDO_Patch
        {
            private static void Postfix(ItemDrop.ItemData itemData, ZDO zdo, int index)
            {
                if (zdo == null)
                    return;
                string key = ItemTagKey(index);
                string tag = TagFor(itemData);
                if (tag.Length > 0 || zdo.GetString(key, "").Length > 0)
                    SetIfChanged(zdo, key, tag);
            }
        }

        /// <summary>Shared by both stands: stand in for a shield we don't have, or tag an old shield we do have.</summary>
        private static void CheckStandSlot(ZNetView nview, int index, int itemHash, string whereSeen)
        {
            if (itemHash == 0 || nview == null || !nview.IsValid() || ObjectDB.instance == null)
                return;
            var zdo = nview.GetZDO();
            string key = ItemTagKey(index);
            var prefab = ObjectDB.instance.GetItemPrefab(itemHash);

            if (prefab != null)
            {
                // Shields put on stands before tagging existed: the first owner who has the pack tags them.
                if (nview.IsOwner() && zdo.GetString(key, "").Length == 0
                    && prefab.name.StartsWith(ShieldShare.ItemPrefabPrefix, StringComparison.Ordinal))
                {
                    string localId = prefab.name.Substring(ShieldShare.ItemPrefabPrefix.Length);
                    string localBase;
                    if (LocalBases.TryGetValue(localId, out localBase))
                        zdo.Set(key, localId + "|" + localBase);
                }
                return;
            }

            string id, baseName;
            if (ParseTag(zdo.GetString(key, ""), out id, out baseName) && PrefabName(id).GetStableHashCode() == itemHash)
                EnsureExists(id, baseName, whereSeen);
        }

        // TODO (later): the orientation of shields on item stands looks a little odd. Not investigated.

        /// <summary>Item stands (wall mounts): register the stand-in before the stand looks the shield up.</summary>
        [HarmonyPatch(typeof(ItemStand), "SetVisualItem", new[] { typeof(int), typeof(int), typeof(int), typeof(int) })]
        private static class ItemStand_SetVisualItem_Patch
        {
            private static void Prefix(ItemStand __instance, int itemHash)
            {
                CheckStandSlot((ZNetView)ItemStandNViewField.GetValue(__instance), WholeObjectIndex, itemHash, "on an item stand");
            }
        }

        /// <summary>Armour stands: one check per slot.</summary>
        [HarmonyPatch(typeof(ArmorStand), "SetVisualItem", new[] { typeof(int), typeof(int), typeof(int) })]
        private static class ArmorStand_SetVisualItem_Patch
        {
            private static void Prefix(ArmorStand __instance, int index, int itemHash)
            {
                CheckStandSlot((ZNetView)ArmorStandNViewField.GetValue(__instance), index, itemHash, "on an armour stand");
            }
        }

        /// <summary>
        ///     Every inventory (player, chest, cart, ship) loads items through this overload by prefab hash.
        ///     Without a prefab the game skips the item - and it's gone for good the next time that inventory
        ///     is saved. For our shields: register a stand-in first; and when we DO have the shield, add the
        ///     ID/base tag to items made before tagging existed.
        /// </summary>
        ///
        ///     The exact overload differs between game builds (the client and the dedicated server builds have had
        ///     different parameter lists), so instead of a fixed signature it is found at startup: the AddItem
        ///     whose first parameter is the int prefab hash and which takes the custom-data dictionary.
        ///     If a build has no such overload, this one patch is skipped - nothing else is affected.
        [HarmonyPatch]
        private static class Inventory_AddItem_ByHash_Patch
        {
            private static MethodBase target;
            private static int hashIndex = -1, customDataIndex = -1;

            private static MethodBase FindTarget()
            {
                foreach (var m in typeof(Inventory).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name != "AddItem")
                        continue;
                    var ps = m.GetParameters();
                    if (ps.Length < 2 || ps[0].ParameterType != typeof(int))
                        continue;
                    int dict = Array.FindIndex(ps, p => p.ParameterType == typeof(Dictionary<string, string>));
                    if (dict < 0)
                        continue;
                    hashIndex = 0;
                    customDataIndex = dict;
                    return m;
                }
                return null;
            }

            private static bool Prepare()
            {
                if (target == null)
                    target = FindTarget();
                if (target == null)
                    Jotunn.Logger.LogWarning("[ShieldShare] This game build has no Inventory.AddItem(prefabHash, ..., customData, ...) - " +
                                             "shields from missing packs in chests and inventories can't be protected.");
                return target != null;
            }

            private static MethodBase TargetMethod() => target ?? FindTarget();

            private static void Prefix(object[] __args)
            {
                int prefabHash = (int)__args[hashIndex];
                var customData = (Dictionary<string, string>)__args[customDataIndex];
                if (ObjectDB.instance == null)
                    return;
                var prefab = ObjectDB.instance.GetItemPrefab(prefabHash);
                if (prefab != null)
                {
                    if (!prefab.name.StartsWith(ShieldShare.ItemPrefabPrefix, StringComparison.Ordinal))
                        return;
                    string localId = prefab.name.Substring(ShieldShare.ItemPrefabPrefix.Length);
                    string localBase;
                    if (!LocalBases.TryGetValue(localId, out localBase))
                        return;
                    if (customData == null)
                    {
                        customData = new Dictionary<string, string>();
                        __args[customDataIndex] = customData; // Harmony passes __args changes on to the game
                    }
                    if (!customData.ContainsKey(ItemIdKey))
                    {
                        customData[ItemIdKey] = localId;
                        customData[ItemBaseKey] = localBase;
                    }
                    return;
                }

                string id, baseName;
                if (customData == null || !customData.TryGetValue(ItemIdKey, out id) || string.IsNullOrEmpty(id))
                    return;
                if (PrefabName(id).GetStableHashCode() != prefabHash)
                    return;
                customData.TryGetValue(ItemBaseKey, out baseName);
                EnsureExists(id, baseName, "in a chest or inventory");
            }
        }
    }
}
