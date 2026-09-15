using System.Collections.Generic;
using UnityEngine;

namespace TorchGolemMod
{
    /// <summary>
    /// Server-side behaviour for one golem. There is no GameObject on a dedicated server: the golem is
    /// "puppeted" by writing position, rotation, velocity and animation values to its ZDO, which every
    /// client (modded or not) renders normally.
    ///
    /// The golem is a spirit: it flies in a straight line to wherever it needs to be, through walls, and
    /// floats back to the spot it was built at whenever it has nothing to do.
    /// </summary>
    internal sealed class GolemBrain
    {
        enum Task { Idle, Refuel, Fetch, Deposit }

        struct FireTarget
        {
            public ZDO Zdo;
            public FireInfo Info;
        }

        struct ChestTarget
        {
            public ZDO Zdo;
            public ChestInfo Info;
        }

        sealed class PendingRefuel
        {
            public ZDOID Fire;
            public string Item;
            public int Amount;
            public float ExpectedFuel;
            public float Deadline;
        }

        sealed class ChestOp
        {
            public ZDOID Chest;
            public string Item;
            public bool Withdraw;
            public float ReadyAt;
        }

        const float OwnershipSettleSeconds = 1.5f;
        const float RefuelConfirmSeconds = 6f;
        const float UnresponsiveCooldown = 30f;
        const float ArriveDistance = 0.3f;

        static readonly List<ZDO> s_nearby = new List<ZDO>();
        static readonly List<FireTarget> s_fires = new List<FireTarget>();
        static readonly List<ChestTarget> s_chests = new List<ChestTarget>();

        public readonly ZDOID Id;
        ZDO m_zdo;

        public Vector3 Home { get; private set; }
        public long Creator { get; private set; }

        // Simulated body
        Vector3 m_pos;
        Quaternion m_rot;
        Vector3 m_velocity;
        float m_speed;
        Vector3 m_sentPos;
        Quaternion m_sentRot;
        Vector3 m_sentVelocity;
        float m_sentSpeed = -1f;
        float m_netTimer;

        // Current job
        Task m_task = Task.Idle;
        ZDOID m_target;
        string m_item;
        Vector3 m_destination;
        float m_taskTimer;

        float m_scanTimer;
        PendingRefuel m_pendingRefuel;
        ChestOp m_chestOp;
        readonly Dictionary<ZDOID, float> m_cooldownUntil = new Dictionary<ZDOID, float>();

        public GolemBrain(ZDO zdo)
        {
            Id = zdo.m_uid;
            m_zdo = zdo;
            m_pos = m_sentPos = zdo.GetPosition();
            m_rot = m_sentRot = zdo.GetRotation();
            Home = GolemData.GetHome(zdo);
            Creator = GolemData.GetCreator(zdo);
            m_scanTimer = Random.Range(1f, 3f);
        }

        Vector3 HomePoint => Home + Vector3.up * Plugin.HoverHeight.Value;

        /// <returns>false once the golem no longer exists.</returns>
        public bool Tick(float dt, float now, bool playersNearby)
        {
            m_zdo = ZDOMan.instance.GetZDO(Id);
            if (m_zdo == null || !GolemData.IsGolem(m_zdo))
                return false;

            long session = ZDOMan.GetSessionID();
            if (m_zdo.GetOwner() != session)
                m_zdo.SetOwner(session);

            // Nobody around to see it, and fires/chests nobody is near aren't simulated anyway.
            if (!playersNearby)
                return true;

            UpdatePendingRefuel(now);

            if (m_chestOp != null)
            {
                if (now >= m_chestOp.ReadyAt)
                    FinishChestOp();
                Hover();
            }
            else if (GolemData.IsResting(m_zdo))
            {
                ClearTask();
                FlyTowards(HomePoint, dt);
            }
            else if (m_task == Task.Idle)
            {
                m_scanTimer -= dt;
                if (m_scanTimer <= 0f)
                {
                    m_scanTimer = Plugin.ScanInterval.Value;
                    ChooseTask();
                }
                if (m_task == Task.Idle)
                    FlyTowards(HomePoint, dt);
            }
            else
            {
                m_taskTimer += dt;
                if (m_taskTimer > 60f || ZDOMan.instance.GetZDO(m_target) == null)
                    ClearTask();
                else if (FlyTowards(m_destination, dt))
                    Arrive(now);
            }

            Apply(dt);
            return true;
        }

        // ------------------------------------------------------------------ decisions

