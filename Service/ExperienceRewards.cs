using System;
using System.Collections.Generic;
using System.Globalization;
using CelemProfessions.Models;
using ProjectM;
using ProjectM.Shared;
using ScarletCore.Resources;
using ScarletCore.Services;
using ScarletCore.Systems;
using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace CelemProfessions.Service;

public static partial class ProfessionService {
  public static string FormatPercent(double percent) {
    return Math.Clamp(percent, 0d, 99.999d).ToString("0.000", PercentCulture);
  }

  public static string FormatExperience(double value) {
    return Math.Round(Math.Max(0d, value), 0).ToString("N0", CultureInfo.InvariantCulture);
  }

  private static void AddExperience(PlayerData player, ProfessionsTypes profession, double baseValue, out ProfessionProgressData progress, out double gainedExperience, out bool leveledUp) {
    gainedExperience = 0d;
    leveledUp = false;
    if (player == null) {
      progress = new ProfessionProgressData();
      return;
    }

    PlayerProfessionsData playerData = EnsurePlayerData(player.PlatformId);
    progress = GetProfessionProgress(playerData, profession);
    int previousLevel = progress.Level;
    double previousExperience = progress.Experience;
    int maxLevel = ProgressionService.GetMaxLevel();
    double maxExperience = ProgressionService.GetLevelStartExperience(maxLevel);
    if (progress.Level >= maxLevel) {
      if (Math.Abs(progress.Experience - maxExperience) > double.Epsilon) {
        progress.Experience = maxExperience;
        SavePlayerData(playerData);
      }

      return;
    }

    double xpMultiplier = ProfessionSettingsService.GetXpMultiplier(profession);
    double resolvedExperience = Math.Floor(Math.Max(0d, baseValue) * xpMultiplier);
    if (resolvedExperience <= 0d) {
      return;
    }

    progress.Experience = Math.Min(maxExperience, previousExperience + resolvedExperience);
    progress.Level = ProgressionService.GetLevelFromExperience(progress.Experience);
    if (progress.Level >= maxLevel) {
      progress.Level = maxLevel;
      progress.Experience = maxExperience;
    }

    gainedExperience = Math.Max(0d, progress.Experience - previousExperience);
    leveledUp = progress.Level > previousLevel;
    SavePlayerData(playerData);
    NotifyExperienceGain(player, playerData, profession, progress, gainedExperience, leveledUp);
  }

  private static void NotifyExperienceGain(PlayerData player, PlayerProfessionsData playerData, ProfessionsTypes profession, ProfessionProgressData progress, double gainedExperience, bool leveledUp) {
    if (player == null || gainedExperience <= 0d) {
      return;
    }

    string displayName = ProfessionCatalogService.GetDisplayName(profession);
    if (leveledUp) {
      MessageService.SendSuccess(player, $"{displayName} subiu para o nivel {progress.Level}.");
    }

    if (playerData.ExperienceLogEnabled) {
      if (progress.Level >= ProgressionService.GetMaxLevel()) {
        MessageService.SendInfo(player, $"+{FormatExperience(gainedExperience)} XP em {displayName}.");
      } else {
        double levelPercent = ProgressionService.GetLevelPercent(progress.Experience, progress.Level);
        MessageService.SendInfo(player, $"+{FormatExperience(gainedExperience)} XP em {displayName} ({FormatPercent(levelPercent)}%).");
      }
    }

    if (playerData.ExperienceSctEnabled) {
      float3 color = ParseHexColor(ProfessionCatalogService.GetColorHex(profession), FallbackSctColor);
      MessageService.SendSCT(player, XpSctPrefab, "4210316d-23d4-4274-96f5-d6f0944bd0bb", color, ToDisplayExperienceValue(gainedExperience));
    }
  }

