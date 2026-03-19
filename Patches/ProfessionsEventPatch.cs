using CelemProfessions.Events;
using CelemProfessions.Service;
using ProjectM;
using ScarletCore.Events;
using ScarletCore.Services;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace CelemProfessions.Patches;

public static class ProfessionsEventPatch {
  public static void Initialize() {
    EventManager.On(PlayerEvents.PlayerJoined, HandlePlayerJoined);
    EventManager.On(PostfixEvents.OnDeath, HandleDeathEvents);
    EventManager.On(PrefixEvents.OnInventoryChanged, HandleInventoryChanged);
  }

  private static void HandlePlayerJoined(PlayerData player) {
    ProfessionService.HandlePlayerJoined(player);
  }

  private static void HandleDeathEvents(NativeArray<Entity> entities) {
    for (int i = 0; i < entities.Length; i++) {
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
    }
  }

  private static void HandleInventoryChanged(NativeArray<Entity> entities) {
    CraftTrackingService.HandleInventoryChanged(entities);
  }
}
