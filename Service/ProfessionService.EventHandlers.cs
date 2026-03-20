using System;
using System.Collections.Generic;
using CelemProfessions.Events;
using CelemProfessions.Models;
using ProjectM;
using ProjectM.Shared;
using ScarletCore.Resources;
using ScarletCore.Services;
using ScarletCore.Systems;
using Stunlock.Core;
using Unity.Entities;

namespace CelemProfessions.Service;

public static partial class ProfessionService {
  private static readonly PrefabGUID FishingTravelToTarget = PrefabGUIDs.AB_Fishing_Draw_TravelToTarget;

  public static void HandleGatherFromEntity(PlayerData player, Entity target, PrefabGUID targetPrefab) {
    if (player == null || !target.Exists() || !target.TryGetBuffer(out DynamicBuffer<YieldResourcesOnDamageTaken> yields) || yields.IsEmpty) {
      return;
    }

    PrefabGUID itemType = yields[0].ItemType;
    if (TryResolveGatherProfession(itemType, out ProfessionType profession)) {
      HandleGatherEvent(new GatherEventData(player, targetPrefab, itemType, profession));
    }
  }

  public static void HandleGatherEvent(in GatherEventData gatherEvent) {
    if (gatherEvent.Player == null || !gatherEvent.Player.CharacterEntity.Exists()) {
      return;
    }

    double baseValue = ResolveGatherBaseExperience(gatherEvent, out int extraAtMaxLevel);
    if (baseValue <= 0d) {
      return;
    }

    AddExperience(gatherEvent.Player, gatherEvent.Profession, baseValue, out ProfessionProgressData progress, out _, out _);
    switch (gatherEvent.Profession) {
      case ProfessionType.Minerador:
        HandleMinerRewards(gatherEvent.Player, gatherEvent.YieldPrefab, progress.Level, extraAtMaxLevel);
        break;
      case ProfessionType.Lenhador:
        HandleWoodRewards(gatherEvent.Player, gatherEvent.YieldPrefab, progress.Level, extraAtMaxLevel);
        break;
      case ProfessionType.Herbalista:
        HandleHerbalRewards(gatherEvent.Player, gatherEvent.YieldPrefab, progress.Level, extraAtMaxLevel);
        break;
      case ProfessionType.Joalheiro:
        HandleJewelGatherRewards(gatherEvent.Player, gatherEvent.YieldPrefab, progress.Level);
        break;
    }
  }

  public static void HandleHunterKillEvent(in HunterKillEventData hunterEvent) {
    if (hunterEvent.Player == null || !hunterEvent.Target.Exists() || hunterEvent.Target.IsPlayer()) {
      return;
    }

    if (!ProfessionExperienceConfigService.TryGetHunterConfiguration(hunterEvent.TargetPrefab, out double baseValue, out PrefabGUID leatherPrefab, out int extraAtMaxLevel)) {
      return;
    }

    AddExperience(hunterEvent.Player, ProfessionType.Cacador, baseValue, out ProfessionProgressData progress, out _, out _);
    int extraReward = CalculateScaledExtraBonus(progress.Level, extraAtMaxLevel);
    if (extraReward > 0) {
      GiveReward(hunterEvent.Player, ProfessionType.Cacador, leatherPrefab, extraReward);
    }
  }

  public static void HandleFishingEvent(in FishingEventData fishingEvent) {
    if (fishingEvent.Player == null || !fishingEvent.Player.CharacterEntity.Exists()) {
      return;
    }

    AddExperience(fishingEvent.Player, ProfessionType.Pescador, ProfessionSettingsService.FishingBaseXp, out ProfessionProgressData progress, out _, out _);
    if (!RollChance(ProfessionSettingsService.PescadorExtraFishChanceAtMax * progress.Level / 100d)) {
      return;
    }

    List<PrefabGUID> fishingAreaDrops = ProfessionCatalogService.GetFishingAreaDrops(fishingEvent.FishingAreaPrefab);
    if (fishingAreaDrops.Count == 0) {
      return;
    }

    PrefabGUID itemPrefab = fishingAreaDrops[Random.Next(0, fishingAreaDrops.Count)];
    GiveReward(fishingEvent.Player, ProfessionType.Pescador, itemPrefab, ProfessionSettingsService.PescadorExtraFishAmount);
  }

  public static void HandleFishingGameplayEvent(Entity entity) {
    if (!entity.TryGetComponent(out EntityOwner owner) || !owner.Owner.Exists() || !entity.TryGetComponent(out PrefabGUID prefabGuid) || !prefabGuid.Equals(FishingTravelToTarget)) {
      return;
    }

    Entity playerCharacter = owner.Owner;
    if (!PlayerOwnershipService.TryResolveOwningPlayer(playerCharacter, out PlayerData player) || player == null) {
      return;
    }

    Entity target = entity.GetBuffTarget();
    if (!target.Exists() || !target.TryGetBuffer(out DynamicBuffer<DropTableBuffer> dropTableBuffer) || dropTableBuffer.IsEmpty) {
      return;
    }

    PrefabGUID fishingAreaPrefab = dropTableBuffer[0].DropTableGuid;
    HandleFishingEvent(new FishingEventData(player, fishingAreaPrefab));
  }

