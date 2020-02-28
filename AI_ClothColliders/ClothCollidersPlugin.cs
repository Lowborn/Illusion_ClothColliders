﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using AIChara;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using KKAPI;
using KKAPI.Chara;
using Sideloader;
using Sideloader.AutoResolver;
using UnityEngine;

namespace AI_ClothColliders
{
    [BepInDependency(KoikatuAPI.GUID)]
    [BepInDependency(Sideloader.Sideloader.GUID)]
    [BepInPlugin(GUID, PluginName, Version)]
    public class ClothCollidersPlugin : BaseUnityPlugin
    {
        public const string PluginName = "AI_ClothColliders";
        public const string GUID = "AI_ClothColliders";
        public const string Version = "0.2";
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
                    Logger.LogError($"Failed to load collider data from {manifest.GUID} - {ex}");
                }
            }

            CharacterApi.RegisterExtraBehaviour<ClothColliderController>(GUID);
        }

        // todo the same but with capsules
        internal static readonly Dictionary<long, List<SphereColliderPair>> SphereColliders = new Dictionary<long, List<SphereColliderPair>>();
        internal static readonly Dictionary<long, List<CapsuleColliderData>> CapsuleColliders = new Dictionary<long, List<CapsuleColliderData>>();

        private void LoadManifest(Manifest manifest)
        {
            var clothElements = manifest.manifestDocument?.Root?.Element(GUID)?.Elements("cloth");
            if (clothElements == null) return;

            Logger.LogDebug($"Loading cloth collider data for {manifest.GUID}");
            foreach (var clothData in clothElements)
            {
                var category = (ChaListDefine.CategoryNo)Enum.Parse(typeof(ChaListDefine.CategoryNo), clothData.Attribute("category")?.Value ?? throw new FormatException("Missing category attribute"));
                var clothPartId = category - ChaListDefine.CategoryNo.fo_top;

                var itemId = int.Parse(clothData.Attribute("id")?.Value ?? throw new FormatException("Missing id attribute"));
                var resolvedItemId = UniversalAutoResolver.TryGetResolutionInfo(itemId, category, manifest.GUID);
                if (resolvedItemId != null)
                {
                    Logger.LogDebug($"Found resolved ID: {itemId} => {resolvedItemId.LocalSlot}");
                    itemId = resolvedItemId.LocalSlot;
                }
                else
                {
                    Logger.LogWarning($"Failed to resolve id={itemId} category={category} guid={manifest.GUID}");
                }

                var uniqueId = clothPartId + "-" + itemId;
                var dictKey = GetDictKey(clothPartId, itemId);

                foreach (var colliderData in clothData.Elements())
                {
                    if (colliderData.Name == "SphereColliderPair")
                    {
                        var result = new SphereColliderPair(GetSphereColliderData(colliderData.Element("first"), uniqueId), GetSphereColliderData(colliderData.Element("second"), uniqueId));
                        var list = GetOrAddList(SphereColliders, dictKey);
                        list.Add(result);
                    }
                    else if (colliderData.Name == "CapsuleCollider")
                    {
                        var result = GetCapsuleColliderData(colliderData.Element("first"), uniqueId);
                        var list = GetOrAddList(CapsuleColliders, dictKey);
                        list.Add(result);
                    }
                    else
                    {
                        throw new FormatException("Unknown collider type " + colliderData.Name);
                    }
                    Logger.LogDebug($"Added {colliderData.Name}: dictKey={dictKey} value={colliderData}");
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

            return new SphereColliderData(
                element.Attribute("boneName")?.Value ?? throw new FormatException("Missing boneName attribute"),
                float.Parse(element.Attribute("radius")?.Value ?? throw new FormatException("Missing radius attribute"), CultureInfo.InvariantCulture),
                ParseVector3(element.Attribute("center")?.Value ?? throw new FormatException("Missing center attribute")),
                uniqueId);
        }

        private CapsuleColliderData GetCapsuleColliderData(XElement element, string uniqueId)
        {
            if (element == null) return null;

            return new CapsuleColliderData(
                element.Attribute("boneName")?.Value ?? throw new FormatException("Missing boneName attribute"),
                float.Parse(element.Attribute("radius")?.Value ?? throw new FormatException("Missing radius attribute"), CultureInfo.InvariantCulture),
                float.Parse(element.Attribute("height")?.Value ?? throw new FormatException("Missing height attribute"), CultureInfo.InvariantCulture),
                ParseVector3(element.Attribute("center")?.Value ?? throw new FormatException("Missing center attribute")),
                int.Parse(element.Attribute("direction")?.Value ?? throw new FormatException("Missing direction attribute"), CultureInfo.InvariantCulture),
                uniqueId);
        }

        private Vector3 ParseVector3(string value)
        {
            var parts = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) throw new FormatException("Could not parse Vector3 from " + value);
            return new Vector3(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[3], CultureInfo.InvariantCulture));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeCustomClothes))]
        [UsedImplicitly]
        private static void ChangeCustomClothes(ChaControl __instance, int kind)
        {
            if (__instance != null)
            {
                var controller = __instance.GetComponent<ClothColliderController>();
                if (controller != null) controller.UpdateColliders(kind);
            }
        }
    }
}