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
    if (player == null || !target.Exists()) {
      return;
    }

    if (!ProfessionExperienceConfigService.TryResolveGatherProfession(targetPrefab, out ProfessionsTypes profession)) {
      return;
    }

    if (!ProfessionExperienceConfigService.TryGetGatherRewardConfiguration(profession, targetPrefab, out PrefabGUID configuredYield, out _, out _, out _, out _)) {
      return;
    }

    HandleGatherEvent(new GatherEventData(player, targetPrefab, configuredYield, profession));
  }

  public static void HandleGatherEvent(in GatherEventData gatherEvent) {
    if (gatherEvent.Player == null || !gatherEvent.Player.CharacterEntity.Exists()) {
      return;
    }

    bool hasRewardConfig = ProfessionExperienceConfigService.TryGetGatherRewardConfiguration(
      gatherEvent.Profession,
      gatherEvent.TargetPrefab,
      out PrefabGUID configuredYield,
      out PrefabGUID configuredSeed,
      out int extraAtMaxLevel,
      out bool goldEnabled,
      out int passiveTier);

    double baseValue = ResolveGatherBaseExperience(gatherEvent);
    if (baseValue <= 0d && !hasRewardConfig) {
      return;
    }

    if (baseValue > 0d) {
      AddExperience(gatherEvent.Player, gatherEvent.Profession, baseValue, out _, out _, out _);
    }

    PrefabGUID rewardYield = configuredYield;
    switch (gatherEvent.Profession) {
      case ProfessionsTypes.Minerador:
        HandleMinerRewards(gatherEvent.Player, rewardYield, extraAtMaxLevel, goldEnabled);
        break;
      case ProfessionsTypes.Lenhador:
        HandleWoodRewards(gatherEvent.Player, rewardYield, configuredSeed, passiveTier, extraAtMaxLevel);
        break;
      case ProfessionsTypes.Herbalista:
        HandleHerbalRewards(gatherEvent.Player, rewardYield, configuredSeed, passiveTier, extraAtMaxLevel);
        break;
    }
  }

  public static void HandleHunterKillEvent(in HunterKillEventData hunterEvent) {
    if (hunterEvent.Player == null || !hunterEvent.Target.Exists() || hunterEvent.Target.IsPlayer()) {
      return;
    }

    if (!ProfessionExperienceConfigService.TryGetHunterConfiguration(hunterEvent.TargetPrefab, out double baseValue, out PrefabGUID leatherPrefab, out int extraAtMaxLevel, out bool aggressive)) {
      return;
    }

    AddExperience(hunterEvent.Player, ProfessionsTypes.Cacador, baseValue, out ProfessionProgressData progress, out _, out _);
    int extraReward = CalculateScaledExtraBonus(progress.Level, extraAtMaxLevel);
    if (extraReward > 0) {
      GiveReward(hunterEvent.Player, ProfessionsTypes.Cacador, leatherPrefab, extraReward);
    }

    HandleHunterPassiveRewards(hunterEvent.Player, leatherPrefab, aggressive);
  }

  public static void HandleFishingEvent(in FishingEventData fishingEvent) {
    if (fishingEvent.Player == null || !fishingEvent.Player.CharacterEntity.Exists()) {
      return;
    }

    double fishingExperience = ProfessionSettingsService.FishingBaseXp;
    if (ProfessionExperienceConfigService.TryGetFishingRegionExtraExperience(fishingEvent.FishingAreaPrefab, out double regionalExtraExperience)) {
      fishingExperience += regionalExtraExperience;
    }

    AddExperience(fishingEvent.Player, ProfessionsTypes.Pescador, fishingExperience, out ProfessionProgressData progress, out _, out _);
    HandleFishingRewards(fishingEvent.Player, fishingEvent.FishingAreaPrefab, progress.Level);
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

  private static double ResolveGatherBaseExperience(in GatherEventData gatherEvent) {
    return gatherEvent.Profession switch {
      ProfessionsTypes.Minerador or ProfessionsTypes.Lenhador or ProfessionsTypes.Herbalista => ProfessionExperienceConfigService.TryGetGatherConfiguration(gatherEvent.Profession, gatherEvent.TargetPrefab, out double configuredExperience, out _)
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

    double experienceToAdd = profession == ProfessionsTypes.Alquimista
      ? Math.Max(0d, baseValue) * amount
      : baseValue;
    if (experienceToAdd <= 0d) {
      return;
    }

    AddExperience(player, profession, experienceToAdd, out _, out _, out _);
    switch (profession) {
      case ProfessionsTypes.Joalheiro:
        ApplyJewelerCraftDurabilityPassive(player.PlatformId, itemEntity, itemPrefab);
        break;
      case ProfessionsTypes.Alfaiate:
        ApplyTailorCraftDurabilityPassive(player.PlatformId, itemEntity, itemPrefab);
        break;
      case ProfessionsTypes.Ferreiro:
        ApplyBlacksmithCraftDurabilityPassive(player.PlatformId, itemEntity, itemPrefab);
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

    ResolveAlquimistaBuffMultipliers(playerData.PlatformId, out double powerMultiplier, out double durationMultiplier);
    if (powerMultiplier <= 1d && durationMultiplier <= 1d) {
      return;
    }

    ApplyConsumableBuffBonus(buffEntity, powerMultiplier, durationMultiplier);
    MessageService.SendInfo(playerData, $"Consumivel aprimorado aplicado: poder x{powerMultiplier:0.###} | duracao x{durationMultiplier:0.###}.");
  }
}



