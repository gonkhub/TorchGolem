using UnityEngine;

namespace TorchGolemMod
{
    /// <summary>Client-side extras for golems: silencing the body's sounds and renaming it.</summary>
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

        public static void RequestRename(ZDO golem)
        {
            if (TextInput.instance == null)
                return;
            TextInput.instance.RequestText(new Renamer(golem), "$hud_rename", 30);
        }

        /// <summary>Feeds Valheim's rename dialog, the same one used for tamed animals.</summary>
        sealed class Renamer : TextReceiver
        {
            readonly ZDOID m_id;
            readonly string m_current;

            public Renamer(ZDO golem)
            {
                m_id = golem.m_uid;
                m_current = golem.GetString(ZDOVars.s_tamedName, Plugin.GolemName.Value);
            }

            public string GetText() => m_current;

            public void SetText(string text)
            {
                var zdo = ZDOMan.instance.GetZDO(m_id);
                if (zdo == null || string.IsNullOrWhiteSpace(text))
                    return;
                // The vanilla rename RPC; the server applies it for golems (see Patches).
                ZRoutedRpc.instance.InvokeRoutedRPC(zdo.GetOwner(), m_id, "SetName", text, "");
            }
        }
    }
}