        void ChooseTask()
        {
            Gather();

            // 1. A hungry fire we already carry fuel for.
            FireTarget? bestFire = null;
            float bestDist = float.MaxValue;
            foreach (var fire in s_fires)
            {
                if (!NeedsFuel(fire) || GolemData.GetCarry(m_zdo, fire.Info.FuelItem) <= 0)
                    continue;
                float d = Vector3.Distance(m_pos, fire.Zdo.GetPosition());
                if (d < bestDist) { bestDist = d; bestFire = fire; }
            }
            if (bestFire.HasValue)
            {
                Begin(Task.Refuel, bestFire.Value.Zdo, bestFire.Value.Info.FuelItem);
                return;
            }

            // 2. A hungry fire whose fuel we don't have: go get some.
            foreach (var fire in s_fires)
            {
                if (NeedsFuel(fire) && TryBeginFetch(fire.Info.FuelItem))
                    return;
            }

            // 3. Put back fuel that nothing in range burns anymore.
            var needed = new HashSet<string>();
            foreach (var fire in s_fires)
                needed.Add(fire.Info.FuelItem);
            foreach (var item in GolemData.GetCarriedItems(m_zdo))
            {
                if (!needed.Contains(item) && TryBeginDeposit(item))
                    return;
            }

            // 4. Top up low supplies, only for fuel types some fire in range uses.
            foreach (var item in needed)
            {
                if (GolemData.GetCarry(m_zdo, item) < GetLimit(item) * Plugin.RestockBelowPercent.Value && TryBeginFetch(item))
                    return;
            }
        }

        void Gather()
        {
            s_nearby.Clear();
            s_fires.Clear();
            s_chests.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(Home), new SimulationDistance(1, 0, true), s_nearby);

            float now = Time.time;
            float radius = Plugin.WorkRadius.Value;
            foreach (var zdo in s_nearby)
            {
                if (Horizontal(zdo.GetPosition(), Home) > radius)
                    continue;
                // Only things players built: no dungeon braziers or village chests.
                long creator = zdo.GetLong(ZDOVars.s_creator);
                if (creator == 0L)
                    continue;

                int prefab = zdo.GetPrefab();
                if (FuelRegistry.TryGetFire(prefab, out var fire))
                {
                    if (m_cooldownUntil.TryGetValue(zdo.m_uid, out float until) && now < until)
                        continue;
                    if (m_pendingRefuel != null && m_pendingRefuel.Fire == zdo.m_uid)
                        continue;
                    s_fires.Add(new FireTarget { Zdo = zdo, Info = fire });
                }
                else if (FuelRegistry.TryGetChest(prefab, out var chest))
                {
                    if (chest.IsPrivate && creator != Creator)
                        continue;
                    s_chests.Add(new ChestTarget { Zdo = zdo, Info = chest });
                }
            }
        }

        static bool NeedsFuel(FireTarget fire)
        {
            // A fire with no owner isn't loaded by any player, so it isn't burning and can't receive fuel.
            if (!fire.Zdo.HasOwner())
                return false;
            float fuel = fire.Zdo.GetFloat(ZDOVars.s_fuel);
            return fuel <= fire.Info.MaxFuel * Plugin.RefuelBelowPercent.Value && fire.Info.MaxFuel - fuel >= 1f;
        }

        bool TryBeginFetch(string item)
        {
            if (GolemData.GetCarry(m_zdo, item) >= GetLimit(item))
                return false;
            var drop = FindItemDrop(item);
            if (drop == null)
                return false;

            ZDO best = null;
            float bestDist = float.MaxValue;
            foreach (var chest in s_chests)
            {
                if (chest.Zdo.GetInt(ZDOVars.s_inUse) == 1)
                    continue;
                if (ReadInventory(chest.Zdo, chest.Info).CountItems(drop.m_itemData.m_shared.m_name, -1, false) <= 0)
                    continue;
                float d = Vector3.Distance(m_pos, chest.Zdo.GetPosition());
                if (d < bestDist) { bestDist = d; best = chest.Zdo; }
            }
            if (best == null)
                return false;

            Begin(Task.Fetch, best, item);
            return true;
        }

        bool TryBeginDeposit(string item)
        {
            var drop = FindItemDrop(item);
            if (drop == null)
                return false;

            // Prefer a chest that already holds this item, so fuel goes back where it came from.
            ZDO best = null;
            float bestScore = float.MaxValue;
            foreach (var chest in s_chests)
            {
                if (chest.Zdo.GetInt(ZDOVars.s_inUse) == 1)
                    continue;
                var inventory = ReadInventory(chest.Zdo, chest.Info);
                if (!inventory.CanAddItem(drop.gameObject, 1))
                    continue;
                float score = Vector3.Distance(m_pos, chest.Zdo.GetPosition());
                if (inventory.CountItems(drop.m_itemData.m_shared.m_name, -1, false) <= 0)
                    score += 1000f;
                if (score < bestScore) { bestScore = score; best = chest.Zdo; }
            }
            if (best == null)
                return false;

            Begin(Task.Deposit, best, item);
            return true;
        }

