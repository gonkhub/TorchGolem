using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TorchGolemMod
{
    /// <summary>
    /// Reports which vanilla creatures would make a quiet golem body.
    ///
    /// A creature makes noise two ways, and both are driven by each player's own game from the prefab:
    /// periodic idle sounds on a timer, and audio the prefab loops constantly. The server can change
    /// neither (its only lever, the asleep flag, freezes the animation rig). Picking a body with no sounds
    /// is therefore the only way to have a golem that is silent for players without the mod.
    /// </summary>
    internal static class BodyCatalog
    {
        static readonly Dictionary<string, bool> s_canSleepAwake = new Dictionary<string, bool>();

        /// <summary>
        /// True if this body syncs its "sleeping" animation switch from the server. Only then can a golem be
        /// held asleep (silent) while being forced to animate normally; see GolemServer.ApplySilence.
        /// </summary>
        public static bool CanSleepAwake(string prefabName)
        {
            if (s_canSleepAwake.TryGetValue(prefabName, out bool can))
                return can;
            if (ZNetScene.instance == null)
                return false;

            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            var animation = prefab != null ? prefab.GetComponent<ZSyncAnimation>() : null;
            can = animation != null && animation.m_syncBools.Contains("sleeping");
            s_canSleepAwake[prefabName] = can;
            Plugin.Log.LogInfo(can
                ? $"'{prefabName}' can be kept quiet for everyone (it syncs the sleeping animation)."
                : $"'{prefabName}' can't be silenced for players without the mod; it doesn't sync the sleeping animation.");
            return can;
        }

        public static void Forget() => s_canSleepAwake.Clear();

        public static void LogCandidates(ZNetScene scene)
        {
            var silentAnimated = new List<string>();
            var silentStatic = new List<string>();
            var silenceable = new List<string>();

            foreach (var prefab in FuelRegistry.GetScenePrefabs(scene))
            {
                if (prefab == null || !prefab.TryGetComponent(out BaseAI ai) || !prefab.TryGetComponent(out Character character))
                    continue;

                int idle = ai.m_idleSound?.m_effectPrefabs?.Count(e => e != null && e.m_enabled && e.m_prefab != null) ?? 0;
                int loops = prefab.GetComponentsInChildren<AudioSource>(true).Count(a => a.loop && a.playOnAwake);
                var animation = prefab.GetComponent<ZSyncAnimation>();
                // The golem is moved by the server, so a body only animates for others if its walk/fly
                // animation is driven by the synced forward_speed value.
                bool animates = animation != null && animation.m_syncFloats.Contains("forward_speed");
                bool syncsSleeping = animation != null && animation.m_syncBools.Contains("sleeping");
                string label = prefab.name + (character.m_flying ? " (flies)" : "");

                if (idle == 0 && loops == 0)
                    (animates ? silentAnimated : silentStatic).Add(label);
                else if (loops == 0 && syncsSleeping && animates)
                    silenceable.Add(label);
            }

            Plugin.Log.LogInfo($"Best bodies - silent for everyone AND animated: {string.Join(", ", silentAnimated)}");
            Plugin.Log.LogInfo($"Silent but static bodies: {string.Join(", ", silentStatic)}");
            Plugin.Log.LogInfo($"Animated bodies this mod can silence (no name plate while silent): {string.Join(", ", silenceable)}");
        }

        /// <summary>Spirit-like creatures and whatever body is configured, for comparison.</summary>
        static bool IsInteresting(string prefabName) =>
            prefabName == Plugin.GolemPrefab.Value ||
            prefabName.IndexOf("ghost", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            prefabName.IndexOf("wisp", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            prefabName.IndexOf("wraith", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            prefabName.IndexOf("spirit", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            prefabName.IndexOf("shade", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