  private static void HandleMinerRewards(PlayerData player, PrefabGUID yieldPrefab, int extraAtMaxLevel, bool goldEnabled) {
    if (player == null || yieldPrefab.IsEmpty()) {
      return;
    }

    ProfessionProgressData mineradorProgress = GetProfessionProgress(EnsurePlayerData(player.PlatformId), ProfessionsTypes.Minerador);
    int extraReward = CalculateScaledExtraBonus(mineradorProgress.Level, extraAtMaxLevel);
    if (extraReward > 0) {
      GiveReward(player, ProfessionsTypes.Minerador, yieldPrefab, extraReward);
    }

    foreach (KeyValuePair<int, int> choice in mineradorProgress.PassiveChoices) {
      int milestone = choice.Key;
      int option = choice.Value;
      if (!PassiveConfigService.TryGetOptionEffect(ProfessionsTypes.Minerador, option, milestone, out PassiveOptionEffectEntry effect)) {
        continue;
      }

      double chancePercent = ResolvePassiveChancePercent(effect);
      if (chancePercent <= 0d || !RollChance(chancePercent / 100d) || effect.RewardPrefabGUID == 0) {
        continue;
      }

      PrefabGUID rewardPrefab = new(effect.RewardPrefabGUID);
      if (option == 1 && !goldEnabled) {
        continue;
      }

      if (option == 2 && rewardPrefab.GuidHash != yieldPrefab.GuidHash) {
        continue;
      }

      GiveReward(player, ProfessionsTypes.Minerador, rewardPrefab, Math.Max(1, effect.Amount));
    }

    if (ProfessionCatalogService.IsGemPrefab(yieldPrefab)) {
      HandleJewelerGemNodeRewards(player);
    }
  }

  private static void HandleWoodRewards(PlayerData player, PrefabGUID yieldPrefab, PrefabGUID seedPrefab, int passiveTier, int extraAtMaxLevel) {
    if (player == null || yieldPrefab.IsEmpty()) {
      return;
    }

    ProfessionProgressData lenhadorProgress = GetProfessionProgress(EnsurePlayerData(player.PlatformId), ProfessionsTypes.Lenhador);
    int extraReward = CalculateScaledExtraBonus(lenhadorProgress.Level, extraAtMaxLevel);
    if (extraReward > 0) {
      GiveReward(player, ProfessionsTypes.Lenhador, yieldPrefab, extraReward);
    }

    TryGrantBaseGatherSeedReward(player, ProfessionsTypes.Lenhador, lenhadorProgress.Level, seedPrefab, ProfessionSettingsService.LenhadorSpecialDropChanceAtMax);
    if (passiveTier <= 0 || !lenhadorProgress.PassiveChoices.TryGetValue(passiveTier, out int selectedOption)) {
      return;
    }

    if (!PassiveConfigService.TryGetOptionEffect(ProfessionsTypes.Lenhador, selectedOption, passiveTier, out PassiveOptionEffectEntry effect)) {
      return;
    }

    double chancePercent = ResolvePassiveChancePercent(effect);
    if (chancePercent <= 0d || !RollChance(chancePercent / 100d)) {
      return;
    }

    if (selectedOption == 1) {
      if (seedPrefab.IsEmpty()) {
        return;
      }

      GiveReward(player, ProfessionsTypes.Lenhador, seedPrefab, Math.Max(1, effect.Amount));
      return;
    }

    GiveReward(player, ProfessionsTypes.Lenhador, yieldPrefab, Math.Max(1, effect.Amount));
  }

