using System.Text;
using HarmonyLib;
using UnityEngine;

namespace TorchGolemMod
{
    [HarmonyPatch(typeof(Game), "Start")]
    static class Game_Start_Patch
    {
        static void Postfix() => Protocol.Register();
    }

    // ====================================================================== server

    /// <summary>
    /// The server normally hands creatures to whichever player is nearby. Golems stay with the server, so
    /// players' games (modded or not) never run the creature's own AI on them.
    /// </summary>
    [HarmonyPatch(typeof(ZDO), nameof(ZDO.SetOwner))]
    static class ZDO_SetOwner_Patch
    {
        static bool Prefix(ZDO __instance, long uid)
        {
            if (ZDOMan.instance == null || ZNet.instance == null || !ZNet.instance.IsServer())
                return true;
            return uid == ZDOMan.GetSessionID() || !GolemData.IsGolem(__instance);
        }
    }

    /// <summary>
    /// RPCs aimed at a golem go to its owner, the server, which usually has no instance to handle them.
    /// Catch the vanilla ones players can send from an unmodded game.
    /// </summary>
    [HarmonyPatch(typeof(ZRoutedRpc), "HandleRoutedRPC")]
    static class ZRoutedRpc_HandleRoutedRPC_Patch
    {
        static readonly int s_command = "Command".GetStableHashCode();
        static readonly int s_setName = "SetName".GetStableHashCode();

