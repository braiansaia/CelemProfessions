using System;
using CelemProfessions.Events;
using CelemProfessions.Service;
using ProjectM;
using ScarletCore.Events;
using ScarletCore.Services;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace CelemProfessions.Patches;

public static class EventPatch {
  public static void Initialize() {
    EventManager.On(PlayerEvents.PlayerJoined, HandlePlayerJoined);
    EventManager.On(PostfixEvents.OnDeath, HandleDeathEvents);
    EventManager.On(PrefixEvents.OnInventoryChanged, HandleInventoryChanged);
    EventManager.On(PrefixEvents.OnMoveItem, HandleMoveItem);
    EventManager.On(PrefixEvents.OnStartCrafting, HandleStartCrafting);
    EventManager.On(PrefixEvents.OnStopCrafting, HandleStopCrafting);
    EventManager.On(PrefixEvents.OnUpdateCrafting, HandleUpdateCrafting);
    EventManager.On(PostfixEvents.OnGameplayEventDestroy, HandleGameplayEventDestroy);
    EventManager.On(PostfixEvents.OnBuffSpawn, HandleBuffSpawn);
  }

  private static void HandlePlayerJoined(PlayerData player) {
    ProfessionService.HandlePlayerJoined(player);
  }

  private static void HandleDeathEvents(NativeArray<Entity> entities) {
    for (int i = 0; i < entities.Length; i++) {
      try {
        Entity eventEntity = entities[i];
        if (!eventEntity.TryGetComponent(out DeathEvent deathEvent) || !deathEvent.Killer.Exists() || !deathEvent.Died.Exists()) {
          continue;
        }

        if (!PlayerOwnershipService.TryResolveOwningPlayer(deathEvent.Killer, out PlayerData killer) || killer == null) {
          continue;
        }

        Entity target = deathEvent.Died;
        PrefabGUID targetPrefab = target.GetPrefabGuid();
        ProfessionService.HandleGatherFromEntity(killer, target, targetPrefab);
        ProfessionService.HandleHunterKillEvent(new HunterKillEventData(killer, target, targetPrefab));
      } catch (Exception ex) {
        Plugin.LogInstance?.LogWarning($"[CelemProfessions] ProfessionsEventPatch death error: {ex.Message}");
      }
    }
  }

  private static void HandleInventoryChanged(NativeArray<Entity> entities) {
    try {
      CraftTrackingService.HandleInventoryChanged(entities);
    } catch (Exception ex) {
      Plugin.LogInstance?.LogWarning($"[CelemProfessions] ProfessionsEventPatch inventory error: {ex.Message}");
    }
  }

  private static void HandleMoveItem(NativeArray<Entity> entities) {
    try {
      CraftTrackingService.HandleMoveItem(entities);
    } catch (Exception ex) {
      Plugin.LogInstance?.LogWarning($"[CelemProfessions] ProfessionsEventPatch move item error: {ex.Message}");
    }
  }

  private static void HandleStartCrafting(NativeArray<Entity> entities) {
    try {
      CraftTrackingService.HandleStartCrafting(entities);
    } catch (Exception ex) {
      Plugin.LogInstance?.LogWarning($"[CelemProfessions] ProfessionsEventPatch start crafting error: {ex.Message}");
    }
  }

  private static void HandleStopCrafting(NativeArray<Entity> entities) {
    try {
      CraftTrackingService.HandleStopCrafting(entities);
    } catch (Exception ex) {
      Plugin.LogInstance?.LogWarning($"[CelemProfessions] ProfessionsEventPatch stop crafting error: {ex.Message}");
    }
  }

  private static void HandleUpdateCrafting(NativeArray<Entity> entities) {
    try {
      CraftTrackingService.HandleUpdateCrafting(entities);
    } catch (Exception ex) {
      Plugin.LogInstance?.LogWarning($"[CelemProfessions] ProfessionsEventPatch update crafting error: {ex.Message}");
    }
  }

  private static void HandleGameplayEventDestroy(NativeArray<Entity> entities) {
    for (int i = 0; i < entities.Length; i++) {
      try {
        ProfessionService.HandleFishingGameplayEvent(entities[i]);
      } catch (Exception ex) {
        Plugin.LogInstance?.LogWarning($"[CelemProfessions] ProfessionsEventPatch gameplay destroy error: {ex.Message}");
      }
    }
  }

  private static void HandleBuffSpawn(NativeArray<Entity> entities) {
    for (int i = 0; i < entities.Length; i++) {
      try {
        ProfessionService.HandleBuffSpawn(entities[i]);
      } catch (Exception ex) {
        Plugin.LogInstance?.LogWarning($"[CelemProfessions] ProfessionsEventPatch buff spawn error: {ex.Message}");
      }
    }
  }
}
