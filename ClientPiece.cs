using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TorchGolemMod
{
    /// <summary>
    /// The hammer piece modded players use to build a golem. It only exists locally as a placement ghost:
    /// placing it asks the server to spawn the golem, so nothing custom ever goes over the network.
    /// Enabled once the server confirms it runs the mod.
    /// </summary>
    internal static class ClientPiece
    {
        static GameObject s_holder;
        static GameObject s_prefab;

        const string PlacerName = "TorchGolem_Placer";

        // By name: build mods (e.g. Infinity Hammer) may hand PlacePiece a copy rather than our prefab.
        public static bool IsGolemPiece(Piece piece) => piece != null && Utils.GetPrefabName(piece.gameObject) == PlacerName;

        public static void Enable(string recipe, string station)
        {
            var scene = ZNetScene.instance;
            if (scene == null)
                return;
            try
            {
                if (s_prefab == null)
                    s_prefab = Build(scene);
                Configure(scene, recipe, station);

                var table = GetHammerTable(scene);
                if (table != null && !table.m_pieces.Contains(s_prefab))
                    table.m_pieces.Add(s_prefab);
                Plugin.Log.LogInfo("Server runs Torch Golem: golem added to the hammer.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Failed to add Torch Golem piece: {e}");
            }
        }

        public static void Disable()
        {
            if (s_prefab == null || ZNetScene.instance == null)
                return;
            GetHammerTable(ZNetScene.instance)?.m_pieces.Remove(s_prefab);
        }

        static PieceTable GetHammerTable(ZNetScene scene)
        {
            var hammer = scene.GetPrefab("Hammer");
            return hammer != null && hammer.TryGetComponent(out ItemDrop drop) ? drop.m_itemData.m_shared.m_buildPieces : null;
        }

        static GameObject Build(ZNetScene scene)
        {
            s_holder = new GameObject("TorchGolem_PrefabHolder");
            s_holder.SetActive(false);
            Object.DontDestroyOnLoad(s_holder);

            var go = new GameObject(PlacerName);
            go.transform.SetParent(s_holder.transform, false);
            go.AddComponent<ZNetView>().m_persistent = false;

            var piece = go.AddComponent<Piece>();
            piece.m_name = Plugin.GolemName.Value;
            piece.m_description = "A friendly spirit that takes fuel from nearby chests and keeps your torches, sconces, braziers and hearths lit.";
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_randomInitBuildRotation = true;

            // Borrow the creature's model so the placement ghost looks like what you'll get.
            var creature = scene.GetPrefab(Plugin.GolemPrefab.Value);
            Transform visual = null;
            if (creature != null)
            {
                visual = creature.transform.Find("Visual");
                if (visual == null)
                {
                    var renderer = creature.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if (renderer != null)
                        visual = renderer.transform.parent;
                }
            }
            if (visual != null)
            {
                var copy = Object.Instantiate(visual.gameObject, go.transform, false);
                copy.name = "Visual";
                copy.transform.localPosition = Vector3.up * Plugin.HoverHeight.Value;
                copy.transform.localRotation = Quaternion.identity;
                foreach (var c in copy.GetComponentsInChildren<MonoBehaviour>(true)) SafeDestroy(c);
                foreach (var c in copy.GetComponentsInChildren<Joint>(true)) SafeDestroy(c);
                foreach (var c in copy.GetComponentsInChildren<Rigidbody>(true)) SafeDestroy(c);
                foreach (var c in copy.GetComponentsInChildren<Collider>(true)) SafeDestroy(c);
            }
            return go;
        }

        static void Configure(ZNetScene scene, string recipe, string station)
        {
            var piece = s_prefab.GetComponent<Piece>();

            var workbench = scene.GetPrefab("piece_workbench");
            if (workbench != null)
                piece.m_placeEffect = workbench.GetComponent<Piece>().m_placeEffect;

            piece.m_craftingStation = null;
            if (!string.IsNullOrWhiteSpace(station))
            {
                var stationPrefab = scene.GetPrefab(station.Trim());
                if (stationPrefab != null && stationPrefab.TryGetComponent(out CraftingStation craftingStation))
                    piece.m_craftingStation = craftingStation;
            }

            var iconItem = scene.GetPrefab(Plugin.IconItem.Value);
            if (iconItem != null && iconItem.TryGetComponent(out ItemDrop iconDrop))
                piece.m_icon = iconDrop.m_itemData.GetIcon();

            var reqs = new List<Piece.Requirement>();
            foreach (var entry in (recipe ?? "").Split(','))
            {
                var parts = entry.Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[1].Trim(), out int amount))
                    continue;
                var item = scene.GetPrefab(parts[0].Trim());
                if (item != null && item.TryGetComponent(out ItemDrop drop))
                    reqs.Add(new Piece.Requirement { m_resItem = drop, m_amount = amount, m_recover = true });
            }
            piece.m_resources = reqs.ToArray();
        }

        static void SafeDestroy(Object o)
        {
            try { Object.DestroyImmediate(o); }
            catch (Exception e) { Plugin.Log.LogDebug($"Couldn't strip {o}: {e.Message}"); }
        }
    }
}
