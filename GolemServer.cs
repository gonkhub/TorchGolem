using System.Collections.Generic;
using UnityEngine;

namespace TorchGolemMod
{
    /// <summary>
    /// Runs on whichever process is the server (dedicated server, or the host of a local game). Tracks every
    /// golem in the world and handles requests from players.
    /// </summary>
    internal sealed class GolemServer : MonoBehaviour
    {
        public static GolemServer Instance { get; private set; }

        readonly Dictionary<ZDOID, GolemBrain> m_brains = new Dictionary<ZDOID, GolemBrain>();
        readonly List<ZDOID> m_dead = new List<ZDOID>();
        readonly List<ZDO> m_temp = new List<ZDO>();
        readonly List<ZDO> m_players = new List<ZDO>();
        string m_world;
        float m_refreshTimer;

        void Awake() => Instance = this;

        static bool IsServerRunning =>
            ZNet.instance != null && ZNet.instance.IsServer() && ZDOMan.instance != null &&
            ZNetScene.instance != null && ZRoutedRpc.instance != null && ObjectDB.instance != null;

        void Update()
        {
            if (!IsServerRunning)
            {
                if (m_world != null)
                    Shutdown();
                return;
            }
            if (m_world == null)
                Startup();

            float dt = Time.deltaTime;
            float now = Time.time;

            if ((m_refreshTimer -= dt) <= 0f)
            {
                m_refreshTimer = 10f;
                RefreshGolems();
            }
            if (m_brains.Count == 0)
                return;

            m_players.Clear();
            m_players.AddRange(ZNet.instance.GetAllCharacterZDOS());

            float wakeRadius = Plugin.WorkRadius.Value + 60f;
            m_dead.Clear();
            foreach (var brain in m_brains.Values)
            {
                if (!brain.Tick(dt, now, AnyPlayerWithin(brain.Home, wakeRadius)))
                    m_dead.Add(brain.Id);
            }
            foreach (var id in m_dead)
                m_brains.Remove(id);
        }

        void Startup()
        {
            m_world = ZNet.instance.GetWorldName();
            m_refreshTimer = 0f;
            Plugin.Log.LogInfo($"Torch Golem server active for world '{m_world}'");
        }

        void Shutdown()
        {
            m_brains.Clear();
            m_world = null;
        }

        // Bodies golems may have been built with under earlier settings or versions.
        static readonly string[] s_knownBodies = { "Ghost", "Wraith", "Skeleton_Friendly" };

        public void RequestRefresh() => m_refreshTimer = 0f;

        void RefreshGolems()
        {
            string body = Plugin.GolemPrefab.Value;
            var bodies = new HashSet<string>(s_knownBodies) { body };

            foreach (string prefab in bodies)
            {
                m_temp.Clear();
                int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, m_temp, ref index)) { }

