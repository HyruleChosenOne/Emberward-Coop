using System;
using System.Collections.Generic;
using EmberwardCoop.Core;
using EmberwardCoop.Networking;
using UnityEngine;

namespace EmberwardCoop.Presence
{
    public sealed class GhostPreviewManager
    {
        private readonly NetworkManager _net;

        private sealed class GhostEntry
        {
            public int ItemType;
            public bool IsTower;
            public GameObject Root;
            public readonly List<Material> GhostMaterials = new List<Material>();
            public Vector3 TargetPos;
            public Quaternion TargetRot;
            public bool IsValid;
        }

        private readonly Dictionary<int, GhostEntry> _ghosts = new Dictionary<int, GhostEntry>();
        private static Shader _cachedTransparentShader;

        public GhostPreviewManager(NetworkManager net)
        {
            _net = net;
        }

        public void UpdateGhost(int playerId, bool isPlacing, bool isTower, int itemType, Vector3 pos, Vector3 euler, bool isValid, Color playerColor)
        {
            if (!CoopConfig.EnableGhostPreviews.Value || !isPlacing || itemType <= 0)
            {
                HideGhost(playerId);
                return;
            }

            if (!_ghosts.TryGetValue(playerId, out var entry) || entry.Root == null || entry.ItemType != itemType || entry.IsTower != isTower)
            {
                DestroyGhost(playerId);
                entry = CreateGhost(playerId, isTower, (eItemType)itemType, pos, Quaternion.Euler(euler), playerColor);
                if (entry == null) return;
                _ghosts[playerId] = entry;
            }

            if (!entry.Root.activeSelf) entry.Root.SetActive(true);
            entry.TargetPos = pos;
            entry.TargetRot = Quaternion.Euler(euler);
            entry.IsValid = isValid;
        }

