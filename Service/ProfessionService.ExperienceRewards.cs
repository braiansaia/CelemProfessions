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
    return Math.Clamp(percent, 0d, 99.999).ToString("0.000", PercentCulture);
  }

  public static string FormatExperience(double value) {
    return Math.Round(Math.Max(0d, value), 0).ToString("N0", CultureInfo.InvariantCulture);
  }

  private static void AddExperience(PlayerData player, ProfessionType profession, double baseValue, out ProfessionProgressData progress, out double gainedExperience, out bool leveledUp) {
    gainedExperience = 0d;
    leveledUp = false;
    if (player == null) {
      progress = new ProfessionProgressData();
      return;
    }

    PlayerProfessionsData playerProfessionsData = EnsurePlayerData(player.PlatformId);
    progress = GetProfessionProgress(playerProfessionsData, profession);
    int previousLevel = progress.Level;
    double previousExperience = progress.Experience;
    if (progress.Level >= 100) {
      double maxExperience = ConvertLevelToXp(100);
      if (progress.Experience != maxExperience) {
        progress.Experience = maxExperience;
        SavePlayerData(playerProfessionsData);
      }

      return;
    }

    double xpMultiplier = ProfessionSettingsService.GetXpMultiplier(profession);
    double resolvedExperience = Math.Floor(Math.Max(0d, baseValue) * xpMultiplier);
    if (resolvedExperience <= 0d) {
      return;
    }

    double maxLevelExperience = ConvertLevelToXp(100);
    progress.Experience = Math.Min(maxLevelExperience, previousExperience + resolvedExperience);
    progress.Level = ConvertXpToLevel(progress.Experience);
    if (progress.Level >= 100) {
      progress.Level = 100;
      progress.Experience = maxLevelExperience;
    }

    gainedExperience = Math.Max(0d, progress.Experience - previousExperience);
    leveledUp = progress.Level > previousLevel;
    SavePlayerData(playerProfessionsData);
    NotifyExperienceGain(player, playerProfessionsData, profession, progress, gainedExperience, leveledUp);
  }

  private static void NotifyExperienceGain(PlayerData player, PlayerProfessionsData playerData, ProfessionType profession, ProfessionProgressData progress, double gainedExperience, bool leveledUp) {
    if (player == null || gainedExperience <= 0d) {
      return;
    }

    string displayName = ProfessionCatalogService.GetDisplayName(profession);
    if (leveledUp) {
      MessageService.SendSuccess(player, $"{displayName} subiu para o nivel {progress.Level}.");
    }

    if (playerData.ExperienceLogEnabled) {
      if (progress.Level >= 100) {
        MessageService.SendInfo(player, $"+{FormatExperience(gainedExperience)} XP em {displayName}.");
      } else {
        double levelPercent = GetLevelPercent(progress.Experience, progress.Level);
        MessageService.SendInfo(player, $"+{FormatExperience(gainedExperience)} XP em {displayName} ({FormatPercent(levelPercent)}%).");
      }
    }

    if (playerData.ExperienceSctEnabled) {
      float3 color = ParseHexColor(ProfessionCatalogService.GetColorHex(profession), FallbackSctColor);
      MessageService.SendSCT(player, XpSctPrefab, "4210316d-23d4-4274-96f5-d6f0944bd0bb", color, ToDisplayExperienceValue(gainedExperience));
    }
  }

  private static void HandleMinerRewards(PlayerData player, PrefabGUID yieldPrefab, int professionLevel, int extraAtMaxLevel) {
    int extraReward = CalculateScaledExtraBonus(professionLevel, extraAtMaxLevel);
    if (extraReward > 0) {
      GiveReward(player, ProfessionType.Minerador, yieldPrefab, extraReward);
    }

    if (RollChance(ProfessionSettingsService.MineradorGoldChanceAtMax * professionLevel / 100d)) {
      GiveReward(player, ProfessionType.Minerador, PrefabGUIDs.Item_Ingredient_Mineral_GoldOre, ProfessionSettingsService.MineradorGoldAmount);
    }
  }

  private static void HandleWoodRewards(PlayerData player, PrefabGUID yieldPrefab, int professionLevel, int extraAtMaxLevel) {
    int extraReward = CalculateScaledExtraBonus(professionLevel, extraAtMaxLevel);
    if (extraReward > 0) {
      GiveReward(player, ProfessionType.Lenhador, yieldPrefab, extraReward);
    }

    if (!RollChance(ProfessionSettingsService.LenhadorSaplingChanceAtMax * professionLevel / 100d)) {
      return;
    }

    IReadOnlyList<PrefabGUID> treeSaplingRewards = ProfessionCatalogService.TreeSaplingRewards;
    if (treeSaplingRewards.Count == 0) {
      return;
    }

    PrefabGUID itemPrefab = treeSaplingRewards[Random.Next(0, treeSaplingRewards.Count)];
    GiveReward(player, ProfessionType.Lenhador, itemPrefab, ProfessionSettingsService.LenhadorSaplingAmount);
  }

  private static void HandleHerbalRewards(PlayerData player, PrefabGUID yieldPrefab, int professionLevel, int extraAtMaxLevel) {
    int extraReward = CalculateScaledExtraBonus(professionLevel, extraAtMaxLevel);
    if (extraReward > 0) {
      GiveReward(player, ProfessionType.Herbalista, yieldPrefab, extraReward);
    }

    if (!RollChance(ProfessionSettingsService.HerbalistaSeedChanceAtMax * professionLevel / 100d)) {
      return;
    }

    IReadOnlyList<PrefabGUID> plantSeedRewards = ProfessionCatalogService.PlantSeedRewards;
    if (plantSeedRewards.Count == 0) {
      return;
    }

    PrefabGUID itemPrefab = plantSeedRewards[Random.Next(0, plantSeedRewards.Count)];
    GiveReward(player, ProfessionType.Herbalista, itemPrefab, ProfessionSettingsService.HerbalistaSeedAmount);
  }

  private static void HandleJewelGatherRewards(PlayerData player, PrefabGUID yieldPrefab, int professionLevel) {
    if (RollChance(ProfessionSettingsService.JoalheiroPerfectGemChanceAtMax * professionLevel / 100d) && ProfessionCatalogService.TryGetPerfectGem(yieldPrefab, out PrefabGUID perfectGem)) {
      GiveReward(player, ProfessionType.Joalheiro, perfectGem, ProfessionSettingsService.JoalheiroPerfectGemAmount);
    }
  }

  private static int CalculateScaledExtraBonus(int professionLevel, int extraAtMaxLevel) {
    if (professionLevel <= 0 || extraAtMaxLevel <= 0) {
      return 0;
    }

    double scaled = extraAtMaxLevel * professionLevel / 100d;
    return Math.Max(0, (int)Math.Floor(scaled));
  }

  private static void ApplyDurabilityBonus(Entity itemEntity, PrefabGUID itemPrefab, int level, double bonusAtMax) {
    if (!itemEntity.Exists() || !itemEntity.Has<Durability>() || bonusAtMax <= 0d || !GameSystems.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(itemPrefab, out Entity prefabEntity) || !prefabEntity.Exists() || !prefabEntity.Has<Durability>()) {
      return;
    }

    Durability currentDurability = itemEntity.Read<Durability>();
    Durability prefabDurability = prefabEntity.Read<Durability>();
    if (currentDurability.MaxDurability > prefabDurability.MaxDurability) {
      return;
    }

    double multiplier = 1d + bonusAtMax * level / 100d;
    if (multiplier <= 1d) {
      return;
    }

    float adjustedMax = (float)(currentDurability.MaxDurability * multiplier);
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
        entry.MaxTicks = Math.Max(1, (int)Math.Round(entry.MaxTicks * durationMultiplier));
        tickEvents[i] = entry;
      }
    }
  }

  private static void GiveReward(PlayerData player, ProfessionType profession, PrefabGUID itemPrefab, int amount) {
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

  private static bool TryResolveGatherProfession(PrefabGUID yieldPrefab, out ProfessionType profession) {
    profession = ProfessionType.Minerador;
    if (ProfessionCatalogService.IsGemPrefab(yieldPrefab)) {
      profession = ProfessionType.Joalheiro;
      return true;
    }

    if (ProfessionCatalogService.IsOrePrefab(yieldPrefab)) {
      profession = ProfessionType.Minerador;
      return true;
    }

    if (ProfessionCatalogService.IsWoodPrefab(yieldPrefab)) {
      profession = ProfessionType.Lenhador;
      return true;
    }

    if (ProfessionCatalogService.IsPlantPrefab(yieldPrefab)) {
      profession = ProfessionType.Herbalista;
      return true;
    }

    return false;
  }

  private static bool TryResolveCraftProfession(PrefabGUID itemPrefab, out ProfessionType profession) {
    profession = ProfessionType.Minerador;
    if (ProfessionCatalogService.IsNecklacePrefab(itemPrefab)) {
      profession = ProfessionType.Joalheiro;
      return true;
    }

    if (ProfessionCatalogService.IsArmorPrefab(itemPrefab)) {
      profession = ProfessionType.Alfaiate;
      return true;
    }

    if (ProfessionCatalogService.IsWeaponPrefab(itemPrefab)) {
      profession = ProfessionType.Ferreiro;
      return true;
    }

    if (ProfessionCatalogService.IsConsumablePrefab(itemPrefab)) {
      profession = ProfessionType.Alquimista;
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
    if (!buffName.Contains("consumable") && !buffName.Contains("potion") && !buffName.Contains("elixir") && !buffName.Contains("coating") && !buffName.Contains("salve") && !buffName.Contains("brew")) {
      return buffName.Contains("canteen");
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

  private static double GetLevelPercent(double experience, int level) {
    if (level >= 100) {
      return 0d;
    }

    double currentLevelBase = ConvertLevelToXp(level);
    double nextLevelBase = ConvertLevelToXp(level + 1);
    double requiredExperience = Math.Max(1d, nextLevelBase - currentLevelBase);
    double currentLevelProgress = Math.Clamp(experience - currentLevelBase, 0d, requiredExperience);
    return Math.Round(Math.Min(99.999, currentLevelProgress / requiredExperience * 100d), 3);
  }

  private static int ConvertXpToLevel(double xp) {
    return Math.Clamp((int)(0.1 * Math.Sqrt(Math.Max(0d, xp))), 1, 100);
  }

  private static double ConvertLevelToXp(int level) {
    return Math.Pow(Math.Clamp(level, 1, 100) / 0.1, 2d);
  }
}
