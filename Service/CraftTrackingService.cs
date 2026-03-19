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
  private const bool EnableCraftTraceLogs = true;

  private static readonly ConcurrentDictionary<ulong, Dictionary<Entity, Dictionary<PrefabGUID, int>>> PlayerCraftingJobs = [];
  private static readonly ConcurrentDictionary<ulong, Dictionary<Entity, Dictionary<PrefabGUID, int>>> ValidatedCraftingJobs = [];
  private static readonly Dictionary<Entity, bool> CraftFinished = [];
  private static bool CraftRateReadErrorLogged;

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
          LogCraftTrace($"START_SKIP station-not-found workstation={startCraftEvent.Workstation}");
          continue;
        }

        PrefabGUID recipeGuid = startCraftEvent.RecipeId;
        Entity recipePrefab = ResolvePrefab(recipeGuid);
        PrefabGUID outputItem = GetItemFromRecipePrefab(recipePrefab);
        if (outputItem.IsEmpty()) {
          LogCraftTrace($"START_SKIP output-empty recipe={FormatPrefab(recipeGuid)} station={GetEntityKey(craftingStation)}");
          continue;
        }

        ulong platformId = ResolvePlatformId(fromCharacter.User);
        if (platformId == 0) {
          LogCraftTrace($"START_SKIP platformId=0 user={GetEntityKey(fromCharacter.User)} character={GetEntityKey(fromCharacter.Character)} output={FormatPrefab(outputItem)}");
          continue;
        }

        Dictionary<Entity, Dictionary<PrefabGUID, int>> stationJobs = PlayerCraftingJobs.GetOrAdd(platformId, _ => []);

        if (!stationJobs.TryGetValue(craftingStation, out Dictionary<PrefabGUID, int> recipeMap)) {
          recipeMap = [];
          stationJobs[craftingStation] = recipeMap;
        }

        recipeMap.TryGetValue(outputItem, out int currentJobs);
        recipeMap[outputItem] = currentJobs + 1;
        LogCraftTrace($"START platform={platformId} station={GetEntityKey(craftingStation)} recipe={FormatPrefab(recipeGuid)} output={FormatPrefab(outputItem)} queued={recipeMap[outputItem]}");
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
          LogCraftTrace($"STOP_SKIP output-empty recipe={FormatPrefab(stopCraftEvent.RecipeGuid)} station={GetEntityKey(craftingStation)}");
          continue;
        }

        ulong platformId = ResolvePlatformId(fromCharacter.User);
        if (platformId == 0) {
          LogCraftTrace($"STOP_SKIP platformId=0 user={GetEntityKey(fromCharacter.User)} character={GetEntityKey(fromCharacter.Character)} output={FormatPrefab(outputItem)}");
          continue;
        }

        TryDecreaseValidatedJob(platformId, craftingStation, outputItem);
        TryDecreasePendingJob(platformId, craftingStation, outputItem);
        LogCraftTrace($"STOP platform={platformId} station={GetEntityKey(craftingStation)} output={FormatPrefab(outputItem)}");
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

      if (changedEvent.ChangeType != InventoryChangedEventType.Obtained) {
        continue;
      }

      bool trackedIncomingItem = IsTrackedCraftItem(changedEvent.Item);

      if (!inventoryOwner.TryGetComponent(out UserOwner userOwner)) {
        if (trackedIncomingItem) {
          LogCraftTrace($"INVENTORY_SKIP owner-without-userowner station={GetEntityKey(inventoryOwner)} item={FormatPrefab(changedEvent.Item)} amount={changedEvent.Amount}");
        }

        continue;
      }

      Entity ownerUserEntity = userOwner.Owner.GetEntityOnServer();
      if (!ownerUserEntity.Exists() || !ownerUserEntity.TryGetComponent(out User user)) {
        if (trackedIncomingItem) {
          LogCraftTrace($"INVENTORY_SKIP station-user-missing station={GetEntityKey(inventoryOwner)} ownerEntity={GetEntityKey(ownerUserEntity)} item={FormatPrefab(changedEvent.Item)} amount={changedEvent.Amount}");
        }

        continue;
      }

      Entity station = inventoryOwner;
      PrefabGUID itemPrefab = changedEvent.Item;
      Entity itemEntity = changedEvent.ItemEntity;

      if (itemEntity.Exists() && itemEntity.Has<UpgradeableLegendaryItem>()) {
        int tier = itemEntity.Read<UpgradeableLegendaryItem>().CurrentTier;
        itemPrefab = itemEntity.ReadBuffer<UpgradeableLegendaryItemTiers>()[tier].TierPrefab;
      }

      bool trackedItem = IsTrackedCraftItem(itemPrefab);
      Dictionary<ulong, User> candidates = ResolveCandidateCrafters(user);
      if (trackedItem) {
        LogCraftTrace($"INVENTORY_OBTAINED station={GetEntityKey(station)} owner={user.PlatformId} item={FormatPrefab(itemPrefab)} amount={changedEvent.Amount} candidates={candidates.Count}");
      }

      bool consumed = false;
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

        LogCraftTrace($"INVENTORY_CONSUMED platform={platformId} station={GetEntityKey(station)} item={FormatPrefab(itemPrefab)} amount={changedEvent.Amount} itemEntity={GetEntityKey(itemEntity)}");
        ProfessionService.HandleCraftedItem(platformId, candidatePlayer, station, itemEntity, itemPrefab, changedEvent.Amount);
        consumed = true;
        break;
      }

      if (trackedItem && !consumed) {
        LogCraftTrace($"INVENTORY_SKIP no-consume station={GetEntityKey(station)} owner={user.PlatformId} item={FormatPrefab(itemPrefab)} amount={changedEvent.Amount}");
      }
    }
  }

  private static void ProcessQueuedCraftAction(Entity station, QueuedWorkstationCraftAction craftAction, float recipeReduction) {
    ulong platformId = ResolvePlatformId(craftAction.InitiateUser);
    if (platformId == 0) {
      LogCraftTrace($"UPDATE_SKIP platformId=0 station={GetEntityKey(station)} recipe={FormatPrefab(craftAction.RecipeGuid)} initiateUser={GetEntityKey(craftAction.InitiateUser)}");
      return;
    }

    bool craftFinished = CraftFinished[station];

    Entity recipePrefab = ResolvePrefab(craftAction.RecipeGuid);
    PrefabGUID outputItem = GetItemFromRecipePrefab(recipePrefab);
    if (outputItem.IsEmpty()) {
      LogCraftTrace($"UPDATE_SKIP output-empty platform={platformId} station={GetEntityKey(station)} recipe={FormatPrefab(craftAction.RecipeGuid)}");
      return;
    }

    if (!recipePrefab.TryGetComponent(out RecipeData recipeData)) {
      LogCraftTrace($"UPDATE_SKIP recipe-data-missing platform={platformId} station={GetEntityKey(station)} recipe={FormatPrefab(craftAction.RecipeGuid)} output={FormatPrefab(outputItem)}");
      return;
    }

    double craftRateModifier = GetCraftRateModifier();
    double totalTime = (recipeData.CraftDuration * recipeReduction) / craftRateModifier;
    double craftProgress = craftAction.ProgressTime;

    double ratio = craftProgress / Math.Max(0.001d, totalTime);

    if (!craftFinished && ratio >= CraftThreshold) {
      CraftFinished[station] = true;
      LogCraftTrace($"UPDATE_VALIDATE_TRIGGER platform={platformId} station={GetEntityKey(station)} output={FormatPrefab(outputItem)} progress={craftProgress:0.###} total={totalTime:0.###} ratio={ratio:0.###} craftRate={craftRateModifier:0.###}");
      ValidateCraftingJob(platformId, station, outputItem);
    } else if (craftFinished && ratio < CraftThreshold) {
      CraftFinished[station] = false;
    }
  }

  private static void ValidateCraftingJob(ulong platformId, Entity station, PrefabGUID itemPrefab) {
    if (!PlayerCraftingJobs.TryGetValue(platformId, out Dictionary<Entity, Dictionary<PrefabGUID, int>> stationJobs) || !stationJobs.TryGetValue(station, out Dictionary<PrefabGUID, int> jobs) || !jobs.TryGetValue(itemPrefab, out int pending) || pending <= 0) {
      LogCraftTrace($"VALIDATE_SKIP no-pending platform={platformId} station={GetEntityKey(station)} item={FormatPrefab(itemPrefab)}");
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

    LogCraftTrace($"VALIDATE_OK platform={platformId} station={GetEntityKey(station)} item={FormatPrefab(itemPrefab)} pendingBefore={pending} validatedNow={validatedByItem[itemPrefab]}");
  }

  private static bool TryConsumeValidatedCraft(ulong platformId, Entity station, PrefabGUID itemPrefab) {
    if (!ValidatedCraftingJobs.TryGetValue(platformId, out Dictionary<Entity, Dictionary<PrefabGUID, int>> stationJobs) || !stationJobs.TryGetValue(station, out Dictionary<PrefabGUID, int> jobs) || !jobs.TryGetValue(itemPrefab, out int pending) || pending <= 0) {
      if (IsTrackedCraftItem(itemPrefab)) {
        LogCraftTrace($"CONSUME_SKIP no-validated-job platform={platformId} station={GetEntityKey(station)} item={FormatPrefab(itemPrefab)}");
      }

      return false;
    }

    jobs[itemPrefab] = pending - 1;
    if (jobs[itemPrefab] <= 0) {
      jobs.Remove(itemPrefab);
    }

    LogCraftTrace($"CONSUME_OK platform={platformId} station={GetEntityKey(station)} item={FormatPrefab(itemPrefab)} remaining={Math.Max(0, pending - 1)}");
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

    LogCraftTrace($"DECREASE_PENDING platform={platformId} station={GetEntityKey(station)} item={FormatPrefab(itemPrefab)} remaining={Math.Max(0, pending - 1)}");
  }

  private static void TryDecreaseValidatedJob(ulong platformId, Entity station, PrefabGUID itemPrefab) {
    if (!ValidatedCraftingJobs.TryGetValue(platformId, out Dictionary<Entity, Dictionary<PrefabGUID, int>> stationJobs) || !stationJobs.TryGetValue(station, out Dictionary<PrefabGUID, int> jobs) || !jobs.TryGetValue(itemPrefab, out int pending) || pending <= 0) {
      return;
    }

    jobs[itemPrefab] = pending - 1;
    if (jobs[itemPrefab] <= 0) {
      jobs.Remove(itemPrefab);
    }

    LogCraftTrace($"DECREASE_VALIDATED platform={platformId} station={GetEntityKey(station)} item={FormatPrefab(itemPrefab)} remaining={Math.Max(0, pending - 1)}");
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
        LogCraftTrace($"SETTINGS_SKIP craft-rate-read-failed error={ex.Message}");
      }

      return 1d;
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

  private static bool IsTrackedCraftItem(PrefabGUID itemPrefab) {
    return ProfessionCatalogService.IsNecklacePrefab(itemPrefab)
      || ProfessionCatalogService.IsArmorPrefab(itemPrefab)
      || ProfessionCatalogService.IsWeaponPrefab(itemPrefab)
      || ProfessionCatalogService.IsConsumablePrefab(itemPrefab);
  }

  private static void LogCraftTrace(string message) {
    if (!EnableCraftTraceLogs || Plugin.LogInstance == null) {
      return;
    }

    Plugin.LogInstance.LogInfo($"[ProfessionsCraft] {message}");
  }

  private static string FormatPrefab(PrefabGUID prefab) {
    if (prefab.IsEmpty()) {
      return "Empty(0)";
    }

    string name;
    try {
      name = prefab.GetName();
    } catch {
      name = "Unknown";
    }

    return $"{name}({prefab.GuidHash})";
  }

  private static string GetEntityKey(Entity entity) {
    return entity.Exists() ? $"{entity.Index}:{entity.Version}" : "Null";
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