  private static void HandleHerbalRewards(PlayerData player, PrefabGUID yieldPrefab, PrefabGUID seedPrefab, int passiveTier, int extraAtMaxLevel) {
    if (player == null || yieldPrefab.IsEmpty()) {
      return;
    }

    ProfessionProgressData herbalistaProgress = GetProfessionProgress(EnsurePlayerData(player.PlatformId), ProfessionsTypes.Herbalista);
    int extraReward = CalculateScaledExtraBonus(herbalistaProgress.Level, extraAtMaxLevel);
    if (extraReward > 0) {
      GiveReward(player, ProfessionsTypes.Herbalista, yieldPrefab, extraReward);
    }

    TryGrantBaseGatherSeedReward(player, ProfessionsTypes.Herbalista, herbalistaProgress.Level, seedPrefab, ProfessionSettingsService.HerbalistaSpecialDropChanceAtMax);
    if (passiveTier <= 0 || !herbalistaProgress.PassiveChoices.TryGetValue(passiveTier, out int selectedOption)) {
      return;
    }

    if (!PassiveConfigService.TryGetOptionEffect(ProfessionsTypes.Herbalista, selectedOption, passiveTier, out PassiveOptionEffectEntry effect)) {
      return;
    }

    double chancePercent = ResolvePassiveChancePercent(effect);
    if (chancePercent <= 0d || !RollChance(chancePercent / 100d)) {
      return;
    }

    if (selectedOption == 1) {
      if (seedPrefab.IsEmpty()) {
        return;
      }

      GiveReward(player, ProfessionsTypes.Herbalista, seedPrefab, Math.Max(1, effect.Amount));
      return;
    }

    GiveReward(player, ProfessionsTypes.Herbalista, yieldPrefab, Math.Max(1, effect.Amount));
  }

  private static void TryGrantBaseGatherSeedReward(PlayerData player, ProfessionsTypes profession, int professionLevel, PrefabGUID seedPrefab, double chanceAtMaxLevel) {
    if (player == null || professionLevel <= 0 || seedPrefab.IsEmpty() || chanceAtMaxLevel <= 0d) {
      return;
    }

    double chance = chanceAtMaxLevel * professionLevel / 100d;
    if (!RollChance(chance)) {
      return;
    }

    GiveReward(player, profession, seedPrefab, 1);
  }

  private static void HandleJewelerGemNodeRewards(PlayerData player) {
    if (player == null) {
      return;
    }

    ProfessionProgressData joalheiroProgress = GetProfessionProgress(EnsurePlayerData(player.PlatformId), ProfessionsTypes.Joalheiro);
    foreach (KeyValuePair<int, int> choice in joalheiroProgress.PassiveChoices) {
      if (choice.Value != 1) {
        continue;
      }

      if (!PassiveConfigService.TryGetOptionEffect(ProfessionsTypes.Joalheiro, 1, choice.Key, out PassiveOptionEffectEntry effect)) {
        continue;
      }

      double chancePercent = ResolvePassiveChancePercent(effect);
      if (chancePercent <= 0d || !RollChance(chancePercent / 100d)) {
        continue;
      }

      if (RewardConfigService.TryGetRandomGemReward(joalheiroProgress.Level, out PrefabGUID rewardPrefab)) {
        GiveReward(player, ProfessionsTypes.Joalheiro, rewardPrefab, Math.Max(1, effect.Amount));
      }
    }
  }

  private static void HandleHunterPassiveRewards(PlayerData player, PrefabGUID leatherPrefab, bool aggressiveTarget) {
    if (player == null || leatherPrefab.IsEmpty()) {
      return;
    }

    int requiredOption = aggressiveTarget ? 2 : 1;
    ProfessionProgressData cacadorProgress = GetProfessionProgress(EnsurePlayerData(player.PlatformId), ProfessionsTypes.Cacador);
    foreach (KeyValuePair<int, int> choice in cacadorProgress.PassiveChoices) {
      if (choice.Value != requiredOption) {
        continue;
      }

      if (!PassiveConfigService.TryGetOptionEffect(ProfessionsTypes.Cacador, requiredOption, choice.Key, out PassiveOptionEffectEntry effect)) {
        continue;
      }

      double chancePercent = ResolvePassiveChancePercent(effect);
      if (chancePercent <= 0d || !RollChance(chancePercent / 100d)) {
        continue;
      }

      GiveReward(player, ProfessionsTypes.Cacador, leatherPrefab, Math.Max(1, effect.Amount));
    }
  }