        // ------------------------------------------------------------------ acting

        void Begin(Task task, ZDO target, string item)
        {
            m_task = task;
            m_target = target.m_uid;
            m_item = item;
            m_taskTimer = 0f;

            // Float just in front of and above the target, on the side we're approaching from.
            Vector3 targetPos = target.GetPosition();
            Vector3 away = m_pos - targetPos;
            away.y = 0f;
            Vector3 side = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            m_destination = targetPos + side * Plugin.InteractRange.Value + Vector3.up * Plugin.HoverHeight.Value;
        }

        void Arrive(float now)
        {
            var target = ZDOMan.instance.GetZDO(m_target);
            var task = m_task;
            string item = m_item;
            ClearTask();
            m_scanTimer = 0.5f;
            if (target == null)
                return;

            switch (task)
            {
                case Task.Refuel:
                    Refuel(target, now);
                    break;
                case Task.Fetch:
                case Task.Deposit:
                    BeginChestOp(target, item, task == Task.Fetch, now);
                    break;
            }
        }

        void Refuel(ZDO fire, float now)
        {
            if (!FuelRegistry.TryGetFire(fire.GetPrefab(), out var info) || !fire.HasOwner())
                return;
            float fuel = fire.GetFloat(ZDOVars.s_fuel);
            int amount = Mathf.Min(GolemData.GetCarry(m_zdo, info.FuelItem), Mathf.FloorToInt(info.MaxFuel - fuel));
            if (amount <= 0)
                return;

            // The vanilla RPC a player's refuel sends. It reaches every client; only the fire's owner applies it.
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, fire.m_uid, "RPC_AddFuelAmount", (float)amount);

