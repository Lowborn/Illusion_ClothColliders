﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using KKAPI;
using KKAPI.Chara;
using KKAPI.Utilities;
using Sideloader;
using Sideloader.AutoResolver;
using UnityEngine;
using KKAPI.Maker;
//using CharaCustom;
#if AI || HS2
using AIChara;
#endif

namespace ClothColliders
{
    [BepInDependency(KoikatuAPI.GUID)]
    [BepInDependency(Sideloader.Sideloader.GUID)]
    [BepInPlugin(GUID, PluginName, Version)]
    public class ClothCollidersPlugin : BaseUnityPlugin
    {
        public const string PluginName = "Cloth colliders support";
        public const string GUID = "ClothColliders";
        private const string ManifestGUID = "ClothColliders";
        public const string Version = "1.3";

#if AI || HS2
        private const ChaListDefine.CategoryNo FirstClothingCategoryNo = ChaListDefine.CategoryNo.fo_top;

#else
        private const ChaListDefine.CategoryNo FirstClothingCategoryNo = ChaListDefine.CategoryNo.co_top;
#endif

        internal static new ManualLogSource Logger;

        private void Start()
        {
            Logger = base.Logger;

            foreach (var manifest in Sideloader.Sideloader.Manifests.Values)
            {
                try
                {
                    LoadManifest(manifest);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to load collider data from {manifest.GUID} - {ex.Message}");
                }
            }

            Logger.LogInfo($"Found collider datas for {SphereColliders.Keys.Union(CapsuleColliders.Keys).Count()} items");

            if (SphereColliders.Count == 0 && CapsuleColliders.Count == 0)
            {
                enabled = false;
                return;
            }

            Harmony.CreateAndPatchAll(typeof(ClothCollidersPlugin));
            CharacterApi.RegisterExtraBehaviour<ClothColliderController>(GUID);
        }

        // todo the same but with capsules
        internal static readonly Dictionary<long, List<SphereColliderPair>> SphereColliders = new Dictionary<long, List<SphereColliderPair>>();
        internal static readonly Dictionary<long, List<CapsuleColliderData>> CapsuleColliders = new Dictionary<long, List<CapsuleColliderData>>();

        private void LoadManifest(Manifest manifest)
        {
            var manifestDocumentRoot = manifest.manifestDocument?.Root;
            if (manifestDocumentRoot == null) return;

            var clothManifestRoot = manifestDocumentRoot.Element(ManifestGUID) ?? manifestDocumentRoot.Element("AI_ClothColliders"); // Import from old tag
            if (clothManifestRoot == null) return;

            var clothElements = clothManifestRoot.Elements("cloth");

            Logger.LogDebug("Cloth Colliders 1.2 Lowborn's Edit");
           
            Logger.LogDebug($"Loading cloth collider data for {manifest.GUID}");
            foreach (var clothData in clothElements)
            {
                int category = (int)(ChaListDefine.CategoryNo)Enum.Parse(typeof(ChaListDefine.CategoryNo), clothData.Attribute("category")?.Value ?? throw new FormatException("Missing category attribute"));
                int clothPartId = (int)(category - FirstClothingCategoryNo);

                #if AI || HS2
                //check the cloth belongs to male or female category for AI & HS2, if its other game, it will not perform this check. Lowborn
                if (category < 200)
                {
                    //male clothes
                    clothPartId = (int)(category - FirstClothingCategoryNo + 100);
                }
                else if (category > 349)
                {
                    //acc
                    clothPartId = (int)(category - FirstClothingCategoryNo - 111);
                }
                
                else
                {
                    //female clothes
                    clothPartId = (int)(category - FirstClothingCategoryNo);
                }
                #endif
                var clothName = clothData.Attribute("clothName")?.Value ?? throw new FormatException("Missing clothName attribute");

                var itemId = int.Parse(clothData.Attribute("id")?.Value ?? throw new FormatException("Missing id attribute"));
                var resolvedItemId = UniversalAutoResolver.TryGetResolutionInfo(itemId, (ChaListDefine.CategoryNo)category, manifest.GUID);
                if (resolvedItemId != null)
                {
                    Logger.LogDebug($"Found resolved ID: {itemId} => {resolvedItemId.LocalSlot}");
                    itemId = resolvedItemId.LocalSlot;
                }
                else
                {
                    Logger.LogWarning($"Failed to resolve id={itemId} category={category} guid={manifest.GUID}");
                }

                var uniqueId = clothPartId + "-" + clothName + "_" + itemId;

                Logger.LogDebug("clothPartIdd "+clothPartId);

                var dictKey = GetDictKey(clothPartId, itemId);
                
                foreach (var colliderData in clothData.Elements())
                {
                    if (colliderData.Name == "SphereColliderPair")
                    {
                        var result = new SphereColliderPair(GetSphereColliderData(colliderData.Element("first"), uniqueId), GetSphereColliderData(colliderData.Element("second"), uniqueId), clothName);
                        var list = GetOrAddList(SphereColliders, dictKey);
                        list.Add(result);
                    }
                    else if (colliderData.Name == "CapsuleCollider")
                    {
                        var result = GetCapsuleColliderData(colliderData, uniqueId, clothName);
                        var list = GetOrAddList(CapsuleColliders, dictKey);
                        list.Add(result);
                    }
                    else
                    {
                        throw new FormatException("Unknown collider type " + colliderData.Name);
                    }
                }
            }
        }