  private static void HandleFishingRewards(PlayerData player, PrefabGUID fishingAreaPrefab, int professionLevel) {
    if (player == null) {
      return;
    }

    if (RollChance(ProfessionSettingsService.PescadorFishChanceAtMax * professionLevel / 100d)
        && RewardConfigService.TryGetRandomFishingReward(fishingAreaPrefab, professionLevel, out PrefabGUID baseRewardPrefab)) {
      GiveReward(player, ProfessionsTypes.Pescador, baseRewardPrefab, 1);
    }

    ProfessionProgressData pescadorProgress = GetProfessionProgress(EnsurePlayerData(player.PlatformId), ProfessionsTypes.Pescador);
    ProfessionCatalogService.TryResolveFishingRegion(fishingAreaPrefab, out string normalizedRegion);

    foreach (KeyValuePair<int, int> choice in pescadorProgress.PassiveChoices) {
      int option = choice.Value;
      if (!PassiveConfigService.TryGetOptionEffect(ProfessionsTypes.Pescador, option, choice.Key, out PassiveOptionEffectEntry effect)) {
        continue;
      }

      double chancePercent = ResolvePassiveChancePercent(effect);
      if (chancePercent <= 0d || !RollChance(chancePercent / 100d)) {
        continue;
      }

      if (option == 1) {
        if (effect.RewardPrefabGUID == 0) {
          continue;
        }

        GiveReward(player, ProfessionsTypes.Pescador, new PrefabGUID(effect.RewardPrefabGUID), Math.Max(1, effect.Amount));
        continue;
      }

      if (option != 2 || string.IsNullOrWhiteSpace(normalizedRegion)) {
        continue;
      }

      bool regionAllowed = false;
      for (int r = 0; r < effect.Regions.Count; r++) {
        if (PassiveConfigService.NormalizeRegion(effect.Regions[r]) == normalizedRegion) {
          regionAllowed = true;
          break;
        }
      }

      if (!regionAllowed || !RewardConfigService.TryGetRandomFishingReward(fishingAreaPrefab, professionLevel, out PrefabGUID regionRewardPrefab)) {
        continue;
      }

      GiveReward(player, ProfessionsTypes.Pescador, regionRewardPrefab, Math.Max(1, effect.Amount));
    }
  }

  private static void ApplyJewelerCraftDurabilityPassive(ulong platformId, Entity itemEntity, PrefabGUID itemPrefab) {
    ProfessionProgressData progress = GetProfessionProgress(EnsurePlayerData(platformId), ProfessionsTypes.Joalheiro);
    double baseBonusPercent = CalculateScaledPercentBonus(progress.Level, ProfessionSettingsService.JoalheiroDurabilityBonusAtMax);
    double passiveBonusPercent = SumSelectedPassiveBonusPercent(platformId, ProfessionsTypes.Joalheiro, requiredOption: 2, itemPrefab, ResolveCraftCategoryOption.Joalheiro);
    ApplyDurabilityBonus(itemEntity, itemPrefab, baseBonusPercent + passiveBonusPercent);
  }

  private static void ApplyTailorCraftDurabilityPassive(ulong platformId, Entity itemEntity, PrefabGUID itemPrefab) {
    ProfessionProgressData progress = GetProfessionProgress(EnsurePlayerData(platformId), ProfessionsTypes.Alfaiate);
    double baseBonusPercent = CalculateScaledPercentBonus(progress.Level, ProfessionSettingsService.AlfaiateDurabilityBonusAtMax);
    double passiveBonusPercent = SumSelectedPassiveBonusPercent(platformId, ProfessionsTypes.Alfaiate, requiredOption: 0, itemPrefab, ResolveCraftCategoryOption.Alfaiate);
    ApplyDurabilityBonus(itemEntity, itemPrefab, baseBonusPercent + passiveBonusPercent);
  }

