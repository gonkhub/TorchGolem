using UnityEngine;

namespace TorchGolemMod
{
    /// <summary>
    /// Mod-to-mod messages. A vanilla server simply ignores them, so a modded client joining one never sees
    /// the hammer piece; a vanilla client never sends them, and doesn't need to.
    /// </summary>
    internal static class Protocol
    {
        public const int Version = 2;

        const string HelloRpc = "TorchGolem_Hello";
        const string WelcomeRpc = "TorchGolem_Welcome";
        const string SpawnRpc = "TorchGolem_Spawn";
        const string DismissRpc = "TorchGolem_Dismiss";

        public static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            rpc.Register<int>(HelloRpc, RPC_Hello);
            rpc.Register<int, string, string>(WelcomeRpc, RPC_Welcome);
            rpc.Register<Vector3, Quaternion>(SpawnRpc, RPC_Spawn);
            rpc.Register<ZDOID>(DismissRpc, RPC_Dismiss);
        }

        // ------------------------------------------------------------------ client -> server

        public static void SendHello() => ZRoutedRpc.instance.InvokeRoutedRPC(HelloRpc, Version);

        public static void SendSpawn(Vector3 position, Quaternion rotation) => ZRoutedRpc.instance.InvokeRoutedRPC(SpawnRpc, position, rotation);

        public static void SendDismiss(ZDOID golem) => ZRoutedRpc.instance.InvokeRoutedRPC(DismissRpc, golem);

        static void RPC_Hello(long sender, int version)
        {
            if (!ZNet.instance.IsServer())
                return;
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, WelcomeRpc, Version, Plugin.Recipe.Value, Plugin.CraftingStation.Value);
        }

        static void RPC_Spawn(long sender, Vector3 position, Quaternion rotation)
        {
            if (ZNet.instance.IsServer() && GolemServer.Instance != null)
                GolemServer.Instance.Spawn(sender, position, rotation);
        }

        static void RPC_Dismiss(long sender, ZDOID golem)
        {
            if (ZNet.instance.IsServer() && GolemServer.Instance != null)
                GolemServer.Instance.Dismiss(sender, golem);
        }

        // ------------------------------------------------------------------ server -> client

        static void RPC_Welcome(long sender, int version, string recipe, string station)
        {
            if (ZNet.instance.IsDedicated())
                return;
            if (version != Version)
            {
                Plugin.Log.LogWarning($"Server runs Torch Golem protocol {version}, this client runs {Version}; building golems is disabled.");
                return;
            }
            ClientPiece.Enable(recipe, station);
        }
    }
}
