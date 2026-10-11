using System.Collections.Generic;

namespace TorchGolemMod
{
    /// <summary>
    /// What it costs to keep one fuel type's lights running: fuel items per hour, and how many fires that
    /// covers. Every fire contributes its own prefab's rate, so a short-lived standing torch and a
    /// long-lived wall sconce both add to resin, each at its own pace.
    /// </summary>
    internal struct FuelDemand
    {
        public float PerHour;
        public int Fires;
    }

    /// <summary>
    /// Everything a golem knows is stored on its ZDO (the network object), so it survives restarts and is
    /// readable by modded clients for hover text. Vanilla clients simply ignore these extra keys.
    /// </summary>
    internal static class GolemData
    {
        const string Marker = "TorchGolem_golem";
        const string HomeKey = "TorchGolem_home";
        const string RestingKey = "TorchGolem_resting";
        const string CreatorKey = "TorchGolem_creator";
        const string CarryPrefix = "TorchGolem_carry_";
        const string CarriedListKey = "TorchGolem_carried";
        const string BurnPrefix = "TorchGolem_burn_";
        const string FiresPrefix = "TorchGolem_fires_";
        const string BurningListKey = "TorchGolem_burning";

        // Animator parameters as ZSyncAnimation stores them for non-owners to read.
        public static readonly int ForwardSpeed = 438569 + ZSyncAnimation.GetHash("forward_speed");
        public static readonly int OnGround = 438569 + ZSyncAnimation.GetHash("onGround");

        public static bool IsGolem(ZDO zdo) => zdo != null && zdo.GetBool(Marker);

        public static void MarkAsGolem(ZDO zdo, UnityEngine.Vector3 home, long creator)
        {
            zdo.Set(Marker, true);
            zdo.Set(HomeKey, home);
            zdo.Set(CreatorKey, creator);
        }

        /// <summary>Copies everything a golem knows onto another ZDO (used when swapping its body).</summary>
        public static void CopyState(ZDO from, ZDO to)
        {
            MarkAsGolem(to, GetHome(from), GetCreator(from));
            SetResting(to, IsResting(from));
            foreach (var item in GetCarriedItems(from))
                AddCarry(to, item, GetCarry(from, item));

            var demand = new Dictionary<string, FuelDemand>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var item in GetBurningItems(from))
                demand[item] = GetDemand(from, item);
            SetDemand(to, demand);
        }

        public static UnityEngine.Vector3 GetHome(ZDO zdo) => zdo.GetVec3(HomeKey, zdo.GetPosition());

        public static long GetCreator(ZDO zdo) => zdo.GetLong(CreatorKey);

        public static bool IsResting(ZDO zdo) => zdo.GetBool(RestingKey);

        public static void SetResting(ZDO zdo, bool resting) => zdo.Set(RestingKey, resting);

        public static int GetCarry(ZDO zdo, string item) => zdo.GetInt(CarryPrefix + item);

        /// <summary>Item prefab names currently carried (ZDOs can't enumerate keys, so the names are listed).</summary>
        public static HashSet<string> GetCarriedItems(ZDO zdo)
        {
            var names = Plugin.ParseList(zdo.GetString(CarriedListKey, ""));
            names.RemoveWhere(n => GetCarry(zdo, n) <= 0);
            return names;
        }

        /// <summary>What one fuel type costs per hour with every fire in range running.</summary>
        public static FuelDemand GetDemand(ZDO zdo, string item) => new FuelDemand
        {
            PerHour = zdo.GetFloat(BurnPrefix + item),
            Fires = zdo.GetInt(FiresPrefix + item),
        };

        /// <summary>Fuel types some fire in range burns (ZDOs can't enumerate keys, so the names are listed).</summary>
        public static HashSet<string> GetBurningItems(ZDO zdo)
        {
            var names = Plugin.ParseList(zdo.GetString(BurningListKey, ""));
            names.RemoveWhere(n => zdo.GetFloat(BurnPrefix + n) <= 0f);
            return names;
        }

        /// <summary>
        /// Replaces the whole demand table at once: a fire going up or coming down changes what its fuel
        /// type costs, and can drop the fuel type entirely.
        /// </summary>
        public static void SetDemand(ZDO zdo, Dictionary<string, FuelDemand> demand)
        {
            var previous = Plugin.ParseList(zdo.GetString(BurningListKey, ""));
            foreach (var item in previous)
            {
                if (!demand.ContainsKey(item))
                    Write(zdo, item, default(FuelDemand));
            }
            foreach (var pair in demand)
                Write(zdo, pair.Key, pair.Value);
            if (!previous.SetEquals(demand.Keys))
                zdo.Set(BurningListKey, string.Join(",", demand.Keys));
        }

        /// <summary>Unchanged values are left alone: every write bumps the ZDO revision and resends it.</summary>
        static void Write(ZDO zdo, string item, FuelDemand demand)
        {
            if (!UnityEngine.Mathf.Approximately(zdo.GetFloat(BurnPrefix + item), demand.PerHour))
                zdo.Set(BurnPrefix + item, demand.PerHour);
            if (zdo.GetInt(FiresPrefix + item) != demand.Fires)
                zdo.Set(FiresPrefix + item, demand.Fires);
        }

        public static void AddCarry(ZDO zdo, string item, int delta)
        {
            int count = UnityEngine.Mathf.Max(0, GetCarry(zdo, item) + delta);
            zdo.Set(CarryPrefix + item, count);

            var carried = Plugin.ParseList(zdo.GetString(CarriedListKey, ""));
            bool changed = count > 0 ? carried.Add(item) : carried.Remove(item);
            if (changed)
                zdo.Set(CarriedListKey, string.Join(",", carried));
        }
    }
}
