using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TorchGolemMod
{
    /// <summary>
    /// Decides which objects count as refuelable light sources and which items the golem may use as fuel.
    /// The golem only ever looks at <see cref="Fireplace"/> pieces; production stations (furnace, kiln,
    /// eitr refinery, spinning wheel, windmill...) are <see cref="Smelter"/>s and never qualify. On top of
    /// that, anything carrying a production/utility component is rejected outright, in case a mod bolts a
    /// Fireplace onto one.
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

        static HashSet<string> s_fuel;
        static HashSet<string> s_excludedPieces;
        static ZNetScene s_resolvedFor;
        static readonly Dictionary<string, bool> s_lightSourceCache = new Dictionary<string, bool>();

        internal static void Invalidate()
        {
            s_fuel = null;
            s_excludedPieces = null;
            s_lightSourceCache.Clear();
        }

        /// <summary>Item prefab names the golem may carry and put into fires.</summary>
        internal static HashSet<string> Fuel
        {
            get
            {
                if (s_fuel == null || s_resolvedFor != ZNetScene.instance)
                    Resolve();
                return s_fuel;
            }
        }

        /// <summary>True if this piece is a decorative/light fire the golem is allowed to touch.</summary>
        internal static bool IsLightSource(GameObject piece, string prefabName)
        {
            if (s_excludedPieces == null)
                s_excludedPieces = Plugin.ParseList(Plugin.ExcludedPieces.Value);
            if (s_excludedPieces.Contains(prefabName))
                return false;

            if (!s_lightSourceCache.TryGetValue(prefabName, out bool ok))
            {
                ok = !s_forbiddenComponents.Any(t => piece.GetComponentInChildren(t, true) != null);
                s_lightSourceCache[prefabName] = ok;
            }
            return ok;
        }

        static void Resolve()
        {
            s_resolvedFor = ZNetScene.instance;
            var fuel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (Plugin.FuelItems.Value.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                var lightSources = new List<string>();
                if (s_resolvedFor != null)
                {
                    foreach (var prefab in GolemPrefab.GetScenePrefabs(s_resolvedFor))
                    {
                        if (prefab == null || prefab.GetComponent<Piece>() == null) continue;
                        if (!prefab.TryGetComponent(out Fireplace fire)) continue;
                        if (fire.m_fuelItem == null || fire.m_infiniteFuel || !fire.m_canRefill) continue;
                        if (!IsLightSource(prefab, prefab.name)) continue;

                        fuel.Add(fire.m_fuelItem.gameObject.name);
                        lightSources.Add($"{prefab.name} ({fire.m_fuelItem.gameObject.name})");
                    }
                    Plugin.Log.LogInfo($"Refuelable pieces: {string.Join(", ", lightSources)}");
                }
            }
            else
            {
                fuel.UnionWith(Plugin.ParseList(Plugin.FuelItems.Value));
            }

            fuel.ExceptWith(Plugin.ParseList(Plugin.ExcludedFuel.Value));
            s_fuel = fuel;
            if (s_resolvedFor != null)
                Plugin.Log.LogInfo($"Torch Golem fuel types: {string.Join(", ", fuel)}");
        }
    }
}
