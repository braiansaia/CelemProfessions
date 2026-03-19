using CelemProfessions.Service;
using HarmonyLib;
using ProjectM;
using ScarletCore.Systems;

namespace CelemProfessions.Patches;

[HarmonyPatch]
public static class CraftingSystemPatches {
  [HarmonyPatch(typeof(StartCraftingSystem), nameof(StartCraftingSystem.OnUpdate))]
  [HarmonyPrefix]
  public static void StartCraftingPrefix(StartCraftingSystem __instance) {
    if (!GameSystems.Initialized) {
      return;
    }

    CraftTrackingService.HandleStartCrafting(__instance);
  }

  [HarmonyPatch(typeof(StopCraftingSystem), nameof(StopCraftingSystem.OnUpdate))]
  [HarmonyPrefix]
  public static void StopCraftingPrefix(StopCraftingSystem __instance) {
    if (!GameSystems.Initialized) {
      return;
    }

    CraftTrackingService.HandleStopCrafting(__instance);
  }

  [HarmonyPatch(typeof(MoveItemBetweenInventoriesSystem), nameof(MoveItemBetweenInventoriesSystem.OnUpdate))]
  [HarmonyPrefix]
  public static void MoveItemPrefix(MoveItemBetweenInventoriesSystem __instance) {
    if (!GameSystems.Initialized) {
      return;
    }

    CraftTrackingService.HandleMoveItem(__instance);
  }

  [HarmonyPatch(typeof(UpdateCraftingSystem), nameof(UpdateCraftingSystem.OnUpdate))]
  [HarmonyPrefix]
  public static void UpdateCraftingPrefix(UpdateCraftingSystem __instance) {
    if (!GameSystems.Initialized) {
      return;
    }

    CraftTrackingService.HandleUpdateCrafting(__instance);
  }

  [HarmonyPatch(typeof(UpdatePrisonSystem), nameof(UpdatePrisonSystem.OnUpdate))]
  [HarmonyPrefix]
  public static void UpdatePrisonPrefix(UpdatePrisonSystem __instance) {
    if (!GameSystems.Initialized) {
      return;
    }

    CraftTrackingService.HandleUpdatePrison(__instance);
  }
}