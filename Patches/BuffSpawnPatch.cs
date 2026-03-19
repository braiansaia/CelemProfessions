using CelemProfessions.Service;
using HarmonyLib;
using ProjectM;
using ScarletCore.Systems;
using Unity.Collections;
using Unity.Entities;

namespace CelemProfessions.Patches;

[HarmonyPatch]
public static class BuffSpawnPatch {
  [HarmonyPatch(typeof(BuffSystem_Spawn_Server), nameof(BuffSystem_Spawn_Server.OnUpdate))]
  [HarmonyPostfix]
  public static void OnUpdatePostfix(BuffSystem_Spawn_Server __instance) {
    if (!GameSystems.Initialized) {
      return;
    }

    NativeArray<Entity> entities = __instance.EntityQueries[0].ToEntityArray(Allocator.Temp);

    try {
      for (int i = 0; i < entities.Length; i++) {
        ProfessionService.HandleBuffSpawn(entities[i]);
      }
    } finally {
      entities.Dispose();
    }
  }
}