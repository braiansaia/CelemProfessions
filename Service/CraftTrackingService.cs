using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.Network;
using ProjectM.Shared;
using ScarletCore.Services;
using ScarletCore.Systems;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace CelemProfessions.Service;

public static class CraftTrackingService {
  private const float CraftThreshold = 0.975f;

  private sealed class ValidatedCraftQueue {
    private const int MinCompactHead = 32;
    private readonly List<ulong> _items = [];
    private int _head;
    private int _count;

    public bool IsEmpty => _count <= 0;

    public void Enqueue(ulong platformId) {
      _items.Add(platformId);
      _count++;
    }

    public bool TryDequeue(out ulong platformId) {
      platformId = 0;
      while (_head < _items.Count && _items[_head] == 0) {
        _head++;
      }

      if (_head >= _items.Count) {
        Compact(force: true);
        return false;
      }

      platformId = _items[_head];
      _items[_head] = 0;
      _head++;
      _count = Math.Max(0, _count - 1);
      Compact(force: false);
      return platformId != 0;
    }

    public bool RemoveFirst(ulong platformId) {
      if (platformId == 0 || _count <= 0) {
        return false;
      }

      for (int i = _head; i < _items.Count; i++) {
        if (_items[i] != platformId) {
          continue;
        }

        _items[i] = 0;
        _count = Math.Max(0, _count - 1);
        Compact(force: false);
        return true;
      }

      return false;
    }

    private void Compact(bool force) {
      while (_head < _items.Count && _items[_head] == 0) {
        _head++;
      }

      if (_head >= _items.Count) {
        _items.Clear();
        _head = 0;
        return;
      }

      if (!force && (_head < MinCompactHead || _head * 2 < _items.Count)) {
        return;
      }

      _items.RemoveRange(0, _head);
      _head = 0;
    }
  }

  private static readonly Dictionary<ulong, Dictionary<Entity, Dictionary<PrefabGUID, int>>> PlayerCraftingJobs = [];
  private static readonly Dictionary<Entity, Dictionary<PrefabGUID, ValidatedCraftQueue>> ValidatedCraftingJobs = [];
  private static readonly Dictionary<Entity, bool> CraftFinished = [];
  private static readonly Dictionary<string, DateTime> NextErrorLogByKey = [];
  private static readonly TimeSpan ErrorLogCooldown = TimeSpan.FromSeconds(10);
  private static bool CraftRateReadErrorLogged;

  private static EntityManager EntityManager => GameSystems.EntityManager;
  private static NetworkIdSystem.Singleton NetworkIdSystem => GameSystems.NetworkIdSystem;
  private static PrefabCollectionSystem PrefabCollectionSystem => GameSystems.PrefabCollectionSystem;

  public static void Shutdown() {
    PlayerCraftingJobs.Clear();
    ValidatedCraftingJobs.Clear();
    CraftFinished.Clear();
    NextErrorLogByKey.Clear();
    CraftRateReadErrorLogged = false;
  }

  public static void HandleStartCrafting(NativeArray<Entity> entities) {
    if (!GameSystems.Initialized) {
      return;
    }

    foreach (Entity entity in entities) {
      try {
        if (!entity.TryGetComponent(out StartCraftItemEvent startCraftEvent) || !entity.TryGetComponent(out FromCharacter fromCharacter)) {
          continue;
        }

        Entity craftingStation = ResolveEntity(startCraftEvent.Workstation);
        if (!craftingStation.Exists()) {
          continue;
        }

        Entity recipePrefab = ResolvePrefab(startCraftEvent.RecipeId);
        PrefabGUID outputItem = GetItemFromRecipePrefab(recipePrefab);
        if (outputItem.IsEmpty()) {
          continue;
        }

        ulong platformId = ResolvePlatformId(fromCharacter.User);
        if (platformId == 0) {
          continue;
        }

        if (!PlayerCraftingJobs.TryGetValue(platformId, out Dictionary<Entity, Dictionary<PrefabGUID, int>> stationJobs)) {
          stationJobs = [];
          PlayerCraftingJobs[platformId] = stationJobs;
        }

        if (!stationJobs.TryGetValue(craftingStation, out Dictionary<PrefabGUID, int> recipeMap)) {
          recipeMap = [];
          stationJobs[craftingStation] = recipeMap;
        }

        recipeMap.TryGetValue(outputItem, out int currentJobs);
        recipeMap[outputItem] = currentJobs + 1;
      } catch (Exception ex) {
        LogThrottledError("start", $"Failed to handle start crafting event for entity {entity.Index}", ex);
      }
    }
  }