                foreach (var zdo in m_temp)
                {
                    if (!GolemData.IsGolem(zdo))
                        continue;
                    if (prefab != body)
                        SwapBody(zdo);
                    else if (!m_brains.ContainsKey(zdo.m_uid))
                        m_brains[zdo.m_uid] = new GolemBrain(zdo);
                }
            }
        }

        /// <summary>
        /// A ZDO's prefab can't change under clients that already spawned it, so a golem whose body setting
        /// changed is recreated with the new body and its old one removed.
        /// </summary>
        void SwapBody(ZDO old)
        {
            var created = CreateGolemZdo(old.GetPosition(), old.GetRotation(), old.GetString(ZDOVars.s_tamedName, Plugin.GolemName.Value));
            if (created == null)
                return;
            GolemData.CopyState(old, created);

            m_brains.Remove(old.m_uid);
            old.SetOwner(ZDOMan.GetSessionID());
            ZDOMan.instance.DestroyZDO(old);
            m_brains[created.m_uid] = new GolemBrain(created);
            Plugin.Log.LogInfo($"Gave torch golem a new body ({Plugin.GolemPrefab.Value}): {old.m_uid} -> {created.m_uid}");
        }

        static ZDO CreateGolemZdo(Vector3 position, Quaternion rotation, string name)
        {
            var prefab = ZNetScene.instance.GetPrefab(Plugin.GolemPrefab.Value);
            if (prefab == null || !prefab.TryGetComponent(out ZNetView prefabView))
            {
                Plugin.Log.LogError($"Golem prefab '{Plugin.GolemPrefab.Value}' not found.");
                return null;
            }

            int hash = prefab.name.GetStableHashCode();
            var zdo = ZDOMan.instance.CreateNewZDO(position, hash);
            zdo.Persistent = prefabView.m_persistent;
            zdo.Type = prefabView.m_type;
            zdo.Distant = prefabView.m_distant;
            zdo.SetPrefab(hash);
            zdo.SetRotation(rotation);
            zdo.SetOwner(ZDOMan.GetSessionID());

            // Tamed creatures aren't targeted by players' tames, turrets or ballistae, even an undead ghost.
            zdo.Set(ZDOVars.s_tamed, true);
            zdo.Set(ZDOVars.s_tamedName, name);
            zdo.Set(ZDOVars.s_overrideHoverName, name);
            // Stops a host's local instance from equipping the creature's default weapons.
            zdo.Set(ZDOVars.s_addedDefaultItems, true);
            return zdo;
        }

        bool AnyPlayerWithin(Vector3 point, float radius)
        {
            foreach (var player in m_players)
            {
                if (Horizontal(player.GetPosition(), point) <= radius)
                    return true;
            }
            return false;
        }

        static float Horizontal(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ------------------------------------------------------------------ requests

        public void Spawn(long sender, Vector3 position, Quaternion rotation)
        {
            if (!TryGetRequester(sender, out long playerId, out _, out _))
                return;

            int max = Plugin.MaxGolemsPerPlayer.Value;
            if (max > 0 && CountOwnedBy(playerId) >= max)
            {
                Message(sender, $"You already have {max} torch golems.");
                DropRecipe(position);
                return;
            }

            var zdo = CreateGolemZdo(position + Vector3.up * Plugin.HoverHeight.Value, rotation, Plugin.GolemName.Value);
            if (zdo == null)
            {
                DropRecipe(position);
                return;
            }
            GolemData.MarkAsGolem(zdo, position, playerId);

            m_brains[zdo.m_uid] = new GolemBrain(zdo);
            Plugin.Log.LogInfo($"Spawned torch golem {zdo.m_uid} for player {playerId} at {position}");
        }

        public void Dismiss(long sender, ZDOID id)
        {
            var zdo = ZDOMan.instance.GetZDO(id);
            if (!GolemData.IsGolem(zdo) || !TryGetRequester(sender, out long playerId, out Vector3 requesterPos, out bool isAdmin))
                return;

            long creator = GolemData.GetCreator(zdo);
            if (creator != 0L && creator != playerId && !isAdmin)
            {
                Message(sender, "That torch golem isn't yours.");
                return;
            }
            if (Horizontal(requesterPos, zdo.GetPosition()) > 10f)
                return;

            Vector3 pos = zdo.GetPosition();
            foreach (var item in GolemData.GetCarriedItems(zdo))
                DropItem(item, GolemData.GetCarry(zdo, item), pos);
            DropRecipe(pos);

            zdo.SetOwner(ZDOMan.GetSessionID());
            ZDOMan.instance.DestroyZDO(zdo);
            m_brains.Remove(id);
        }

        /// <summary>Vanilla "Command" (a player pressing E on a tamed creature) toggles rest.</summary>
        public void ToggleRest(ZDO zdo, long sender)
        {
            bool resting = !GolemData.IsResting(zdo);
            GolemData.SetResting(zdo, resting);
            Message(sender, resting ? $"{Plugin.GolemName.Value} is resting" : $"{Plugin.GolemName.Value} is back to work");
        }

        int CountOwnedBy(long playerId)
        {
            int count = 0;
            foreach (var brain in m_brains.Values)
            {
                if (brain.Creator == playerId)
                    count++;
            }
            return count;
        }

        static bool TryGetRequester(long sender, out long playerId, out Vector3 position, out bool isAdmin)
        {
            playerId = 0L;
            position = Vector3.zero;
            isAdmin = false;

            if (sender == ZDOMan.GetSessionID())
            {
                var local = Player.m_localPlayer;
                if (local == null)
                    return false;
                playerId = local.GetPlayerID();
                position = local.transform.position;
                isAdmin = true;
                return true;
            }

            var peer = ZNet.instance.GetPeer(sender);
            var character = peer != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
            if (character == null)
                return false;
            playerId = character.GetLong(ZDOVars.s_playerID);
            position = character.GetPosition();
            isAdmin = peer.m_socket != null && ZNet.instance.IsAdmin(peer.m_socket.GetHostName());
            return true;
        }

        static void Message(long peer, string text) =>
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, "ShowMessage", (int)MessageHud.MessageType.Center, text);

        static void DropRecipe(Vector3 position)
        {
            foreach (var entry in Plugin.Recipe.Value.Split(','))
            {
                var parts = entry.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int amount))
                    DropItem(parts[0].Trim(), amount, position);
            }
        }

        /// <summary>Creates item drops purely as ZDOs; nearby clients spawn and simulate them.</summary>
        static void DropItem(string prefabName, int amount, Vector3 position)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null || amount <= 0 || !prefab.TryGetComponent(out ItemDrop drop) || !prefab.TryGetComponent(out ZNetView view))
                return;

            int hash = prefab.name.GetStableHashCode();
            int maxStack = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
            while (amount > 0)
            {
                int stack = Mathf.Min(amount, maxStack);
                amount -= stack;

                Vector3 pos = position + Vector3.up * 0.75f + Random.insideUnitSphere * 0.4f;
                var zdo = ZDOMan.instance.CreateNewZDO(pos, hash);
                zdo.Persistent = view.m_persistent;
                zdo.Type = view.m_type;
                zdo.Distant = view.m_distant;
                zdo.SetPrefab(hash);
                zdo.SetRotation(Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

                var data = drop.m_itemData.Clone();
                data.m_stack = stack;
                data.m_dropPrefab = prefab;
                ItemDrop.SaveToZDO(data, zdo);
                // Unowned: the server hands it to a nearby player, who simulates it falling.
                zdo.SetOwner(0L);
            }
        }
    }
}