        static bool Prefix(ZRoutedRpc.RoutedRPCData data)
        {
            if (data.m_targetZDO.IsNone() || (data.m_methodHash != s_command && data.m_methodHash != s_setName))
                return true;
            if (ZNet.instance == null || !ZNet.instance.IsServer() || GolemServer.Instance == null)
                return true;
            var zdo = ZDOMan.instance.GetZDO(data.m_targetZDO);
            if (!GolemData.IsGolem(zdo))
                return true;

            if (data.m_methodHash == s_command)
            {
                GolemServer.Instance.ToggleRest(zdo, data.m_senderPeerID);
            }
            else
            {
                data.m_parameters.SetPos(0);
                string name = data.m_parameters.ReadString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    zdo.Set(ZDOVars.s_tamedName, name);
                    zdo.Set(ZDOVars.s_overrideHoverName, name);
                }
            }
            return false;
        }
    }

    /// <summary>
    /// A host (single player / listen server) can have a real instance of a golem it owns. Its vanilla AI and
    /// physics would fight the server's puppeteering, so they're switched off for golems.
    /// </summary>
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.UpdateAI))]
    static class BaseAI_UpdateAI_Patch
    {
        static readonly AccessTools.FieldRef<BaseAI, ZNetView> s_nview = AccessTools.FieldRefAccess<BaseAI, ZNetView>("m_nview");

        static bool Prefix(BaseAI __instance, ref bool __result)
        {
            var nview = s_nview(__instance);
            if (nview == null || !nview.IsValid() || !GolemData.IsGolem(nview.GetZDO()))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
    static class Character_CustomFixedUpdate_Patch
    {
        internal static readonly AccessTools.FieldRef<Character, ZNetView> s_nview = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

        internal static bool IsOwnedGolem(Character character)
        {
            var nview = s_nview(character);
            return nview != null && nview.IsValid() && nview.IsOwner() && GolemData.IsGolem(nview.GetZDO());
        }

        static bool Prefix(Character __instance) => !IsOwnedGolem(__instance);
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.CustomFixedUpdate))]
    static class Humanoid_CustomFixedUpdate_Patch
    {
        static bool Prefix(Humanoid __instance) => !Character_CustomFixedUpdate_Patch.IsOwnedGolem(__instance);
    }

    // On a dedicated server nothing can hurt a golem (damage goes to its owner, which has no instance).
    // Keep that true for hosts as well.
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    static class Character_RPC_Damage_Patch
    {
        static bool Prefix(Character __instance) => !Character_CustomFixedUpdate_Patch.IsOwnedGolem(__instance);
    }

    // Summoned creatures can carry a self-destruct timer; never let it fire on a golem.
    [HarmonyPatch(typeof(CharacterTimedDestruction), "DestroyNow")]
    static class CharacterTimedDestruction_DestroyNow_Patch
    {
        static bool Prefix(CharacterTimedDestruction __instance) =>
            !(__instance.TryGetComponent(out Character character) && Character_CustomFixedUpdate_Patch.IsOwnedGolem(character));
    }

    // ====================================================================== client

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    static class Player_OnSpawned_Patch
    {
        static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer)
                Protocol.SendHello();
        }
    }

    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    static class ZNet_OnDestroy_Patch
    {
        static void Prefix() => ClientPiece.Disable();
    }

    /// <summary>
    /// Placing the golem piece asks the server to spawn a golem instead of creating an object. Runs first,
    /// and matches by name, because build mods like Infinity Hammer swap in their own copy of the piece.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
    [HarmonyPriority(Priority.First)]
    static class Player_PlacePiece_Patch
    {
        static bool Prefix(Piece piece, Vector3 pos, Quaternion rot)
        {
            if (!ClientPiece.IsGolemPiece(piece))
                return true;
            Protocol.SendSpawn(pos, rot);
            piece.m_placeEffect.Create(pos, rot);
            return false;
        }
    }

    internal static class GolemInstances
    {
        public static bool TryGetGolem(GameObject go, out ZDO zdo)
        {
            zdo = null;
            var character = go != null ? go.GetComponentInParent<Character>() : null;
            var nview = character != null ? Character_CustomFixedUpdate_Patch.s_nview(character) : null;
            if (nview == null || !nview.IsValid() || !GolemData.IsGolem(nview.GetZDO()))
                return false;
            zdo = nview.GetZDO();
            return true;
        }
    }

    /// <summary>Hover text for modded players (vanilla players see only the name).</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
    static class Character_GetHoverText_Patch
    {
        static void Postfix(Character __instance, ref string __result)
        {
            if (!GolemInstances.TryGetGolem(__instance.gameObject, out var zdo))
                return;

            var sb = new StringBuilder();
            sb.Append(__instance.GetHoverName());
            sb.Append(GolemData.IsResting(zdo) ? " <color=#9aa0a6>(resting)</color>" : " <color=orange>(working)</color>");
            foreach (var item in GolemData.GetCarriedItems(zdo))
            {
                var drop = GolemBrain.FindItemDrop(item);
                sb.Append('\n').Append(drop != null ? drop.m_itemData.m_shared.m_name : item).Append(": ").Append(GolemData.GetCarry(zdo, item));
            }
            sb.Append("\n[<color=yellow><b>$KEY_Use</b></color>] ").Append(GolemData.IsResting(zdo) ? "Wake up" : "Rest");
            sb.Append("\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $hud_rename");
            sb.Append("\n[<color=yellow><b>$KEY_Use</b></color>] Hold: dismiss (returns build cost and fuel)");
            __result = Localization.instance.Localize(sb.ToString());
        }
    }

    /// <summary>
    /// A ghost isn't interactable in vanilla; give modded players E (rest), Shift+E (rename, the same
    /// dialog tamed animals use) and hold E (dismiss).
    /// </summary>
    [HarmonyPatch(typeof(Player), "Interact", typeof(GameObject), typeof(bool), typeof(bool))]
    static class Player_Interact_Patch
    {
        const float HoldSecondsToDismiss = 1f;

        static ZDOID s_holdTarget = ZDOID.None;
        static float s_holdStarted;

        static bool Prefix(Player __instance, GameObject go, bool hold, bool alt)
        {
            if (!GolemInstances.TryGetGolem(go, out var zdo))
                return true;

            if (hold)
            {
                if (s_holdTarget != zdo.m_uid)
                {
                    s_holdTarget = zdo.m_uid;
                    s_holdStarted = Time.time;
                }
                else if (Time.time - s_holdStarted >= HoldSecondsToDismiss)
                {
                    s_holdTarget = ZDOID.None;
                    Protocol.SendDismiss(zdo.m_uid);
                }
                return false;
            }

            s_holdTarget = ZDOID.None;
            if (alt)
                ClientGolem.RequestRename(zdo);
            else
                ZRoutedRpc.instance.InvokeRoutedRPC(zdo.GetOwner(), zdo.m_uid, "Command", __instance.GetZDOID(), true);
            return false;
        }
    }

    /// <summary>Golems are silenced as they spawn (for players running the mod).</summary>
    [HarmonyPatch(typeof(ZNetScene), "CreateObject")]
    static class ZNetScene_CreateObject_Patch
    {
        static void Postfix(ZDO zdo, GameObject __result)
        {
            if (__result != null && GolemData.IsGolem(zdo))
                ClientGolem.Silence(__result);
        }
    }
}
