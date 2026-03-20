using System;
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

  private static void HandleMinerRewards(PlayerData player, PrefabGUID yieldPrefab, int professionLevel, int extraAtMaxLevel) {
    int extraReward = CalculateScaledExtraBonus(professionLevel, extraAtMaxLevel);
    if (extraReward > 0) {
      GiveReward(player, ProfessionsTypes.Minerador, yieldPrefab, extraReward);
    }

    if (RollChance(ProfessionSettingsService.MineradorGoldChanceAtMax * professionLevel / 100d)) {
      GiveReward(player, ProfessionsTypes.Minerador, PrefabGUIDs.Item_Ingredient_Mineral_GoldOre, ProfessionSettingsService.MineradorGoldAmount);
    }
  }

  private static void HandleWoodRewards(PlayerData player, PrefabGUID yieldPrefab, int professionLevel, int extraAtMaxLevel) {
    int extraReward = CalculateScaledExtraBonus(professionLevel, extraAtMaxLevel);
    if (extraReward > 0) {
      GiveReward(player, ProfessionsTypes.Lenhador, yieldPrefab, extraReward);
    }

    if (!RollChance(ProfessionSettingsService.LenhadorSpecialDropChanceAtMax * professionLevel / 100d)) {
      return;
    }

    if (RewardConfigService.TryGetRandomSaplingReward(professionLevel, out PrefabGUID rewardPrefab)) {
      GiveReward(player, ProfessionsTypes.Lenhador, rewardPrefab, 1);
    }
  }

  private static void HandleHerbalRewards(PlayerData player, PrefabGUID yieldPrefab, int professionLevel, int extraAtMaxLevel) {
    int extraReward = CalculateScaledExtraBonus(professionLevel, extraAtMaxLevel);
    if (extraReward > 0) {
      GiveReward(player, ProfessionsTypes.Herbalista, yieldPrefab, extraReward);
    }

    if (!RollChance(ProfessionSettingsService.HerbalistaSpecialDropChanceAtMax * professionLevel / 100d)) {
      return;
    }

    if (RewardConfigService.TryGetRandomSeedReward(professionLevel, out PrefabGUID rewardPrefab)) {
      GiveReward(player, ProfessionsTypes.Herbalista, rewardPrefab, 1);
    }
  }

  private static void HandleJewelCraftRewards(PlayerData player, int professionLevel) {
    if (!RollChance(ProfessionSettingsService.JoalheiroGemChanceAtMax * professionLevel / 100d)) {
      return;
    }

    if (RewardConfigService.TryGetRandomGemReward(professionLevel, out PrefabGUID rewardPrefab)) {
      GiveReward(player, ProfessionsTypes.Joalheiro, rewardPrefab, 1);
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

  private static bool TryResolveGatherProfession(PrefabGUID yieldPrefab, out ProfessionsTypes profession) {
    profession = ProfessionsTypes.Minerador;
    if (ProfessionCatalogService.IsOrePrefab(yieldPrefab)) {
      profession = ProfessionsTypes.Minerador;
      return true;
    }

    if (ProfessionCatalogService.IsWoodPrefab(yieldPrefab)) {
      profession = ProfessionsTypes.Lenhador;
      return true;
    }

    if (ProfessionCatalogService.IsPlantPrefab(yieldPrefab)) {
      profession = ProfessionsTypes.Herbalista;
      return true;
    }

    return false;
  }

  private static bool TryResolveCraftProfession(PrefabGUID itemPrefab, out ProfessionsTypes profession) {
    profession = ProfessionsTypes.Minerador;
    if (ProfessionCatalogService.IsNecklacePrefab(itemPrefab)) {
      profession = ProfessionsTypes.Joalheiro;
      return true;
    }

    if (ProfessionCatalogService.IsArmorPrefab(itemPrefab)) {
      profession = ProfessionsTypes.Alfaiate;
      return true;
    }

    if (ProfessionCatalogService.IsWeaponPrefab(itemPrefab)) {
      profession = ProfessionsTypes.Ferreiro;
      return true;
    }

    if (ProfessionCatalogService.IsConsumablePrefab(itemPrefab)) {
      profession = ProfessionsTypes.Alquimista;
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
