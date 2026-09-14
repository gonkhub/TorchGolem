using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace TorchGolemMod
{
    [BepInPlugin(Guid, ModName, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "jtboyd.torchgolem";
        public const string ModName = "Torch Golem";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<float> WorkRadius;
        internal static ConfigEntry<float> RefuelBelowPercent;
        internal static ConfigEntry<float> RestockBelowPercent;
        internal static ConfigEntry<int> CarryLimit;
        internal static ConfigEntry<string> FuelItems;
        internal static ConfigEntry<string> ExcludedFuel;
        internal static ConfigEntry<string> ExcludedPieces;
        internal static ConfigEntry<float> MoveSpeed;
        internal static ConfigEntry<float> ScanInterval;
        internal static ConfigEntry<float> InteractRange;
        internal static ConfigEntry<bool> Wander;
        internal static ConfigEntry<float> WanderRadius;
        internal static ConfigEntry<bool> TeleportWhenStuck;
        internal static ConfigEntry<float> StuckSeconds;

        internal static ConfigEntry<string> Recipe;
        internal static ConfigEntry<string> CraftingStation;
        internal static ConfigEntry<string> IconItem;

        internal static ConfigEntry<string> VisualPrefab;
        internal static ConfigEntry<float> VisualScale;
        internal static ConfigEntry<bool> Glow;
        internal static ConfigEntry<float> AnimSpeedMultiplier;

        internal static HashSet<string> ParseList(string value) =>
            new HashSet<string>(value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);

        void Awake()
        {
            Log = Logger;

            WorkRadius = Config.Bind("Behaviour", "WorkRadius", 50f, "How far (meters) from where the golem was built it will look for fires and containers.");
            RefuelBelowPercent = Config.Bind("Behaviour", "RefuelBelowPercent", 0.5f, "A fire gets topped up once its fuel drops to this fraction of max (0.5 = half empty).");
            RestockBelowPercent = Config.Bind("Behaviour", "RestockBelowPercent", 0.25f, "When idle, the golem restocks a fuel type from containers once it carries less than this fraction of its carry limit.");
            CarryLimit = Config.Bind("Behaviour", "CarryLimit", 0, "Max of each fuel type carried. 0 = one full stack of that item.");
            FuelItems = Config.Bind("Fuel", "FuelItems", "Auto", "Fuel the golem may take from containers. 'Auto' = every fuel used by a buildable torch/fire/brazier in the game (including modded ones, listed in the log at world load). Otherwise a comma-separated list of item prefab names, e.g. Wood,Resin,GreydwarfEye,Guck.");
            ExcludedFuel = Config.Bind("Fuel", "ExcludedFuel", "", "Item prefab names never used as fuel, even in Auto mode. e.g. Coal if you want to keep it for smelting.");
            ExcludedPieces = Config.Bind("Fuel", "ExcludedPieces", "", "Piece prefab names the golem must never refuel, e.g. piece_bathtub. Furnaces, kilns, refineries and other production stations are always excluded.");
            MoveSpeed = Config.Bind("Behaviour", "MoveSpeed", 2.2f, "Walking speed in m/s.");
            ScanInterval = Config.Bind("Behaviour", "ScanInterval", 3f, "Seconds between looking for work while idle.");
            InteractRange = Config.Bind("Behaviour", "InteractRange", 1.8f, "Horizontal distance at which the golem can reach a fire or container.");
            Wander = Config.Bind("Behaviour", "Wander", true, "Wander around home when there's nothing to do.");
            WanderRadius = Config.Bind("Behaviour", "WanderRadius", 6f, "How far the golem wanders from home while idle.");
            TeleportWhenStuck = Config.Bind("Behaviour", "TeleportWhenStuck", true, "If the golem can't path to a target, pop it next to the target instead of giving up.");
            StuckSeconds = Config.Bind("Behaviour", "StuckSeconds", 4f, "Seconds without progress before the golem counts as stuck.");

            Recipe = Config.Bind("Building", "Recipe", "SurtlingCore:2,GreydwarfEye:5,Ectoplasm:1", "Build cost as PrefabName:Amount pairs. Applied when a world loads.");
            CraftingStation = Config.Bind("Building", "CraftingStation", "forge", "Prefab name of the crafting station the golem must be built near (e.g. piece_workbench, forge). Empty = none. Applied when a world loads.");
            IconItem = Config.Bind("Building", "IconItem", "TrophyGreydwarf", "Item prefab whose icon is used in the hammer menu.");

            VisualPrefab = Config.Bind("Visual", "VisualPrefab", "Greyling", "Creature prefab whose model the golem borrows. Requires restart.");
            VisualScale = Config.Bind("Visual", "VisualScale", 0.85f, "Model scale. Requires restart.");
            Glow = Config.Bind("Visual", "Glow", true, "Give the golem a warm point light. Requires restart.");
            AnimSpeedMultiplier = Config.Bind("Visual", "AnimSpeedMultiplier", 1f, "Scales the value fed to the walk animation, if the legs look like they're skating.");

            FuelItems.SettingChanged += (_, __) => FuelRegistry.Invalidate();
            ExcludedFuel.SettingChanged += (_, __) => FuelRegistry.Invalidate();
            ExcludedPieces.SettingChanged += (_, __) => FuelRegistry.Invalidate();

            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{ModName} {Version} loaded");
        }
    }
}