  public static void HandleStopCrafting(NativeArray<Entity> entities) {
    if (!GameSystems.Initialized) {
      return;
    }

    foreach (Entity entity in entities) {
      try {
        if (!entity.TryGetComponent(out StopCraftItemEvent stopCraftEvent) || !entity.TryGetComponent(out FromCharacter fromCharacter)) {
          continue;
        }

        Entity craftingStation = ResolveEntity(stopCraftEvent.Workstation);
        if (!craftingStation.Exists()) {
          continue;
        }

        Entity recipePrefab = ResolvePrefab(stopCraftEvent.RecipeGuid);
        PrefabGUID outputItem = GetItemFromRecipePrefab(recipePrefab);
        if (outputItem.IsEmpty()) {
          continue;
        }

        ulong platformId = ResolvePlatformId(fromCharacter.User);
        if (platformId == 0) {
          continue;
        }

        TryDecreaseValidatedJob(platformId, craftingStation, outputItem);
        TryDecreasePendingJob(platformId, craftingStation, outputItem);
      } catch (Exception ex) {
        LogThrottledError("stop", $"Failed to handle stop crafting event for entity {entity.Index}", ex);
      }
    }
  }

  public static void HandleMoveItem(NativeArray<Entity> entities) {
    if (!GameSystems.Initialized) {
      return;
    }

    foreach (Entity entity in entities) {
      try {
        if (!entity.TryGetComponent(out MoveItemBetweenInventoriesEvent moveEvent) || !entity.TryGetComponent(out FromCharacter fromCharacter)) {
          continue;
        }

        Entity destination = ResolveEntity(moveEvent.ToInventory);
        if (!destination.Exists() || !destination.Has<CastleWorkstation>()) {
          continue;
        }

        ulong platformId = ResolvePlatformId(fromCharacter.User);
        if (platformId == 0) {
          continue;
        }

        PrefabGUID movedItem = GetItemFromCharacterSlot(fromCharacter.Character, moveEvent.FromSlot);
        if (movedItem.IsEmpty()) {
          continue;
        }

        TryDecreasePendingJob(platformId, destination, movedItem);
        TryDecreaseValidatedJob(platformId, destination, movedItem);
      } catch (Exception ex) {
        LogThrottledError("move", $"Failed to handle move item event for entity {entity.Index}", ex);
      }
    }
  }

  public static void HandleUpdateCrafting(NativeArray<Entity> entities) {
    if (!GameSystems.Initialized) {
      return;
    }

    double craftRateModifier = GetCraftRateModifier();
    for (int i = 0; i < entities.Length; i++) {
      Entity entity = entities[i];
      try {
        if (!entity.Has<CastleWorkstation>() || !GameSystems.ServerGameManager.TryGetBuffer(entity, out DynamicBuffer<QueuedWorkstationCraftAction> queueBuffer) || queueBuffer.IsEmpty) {
          CraftFinished.Remove(entity);
          continue;
        }

        bool hasMatchingFloor = entity.Read<CastleWorkstation>().WorkstationLevel.HasFlag(WorkstationLevel.MatchingFloor);
        float recipeReduction = hasMatchingFloor ? 0.75f : 1f;
        ProcessQueuedCraftAction(entity, queueBuffer[0], recipeReduction, craftRateModifier);
      } catch (Exception ex) {
        LogThrottledError("update", $"Failed to handle update crafting event for entity {entity.Index}", ex);
      }
    }
  }

  public static void HandleInventoryChanged(NativeArray<Entity> entities) {
    if (!GameSystems.Initialized) {
      return;
    }

    foreach (Entity entity in entities) {
      try {
        if (!entity.TryGetComponent(out InventoryChangedEvent changedEvent)) {
          continue;
        }

        if (!changedEvent.InventoryEntity.TryGetComponent(out InventoryConnection inventoryConnection)) {
          continue;
        }

        Entity inventoryOwner = inventoryConnection.InventoryOwner;
        if (!inventoryOwner.Exists() || changedEvent.ChangeType != InventoryChangedEventType.Obtained) {
          continue;
        }

        if (!inventoryOwner.TryGetComponent(out UserOwner _)) {
          continue;
        }

        PrefabGUID itemPrefab = changedEvent.Item;
        Entity itemEntity = changedEvent.ItemEntity;
        if (itemEntity.Exists() && itemEntity.Has<UpgradeableLegendaryItem>() && itemEntity.TryGetBuffer(out DynamicBuffer<UpgradeableLegendaryItemTiers> legendaryTiers)) {
          int tier = itemEntity.Read<UpgradeableLegendaryItem>().CurrentTier;
          if (tier >= 0 && tier < legendaryTiers.Length) {
            itemPrefab = legendaryTiers[tier].TierPrefab;
          }
        }

        if (!IsTrackedCraftItem(itemPrefab)) {
          continue;
        }

        if (!TryConsumeValidatedCraft(inventoryOwner, itemPrefab, out ulong platformId)) {
          continue;
        }

        if (!platformId.TryGetPlayerData(out PlayerData player) || player == null) {
          continue;
        }

        ProfessionService.HandleCraftedItem(player, itemEntity, itemPrefab, changedEvent.Amount);
      } catch (Exception ex) {
        LogThrottledError("inventory", $"Failed to handle inventory changed event for entity {entity.Index}", ex);
      }
    }
  }