  private static void ApplyBlacksmithCraftDurabilityPassive(ulong platformId, Entity itemEntity, PrefabGUID itemPrefab) {
    ProfessionProgressData progress = GetProfessionProgress(EnsurePlayerData(platformId), ProfessionsTypes.Ferreiro);
    double baseBonusPercent = CalculateScaledPercentBonus(progress.Level, ProfessionSettingsService.FerreiroDurabilityBonusAtMax);
    double passiveBonusPercent = SumSelectedPassiveBonusPercent(platformId, ProfessionsTypes.Ferreiro, requiredOption: 0, itemPrefab, ResolveCraftCategoryOption.Ferreiro);
    ApplyDurabilityBonus(itemEntity, itemPrefab, baseBonusPercent + passiveBonusPercent);
  }

  private enum ResolveCraftCategoryOption {
    Joalheiro,
    Alfaiate,
    Ferreiro
  }

  private static int ResolveOptionForCraftItem(PrefabGUID itemPrefab, ResolveCraftCategoryOption category) {
    string itemName = ProfessionCatalogService.GetNormalizedPrefabName(itemPrefab);
    if (string.IsNullOrWhiteSpace(itemName)) {
      return 0;
    }

    return category switch {
      ResolveCraftCategoryOption.Joalheiro => 2,
      ResolveCraftCategoryOption.Alfaiate => itemName.Contains("_rogue", StringComparison.Ordinal) || itemName.Contains("_warrior", StringComparison.Ordinal)
        ? 1
        : (itemName.Contains("_brute", StringComparison.Ordinal) || itemName.Contains("_scholar", StringComparison.Ordinal) ? 2 : 0),
      ResolveCraftCategoryOption.Ferreiro => itemName.Contains("crossbow", StringComparison.Ordinal) || itemName.Contains("longbow", StringComparison.Ordinal) || itemName.Contains("daggers", StringComparison.Ordinal)
        ? 2
        : 1,
      _ => 0
    };
  }

  private static double SumSelectedPassiveBonusPercent(ulong platformId, ProfessionsTypes profession, int requiredOption, PrefabGUID itemPrefab, ResolveCraftCategoryOption category) {
    int option = requiredOption > 0 ? requiredOption : ResolveOptionForCraftItem(itemPrefab, category);
    if (option <= 0) {
      return 0d;
    }

    ProfessionProgressData progress = GetProfessionProgress(EnsurePlayerData(platformId), profession);
    double totalBonusPercent = 0d;
    foreach (KeyValuePair<int, int> choice in progress.PassiveChoices) {
      if (choice.Value != option) {
        continue;
      }

      if (!PassiveConfigService.TryGetOptionEffect(profession, option, choice.Key, out PassiveOptionEffectEntry effect)) {
        continue;
      }

      totalBonusPercent += ResolvePassiveBonusPercent(effect);
    }

    return Math.Max(0d, totalBonusPercent);
  }
  private static void ResolveAlquimistaBuffMultipliers(ulong platformId, out double powerMultiplier, out double durationMultiplier) {
    powerMultiplier = 1d;
    durationMultiplier = 1d;

    ProfessionProgressData progress = GetProfessionProgress(EnsurePlayerData(platformId), ProfessionsTypes.Alquimista);
    double totalDurationPercent = CalculateScaledPercentBonus(progress.Level, ProfessionSettingsService.AlquimistaDurationBonusAtMax);
    double totalPowerPercent = CalculateScaledPercentBonus(progress.Level, ProfessionSettingsService.AlquimistaPowerBonusAtMax);

    foreach (KeyValuePair<int, int> choice in progress.PassiveChoices) {
      int option = choice.Value;
      if (!PassiveConfigService.TryGetOptionEffect(ProfessionsTypes.Alquimista, option, choice.Key, out PassiveOptionEffectEntry effect)) {
        continue;
      }

      double bonusPercent = ResolvePassiveBonusPercent(effect);
      if (bonusPercent <= 0d) {
        continue;
      }

      if (option == 1) {
        totalDurationPercent += bonusPercent;
      } else if (option == 2) {
        totalPowerPercent += bonusPercent;
      }
    }

    powerMultiplier = 1d + totalPowerPercent / 100d;
    durationMultiplier = 1d + totalDurationPercent / 100d;
  }