        private List<T> GetOrAddList<T>(Dictionary<long, List<T>> dictionary, long key)
        {
            if (!dictionary.TryGetValue(key, out var existing))
            {
                existing = new List<T>();
                dictionary.Add(key, existing);
            }

            return existing;
        }

        internal static long GetDictKey(int clothPartId, int itemId)
        {
            return ((long)clothPartId << sizeof(int) * 8) | (uint)itemId;
        }

        private SphereColliderData GetSphereColliderData(XElement element, string uniqueId)
        {
            if (element == null) return null;

            var colliderData = new SphereColliderData(
                element.Attribute("boneName")?.Value ?? throw new FormatException("Missing boneName attribute"),
                float.Parse(element.Attribute("radius")?.Value ?? throw new FormatException("Missing radius attribute"), CultureInfo.InvariantCulture),
                ParseVector3(element.Attribute("center")?.Value ?? throw new FormatException("Missing center attribute")),
                uniqueId);
            Logger.LogDebug($"Added SphereCollider: boneName={colliderData.BoneName} radius={colliderData.ColliderRadius} center={colliderData.ColliderCenter}");
            return colliderData;
        }

        private CapsuleColliderData GetCapsuleColliderData(XElement element, string uniqueId, string clothName)
        {
            if (element == null) return null;

            var colliderData = new CapsuleColliderData(
                element.Attribute("boneName")?.Value ?? throw new FormatException("Missing boneName attribute"),
                float.Parse(element.Attribute("radius")?.Value ?? throw new FormatException("Missing radius attribute"), CultureInfo.InvariantCulture),
                float.Parse(element.Attribute("height")?.Value ?? throw new FormatException("Missing height attribute"), CultureInfo.InvariantCulture),
                ParseVector3(element.Attribute("center")?.Value ?? throw new FormatException("Missing center attribute")),
                int.Parse(element.Attribute("direction")?.Value ?? throw new FormatException("Missing direction attribute"), CultureInfo.InvariantCulture),
                uniqueId, clothName);
            Logger.LogDebug($"Added CapsuleCollider: boneName={colliderData.BoneName} radius={colliderData.ColliderRadius} height={colliderData.CollierHeight} center={colliderData.ColliderCenter} direction={colliderData.Direction}");
            return colliderData;
        }

        private Vector3 ParseVector3(string value)
        {
            var parts = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) throw new FormatException("Could not parse Vector3 from " + value);
            return new Vector3(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeCustomClothes))]
        [UsedImplicitly]
        private static void ChangeCustomClothes(ChaControl __instance, int kind)
        {
            if (__instance != null)
            {
                //Logger.LogMessage("Changing Clothes");
                var controller = __instance.GetComponent<ClothColliderController>();
                if (controller != null)
                {
                    //IEnumerator DelayedCo
                    __instance.StartCoroutine(CoroutineUtils.CreateCoroutine(new WaitForEndOfFrame(), () => controller.UpdateColliders(kind)));
                    //Logger.LogMessage("Clothes kind "+kind.ToString());
                }
            }
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeAccessoryParent))]
        [UsedImplicitly]
        private static void ChangeAccessoryParent(ChaControl __instance, int slotNo, string parentStr)
        {
            if (__instance != null)
            {               
                
                var controller = __instance.GetComponent<ClothColliderController>();
                if (controller != null && slotNo < 20)
                {
                    //IEnumerator DelayedCo
                    int kind = __instance.nowCoordinate.accessory.parts[slotNo].type;
                    ClothCollidersPlugin.Logger.LogDebug("kind " + kind);
                    __instance.StartCoroutine(CoroutineUtils.CreateCoroutine(new WaitForEndOfFrame(), () => controller.UpdateCollidersAcc(kind, slotNo)));
                    Logger.LogDebug("Acc kind " + slotNo.ToString());
                }
                else
                {
                    Logger.LogDebug("Acc Slot Out of Bounds, not Compatible with Cloth Colliders");
                    return; 
                }
            }
        }

    }
}