            // Only spend carried fuel once the fire's fuel visibly rises, so a lost RPC never eats items.
            m_pendingRefuel = new PendingRefuel
            {
                Fire = fire.m_uid,
                Item = info.FuelItem,
                Amount = amount,
                ExpectedFuel = Mathf.Min(fuel + amount, info.MaxFuel) - 0.5f,
                Deadline = now + RefuelConfirmSeconds,
            };
        }

        void UpdatePendingRefuel(float now)
        {
            var pending = m_pendingRefuel;
            if (pending == null)
                return;
            var fire = ZDOMan.instance.GetZDO(pending.Fire);
            if (fire != null && fire.GetFloat(ZDOVars.s_fuel) >= pending.ExpectedFuel)
            {
                GolemData.AddCarry(m_zdo, pending.Item, -pending.Amount);
                m_pendingRefuel = null;
            }
            else if (now > pending.Deadline)
            {
                Plugin.Log.LogDebug($"Fire {pending.Fire} didn't take fuel; trying elsewhere for a while.");
                m_cooldownUntil[pending.Fire] = now + UnresponsiveCooldown;
                m_pendingRefuel = null;
            }
        }

        void BeginChestOp(ZDO chest, string item, bool withdraw, float now)
        {
            if (chest.GetInt(ZDOVars.s_inUse) == 1)
                return;

            // Only the owner's writes to a chest stick. Take ownership, then give the previous owner time to
            // hear about it (and flush anything in flight) before rewriting the contents.
            float settle = 0.25f;
            if (chest.GetOwner() != ZDOMan.GetSessionID())
            {
                chest.SetOwner(ZDOMan.GetSessionID());
                settle = OwnershipSettleSeconds;
            }
            m_chestOp = new ChestOp { Chest = chest.m_uid, Item = item, Withdraw = withdraw, ReadyAt = now + settle };
        }

        void FinishChestOp()
        {
            var op = m_chestOp;
            m_chestOp = null;
            var chest = ZDOMan.instance.GetZDO(op.Chest);
            if (chest == null || !FuelRegistry.TryGetChest(chest.GetPrefab(), out var info))
                return;

            try
            {
                var drop = FindItemDrop(op.Item);
                if (drop == null || chest.GetInt(ZDOVars.s_inUse) == 1 || chest.GetOwner() != ZDOMan.GetSessionID())
                    return;

                var inventory = ReadInventory(chest, info);
                string sharedName = drop.m_itemData.m_shared.m_name;
                bool changed = false;

                if (op.Withdraw)
                {
                    int amount = Mathf.Min(inventory.CountItems(sharedName, -1, false), GetLimit(op.Item) - GolemData.GetCarry(m_zdo, op.Item));
                    if (amount > 0)
                    {
                        inventory.RemoveItem(sharedName, amount, -1, false);
                        GolemData.AddCarry(m_zdo, op.Item, amount);
                        changed = true;
                    }
                }
                else
                {
                    int maxStack = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
                    int remaining = GolemData.GetCarry(m_zdo, op.Item);
                    int deposited = 0;
                    while (remaining > 0)
                    {
                        int chunk = Mathf.Min(remaining, maxStack);
                        while (chunk > 1 && !inventory.CanAddItem(drop.gameObject, chunk))
                            chunk /= 2;
                        if (!inventory.CanAddItem(drop.gameObject, chunk) || !inventory.AddItem(drop.gameObject, chunk))
                            break;
                        remaining -= chunk;
                        deposited += chunk;
                    }
                    if (deposited > 0)
                    {
                        GolemData.AddCarry(m_zdo, op.Item, -deposited);
                        changed = true;
                    }
                }

                if (changed)
                {
                    var pkg = new ZPackage();
                    inventory.Save(pkg);
                    chest.Set(ZDOVars.s_items, pkg.GetArray());
                }
            }
            finally
            {
                // Hand the chest back; the server reassigns it to a nearby player within a couple of seconds.
                if (chest.GetOwner() == ZDOMan.GetSessionID())
                    chest.SetOwner(0L);
            }
        }

        void ClearTask()
        {
            m_task = Task.Idle;
            m_target = ZDOID.None;
            m_item = null;
        }

        // ------------------------------------------------------------------ movement

        /// <summary>Straight-line flight, ignoring walls. Returns true once there.</summary>
        bool FlyTowards(Vector3 destination, float dt)
        {
            Vector3 delta = destination - m_pos;
            float distance = delta.magnitude;
            if (distance <= ArriveDistance)
            {
                Hover();
                return true;
            }

            float step = Plugin.MoveSpeed.Value * dt;
            Vector3 direction = delta / distance;
            m_pos = distance <= step ? destination : m_pos + direction * step;

            Vector3 flat = new Vector3(delta.x, 0f, delta.z);
            if (flat.sqrMagnitude > 0.0001f)
                m_rot = Quaternion.RotateTowards(m_rot, Quaternion.LookRotation(flat), 360f * dt);
            m_speed = Plugin.MoveSpeed.Value;
            m_velocity = direction * m_speed;
            return false;
        }

        void Hover()
        {
            m_speed = 0f;
            m_velocity = Vector3.zero;
        }

        /// <summary>Publish the simulated body.</summary>
        void Apply(float dt)
        {
            // Listen-server host / single player: the host may have a real instance. It owns it, so its
            // ZSyncTransform would overwrite ZDO writes; move the instance instead (AI and physics are
            // disabled for golems in Patches).
            var view = ZNetScene.instance.FindInstance(m_zdo);
            if (view != null && m_zdo.IsOwner())
            {
                view.transform.SetPositionAndRotation(m_pos, m_rot);
                if (view.TryGetComponent(out Rigidbody body))
                {
                    body.isKinematic = true;
                    body.position = m_pos;
                    body.rotation = m_rot;
                }
                if (view.TryGetComponent(out ZSyncAnimation anim))
                    anim.SetFloat("forward_speed", m_speed);
                return;
            }

            m_netTimer -= dt;
            if (m_netTimer > 0f)
                return;
            m_netTimer = 0.1f;

            // Only write what changed; every write bumps the ZDO revision and resends it.
            if ((m_pos - m_sentPos).sqrMagnitude > 0.0001f)
            {
                m_zdo.SetPosition(m_pos);
                m_sentPos = m_pos;
            }
            if (Quaternion.Angle(m_rot, m_sentRot) > 0.5f)
            {
                m_zdo.SetRotation(m_rot);
                m_sentRot = m_rot;
            }
            if ((m_velocity - m_sentVelocity).sqrMagnitude > 0.0001f)
            {
                m_zdo.Set(ZDOVars.s_velHash, m_velocity);
                m_sentVelocity = m_velocity;
            }
            if (!Mathf.Approximately(m_speed, m_sentSpeed))
            {
                m_zdo.Set(GolemData.ForwardSpeed, m_speed);
                m_sentSpeed = m_speed;
            }
        }

        // ------------------------------------------------------------------ helpers

        static float Horizontal(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static Inventory ReadInventory(ZDO chest, ChestInfo info)
        {
            var inventory = new Inventory(info.PrefabName, null, info.Width, info.Height);
            byte[] bytes = chest.GetByteArray(ZDOVars.s_items);
            if (bytes != null)
                inventory.Load(new ZPackage(bytes));
            return inventory;
        }

        internal static ItemDrop FindItemDrop(string prefab)
        {
            var go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            return go != null ? go.GetComponent<ItemDrop>() : null;
        }

        static int GetLimit(string item)
        {
            if (Plugin.CarryLimit.Value > 0)
                return Plugin.CarryLimit.Value;
            var drop = FindItemDrop(item);
            return drop != null ? drop.m_itemData.m_shared.m_maxStackSize : 50;
        }
    }
}