  private static double ResolvePassiveChancePercent(PassiveOptionEffectEntry effect) {
    if (effect == null) {
      return 0d;
    }

    return effect.ChancePercent > 0d
      ? Math.Max(0d, effect.ChancePercent)
      : Math.Max(0d, effect.BonusPercent);
  }

  private static double ResolvePassiveBonusPercent(PassiveOptionEffectEntry effect) {
    if (effect == null) {
      return 0d;
    }

    return effect.BonusPercent > 0d
      ? Math.Max(0d, effect.BonusPercent)
      : Math.Max(0d, effect.ChancePercent);
  }
  private static double CalculateScaledPercentBonus(int professionLevel, double bonusAtMaxLevelFraction) {
    if (professionLevel <= 0 || bonusAtMaxLevelFraction <= 0d) {
      return 0d;
    }

    int clampedLevel = Math.Clamp(professionLevel, ProgressionService.GetStartingLevel(), ProgressionService.GetMaxLevel());
    double scaledFraction = bonusAtMaxLevelFraction * clampedLevel / 100d;
    return Math.Max(0d, scaledFraction * 100d);
  }

  private static int CalculateScaledExtraBonus(int professionLevel, int extraAtMaxLevel) {
    if (professionLevel <= 0 || extraAtMaxLevel <= 0) {
      return 0;
    }

    double scaled = extraAtMaxLevel * professionLevel / 100d;
    return Math.Max(0, (int)Math.Floor(scaled));
  }

  private static void ApplyDurabilityBonus(Entity itemEntity, PrefabGUID itemPrefab, double bonusPercent) {
    if (!itemEntity.Exists() || !itemEntity.Has<Durability>() || bonusPercent <= 0d) {
      return;
    }

    Durability currentDurability = itemEntity.Read<Durability>();
    double multiplier = 1d + bonusPercent / 100d;
    if (multiplier <= 1d) {
      return;
    }

    float adjustedMax = (float)Math.Floor(currentDurability.MaxDurability * multiplier);
    if (adjustedMax <= currentDurability.MaxDurability) {
      return;
    }

    itemEntity.With<Durability>((ECSExtensions.WithRefHandler<Durability>)delegate(ref Durability value) {
      value.MaxDurability = adjustedMax;
      value.Value = adjustedMax;
    });
  }
  private static void ApplyConsumableBuffBonus(Entity buffEntity, double powerMultiplier, double durationMultiplier) {
    if (powerMultiplier > 1d && buffEntity.TryGetBuffer(out DynamicBuffer<ModifyUnitStatBuff_DOTS> statBuffs) && !statBuffs.IsEmpty) {
      for (int i = 0; i < statBuffs.Length; i++) {
        ModifyUnitStatBuff_DOTS entry = statBuffs[i];
        entry.Value = (float)(entry.Value * powerMultiplier);
        statBuffs[i] = entry;
      }
    }

    if (durationMultiplier > 1d && buffEntity.Has<LifeTime>()) {
      buffEntity.With<LifeTime>((ECSExtensions.WithRefHandler<LifeTime>)delegate(ref LifeTime lifeTime) {
        if (lifeTime.Duration > 0f) {
          lifeTime.Duration = (float)(lifeTime.Duration * durationMultiplier);
        }
      });
    }

    if (durationMultiplier > 1d && buffEntity.Has<HealOnGameplayEvent>() && buffEntity.TryGetBuffer(out DynamicBuffer<CreateGameplayEventsOnTick> tickEvents) && !tickEvents.IsEmpty) {
      for (int i = 0; i < tickEvents.Length; i++) {
        CreateGameplayEventsOnTick entry = tickEvents[i];
        entry.MaxTicks = Math.Max(1, (int)Math.Floor(entry.MaxTicks * durationMultiplier));
        tickEvents[i] = entry;
      }
    }
  }

