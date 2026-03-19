using CelemProfessions.Events;
using CelemProfessions.Service;
using HarmonyLib;
using ProjectM;
using ProjectM.Gameplay.Systems;
using ProjectM.Shared;
using ScarletCore.Resources;
using ScarletCore.Services;
using ScarletCore.Systems;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace CelemProfessions.Patches;

[HarmonyPatch]
public static class FishingEventPatch {
  private static readonly PrefabGUID FishingTravelToTarget = PrefabGUIDs.AB_Fishing_Draw_TravelToTarget;

  [HarmonyPatch(typeof(CreateGameplayEventOnDestroySystem), nameof(CreateGameplayEventOnDestroySystem.OnUpdate))]
  [HarmonyPostfix]
  public static void OnUpdatePostfix(CreateGameplayEventOnDestroySystem __instance) {
    if (!GameSystems.Initialized) {
      return;
    }

    NativeArray<Entity> entities = __instance.EntityQueries[0].ToEntityArray(Allocator.Temp);

    try {
      for (int i = 0; i < entities.Length; i++) {
        Entity entity = entities[i];
        if (!entity.TryGetComponent(out EntityOwner owner) || !owner.Owner.Exists() || !entity.TryGetComponent(out PrefabGUID prefabGuid) || !prefabGuid.Equals(FishingTravelToTarget)) {
          continue;
        }

        Entity playerCharacter = owner.Owner;
        if (!PlayerOwnershipService.TryResolveOwningPlayer(playerCharacter, out PlayerData player) || player == null) {
          continue;
        }

        Entity target = entity.GetBuffTarget();
        if (!target.Exists() || !target.TryGetBuffer(out DynamicBuffer<DropTableBuffer> dropTableBuffer) || dropTableBuffer.IsEmpty) {
          continue;
        }

        PrefabGUID fishingAreaPrefab = dropTableBuffer[0].DropTableGuid;
        ProfessionService.HandleFishingEvent(new FishingEventData(player, target, fishingAreaPrefab));
      }
    } finally {
      entities.Dispose();
    }
  }
}