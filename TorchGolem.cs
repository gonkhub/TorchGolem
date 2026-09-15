using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TorchGolemMod
{
    /// <summary>
    /// Brain of the golem. All decisions and movement run only on the ZDO owner; everyone else just
    /// sees the synced transform (ZSyncTransform) and animates from the observed movement.
    /// Carried fuel lives in the ZDO so it survives reloads and ownership handoffs.
    /// </summary>
    public class TorchGolem : MonoBehaviour, Hoverable, Interactable
    {
        const string ZdoHome = "TorchGolem_home";
        const string ZdoHasHome = "TorchGolem_hasHome";
        const string ZdoResting = "TorchGolem_resting";
        const string ZdoCarryPrefix = "TorchGolem_carry_";
        const string ZdoCarriedList = "TorchGolem_carried";
        const string RpcToggleRest = "TorchGolem_ToggleRest";

        enum Task { Idle, Refuel, Fetch, Deposit, Wander }

        static readonly List<Piece> s_pieces = new List<Piece>();
        static readonly List<Fireplace> s_fires = new List<Fireplace>();
        static readonly List<Container> s_chests = new List<Container>();
        static readonly HashSet<string> s_neededFuel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly int s_forwardSpeed = Animator.StringToHash("forward_speed");
        static readonly int s_onGround = Animator.StringToHash("onGround");
        static int s_groundMask;

        // Vanilla private/public chest rules, applied as if the golem's builder were opening it.
        static readonly Func<Container, long, bool> s_containerCheckAccess =
            AccessTools.MethodDelegate<Func<Container, long, bool>>(AccessTools.Method(typeof(Container), "CheckAccess"));

        ZNetView m_nview;
        Piece m_piece;
        Animator m_animator;
        bool m_hasForwardSpeed;

        Task m_task = Task.Idle;
        Fireplace m_targetFire;
        Container m_targetChest;
        string m_fetchItem;
        Vector3 m_targetPos;

        readonly List<Vector3> m_path = new List<Vector3>();
        int m_pathIndex;
        float m_repathTimer;
        float m_bestDist;
        float m_noProgressTimer;
        int m_stuckCount;

        float m_scanTimer;
        float m_wanderCooldown;
        Vector3 m_lastPos;
        float m_animSpeed;

        void Awake()
        {
            m_nview = GetComponent<ZNetView>();
            m_piece = GetComponent<Piece>();
            m_animator = GetComponentInChildren<Animator>();
            m_lastPos = transform.position;

            if (m_animator != null)
            {
                foreach (var p in m_animator.parameters)
                {
                    if (p.nameHash == s_forwardSpeed && p.type == AnimatorControllerParameterType.Float) m_hasForwardSpeed = true;
                    if (p.nameHash == s_onGround && p.type == AnimatorControllerParameterType.Bool) m_animator.SetBool(s_onGround, true);
                }
            }

            // Placement ghost: no ZDO, nothing to do.
            if (m_nview == null || m_nview.GetZDO() == null)
            {
                enabled = false;
                return;
            }

            if (s_groundMask == 0)
                s_groundMask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "vehicle");

            m_nview.Register(RpcToggleRest, RPC_ToggleRest);

            var zdo = m_nview.GetZDO();
            if (m_nview.IsOwner() && !zdo.GetBool(ZdoHasHome))
            {
                zdo.Set(ZdoHome, transform.position);
                zdo.Set(ZdoHasHome, true);
            }

            m_scanTimer = Random.Range(0.5f, 2f);
        }

        void Update()
        {
            if (!m_nview.IsValid() || !m_nview.IsOwner() || IsResting())
            {
                ClearTask();
                return;
            }

            float dt = Time.deltaTime;
            m_wanderCooldown -= dt;

            if (m_task == Task.Idle)
            {
                m_scanTimer -= dt;
                if (m_scanTimer <= 0f)
                {
                    m_scanTimer = Plugin.ScanInterval.Value;
                    ChooseTask();
                }
                return;
            }

            if (!TargetStillValid())
            {
                ClearTask();
                return;
            }

            if (MoveTowardsTarget(dt))
                Arrive();
        }

        void LateUpdate()
        {
            if (m_animator == null || !m_hasForwardSpeed) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 delta = transform.position - m_lastPos;
            delta.y = 0f;
            m_lastPos = transform.position;
            // Clamp so ZSyncTransform catch-up snaps on remote clients don't make it sprint.
            float speed = Mathf.Min(delta.magnitude / dt, Plugin.MoveSpeed.Value * 2f);
            m_animSpeed = Mathf.Lerp(m_animSpeed, speed, dt * 8f);
            m_animator.SetFloat(s_forwardSpeed, m_animSpeed * Plugin.AnimSpeedMultiplier.Value);
        }

        // ---------------------------------------------------------------- decisions

        void ChooseTask()
        {
            GatherNearby();

            // 1. A hungry fire we already carry fuel for.
            Fireplace bestFire = null;
            float bestDist = float.MaxValue;
            foreach (var fire in s_fires)
            {
                if (!NeedsFuel(fire) || GetCarry(FuelPrefab(fire)) <= 0) continue;
                float d = Vector3.Distance(transform.position, fire.transform.position);
                if (d < bestDist) { bestDist = d; bestFire = fire; }
            }
            if (bestFire != null)
            {
                m_task = Task.Refuel;
                m_targetFire = bestFire;
                StartMove(bestFire.transform.position);
                return;
            }

            // 2. A hungry fire whose fuel we don't have: go get some.
            foreach (var fire in s_fires)
            {
                if (!NeedsFuel(fire)) continue;
                if (TryStartFetch(fire.m_fuelItem)) return;
            }

            // 3. Put back fuel nothing in range burns anymore (torch removed, swapped for another kind...).
            s_neededFuel.Clear();
            foreach (var fire in s_fires)
                s_neededFuel.Add(FuelPrefab(fire));
            foreach (var carried in GetCarriedItems())
            {
                if (!s_neededFuel.Contains(carried) && TryStartDeposit(carried)) return;
            }

            // 4. Nothing urgent: top up low supplies, but only of fuel types some fire in range actually uses.
            foreach (var fire in s_fires)
            {
                var item = fire.m_fuelItem;
                if (GetCarry(item.gameObject.name) >= GetLimit(item) * Plugin.RestockBelowPercent.Value) continue;
                if (TryStartFetch(item)) return;
            }

            // 5. Potter about.
            if (Plugin.Wander.Value && m_wanderCooldown <= 0f)
            {
                m_wanderCooldown = Random.Range(5f, 12f);
                Vector2 offset = Random.insideUnitCircle * Plugin.WanderRadius.Value;
                m_task = Task.Wander;
                StartMove(GetHome() + new Vector3(offset.x, 0f, offset.y));
            }
        }

        void GatherNearby()
        {
            s_pieces.Clear();
            s_fires.Clear();
            s_chests.Clear();
            Piece.GetAllPiecesInRadius(GetHome(), Plugin.WorkRadius.Value, s_pieces);

            foreach (var piece in s_pieces)
            {
                if (piece == null) continue;
                if (piece.TryGetComponent(out Fireplace fire) && IsServiceable(fire)) s_fires.Add(fire);
                if (piece.TryGetComponent(out Container chest) && CanUseContainer(chest)) s_chests.Add(chest);
            }
        }

        bool TryStartFetch(ItemDrop fuel)
        {
            string sharedName = fuel.m_itemData.m_shared.m_name;
            if (GetCarry(fuel.gameObject.name) >= GetLimit(fuel)) return false;

            Container best = null;
            float bestDist = float.MaxValue;
            foreach (var chest in s_chests)
            {
                if (chest.GetInventory().CountItems(sharedName, -1, false) <= 0) continue;
                float d = Vector3.Distance(transform.position, chest.transform.position);
                if (d < bestDist) { bestDist = d; best = chest; }
            }
            if (best == null) return false;

            m_task = Task.Fetch;
            m_targetChest = best;
            m_fetchItem = fuel.gameObject.name;
            StartMove(best.transform.position);
            return true;
        }

        bool TryStartDeposit(string prefab)
        {
            var drop = FindItemDrop(prefab);
            if (drop == null || GetCarry(prefab) <= 0) return false;
            string sharedName = drop.m_itemData.m_shared.m_name;

            // Prefer a chest that already holds this item, so fuel ends up back where it came from.
            Container best = null;
            float bestScore = float.MaxValue;
            foreach (var chest in s_chests)
            {
                var inventory = chest.GetInventory();
                if (!inventory.CanAddItem(drop.gameObject, 1)) continue;
                float score = Vector3.Distance(transform.position, chest.transform.position);
                if (inventory.CountItems(sharedName, -1, false) <= 0) score += 1000f;
                if (score < bestScore) { bestScore = score; best = chest; }
            }
            if (best == null) return false;

            m_task = Task.Deposit;
            m_targetChest = best;
            m_fetchItem = prefab;
            StartMove(best.transform.position);
            return true;
        }

        void Arrive()
        {
            switch (m_task)
            {
                case Task.Refuel: DoRefuel(m_targetFire); break;
                case Task.Fetch: DoFetch(m_targetChest); break;
                case Task.Deposit: DoDeposit(m_targetChest); break;
            }
            ClearTask();
            // Look for the next job almost immediately after finishing one.
            m_scanTimer = 0.4f;
        }

        void DoRefuel(Fireplace fire)
        {
            if (fire == null || !IsServiceable(fire)) return;
            string prefab = FuelPrefab(fire);
            int missing = Mathf.FloorToInt(fire.m_maxFuel - GetFuel(fire));
            int amount = Mathf.Min(missing, GetCarry(prefab));
            if (amount <= 0) return;

            fire.AddFuel(amount);
            AddCarry(prefab, -amount);
        }

        void DoFetch(Container chest)
        {
            if (chest == null || !CanUseContainer(chest)) return;

            var drop = FindItemDrop(m_fetchItem);
            if (drop == null) return;

            var inventory = chest.GetInventory();
            string sharedName = drop.m_itemData.m_shared.m_name;
            int amount = Mathf.Min(inventory.CountItems(sharedName, -1, false), GetLimit(drop) - GetCarry(m_fetchItem));
            if (amount <= 0) return;

            // Container only saves its inventory to the ZDO when we own it.
            var chestView = chest.GetComponent<ZNetView>();
            if (!chestView.IsOwner())
                chestView.ClaimOwnership();

            inventory.RemoveItem(sharedName, amount, -1, false);
            AddCarry(m_fetchItem, amount);
        }

        void DoDeposit(Container chest)
        {
            if (chest == null || !CanUseContainer(chest)) return;

            var drop = FindItemDrop(m_fetchItem);
            if (drop == null) return;

            var chestView = chest.GetComponent<ZNetView>();
            if (!chestView.IsOwner())
                chestView.ClaimOwnership();

            var inventory = chest.GetInventory();
            int maxStack = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
            int remaining = GetCarry(m_fetchItem);
            while (remaining > 0)
            {
                int chunk = Mathf.Min(remaining, maxStack);
                while (chunk > 1 && !inventory.CanAddItem(drop.gameObject, chunk))
                    chunk /= 2;
                if (!inventory.CanAddItem(drop.gameObject, chunk) || !inventory.AddItem(drop.gameObject, chunk))
                    break;
                remaining -= chunk;
            }
            // Whatever didn't fit stays carried; it'll try another chest on a later scan.
            AddCarry(m_fetchItem, remaining - GetCarry(m_fetchItem));
        }

        // ---------------------------------------------------------------- rules

        static bool IsServiceable(Fireplace fire)
        {
            if (fire.m_fuelItem == null || fire.m_infiniteFuel || !fire.m_canRefill) return false;
            if (!FuelRegistry.Fuel.Contains(fire.m_fuelItem.gameObject.name)) return false;
            if (!IsPlayerBuilt(fire)) return false;
            var view = fire.GetComponent<ZNetView>();
            return view != null && view.IsValid() && FuelRegistry.IsLightSource(fire.gameObject, Utils.GetPrefabName(fire.gameObject));
        }

        // Dungeon braziers, village chests etc. have no creator; the golem only works on things players built.
        static bool IsPlayerBuilt(Component c)
        {
            var piece = c.GetComponent<Piece>();
            return piece != null && piece.GetCreator() != 0L;
        }

        static bool NeedsFuel(Fireplace fire)
        {
            float fuel = GetFuel(fire);
            return fuel <= fire.m_maxFuel * Plugin.RefuelBelowPercent.Value && fire.m_maxFuel - fuel >= 1f;
        }

        static float GetFuel(Fireplace fire) => fire.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_fuel, 0f);

        static string FuelPrefab(Fireplace fire) => fire.m_fuelItem.gameObject.name;

        bool CanUseContainer(Container chest)
        {
            var view = chest.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || chest.GetInventory() == null) return false;
            if (!IsPlayerBuilt(chest)) return false;
            if (chest.GetComponent<Incinerator>() != null) return false;
            if (chest.IsInUse()) return false;
            if (!s_containerCheckAccess(chest, m_piece.GetCreator())) return false;

            // IsInUse is only reliable on the chest's owner. If someone else owns it and is standing
            // right there, they may have it open, so leave it alone to avoid duplicating items.
            if (!view.IsOwner())
            {
                foreach (var player in Player.GetAllPlayers())
                {
                    if (player != Player.m_localPlayer &&
                        Vector3.Distance(player.transform.position, chest.transform.position) < 5f)
                        return false;
                }
            }
            return true;
        }

        bool TargetStillValid()
        {
            switch (m_task)
            {
                case Task.Refuel: return m_targetFire != null && IsServiceable(m_targetFire);
                case Task.Fetch:
                case Task.Deposit: return m_targetChest != null;
                default: return true;
            }
        }

        // ---------------------------------------------------------------- movement

        void StartMove(Vector3 target)
        {
            m_targetPos = target;
            m_path.Clear();
            m_pathIndex = 0;
            m_repathTimer = 0f;
            m_bestDist = HorizontalDistance(transform.position, target);
            m_noProgressTimer = 0f;
            m_stuckCount = 0;
        }

        /// <returns>true once within reach of the target.</returns>
        bool MoveTowardsTarget(float dt)
        {
            Vector3 pos = transform.position;
            float reach = m_task == Task.Wander ? 0.6f : Plugin.InteractRange.Value;
            float horizontal = HorizontalDistance(pos, m_targetPos);
            if (horizontal < reach && Mathf.Abs(pos.y - m_targetPos.y) < 3f)
                return true;

            m_repathTimer -= dt;
            if (m_repathTimer <= 0f)
            {
                m_repathTimer = 3f;
                RecalculatePath(pos);
            }

            while (m_pathIndex < m_path.Count && HorizontalDistance(pos, m_path[m_pathIndex]) < 0.3f)
                m_pathIndex++;

            // Wall torches and sconces sit off the navmesh; the end of the path is as close as feet can get.
            if (m_path.Count > 0 && m_pathIndex >= m_path.Count && horizontal < reach + 1.5f && Mathf.Abs(pos.y - m_targetPos.y) < 4f)
                return true;

            Vector3 waypoint = m_pathIndex < m_path.Count ? m_path[m_pathIndex] : m_targetPos;
            Vector3 dir = waypoint - pos;
            dir.y = 0f;
            float dist = dir.magnitude;
            if (dist > 0.01f)
            {
                dir /= dist;
                Vector3 next = pos + dir * Mathf.Min(dist, Plugin.MoveSpeed.Value * dt);
                next.y = GroundHeight(next, Mathf.Max(pos.y, waypoint.y), waypoint.y);
                transform.position = next;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 540f * dt);
            }

            TrackProgress(dt);
            return false;
        }

        void TrackProgress(float dt)
        {
            float d = HorizontalDistance(transform.position, m_targetPos);
            if (d < m_bestDist - 0.5f)
            {
                m_bestDist = d;
                m_noProgressTimer = 0f;
                return;
            }

            m_noProgressTimer += dt;
            if (m_noProgressTimer < Plugin.StuckSeconds.Value) return;

            m_noProgressTimer = 0f;
            m_stuckCount++;
            m_repathTimer = 0f;
            if (m_stuckCount < 2) return;

            if (m_task == Task.Wander || !Plugin.TeleportWhenStuck.Value)
            {
                ClearTask();
                return;
            }

            Vector3 away = transform.position - m_targetPos;
            away.y = 0f;
            Vector3 spot = m_targetPos + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 1f;
            spot.y = GroundHeight(spot, m_targetPos.y + 1f, m_targetPos.y);
            transform.position = spot;
            m_bestDist = HorizontalDistance(spot, m_targetPos);
        }

        void RecalculatePath(Vector3 from)
        {
            m_path.Clear();
            m_pathIndex = 0;
            var pathfinding = Pathfinding.instance;
            if (pathfinding == null) return;
            // Navmesh tiles build asynchronously; a failed query just means walk straight for now.
            if (!pathfinding.GetPath(from, m_targetPos, m_path, Pathfinding.AgentType.HumanoidNoSwim, false, true, false))
                m_path.Clear();
        }

        static float GroundHeight(Vector3 at, float fromY, float fallbackY)
        {
            var origin = new Vector3(at.x, fromY + 1.2f, at.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 3.5f, s_groundMask, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return Mathf.MoveTowards(at.y, fallbackY, 0.1f);
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        void ClearTask()
        {
            m_task = Task.Idle;
            m_targetFire = null;
            m_targetChest = null;
            m_fetchItem = null;
            m_path.Clear();
        }

        // ---------------------------------------------------------------- state

        Vector3 GetHome() => m_nview.GetZDO().GetVec3(ZdoHome, transform.position);

        bool IsResting() => m_nview.GetZDO().GetBool(ZdoResting);

        int GetCarry(string prefab) => m_nview.GetZDO().GetInt(ZdoCarryPrefix + prefab);

        void AddCarry(string prefab, int delta)
        {
            var zdo = m_nview.GetZDO();
            int count = Mathf.Max(0, zdo.GetInt(ZdoCarryPrefix + prefab) + delta);
            zdo.Set(ZdoCarryPrefix + prefab, count);

            var carried = GetCarriedItems();
            bool changed = count > 0 ? carried.Add(prefab) : carried.Remove(prefab);
            if (changed)
                zdo.Set(ZdoCarriedList, string.Join(",", carried));
        }

        /// <summary>
        /// Item types currently carried. The ZDO can't enumerate keys, so the names are kept in a list;
        /// the fuel registry is merged in for golems saved before the list existed.
        /// </summary>
        HashSet<string> GetCarriedItems()
        {
            var zdo = m_nview.GetZDO();
            var names = Plugin.ParseList(zdo.GetString(ZdoCarriedList, ""));
            names.UnionWith(FuelRegistry.Fuel);
            names.RemoveWhere(n => zdo.GetInt(ZdoCarryPrefix + n) <= 0);
            return names;
        }

        static int GetLimit(ItemDrop item) =>
            Plugin.CarryLimit.Value > 0 ? Plugin.CarryLimit.Value : item.m_itemData.m_shared.m_maxStackSize;

        static ItemDrop FindItemDrop(string prefab)
        {
            var go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            return go != null ? go.GetComponent<ItemDrop>() : null;
        }

        void RPC_ToggleRest(long sender)
        {
            if (!m_nview.IsOwner()) return;
            m_nview.GetZDO().Set(ZdoResting, !IsResting());
        }

        /// <summary>Spills carried fuel on the ground. Called when the golem is deconstructed.</summary>
        public void DropCarried()
        {
            if (m_nview == null || !m_nview.IsValid()) return;
            var zdo = m_nview.GetZDO();

            foreach (var prefab in GetCarriedItems())
            {
                int count = zdo.GetInt(ZdoCarryPrefix + prefab);
                var drop = FindItemDrop(prefab);
                if (count <= 0 || drop == null) continue;

                int stackSize = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
                while (count > 0)
                {
                    int stack = Mathf.Min(count, stackSize);
                    var spawned = Instantiate(drop.gameObject, transform.position + Vector3.up * 0.5f + Random.insideUnitSphere * 0.3f, Quaternion.identity);
                    spawned.GetComponent<ItemDrop>().SetStack(stack);
                    count -= stack;
                }
                zdo.Set(ZdoCarryPrefix + prefab, 0);
            }
        }

        // ---------------------------------------------------------------- hover / interact

        public string GetHoverName() => m_piece != null ? m_piece.m_name : "Torch Golem";

        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            if (m_nview == null || !m_nview.IsValid()) return "";

            var sb = new StringBuilder();
            sb.Append(GetHoverName());
            sb.Append(IsResting() ? " <color=#9aa0a6>(resting)</color>" : " <color=orange>(working)</color>");

            foreach (var prefab in GetCarriedItems())
            {
                int count = GetCarry(prefab);
                if (count <= 0) continue;
                var drop = FindItemDrop(prefab);
                sb.Append('\n').Append(drop != null ? drop.m_itemData.m_shared.m_name : prefab).Append(": ").Append(count);
            }

            sb.Append("\n[<color=yellow><b>$KEY_Use</b></color>] ").Append(IsResting() ? "Wake up" : "Rest");
            return Localization.instance.Localize(sb.ToString());
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            m_nview.InvokeRPC(RpcToggleRest);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
