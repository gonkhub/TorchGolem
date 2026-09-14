using HarmonyLib;

namespace TorchGolemMod
{
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    static class ZNetScene_Awake_Patch
    {
        static void Postfix(ZNetScene __instance) => GolemPrefab.Register(__instance);
    }

    // Deconstructing the golem with the hammer should give back whatever fuel it was holding.
    [HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
    static class Piece_DropResources_Patch
    {
        static void Prefix(Piece __instance)
        {
            var golem = __instance.GetComponent<TorchGolem>();
            if (golem != null)
                golem.DropCarried();
        }
    }
}
