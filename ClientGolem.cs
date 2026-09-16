using UnityEngine;

namespace TorchGolemMod
{
    /// <summary>Client-side extras for golems: silencing the body's sounds.</summary>
    internal static class ClientGolem
    {
        /// <summary>
        /// Ghosts wail constantly. The sounds come from components on the creature itself, played locally by
        /// each game, so this only quiets golems for players who have the mod.
        /// </summary>
        public static void Silence(GameObject golem)
        {
            if (!Plugin.MuteSounds.Value || golem == null)
                return;
            foreach (var sfx in golem.GetComponentsInChildren<ZSFX>(true))
                sfx.enabled = false;
            foreach (var source in golem.GetComponentsInChildren<AudioSource>(true))
            {
                source.Stop();
                source.volume = 0f;
                source.enabled = false;
            }
        }

    }
}