  private static void ProcessQueuedCraftAction(Entity station, QueuedWorkstationCraftAction craftAction, float recipeReduction, double craftRateModifier) {
    ulong platformId = ResolvePlatformId(craftAction.InitiateUser);
    if (platformId == 0) {
      return;
    }

    bool craftFinished = CraftFinished.TryGetValue(station, out bool finished) && finished;
    Entity recipePrefab = ResolvePrefab(craftAction.RecipeGuid);
    PrefabGUID outputItem = GetItemFromRecipePrefab(recipePrefab);
    if (outputItem.IsEmpty() || !recipePrefab.TryGetComponent(out RecipeData recipeData)) {
      return;
    }

    double totalTime = (recipeData.CraftDuration * recipeReduction) / craftRateModifier;
    double craftProgress = craftAction.ProgressTime;
    double ratio = craftProgress / Math.Max(0.001d, totalTime);
    if (!craftFinished && ratio >= CraftThreshold) {
      CraftFinished[station] = true;
      ValidateCraftingJob(platformId, station, outputItem);
    } else if (craftFinished && ratio < CraftThreshold) {
      CraftFinished[station] = false;
    }
  }

  private static void ValidateCraftingJob(ulong platformId, Entity station, PrefabGUID itemPrefab) {
    if (!PlayerCraftingJobs.TryGetValue(platformId, out Dictionary<Entity, Dictionary<PrefabGUID, int>> stationJobs) || !stationJobs.TryGetValue(station, out Dictionary<PrefabGUID, int> jobs) || !jobs.TryGetValue(itemPrefab, out int pending) || pending <= 0) {
      return;
    }

    if (!ValidatedCraftingJobs.TryGetValue(station, out Dictionary<PrefabGUID, ValidatedCraftQueue> validatedByItem)) {
      validatedByItem = [];
      ValidatedCraftingJobs[station] = validatedByItem;
    }

    if (!validatedByItem.TryGetValue(itemPrefab, out ValidatedCraftQueue validatedQueue)) {
      validatedQueue = new ValidatedCraftQueue();
      validatedByItem[itemPrefab] = validatedQueue;
    }

    validatedQueue.Enqueue(platformId);
    jobs[itemPrefab] = pending - 1;
    if (jobs[itemPrefab] <= 0) {
      jobs.Remove(itemPrefab);
    }

    CleanupPendingPlatformState(platformId, station);
  }

  private static bool TryConsumeValidatedCraft(Entity station, PrefabGUID itemPrefab, out ulong platformId) {
    platformId = 0;
    if (!ValidatedCraftingJobs.TryGetValue(station, out Dictionary<PrefabGUID, ValidatedCraftQueue> validatedByItem) || !validatedByItem.TryGetValue(itemPrefab, out ValidatedCraftQueue validatedQueue)) {
      return false;
    }

    if (!validatedQueue.TryDequeue(out platformId) || platformId == 0) {
      CleanupValidatedStationState(station, itemPrefab);
      return false;
    }

    CleanupValidatedStationState(station, itemPrefab);
    return true;
  }

  private static void TryDecreasePendingJob(ulong platformId, Entity station, PrefabGUID itemPrefab) {
    if (!PlayerCraftingJobs.TryGetValue(platformId, out Dictionary<Entity, Dictionary<PrefabGUID, int>> stationJobs) || !stationJobs.TryGetValue(station, out Dictionary<PrefabGUID, int> jobs) || !jobs.TryGetValue(itemPrefab, out int pending) || pending <= 0) {
      return;
    }

    jobs[itemPrefab] = pending - 1;
    if (jobs[itemPrefab] <= 0) {
      jobs.Remove(itemPrefab);
    }

    CleanupPendingPlatformState(platformId, station);
  }

  private static void TryDecreaseValidatedJob(ulong platformId, Entity station, PrefabGUID itemPrefab) {
    if (!ValidatedCraftingJobs.TryGetValue(station, out Dictionary<PrefabGUID, ValidatedCraftQueue> validatedByItem) || !validatedByItem.TryGetValue(itemPrefab, out ValidatedCraftQueue validatedQueue)) {
      return;
    }

    if (!validatedQueue.RemoveFirst(platformId)) {
      return;
    }

    CleanupValidatedStationState(station, itemPrefab);
  }

