using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TorchGolemMod
{
    /// <summary>Builds the golem prefab at runtime from vanilla parts and registers it with ZNetScene and the Hammer.</summary>
    internal static class GolemPrefab
    {
        public const string PrefabName = "TorchGolem";

        static GameObject s_holder;
        static GameObject s_prefab;

        static readonly AccessTools.FieldRef<ZNetScene, List<GameObject>> s_prefabsRef =
            AccessTools.FieldRefAccess<ZNetScene, List<GameObject>>("m_prefabs");
        static readonly AccessTools.FieldRef<ZNetScene, Dictionary<int, GameObject>> s_namedPrefabsRef =
            AccessTools.FieldRefAccess<ZNetScene, Dictionary<int, GameObject>>("m_namedPrefabs");

        public static List<GameObject> GetScenePrefabs(ZNetScene scene) => s_prefabsRef(scene);

        /// <summary>Re-applies recipe/station/icon settings, e.g. after the server's config arrives.</summary>
        public static void RefreshPiece()
        {
            if (s_prefab == null || ZNetScene.instance == null) return;
            try { ConfigurePiece(ZNetScene.instance); }
            catch (Exception e) { Plugin.Log.LogError($"Failed to refresh Torch Golem piece: {e}"); }
        }

        public static void Register(ZNetScene scene)
        {
            try
            {
                if (s_prefab == null)
                    s_prefab = Build(scene);

                var prefabs = s_prefabsRef(scene);
                if (!prefabs.Contains(s_prefab))
                    prefabs.Add(s_prefab);
                s_namedPrefabsRef(scene)[PrefabName.GetStableHashCode()] = s_prefab;

                ConfigurePiece(scene);
                AddToHammer(scene);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Failed to register Torch Golem: {e}");
            }
        }

        static GameObject Build(ZNetScene scene)
        {
            // Children of an inactive holder don't run Awake, so the prefab stays inert until ZNetScene instantiates it.
            s_holder = new GameObject("TorchGolem_PrefabHolder");
            s_holder.SetActive(false);
            Object.DontDestroyOnLoad(s_holder);

            var go = new GameObject(PrefabName);
            go.transform.SetParent(s_holder.transform, false);
            go.layer = LayerMask.NameToLayer("piece_nonsolid");

            var nview = go.AddComponent<ZNetView>();
            nview.m_persistent = true;
            nview.m_type = ZDO.ObjectType.Default;

            var sync = go.AddComponent<ZSyncTransform>();
            sync.m_syncPosition = true;
            sync.m_syncRotation = true;

            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.6f, 0f);
            col.height = 1.2f;
            col.radius = 0.35f;

            var piece = go.AddComponent<Piece>();
            piece.m_name = "Torch Golem";
            piece.m_description = "A little friend who keeps your torches, sconces and hearths fed from nearby chests.";
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_canBeRemoved = true;
            piece.m_randomInitBuildRotation = true;

            go.AddComponent<TorchGolem>();

            AttachVisual(scene, go);

            if (Plugin.Glow.Value)
            {
                var glow = new GameObject("Glow");
                glow.transform.SetParent(go.transform, false);
                glow.transform.localPosition = new Vector3(0f, 1.1f, 0f);
                var light = glow.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.62f, 0.28f);
                light.range = 4.5f;
                light.intensity = 1.1f;
                light.shadows = LightShadows.None;
            }

            return go;
        }

        static void AttachVisual(ZNetScene scene, GameObject root)
        {
            var source = scene.GetPrefab(Plugin.VisualPrefab.Value);
            Transform visual = null;
            if (source != null)
            {
                visual = source.transform.Find("Visual");
                if (visual == null)
                {
                    var smr = source.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if (smr != null) visual = smr.transform.parent;
                }
            }

            if (visual == null)
            {
                Plugin.Log.LogWarning($"Couldn't find a model on '{Plugin.VisualPrefab.Value}', using a placeholder capsule.");
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.DestroyImmediate(capsule.GetComponent<Collider>());
                capsule.transform.SetParent(root.transform, false);
                capsule.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                capsule.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                return;
            }

            var copy = Object.Instantiate(visual.gameObject, root.transform, false);
            copy.name = "Visual";
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = visual.localScale * Plugin.VisualScale.Value;

            // Strip creature gameplay components (anim events, footsteps, ragdoll) so only the model + animator remain.
            foreach (var c in copy.GetComponentsInChildren<MonoBehaviour>(true)) SafeDestroy(c);
            foreach (var c in copy.GetComponentsInChildren<Joint>(true)) SafeDestroy(c);
            foreach (var c in copy.GetComponentsInChildren<Rigidbody>(true)) SafeDestroy(c);
            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) SafeDestroy(c);

            var animator = copy.GetComponentInChildren<Animator>(true);
            if (animator != null)
                animator.applyRootMotion = false;
        }

        static void SafeDestroy(Object o)
        {
            try { Object.DestroyImmediate(o); }
            catch (Exception e) { Plugin.Log.LogDebug($"Couldn't strip {o}: {e.Message}"); }
        }

        static void ConfigurePiece(ZNetScene scene)
        {
            var piece = s_prefab.GetComponent<Piece>();

            // Borrow the workbench's build sound/dust regardless of which station is required.
            var workbench = scene.GetPrefab("piece_workbench");
            if (workbench != null)
                piece.m_placeEffect = workbench.GetComponent<Piece>().m_placeEffect;

            piece.m_craftingStation = null;
            string stationName = Plugin.CraftingStation.Value.Trim();
            if (stationName.Length > 0)
            {
                var station = scene.GetPrefab(stationName);
                if (station != null && station.TryGetComponent(out CraftingStation craftingStation))
                    piece.m_craftingStation = craftingStation;
                else
                    Plugin.Log.LogWarning($"Crafting station '{stationName}' not found; the golem can be built anywhere.");
            }

            var iconItem = scene.GetPrefab(Plugin.IconItem.Value);
            if (iconItem != null && iconItem.TryGetComponent(out ItemDrop iconDrop))
                piece.m_icon = iconDrop.m_itemData.GetIcon();

            var reqs = new List<Piece.Requirement>();
            foreach (var entry in Plugin.Recipe.Value.Split(','))
            {
                var parts = entry.Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[1].Trim(), out int amount)) continue;
                var item = scene.GetPrefab(parts[0].Trim());
                if (item == null || !item.TryGetComponent(out ItemDrop drop))
                {
                    Plugin.Log.LogWarning($"Recipe item '{parts[0].Trim()}' not found, skipping.");
                    continue;
                }
                reqs.Add(new Piece.Requirement { m_resItem = drop, m_amount = amount, m_recover = true });
            }
            piece.m_resources = reqs.ToArray();
        }

        static void AddToHammer(ZNetScene scene)
        {
            var hammer = scene.GetPrefab("Hammer");
            if (hammer == null || !hammer.TryGetComponent(out ItemDrop drop)) return;
            var table = drop.m_itemData.m_shared.m_buildPieces;
            if (table != null && !table.m_pieces.Contains(s_prefab))
                table.m_pieces.Add(s_prefab);
        }
    }
}
