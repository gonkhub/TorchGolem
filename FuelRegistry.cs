using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TorchGolemMod
{
    internal sealed class FireInfo
    {
        public string PrefabName;
        public float MaxFuel;
        public string FuelItem;
    }

    internal sealed class ChestInfo
    {
        public string PrefabName;
        public int Width;
        public int Height;
        public bool IsPrivate;
    }

    /// <summary>
    /// Server-side knowledge about prefabs, read once per world from ZNetScene. The server has no instances of
    /// fires or chests near players, so everything about them comes from their prefab plus their ZDO data.
    /// Only <see cref="Fireplace"/> pieces count as refuelable; production stations (furnace, kiln, eitr
    /// refinery, windmill...) are <see cref="Smelter"/>s and never qualify, and anything carrying a
    /// production/utility component is rejected outright in case a mod bolts a Fireplace onto one.
    /// </summary>
    internal static class FuelRegistry
    {
        static readonly Type[] s_forbiddenComponents =
        {
            typeof(Smelter),
            typeof(CraftingStation),
            typeof(StationExtension),
            typeof(CookingStation),
            typeof(Fermenter),
            typeof(Beehive),
            typeof(SapCollector),
            typeof(Windmill),
            typeof(ShieldGenerator),
            typeof(Turret),
            typeof(Incinerator),
        };

        static ZNetScene s_resolvedFor;
        static HashSet<string> s_fuel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<int, FireInfo> s_fires = new Dictionary<int, FireInfo>();
        static readonly Dictionary<int, ChestInfo> s_chests = new Dictionary<int, ChestInfo>();

        internal static void Invalidate() => s_resolvedFor = null;

        static void EnsureResolved()
        {
            if (s_resolvedFor != ZNetScene.instance || s_resolvedFor == null)
                Resolve();
        }

        /// <summary>Item prefab names golems may carry and put into fires.</summary>
        internal static HashSet<string> Fuel
        {
            get { EnsureResolved(); return s_fuel; }
        }

        internal static bool TryGetFire(int prefabHash, out FireInfo info)
        {
            EnsureResolved();
            return s_fires.TryGetValue(prefabHash, out info);
        }

        internal static bool TryGetChest(int prefabHash, out ChestInfo info)
        {
            EnsureResolved();
            return s_chests.TryGetValue(prefabHash, out info);
        }

        static void Resolve()
        {
            s_resolvedFor = ZNetScene.instance;
            s_fires.Clear();
            s_chests.Clear();
            s_fuel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (s_resolvedFor == null)
                return;

            var excludedPieces = Plugin.ParseList(Plugin.ExcludedPieces.Value);
            var excludedFuel = Plugin.ParseList(Plugin.ExcludedFuel.Value);
            bool auto = Plugin.FuelItems.Value.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase);
            var explicitFuel = auto ? null : Plugin.ParseList(Plugin.FuelItems.Value);

            foreach (var prefab in GetScenePrefabs(s_resolvedFor))
            {
                if (prefab == null || prefab.GetComponent<Piece>() == null || excludedPieces.Contains(prefab.name))
                    continue;

                if (prefab.TryGetComponent(out Fireplace fire))
                {
                    if (fire.m_fuelItem == null || fire.m_infiniteFuel || !fire.m_canRefill || !IsLightSource(prefab))
                        continue;
                    string fuel = fire.m_fuelItem.gameObject.name;
                    if (excludedFuel.Contains(fuel) || (!auto && !explicitFuel.Contains(fuel)))
                        continue;

                    s_fuel.Add(fuel);
                    s_fires[prefab.name.GetStableHashCode()] = new FireInfo { PrefabName = prefab.name, MaxFuel = fire.m_maxFuel, FuelItem = fuel };
                }
                else if (prefab.TryGetComponent(out Container chest) && prefab.GetComponent<Incinerator>() == null)
                {
                    s_chests[prefab.name.GetStableHashCode()] = new ChestInfo
                    {
                        PrefabName = prefab.name,
                        Width = chest.m_width,
                        Height = chest.m_height,
                        IsPrivate = chest.m_privacy == Container.PrivacySetting.Private,
                    };
                }
            }

            Plugin.Log.LogInfo($"Refuelable pieces: {string.Join(", ", s_fires.Values.Select(f => $"{f.PrefabName} ({f.FuelItem})"))}");
            Plugin.Log.LogInfo($"Torch Golem fuel types: {string.Join(", ", s_fuel)}");
        }

        static bool IsLightSource(GameObject prefab) =>
            !s_forbiddenComponents.Any(t => prefab.GetComponentInChildren(t, true) != null);

        static readonly HarmonyLib.AccessTools.FieldRef<ZNetScene, List<GameObject>> s_prefabsRef =
            HarmonyLib.AccessTools.FieldRefAccess<ZNetScene, List<GameObject>>("m_prefabs");

        internal static List<GameObject> GetScenePrefabs(ZNetScene scene) => s_prefabsRef(scene);
    }
}