  private static void GiveReward(PlayerData player, ProfessionsTypes profession, PrefabGUID itemPrefab, int amount) {
    if (player == null || !player.CharacterEntity.Exists() || amount <= 0) {
      return;
    }

    bool addedToInventory = InventoryService.AddItem(player.CharacterEntity, itemPrefab, amount);
    if (!addedToInventory) {
      InventoryService.CreateDropItem(player.CharacterEntity, itemPrefab, amount);
    }

    string displayName = ProfessionCatalogService.GetDisplayName(profession);
    string localizedItemName = itemPrefab.LocalizedName(player.Language);
    if (addedToInventory) {
      MessageService.SendSuccess(player, $"{amount}x {localizedItemName} extra recebido de {displayName}.");
    } else {
      MessageService.SendWarning(player, $"{amount}x {localizedItemName} extra de {displayName} caiu no chao (inventario cheio).");
    }
  }

  private static bool TryResolveCraftProfession(PrefabGUID itemPrefab, out ProfessionsTypes profession) {
    profession = ProfessionsTypes.Minerador;
    if (ProfessionExperienceConfigService.IsAlchemyCraftConfigured(itemPrefab)) {
      profession = ProfessionsTypes.Alquimista;
      return true;
    }

    if (ProfessionExperienceConfigService.IsJewelerCraftConfigured(itemPrefab)) {
      profession = ProfessionsTypes.Joalheiro;
      return true;
    }

    if (ProfessionExperienceConfigService.IsTailorCraftConfigured(itemPrefab)) {
      profession = ProfessionsTypes.Alfaiate;
      return true;
    }

    if (ProfessionExperienceConfigService.IsBlacksmithCraftConfigured(itemPrefab)) {
      profession = ProfessionsTypes.Ferreiro;
      return true;
    }

    return false;
  }
  private static bool RollChance(double chance) {
    if (chance <= 0d) {
      return false;
    }

    if (chance >= 1d) {
      return true;
    }

    return Random.NextDouble() <= chance;
  }

  private static bool TryResolveOnlinePlayer(ulong platformId, out PlayerData player) {
    if (platformId.TryGetPlayerData(out player)) {
      return player != null;
    }

    return false;
  }

  private static bool IsConsumableBuff(PrefabGUID buffPrefab) {
    string buffName = ProfessionCatalogService.GetNormalizedPrefabName(buffPrefab);
    if (!buffName.Contains("consumable", StringComparison.Ordinal) && !buffName.Contains("potion", StringComparison.Ordinal) && !buffName.Contains("elixir", StringComparison.Ordinal) && !buffName.Contains("coating", StringComparison.Ordinal) && !buffName.Contains("salve", StringComparison.Ordinal) && !buffName.Contains("brew", StringComparison.Ordinal)) {
      return buffName.Contains("canteen", StringComparison.Ordinal);
    }

    return true;
  }

  private static float3 ParseHexColor(string hex, float3 fallback) {
    if (string.IsNullOrWhiteSpace(hex)) {
      return fallback;
    }

    string normalized = hex.Trim().TrimStart('#');
    if (normalized.Length != 6) {
      return fallback;
    }

    if (!byte.TryParse(normalized.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red)
      || !byte.TryParse(normalized.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green)
      || !byte.TryParse(normalized.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue)) {
      return fallback;
    }

    return new float3(red / 255f, green / 255f, blue / 255f);
  }

  private static int ToDisplayExperienceValue(double value) {
    return (int)Math.Max(1d, Math.Round(Math.Max(0d, value)));
  }
}















