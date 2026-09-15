using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace TorchGolemMod
{
    /// <summary>
    /// Torch Golem is server-authoritative. The server owns every golem (a vanilla ghost, so unmodded
    /// players see it normally) and runs all of its behaviour by writing to its network data.
    /// Players with the mod additionally get a hammer piece to build golems and can rest or dismiss them;
    /// players without it still see golems at work and benefit from lit torches.
    /// </summary>
    [BepInPlugin(Guid, ModName, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "gonkhub.torchgolem";
        public const string ModName = "Torch Golem";
        public const string Version = "2.0.0";

        internal static ManualLogSource Log;

        // Server: behaviour
        internal static ConfigEntry<float> WorkRadius;
        internal static ConfigEntry<float> RefuelBelowPercent;
        internal static ConfigEntry<float> RestockBelowPercent;
        internal static ConfigEntry<int> CarryLimit;
        internal static ConfigEntry<float> MoveSpeed;
        internal static ConfigEntry<float> ScanInterval;
        internal static ConfigEntry<float> InteractRange;
        internal static ConfigEntry<int> MaxGolemsPerPlayer;

        // Server: fuel
        internal static ConfigEntry<string> FuelItems;
        internal static ConfigEntry<string> ExcludedFuel;
        internal static ConfigEntry<string> ExcludedPieces;

        // Server: appearance (vanilla assets only, so unmodded players can see it)
        internal static ConfigEntry<string> GolemPrefab;
        internal static ConfigEntry<string> GolemName;
        internal static ConfigEntry<float> HoverHeight;

        // Server, sent to modded clients: building
        internal static ConfigEntry<string> Recipe;
        internal static ConfigEntry<string> CraftingStation;

        // Client
        internal static ConfigEntry<string> IconItem;

        internal static HashSet<string> ParseList(string value) =>
            new HashSet<string>((value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);

        void Awake()
        {
            Log = Logger;

            WorkRadius = Config.Bind("Behaviour", "WorkRadius", 50f, "How far (meters) from where a golem was built it looks for fires and chests.");
            RefuelBelowPercent = Config.Bind("Behaviour", "RefuelBelowPercent", 0.5f, "A fire gets topped up once its fuel drops to this fraction of max.");
            RestockBelowPercent = Config.Bind("Behaviour", "RestockBelowPercent", 0.25f, "When idle, restock a fuel type once carrying less than this fraction of the carry limit.");
            CarryLimit = Config.Bind("Behaviour", "CarryLimit", 0, "Max of each fuel type carried. 0 = one full stack of that item.");
            MoveSpeed = Config.Bind("Behaviour", "MoveSpeed", 3f, "Flying speed in m/s.");
            ScanInterval = Config.Bind("Behaviour", "ScanInterval", 3f, "Seconds between looking for work while idle.");
            InteractRange = Config.Bind("Behaviour", "InteractRange", 1.5f, "How far from a fire or chest the golem hovers while using it.");
            HoverHeight = Config.Bind("Behaviour", "HoverHeight", 1.2f, "How high above its build spot, fires and chests the golem floats.");
            MaxGolemsPerPlayer = Config.Bind("Behaviour", "MaxGolemsPerPlayer", 3, "How many golems one player may own. 0 = unlimited.");

            FuelItems = Config.Bind("Fuel", "FuelItems", "Auto", "Fuel golems may take from chests. 'Auto' = every fuel used by a buildable torch/fire/brazier (listed in the log at world load). Otherwise a comma-separated list of item prefab names.");
            ExcludedFuel = Config.Bind("Fuel", "ExcludedFuel", "", "Item prefab names never used as fuel, even in Auto mode, e.g. Coal.");
            ExcludedPieces = Config.Bind("Fuel", "ExcludedPieces", "", "Piece prefab names golems never refuel, e.g. piece_bathtub. Production stations are always excluded.");

            GolemPrefab = Config.Bind("Appearance", "GolemPrefab", "Ghost", "Vanilla creature used as the golem's body, e.g. Ghost or Wraith. Must be vanilla so players without the mod can see it. Existing golems switch to the new body automatically.");
            GolemName = Config.Bind("Appearance", "GolemName", "Torch Golem", "Name shown above the golem.");

            Recipe = Config.Bind("Building", "Recipe", "SurtlingCore:2,BoneFragments:30,Ectoplasm:5", "Build cost as PrefabName:Amount pairs. The server's value is sent to modded players when they join.");
            CraftingStation = Config.Bind("Building", "CraftingStation", "forge", "Crafting station the golem must be built near. Empty = none. The server's value is sent to modded players.");

            IconItem = Config.Bind("Client", "IconItem", "Ectoplasm", "Item whose icon is used for the golem in the hammer menu.");

            GolemPrefab.SettingChanged += (_, __) => GolemServer.Instance?.RequestRefresh();
            FuelItems.SettingChanged += (_, __) => FuelRegistry.Invalidate();
            ExcludedFuel.SettingChanged += (_, __) => FuelRegistry.Invalidate();
            ExcludedPieces.SettingChanged += (_, __) => FuelRegistry.Invalidate();

            gameObject.AddComponent<GolemServer>();
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{ModName} {Version} loaded");
        }
    }
}