        public void TickVisuals(float dt, float now)
        {
            float pulse = 0.36f + 0.10f * Mathf.Sin(now * 5.0f);
            float tPos = Mathf.Clamp01(dt * 18f);
            float tRot = Mathf.Clamp01(dt * 20f);

            foreach (var kv in _ghosts)
            {
                var entry = kv.Value;
                if (entry?.Root == null || !entry.Root.activeSelf) continue;

                entry.Root.transform.position = Vector3.Lerp(entry.Root.transform.position, entry.TargetPos, tPos);
                entry.Root.transform.rotation = Quaternion.Slerp(entry.Root.transform.rotation, entry.TargetRot, tRot);

                Color baseCol = CoopConfig.GetPlayerColor(_net.GetPlayerColorIndex(kv.Key));
                if (!entry.IsValid)
                    baseCol = Color.Lerp(baseCol, new Color(1f, 0.22f, 0.22f, 1f), 0.65f);
                baseCol.a = pulse;

                for (int i = 0; i < entry.GhostMaterials.Count; i++)
                {
                    var mat = entry.GhostMaterials[i];
                    if (mat == null) continue;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseCol);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseCol);
                }
            }
        }

        public void HideGhost(int playerId)
        {
            if (_ghosts.TryGetValue(playerId, out var entry) && entry?.Root != null)
                entry.Root.SetActive(false);
        }

        public void DestroyGhost(int playerId)
        {
            if (_ghosts.TryGetValue(playerId, out var entry))
            {
                if (entry != null)
                {
                    for (int i = 0; i < entry.GhostMaterials.Count; i++)
                        if (entry.GhostMaterials[i] != null) UnityEngine.Object.Destroy(entry.GhostMaterials[i]);
                    if (entry.Root != null) UnityEngine.Object.Destroy(entry.Root);
                }
                _ghosts.Remove(playerId);
            }
        }

        public void ClearAll()
        {
            var ids = new List<int>(_ghosts.Keys);
            for (int i = 0; i < ids.Count; i++) DestroyGhost(ids[i]);
            _ghosts.Clear();
        }

        private GhostEntry CreateGhost(int playerId, bool isTower, eItemType itemType, Vector3 pos, Quaternion rot, Color playerColor)
        {
            try
            {
                var rm = ResourceManager.Instance;
                if (rm == null) return null;

                GameObject prefab = null;
                if (isTower)
                    prefab = rm.GetTowerDataByType(itemType)?.GetPrefab();
                else
                    prefab = rm.GetPanelDataByType(itemType)?.GetPrefab();

                GameObject clone;
                if (prefab != null)
                {
                    // Instantiate under an inactive parent so Awake/OnEnable don't register the clone with game managers.
                    var tempHolder = new GameObject($"EWCoop_GhostHost_{playerId}");
                    tempHolder.SetActive(false);
                    clone = UnityEngine.Object.Instantiate(prefab, pos, rot, tempHolder.transform);
                    clone.name = $"EWCoop_Ghost_{playerId}_{itemType}";
                    StripGameplayComponents(clone);
                    clone.transform.SetParent(null, true);
                    UnityEngine.Object.Destroy(tempHolder);
                }
                else
                {
                    clone = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    clone.name = $"EWCoop_GhostFallback_{playerId}";
                    clone.transform.position = pos + new Vector3(0f, 0.5f, 0f);
                    clone.transform.rotation = rot;
                    clone.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
                    var col = clone.GetComponent<Collider>();
                    if (col != null) UnityEngine.Object.DestroyImmediate(col);
                }

                clone.layer = 2; // Ignore Raycast
                var entry = new GhostEntry
                {
                    ItemType = (int)itemType,
                    IsTower = isTower,
                    Root = clone,
                    TargetPos = pos,
                    TargetRot = rot,
                    IsValid = true
                };

                ApplyGhostMaterials(clone, playerColor, entry.GhostMaterials);
                clone.SetActive(true);
                return entry;
            }
            catch (Exception ex)
            {
                CoopLogger.Debug(LogCategory.Presence, $"CreateGhost({itemType}) fallback: {ex.Message}");
                return null;
            }
        }

        private static void StripGameplayComponents(GameObject root)
        {
            var mbs = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < mbs.Length; i++)
                if (mbs[i] != null) UnityEngine.Object.DestroyImmediate(mbs[i]);

            var cols = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                if (cols[i] != null) UnityEngine.Object.DestroyImmediate(cols[i]);

            var anims = root.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < anims.Length; i++)
                if (anims[i] != null) UnityEngine.Object.DestroyImmediate(anims[i]);

            var parts = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != null) UnityEngine.Object.DestroyImmediate(parts[i]);

            var auds = root.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < auds.Length; i++)
                if (auds[i] != null) UnityEngine.Object.DestroyImmediate(auds[i]);

            var rbs = root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rbs.Length; i++)
                if (rbs[i] != null) UnityEngine.Object.DestroyImmediate(rbs[i]);

            var trs = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < trs.Length; i++)
                if (trs[i] != null) trs[i].gameObject.layer = 2;
        }

        private static Shader ResolveTransparentShader()
        {
            if (_cachedTransparentShader != null) return _cachedTransparentShader;
            _cachedTransparentShader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Standard");
            return _cachedTransparentShader;
        }

        private static void ApplyGhostMaterials(GameObject root, Color tint, List<Material> created)
        {
            var shader = ResolveTransparentShader();
            if (shader == null) return;

            var ghostMat = new Material(shader);
            Color c = new Color(tint.r, tint.g, tint.b, 0.40f);
            if (ghostMat.HasProperty("_BaseColor")) ghostMat.SetColor("_BaseColor", c);
            if (ghostMat.HasProperty("_Color")) ghostMat.SetColor("_Color", c);
            if (ghostMat.HasProperty("_Surface")) ghostMat.SetFloat("_Surface", 1f);
            if (ghostMat.HasProperty("_Blend")) ghostMat.SetFloat("_Blend", 0f);
            if (ghostMat.HasProperty("_SrcBlend")) ghostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (ghostMat.HasProperty("_DstBlend")) ghostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (ghostMat.HasProperty("_ZWrite")) ghostMat.SetInt("_ZWrite", 0);
            ghostMat.renderQueue = 3100;
            created.Add(ghostMat);

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                var shared = r.sharedMaterials;
                if (shared != null && shared.Length > 1)
                {
                    var mats = new Material[shared.Length];
                    for (int m = 0; m < mats.Length; m++) mats[m] = ghostMat;
                    r.sharedMaterials = mats;
                }
                else
                {
                    r.sharedMaterial = ghostMat;
                }
            }
        }
    }
}
