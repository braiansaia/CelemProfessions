using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BepInEx;
using CelemProfessions.Models;
using ScarletCore.Resources;
using Stunlock.Core;

namespace CelemProfessions.Service;

public static class RewardConfigService {
  private static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = true
  };

  private static readonly List<RewardEntry> GemRewards = [];
  private static readonly List<RewardEntry> SaplingRewards = [];
  private static readonly List<RewardEntry> SeedRewards = [];
  private static readonly List<FishingRewardEntry> FishingRewards = [];
  private static readonly Random Random = new();

  private static bool _initialized;

  private static string ConfigDirectory => Path.Combine(Paths.ConfigPath, "CelemProfessions");
  private static string GemRewardsPath => Path.Combine(ConfigDirectory, "joalheiro_gemas.json");
  private static string SaplingRewardsPath => Path.Combine(ConfigDirectory, "lenhador_saplings.json");
  private static string SeedRewardsPath => Path.Combine(ConfigDirectory, "herbalista_seeds.json");
  private static string FishingRewardsPath => Path.Combine(ConfigDirectory, "pescador_peixes.json");

  public static void Initialize() {
    if (_initialized) {
      return;
    }

    Directory.CreateDirectory(ConfigDirectory);
    EnsureConfigFile(GemRewardsPath, BuildGemRewards());
    EnsureConfigFile(SaplingRewardsPath, BuildSaplingRewards());
    EnsureConfigFile(SeedRewardsPath, BuildSeedRewards());
    EnsureConfigFile(FishingRewardsPath, BuildFishingRewards());

    LoadRewards(GemRewardsPath, GemRewards);
    LoadRewards(SaplingRewardsPath, SaplingRewards);
    LoadRewards(SeedRewardsPath, SeedRewards);
    LoadFishingRewards(FishingRewardsPath, FishingRewards);

    _initialized = true;
  }

  public static void Shutdown() {
    GemRewards.Clear();
    SaplingRewards.Clear();
    SeedRewards.Clear();
    FishingRewards.Clear();
    _initialized = false;
  }

  public static bool TryGetRandomGemReward(int professionLevel, out PrefabGUID rewardPrefab) {
    return TryGetRandomReward(GemRewards, professionLevel, out rewardPrefab);
  }

  public static bool TryGetRandomSaplingReward(int professionLevel, out PrefabGUID rewardPrefab) {
    return TryGetRandomReward(SaplingRewards, professionLevel, out rewardPrefab);
  }

  public static bool TryGetRandomSeedReward(int professionLevel, out PrefabGUID rewardPrefab) {
    return TryGetRandomReward(SeedRewards, professionLevel, out rewardPrefab);
  }

  public static bool TryGetRandomFishingReward(PrefabGUID fishingAreaPrefab, int professionLevel, out PrefabGUID rewardPrefab) {
    rewardPrefab = PrefabGUID.Empty;
    if (!ProfessionCatalogService.TryResolveFishingRegion(fishingAreaPrefab, out string region)) {
      return false;
    }

    int eligibleCount = 0;
    for (int i = 0; i < FishingRewards.Count; i++) {
      FishingRewardEntry entry = FishingRewards[i];
      if (!entry.Enabled || entry.PrefabGUID == 0 || entry.Level > professionLevel || !string.Equals(NormalizeRegion(entry.Region), region, StringComparison.Ordinal)) {
        continue;
      }

      eligibleCount++;
      if (Random.Next(eligibleCount) == 0) {
        rewardPrefab = new PrefabGUID(entry.PrefabGUID);
      }
    }

    return eligibleCount > 0 && !rewardPrefab.IsEmpty();
  }

  private static bool TryGetRandomReward(List<RewardEntry> entries, int professionLevel, out PrefabGUID rewardPrefab) {
    rewardPrefab = PrefabGUID.Empty;
    int eligibleCount = 0;
    for (int i = 0; i < entries.Count; i++) {
      RewardEntry entry = entries[i];
      if (!entry.Enabled || entry.PrefabGUID == 0 || entry.Level > professionLevel) {
        continue;
      }

      eligibleCount++;
      if (Random.Next(eligibleCount) == 0) {
        rewardPrefab = new PrefabGUID(entry.PrefabGUID);
      }
    }

    return eligibleCount > 0 && !rewardPrefab.IsEmpty();
  }

  private static void LoadRewards(string path, List<RewardEntry> target) {
    target.Clear();
    try {
      string json = File.ReadAllText(path);
      List<RewardEntry> entries = JsonSerializer.Deserialize<List<RewardEntry>>(json);
      if (entries != null) {
        target.AddRange(entries);
      }
    } catch (Exception ex) {
      Plugin.LogInstance?.LogWarning($"[ProfessionsRewards] Falha ao ler '{Path.GetFileName(path)}': {ex.Message}");
    }
  }

  private static void LoadFishingRewards(string path, List<FishingRewardEntry> target) {
    target.Clear();
    try {
      string json = File.ReadAllText(path);
      List<FishingRewardEntry> entries = JsonSerializer.Deserialize<List<FishingRewardEntry>>(json);
      if (entries != null) {
        target.AddRange(entries);
      }
    } catch (Exception ex) {
      Plugin.LogInstance?.LogWarning($"[ProfessionsRewards] Falha ao ler '{Path.GetFileName(path)}': {ex.Message}");
    }
  }

  private static void EnsureConfigFile<T>(string path, List<T> entries) {
    if (File.Exists(path)) {
      return;
    }

    string json = JsonSerializer.Serialize(entries, JsonOptions);
    File.WriteAllText(path, json);
    Plugin.LogInstance?.LogInfo($"[ProfessionsRewards] Arquivo criado em '{path}'.");
  }

  private static List<RewardEntry> BuildGemRewards() {
    return [
      BuildRewardEntry(PrefabGUIDs.Item_Ingredient_Gem_Amethyst_T04),
      BuildRewardEntry(PrefabGUIDs.Item_Ingredient_Gem_Emerald_T04),
      BuildRewardEntry(PrefabGUIDs.Item_Ingredient_Gem_Miststone_T04),
      BuildRewardEntry(PrefabGUIDs.Item_Ingredient_Gem_Ruby_T04),
      BuildRewardEntry(PrefabGUIDs.Item_Ingredient_Gem_Sapphire_T04),
      BuildRewardEntry(PrefabGUIDs.Item_Ingredient_Gem_Topaz_T04)
    ];
  }

  private static List<RewardEntry> BuildSaplingRewards() {
    return [
      BuildRewardEntry(PrefabGUIDs.Item_Building_Sapling_AppleCursed_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Sapling_AppleTree_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Sapling_Aspen_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Sapling_AspenAutum_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Sapling_Birch_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Sapling_BirchAutum_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Sapling_Cypress_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Sapling_GloomTree_Seed)
    ];
  }

  private static List<RewardEntry> BuildSeedRewards() {
    return [
      BuildRewardEntry(PrefabGUIDs.Item_Building_Plants_BleedingHeart_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Plants_BloodRose_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Plants_CorruptedFlower_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Plants_Cotton_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Plants_FireBlossom_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Plants_GhostShroom_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Plants_Grapes_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Plants_HellsClarion_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Plants_MourningLily_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Plants_PlagueBrier_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Plants_SnowFlower_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Plants_Sunflower_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Plants_Thistle_Seed),
      BuildRewardEntry(PrefabGUIDs.Item_Building_Plants_TrippyShroom_Seed)
    ];
  }

  private static List<FishingRewardEntry> BuildFishingRewards() {
    return [
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_FatGoby_T01, "Farbane"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_FatGoby_T01, "Dunley"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_FierceStinger_T01, "Dunley"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_RainbowTrout_T01, "Dunley"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_FatGoby_T01, "Gloomrot"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_FierceStinger_T01, "Gloomrot"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_RainbowTrout_T01, "Gloomrot"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_SageFish_T02, "Gloomrot"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_BloodSnapper_T02, "Gloomrot"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_FatGoby_T01, "Cursed"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_FierceStinger_T01, "Cursed"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_RainbowTrout_T01, "Cursed"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_SageFish_T02, "Cursed"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_BloodSnapper_T02, "Cursed"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_SwampDweller_T03, "Cursed"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_FatGoby_T01, "Silverlight"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_FierceStinger_T01, "Silverlight"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_RainbowTrout_T01, "Silverlight"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_SageFish_T02, "Silverlight"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_BloodSnapper_T02, "Silverlight"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_GoldenRiverBass_T03, "Silverlight"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_FatGoby_T01, "Strongblade"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_FierceStinger_T01, "Strongblade"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_RainbowTrout_T01, "Strongblade"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_SageFish_T02, "Strongblade"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_BloodSnapper_T02, "Strongblade"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_GoldenRiverBass_T03, "Strongblade"),
      BuildFishingRewardEntry(PrefabGUIDs.Item_Ingredient_Fish_Corrupted_T03, "Strongblade")
    ];
  }

  private static RewardEntry BuildRewardEntry(PrefabGUID prefabGuid) {
    return new RewardEntry {
      PrefabGUID = prefabGuid.GuidHash,
      Name = ResolvePrefabName(prefabGuid),
      Level = 1,
      Enabled = true
    };
  }

  private static FishingRewardEntry BuildFishingRewardEntry(PrefabGUID prefabGuid, string region) {
    return new FishingRewardEntry {
      PrefabGUID = prefabGuid.GuidHash,
      Name = ResolvePrefabName(prefabGuid),
      Region = region,
      Level = 1,
      Enabled = true
    };
  }

  private static string NormalizeRegion(string region) {
    return string.IsNullOrWhiteSpace(region)
      ? string.Empty
      : region.Trim().ToLowerInvariant();
  }

  private static string ResolvePrefabName(PrefabGUID prefabGuid) {
    try {
      string name = prefabGuid.GetName();
      return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
    } catch {
      return string.Empty;
    }
  }
}

