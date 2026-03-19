using System;
using System.Collections.Concurrent;
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
  private const double CraftRateModifier = 1d;

  private static readonly ConcurrentDictionary<ulong, Dictionary<Entity, Dictionary<PrefabGUID, int>>> PlayerCraftingJobs = [];
  private static readonly ConcurrentDictionary<ulong, Dictionary<Entity, Dictionary<PrefabGUID, int>>> ValidatedCraftingJobs = [];
  private static readonly Dictionary<Entity, bool> CraftFinished = [];

  private static EntityManager EntityManager => GameSystems.EntityManager;
  private static NetworkIdSystem.Singleton NetworkIdSystem => GameSystems.NetworkIdSystem;
  private static PrefabCollectionSystem PrefabCollectionSystem => GameSystems.PrefabCollectionSystem;

  public static void HandleStartCrafting(StartCraftingSystem system) {
    if (!GameSystems.Initialized) {
      return;
    }

    NativeArray<Entity> entities = system._StartCraftItemEventQuery.ToEntityArray(Allocator.Temp);

    try {
      foreach (Entity entity in entities) {
        if (!entity.TryGetComponent(out StartCraftItemEvent startCraftEvent) || !entity.TryGetComponent(out FromCharacter fromCharacter)) {
          continue;
        }

        Entity craftingStation = ResolveEntity(startCraftEvent.Workstation);
        if (!craftingStation.Exists()) {
          continue;
        }

        PrefabGUID recipeGuid = startCraftEvent.RecipeId;
        Entity recipePrefab = ResolvePrefab(recipeGuid);
        PrefabGUID outputItem = GetItemFromRecipePrefab(recipePrefab);
        if (outputItem.IsEmpty()) {
          continue;
        }

        ulong platformId = ResolvePlatformId(fromCharacter.User);
        if (platformId == 0) {
          continue;
        }

        Dictionary<Entity, Dictionary<PrefabGUID, int>> stationJobs = PlayerCraftingJobs.GetOrAdd(platformId, _ => []);

        if (!stationJobs.TryGetValue(craftingStation, out Dictionary<PrefabGUID, int> recipeMap)) {
          recipeMap = [];
          stationJobs[craftingStation] = recipeMap;
        }

        recipeMap.TryGetValue(outputItem, out int currentJobs);
        recipeMap[outputItem] = currentJobs + 1;
      }
    } finally {
      entities.Dispose();
    }
  }

  public static void HandleStopCrafting(StopCraftingSystem system) {
    if (!GameSystems.Initialized) {
      return;
    }

    NativeArray<Entity> entities = system._EventQuery.ToEntityArray(Allocator.Temp);

    try {
      foreach (Entity entity in entities) {
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
      }
    } finally {
      entities.Dispose();
    }
  }

  public static void HandleMoveItem(MoveItemBetweenInventoriesSystem system) {
    if (!GameSystems.Initialized) {
      return;
    }

    NativeArray<Entity> entities = system._MoveItemBetweenInventoriesEventQuery.ToEntityArray(Allocator.Temp);

    try {
      foreach (Entity entity in entities) {
        if (!entity.TryGetComponent(out MoveItemBetweenInventoriesEvent moveEvent) || !entity.TryGetComponent(out FromCharacter fromCharacter)) {
          continue;
        }

        Entity destination = ResolveEntity(moveEvent.ToInventory);
        if (!destination.Exists() || !destination.Has<CastleWorkstation>()) {
          continue;
        }

        ulong platformId = ResolvePlatformId(fromCharacter.Character);
        if (platformId == 0) {
          continue;
        }

        PrefabGUID movedItem = GetItemFromCharacterSlot(fromCharacter.Character, moveEvent.FromSlot);
        if (movedItem.IsEmpty()) {
          continue;
        }

        TryDecreasePendingJob(platformId, destination, movedItem);
        TryDecreaseValidatedJob(platformId, destination, movedItem);
      }
    } finally {
      entities.Dispose();
    }
  }

  public static void HandleUpdateCrafting(UpdateCraftingSystem system) {
    if (!GameSystems.Initialized) {
      return;
    }

    NativeArray<Entity> entities = system.EntityQueries[0].ToEntityArray(Allocator.Temp);

    try {
      foreach (Entity entity in entities) {
        if (!entity.Has<CastleWorkstation>() || !GameSystems.ServerGameManager.TryGetBuffer(entity, out DynamicBuffer<QueuedWorkstationCraftAction> queueBuffer) || queueBuffer.IsEmpty) {
          continue;
        }

        if (!CraftFinished.ContainsKey(entity)) {
          CraftFinished[entity] = false;
        }

        float recipeReduction = entity.Read<CastleWorkstation>().WorkstationLevel.HasFlag(WorkstationLevel.MatchingFloor) ? 0.75f : 1f;
        ProcessQueuedCraftAction(entity, queueBuffer[0], recipeReduction);
      }
    } finally {
      entities.Dispose();
    }
  }

  public static void HandleUpdatePrison(UpdatePrisonSystem system) {
    if (!GameSystems.Initialized) {
      return;
    }

    NativeArray<Entity> entities = system.EntityQueries[0].ToEntityArray(Allocator.Temp);

    try {
      foreach (Entity entity in entities) {
        if (!entity.Has<CastleWorkstation>() || !GameSystems.ServerGameManager.TryGetBuffer(entity, out DynamicBuffer<QueuedWorkstationCraftAction> queueBuffer) || queueBuffer.IsEmpty) {
          continue;
        }

        if (!CraftFinished.ContainsKey(entity)) {
          CraftFinished[entity] = false;
        }

        ProcessQueuedCraftAction(entity, queueBuffer[0], 1f);
      }
    } finally {
      entities.Dispose();
    }
  }

  public static void HandleInventoryChanged(NativeArray<Entity> entities) {
    if (!GameSystems.Initialized) {
      return;
    }

    foreach (Entity entity in entities) {
      if (!entity.TryGetComponent(out InventoryChangedEvent changedEvent)) {
        continue;
      }

      if (!changedEvent.InventoryEntity.TryGetComponent(out InventoryConnection inventoryConnection)) {
        continue;
      }

      Entity inventoryOwner = inventoryConnection.InventoryOwner;
      if (!inventoryOwner.Exists()) {
        continue;
      }

      if (changedEvent.ChangeType == InventoryChangedEventType.Removed && inventoryOwner.IsPlayer()) {
        ulong ownerPlatformId = ResolvePlatformId(inventoryOwner);
        if (ownerPlatformId != 0) {
          ProfessionService.HandleConsumableRemoved(ownerPlatformId, changedEvent.InventoryEntity, changedEvent.ItemEntity, changedEvent.Item, changedEvent.Amount);
        }

        continue;
      }

      if (changedEvent.ChangeType != InventoryChangedEventType.Obtained || !inventoryOwner.TryGetComponent(out UserOwner userOwner) || !userOwner.Owner.GetEntityOnServer().TryGetComponent(out User user)) {
        continue;
      }

      Entity station = inventoryOwner;
      PrefabGUID itemPrefab = changedEvent.Item;
      Entity itemEntity = changedEvent.ItemEntity;

      if (itemEntity.Exists() && itemEntity.Has<UpgradeableLegendaryItem>()) {
        int tier = itemEntity.Read<UpgradeableLegendaryItem>().CurrentTier;
        itemPrefab = itemEntity.ReadBuffer<UpgradeableLegendaryItemTiers>()[tier].TierPrefab;
      }

      Dictionary<ulong, User> candidates = ResolveCandidateCrafters(user);
      foreach ((ulong platformId, User _) in candidates) {
        if (!TryConsumeValidatedCraft(platformId, station, itemPrefab)) {
          continue;
        }

        PlayerData candidatePlayer = null;
        if (platformId.TryGetPlayerData(out PlayerData onlinePlayer)) {
          candidatePlayer = onlinePlayer;
        } else if (PlayerService.TryGetById(platformId, out PlayerData cachedPlayer)) {
          candidatePlayer = cachedPlayer;
        }

        ProfessionService.HandleCraftedItem(platformId, candidatePlayer, station, itemEntity, itemPrefab, changedEvent.Amount);
        break;
      }
    }
  }

  private static void ProcessQueuedCraftAction(Entity station, QueuedWorkstationCraftAction craftAction, float recipeReduction) {
    ulong platformId = ResolvePlatformId(craftAction.InitiateUser);
    if (platformId == 0) {
      return;
    }

    bool craftFinished = CraftFinished[station];

    Entity recipePrefab = ResolvePrefab(craftAction.RecipeGuid);
    PrefabGUID outputItem = GetItemFromRecipePrefab(recipePrefab);
    if (outputItem.IsEmpty()) {
      return;
    }

    if (!recipePrefab.TryGetComponent(out RecipeData recipeData)) {
      return;
    }

    double totalTime = (recipeData.CraftDuration * recipeReduction) / Math.Max(0.1d, CraftRateModifier);
    double craftProgress = craftAction.ProgressTime;

    if (!craftFinished && (craftProgress / totalTime) >= CraftThreshold) {
      CraftFinished[station] = true;
      ValidateCraftingJob(platformId, station, outputItem);
    } else if (craftFinished && (craftProgress / totalTime) < CraftThreshold) {
      CraftFinished[station] = false;
    }
  }

  private static void ValidateCraftingJob(ulong platformId, Entity station, PrefabGUID itemPrefab) {
    if (!PlayerCraftingJobs.TryGetValue(platformId, out Dictionary<Entity, Dictionary<PrefabGUID, int>> stationJobs) || !stationJobs.TryGetValue(station, out Dictionary<PrefabGUID, int> jobs) || !jobs.TryGetValue(itemPrefab, out int pending) || pending <= 0) {
      return;
    }

    Dictionary<Entity, Dictionary<PrefabGUID, int>> validatedStations = ValidatedCraftingJobs.GetOrAdd(platformId, _ => []);
    if (!validatedStations.TryGetValue(station, out Dictionary<PrefabGUID, int> validatedByItem)) {
      validatedByItem = [];
      validatedStations[station] = validatedByItem;
    }

    validatedByItem.TryGetValue(itemPrefab, out int validatedCount);
    validatedByItem[itemPrefab] = validatedCount + 1;

    jobs[itemPrefab] = pending - 1;
    if (jobs[itemPrefab] <= 0) {
      jobs.Remove(itemPrefab);
    }
  }

  private static bool TryConsumeValidatedCraft(ulong platformId, Entity station, PrefabGUID itemPrefab) {
    if (!ValidatedCraftingJobs.TryGetValue(platformId, out Dictionary<Entity, Dictionary<PrefabGUID, int>> stationJobs) || !stationJobs.TryGetValue(station, out Dictionary<PrefabGUID, int> jobs) || !jobs.TryGetValue(itemPrefab, out int pending) || pending <= 0) {
      return false;
    }

    jobs[itemPrefab] = pending - 1;
    if (jobs[itemPrefab] <= 0) {
      jobs.Remove(itemPrefab);
    }

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
  }

  private static void TryDecreaseValidatedJob(ulong platformId, Entity station, PrefabGUID itemPrefab) {
    if (!ValidatedCraftingJobs.TryGetValue(platformId, out Dictionary<Entity, Dictionary<PrefabGUID, int>> stationJobs) || !stationJobs.TryGetValue(station, out Dictionary<PrefabGUID, int> jobs) || !jobs.TryGetValue(itemPrefab, out int pending) || pending <= 0) {
      return;
    }

    jobs[itemPrefab] = pending - 1;
    if (jobs[itemPrefab] <= 0) {
      jobs.Remove(itemPrefab);
    }
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

  private static Dictionary<ulong, User> ResolveCandidateCrafters(User user) {
    var candidates = new Dictionary<ulong, User>();

    if (!user.ClanEntity.GetEntityOnServer().Exists()) {
      candidates[user.PlatformId] = user;
      return candidates;
    }

    Entity clan = user.ClanEntity.GetEntityOnServer();
    if (!clan.TryGetBuffer(out DynamicBuffer<SyncToUserBuffer> clanUsers) || clanUsers.IsEmpty) {
      candidates[user.PlatformId] = user;
      return candidates;
    }

    foreach (SyncToUserBuffer syncToUser in clanUsers) {
      if (syncToUser.UserEntity.TryGetComponent(out User clanUser)) {
        candidates[clanUser.PlatformId] = clanUser;
      }
    }

    if (!candidates.ContainsKey(user.PlatformId)) {
      candidates[user.PlatformId] = user;
    }

    return candidates;
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