  private static void CleanupPendingPlatformState(ulong platformId, Entity station) {
    if (!PlayerCraftingJobs.TryGetValue(platformId, out Dictionary<Entity, Dictionary<PrefabGUID, int>> stationJobs)) {
      return;
    }

    if (stationJobs.TryGetValue(station, out Dictionary<PrefabGUID, int> jobs) && jobs.Count == 0) {
      stationJobs.Remove(station);
    }

    if (stationJobs.Count == 0) {
      PlayerCraftingJobs.Remove(platformId);
    }
  }

  private static void CleanupValidatedStationState(Entity station, PrefabGUID itemPrefab) {
    if (!ValidatedCraftingJobs.TryGetValue(station, out Dictionary<PrefabGUID, ValidatedCraftQueue> validatedByItem)) {
      return;
    }

    if (validatedByItem.TryGetValue(itemPrefab, out ValidatedCraftQueue validatedQueue) && validatedQueue.IsEmpty) {
      validatedByItem.Remove(itemPrefab);
    }

    if (validatedByItem.Count == 0) {
      ValidatedCraftingJobs.Remove(station);
    }
  }

  private static double GetCraftRateModifier() {
    try {
      ServerGameSettingsSystem settingsSystem = GameSystems.Server.GetExistingSystemManaged<ServerGameSettingsSystem>();
      if (settingsSystem == null) {
        return 1d;
      }

      return Math.Max(0.1d, settingsSystem._Settings.CraftRateModifier);
    } catch (Exception ex) {
      if (!CraftRateReadErrorLogged) {
        CraftRateReadErrorLogged = true;
        Plugin.LogInstance?.LogWarning($"[ProfessionsCraft] Failed to read craft rate modifier: {ex.Message}");
      }

      return 1d;
    }
  }

  private static void LogThrottledError(string key, string message, Exception ex) {
    DateTime now = DateTime.UtcNow;
    if (NextErrorLogByKey.TryGetValue(key, out DateTime nextAllowedAt) && nextAllowedAt > now) {
      return;
    }

    NextErrorLogByKey[key] = now + ErrorLogCooldown;
    Plugin.LogInstance?.LogWarning($"[ProfessionsCraft] {message}: {ex.Message}");
  }

  private static PrefabGUID GetItemFromCharacterSlot(Entity character, int slot) {
    if (!InventoryUtilities.TryGetInventoryEntity(EntityManager, character, out Entity inventoryEntity) || !GameSystems.ServerGameManager.TryGetBuffer(inventoryEntity, out DynamicBuffer<InventoryBuffer> inventoryBuffer) || slot < 0 || slot >= inventoryBuffer.Length) {
      return PrefabGUID.Empty;
    }

    return inventoryBuffer[slot].ItemType;
  }

  private static Entity ResolveEntity(NetworkId networkId) {
    return NetworkIdSystem._NetworkIdLookupMap.TryGetValue(networkId, out Entity entity) ? entity : Entity.Null;
  }

  private static Entity ResolvePrefab(PrefabGUID prefab) {
    return PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(prefab, out Entity entity) ? entity : Entity.Null;
  }

  private static PrefabGUID GetItemFromRecipePrefab(Entity recipePrefab) {
    if (!recipePrefab.Exists() || !recipePrefab.Has<RecipeData>()) {
      return PrefabGUID.Empty;
    }

    DynamicBuffer<RecipeOutputBuffer> outputBuffer = recipePrefab.ReadBuffer<RecipeOutputBuffer>();
    return outputBuffer.IsEmpty ? PrefabGUID.Empty : outputBuffer[0].Guid;
  }

  private static bool IsTrackedCraftItem(PrefabGUID itemPrefab) {
    if (ProfessionExperienceConfigService.IsAlchemyCraftConfigured(itemPrefab)) {
      return true;
    }

    return ProfessionCatalogService.IsNecklacePrefab(itemPrefab)
      || ProfessionCatalogService.IsArmorPrefab(itemPrefab)
      || ProfessionCatalogService.IsWeaponPrefab(itemPrefab);
  }

  private static ulong ResolvePlatformId(Entity entity) {
    if (!entity.Exists()) {
      return 0;
    }

    if (entity.TryGetComponent(out User user)) {
      return user.PlatformId;
    }

    if (entity.TryGetPlayerData(out PlayerData player) && player != null) {
      return player.PlatformId;
    }

    Entity userEntity = entity.GetUserEntity();
    if (userEntity.Exists() && userEntity.TryGetComponent(out User userFromEntity)) {
      return userFromEntity.PlatformId;
    }

    return 0;
  }
}
