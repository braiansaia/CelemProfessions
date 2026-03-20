using System;
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

    PrefabGUID yieldPrefab = yields[0].ItemType;
    if (TryResolveGatherProfession(yieldPrefab, out ProfessionsTypes profession)) {
      HandleGatherEvent(new GatherEventData(player, targetPrefab, yieldPrefab, profession));
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
      case ProfessionsTypes.Minerador:
        HandleMinerRewards(gatherEvent.Player, gatherEvent.YieldPrefab, progress.Level, extraAtMaxLevel);
        break;
      case ProfessionsTypes.Lenhador:
        HandleWoodRewards(gatherEvent.Player, gatherEvent.YieldPrefab, progress.Level, extraAtMaxLevel);
        break;
      case ProfessionsTypes.Herbalista:
        HandleHerbalRewards(gatherEvent.Player, gatherEvent.YieldPrefab, progress.Level, extraAtMaxLevel);
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

    AddExperience(hunterEvent.Player, ProfessionsTypes.Cacador, baseValue, out ProfessionProgressData progress, out _, out _);
    int extraReward = CalculateScaledExtraBonus(progress.Level, extraAtMaxLevel);
    if (extraReward > 0) {
      GiveReward(hunterEvent.Player, ProfessionsTypes.Cacador, leatherPrefab, extraReward);
    }
  }

  public static void HandleFishingEvent(in FishingEventData fishingEvent) {
    if (fishingEvent.Player == null || !fishingEvent.Player.CharacterEntity.Exists()) {
      return;
    }

    AddExperience(fishingEvent.Player, ProfessionsTypes.Pescador, ProfessionSettingsService.FishingBaseXp, out ProfessionProgressData progress, out _, out _);
    if (!RollChance(ProfessionSettingsService.PescadorFishChanceAtMax * progress.Level / 100d)) {
      return;
    }

    if (RewardConfigService.TryGetRandomFishingReward(fishingEvent.FishingAreaPrefab, progress.Level, out PrefabGUID rewardPrefab)) {
      GiveReward(fishingEvent.Player, ProfessionsTypes.Pescador, rewardPrefab, 1);
    }
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

    HandleFishingEvent(new FishingEventData(player, dropTableBuffer[0].DropTableGuid));
  }

  private static double ResolveGatherBaseExperience(in GatherEventData gatherEvent, out int extraAtMaxLevel) {
    extraAtMaxLevel = 0;
    return gatherEvent.Profession switch {
      ProfessionsTypes.Minerador or ProfessionsTypes.Lenhador or ProfessionsTypes.Herbalista => ProfessionExperienceConfigService.TryGetGatherConfiguration(gatherEvent.Profession, gatherEvent.TargetPrefab, out double configuredExperience, out extraAtMaxLevel)
        ? Math.Max(0d, configuredExperience)
        : 0d,
      _ => 0d
    };
  }

  private static bool TryResolveCraftBaseExperience(ProfessionsTypes profession, PrefabGUID itemPrefab, out double baseValue) {
    baseValue = 0d;
    return profession switch {
      ProfessionsTypes.Joalheiro or ProfessionsTypes.Alfaiate or ProfessionsTypes.Ferreiro => TryResolveDurabilityBasedCraftExperience(itemPrefab, out baseValue),
      ProfessionsTypes.Alquimista => ProfessionExperienceConfigService.TryGetAlchemyCraftExperience(itemPrefab, out baseValue),
      _ => false
    };
  }

  private static bool TryResolveDurabilityBasedCraftExperience(PrefabGUID itemPrefab, out double baseValue) {
    baseValue = 0d;
    if (!GameSystems.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(itemPrefab, out Entity prefabEntity) || !prefabEntity.Exists() || !prefabEntity.TryGetComponent(out Durability durability) || durability.MaxDurability <= 0f) {
      return false;
    }

    baseValue = Math.Max(0d, durability.MaxDurability);
    return baseValue > 0d;
  }

  public static void HandleCraftedItem(PlayerData player, Entity itemEntity, PrefabGUID itemPrefab, int amount) {
    if (player == null || itemPrefab.IsEmpty() || amount <= 0) {
      return;
    }

    if (!TryResolveCraftProfession(itemPrefab, out ProfessionsTypes profession)) {
      return;
    }

    if (!TryResolveCraftBaseExperience(profession, itemPrefab, out double baseValue)) {
      return;
    }

    AddExperience(player, profession, baseValue, out ProfessionProgressData progress, out _, out _);
    switch (profession) {
      case ProfessionsTypes.Joalheiro:
        ApplyDurabilityBonus(itemEntity, itemPrefab, progress.Level, ProfessionSettingsService.JoalheiroDurabilityBonusAtMax);
        HandleJewelCraftRewards(player, progress.Level);
        break;
      case ProfessionsTypes.Alfaiate:
        ApplyDurabilityBonus(itemEntity, itemPrefab, progress.Level, ProfessionSettingsService.AlfaiateDurabilityBonusAtMax);
        break;
      case ProfessionsTypes.Ferreiro:
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

    ProfessionProgressData progress = GetProfessionProgress(EnsurePlayerData(playerData.PlatformId), ProfessionsTypes.Alquimista);
    double powerMultiplier = 1d + ProfessionSettingsService.AlquimistaPowerBonusAtMax * progress.Level / 100d;
    double durationMultiplier = 1d + ProfessionSettingsService.AlquimistaDurationBonusAtMax * progress.Level / 100d;
    if (powerMultiplier <= 1d && durationMultiplier <= 1d) {
      return;
    }

    ApplyConsumableBuffBonus(buffEntity, powerMultiplier, durationMultiplier);
    MessageService.SendInfo(playerData, $"Consumivel aprimorado aplicado: poder x{powerMultiplier:0.###} | duracao x{durationMultiplier:0.###}.");
  }
}
