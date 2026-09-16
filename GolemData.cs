using System.Collections.Generic;

namespace TorchGolemMod
{
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