  private static double ResolveGatherBaseExperience(in GatherEventData gatherEvent, out int extraAtMaxLevel) {
    extraAtMaxLevel = 0;
    return gatherEvent.Profession switch {
      ProfessionType.Minerador or ProfessionType.Lenhador or ProfessionType.Herbalista or ProfessionType.Joalheiro => ProfessionExperienceConfigService.TryGetGatherConfiguration(gatherEvent.Profession, gatherEvent.TargetPrefab, out double configuredExperience, out extraAtMaxLevel)
        ? Math.Max(0d, configuredExperience)
        : 0d,
      _ => 0d
    };
  }

  private static bool TryResolveCraftBaseExperience(ProfessionType profession, PrefabGUID itemPrefab, out double baseValue) {
    baseValue = 0d;
    switch (profession) {
      case ProfessionType.Joalheiro:
      case ProfessionType.Alfaiate:
      case ProfessionType.Ferreiro:
        return TryResolveDurabilityBasedCraftExperience(itemPrefab, out baseValue);
      case ProfessionType.Alquimista:
        return ProfessionExperienceConfigService.TryGetAlchemyCraftExperience(itemPrefab, out baseValue);
      default:
        baseValue = 50d * ProfessionCatalogService.GetTierMultiplier(itemPrefab);
        return baseValue > 0d;
    }
  }

  private static bool TryResolveDurabilityBasedCraftExperience(PrefabGUID itemPrefab, out double baseValue) {
    baseValue = 0d;
    if (!GameSystems.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(itemPrefab, out Entity prefabEntity) || !prefabEntity.Exists() || !prefabEntity.TryGetComponent(out Durability durability) || durability.MaxDurability <= 0f) {
      return false;
    }

    baseValue = Math.Max(0d, durability.MaxDurability * DurabilityCraftExperienceFactor);
    return baseValue > 0d;
  }

  public static void HandleCraftedItem(PlayerData player, Entity itemEntity, PrefabGUID itemPrefab, int amount) {
    if (player == null || itemPrefab.IsEmpty() || amount <= 0) {
      return;
    }

    if (!TryResolveCraftProfession(itemPrefab, out ProfessionType profession)) {
      return;
    }

    if (!TryResolveCraftBaseExperience(profession, itemPrefab, out double baseValue)) {
      return;
    }

    AddExperience(player, profession, baseValue, out ProfessionProgressData progress, out _, out _);
    switch (profession) {
      case ProfessionType.Joalheiro:
        ApplyDurabilityBonus(itemEntity, itemPrefab, progress.Level, ProfessionSettingsService.JoalheiroDurabilityBonusAtMax);
        break;
      case ProfessionType.Alfaiate:
        ApplyDurabilityBonus(itemEntity, itemPrefab, progress.Level, ProfessionSettingsService.AlfaiateDurabilityBonusAtMax);
        break;
      case ProfessionType.Ferreiro:
        ApplyDurabilityBonus(itemEntity, itemPrefab, progress.Level, ProfessionSettingsService.FerreiroDurabilityBonusAtMax);
        break;
    }
  }

  public static void HandleBuffSpawn(Entity buffEntity) {
    if (!buffEntity.Exists() || !buffEntity.TryGetComponent(out Buff buff) || !buff.Target.Exists() || !buff.Target.IsPlayer() || !buffEntity.TryGetComponent(out PrefabGUID buffPrefab) || !IsConsumableBuff(buffPrefab)) {
      return;
    }

    PlayerData playerData = buff.Target.GetPlayerData();
    if (playerData == null) {
      return;
    }

    ProfessionProgressData professionProgress = GetProfessionProgress(EnsurePlayerData(playerData.PlatformId), ProfessionType.Alquimista);
    double powerMultiplier = 1d + ProfessionSettingsService.AlquimistaPowerBonusAtMax * professionProgress.Level / 100d;
    double durationMultiplier = 1d + ProfessionSettingsService.AlquimistaDurationBonusAtMax * professionProgress.Level / 100d;
    if (powerMultiplier <= 1d && durationMultiplier <= 1d) {
      return;
    }

    ApplyConsumableBuffBonus(buffEntity, powerMultiplier, durationMultiplier);
    MessageService.SendInfo(playerData, $"Consumivel aprimorado aplicado: poder x{powerMultiplier:0.###} | duracao x{durationMultiplier:0.###}.");
  }
}
