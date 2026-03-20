using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;
using CelemProfessions.Models;
using ScarletCore.Resources;
using Stunlock.Core;

namespace CelemProfessions.Service;

public static class PassiveConfigService {
  private static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = true
  };

  private static readonly Dictionary<ProfessionsTypes, Dictionary<int, PassiveOptionEffectEntry>> Option1ByProfession = [];
  private static readonly Dictionary<ProfessionsTypes, Dictionary<int, PassiveOptionEffectEntry>> Option2ByProfession = [];
  private static readonly int[] DefaultMilestones = [25, 50, 75, 100];

  private static bool _initialized;

  private static string ConfigDirectory => Path.Combine(Paths.ConfigPath, "CelemProfessions");
  private static string FilePath => Path.Combine(ConfigDirectory, "passivas_profissoes.json");

  public static void Initialize() {
    if (_initialized) {
      return;
    }

    Directory.CreateDirectory(ConfigDirectory);

    PassiveConfigFile defaults = BuildDefaultConfig();
    PassiveConfigFile config = defaults;
    bool shouldWrite = !File.Exists(FilePath);

    if (!shouldWrite) {
      try {
        string json = File.ReadAllText(FilePath);
        PassiveConfigFile userConfig = JsonSerializer.Deserialize<PassiveConfigFile>(json);
        if (userConfig == null) {
          shouldWrite = true;
        } else {
          config = MergeWithDefaults(userConfig, defaults, out bool changed);
          shouldWrite = changed;
        }
      } catch (Exception ex) {
        shouldWrite = true;
        Plugin.LogInstance?.LogWarning($"[ProfessionsPassives] Falha ao ler '{Path.GetFileName(FilePath)}': {ex.Message}. Valores padrao serao usados.");
      }
    }

    if (shouldWrite) {
      WriteConfigFile(config);
    }

    BuildCache(config);
    _initialized = true;
  }

  public static void Shutdown() {
    Option1ByProfession.Clear();
    Option2ByProfession.Clear();
    _initialized = false;
  }

  public static bool TryGetOptionEffect(ProfessionsTypes profession, int option, int milestone, out PassiveOptionEffectEntry effect) {
    effect = null;
    Dictionary<int, PassiveOptionEffectEntry> cache = option == 1
      ? GetCache(Option1ByProfession, profession)
      : GetCache(Option2ByProfession, profession);

    return cache != null && cache.TryGetValue(milestone, out effect) && effect != null && effect.Enabled;
  }

  public static string NormalizeRegion(string region) {
    return string.IsNullOrWhiteSpace(region)
      ? string.Empty
      : region.Trim().ToLowerInvariant();
  }

  private static Dictionary<int, PassiveOptionEffectEntry> GetCache(Dictionary<ProfessionsTypes, Dictionary<int, PassiveOptionEffectEntry>> source, ProfessionsTypes profession) {
    return source.TryGetValue(profession, out Dictionary<int, PassiveOptionEffectEntry> cache)
      ? cache
      : null;
  }

  private static void BuildCache(PassiveConfigFile config) {
    Option1ByProfession.Clear();
    Option2ByProfession.Clear();

    RegisterCache(ProfessionsTypes.Minerador, config.Minerador);
    RegisterCache(ProfessionsTypes.Lenhador, config.Lenhador);
    RegisterCache(ProfessionsTypes.Herbalista, config.Herbalista);
    RegisterCache(ProfessionsTypes.Joalheiro, config.Joalheiro);
    RegisterCache(ProfessionsTypes.Alfaiate, config.Alfaiate);
    RegisterCache(ProfessionsTypes.Ferreiro, config.Ferreiro);
    RegisterCache(ProfessionsTypes.Alquimista, config.Alquimista);
    RegisterCache(ProfessionsTypes.Cacador, config.Cacador);
    RegisterCache(ProfessionsTypes.Pescador, config.Pescador);
  }

  private static void RegisterCache(ProfessionsTypes profession, PassiveProfessionConfig config) {
    Option1ByProfession[profession] = BuildMilestoneMap(config?.Option1);
    Option2ByProfession[profession] = BuildMilestoneMap(config?.Option2);
  }

  private static Dictionary<int, PassiveOptionEffectEntry> BuildMilestoneMap(List<PassiveOptionEffectEntry> entries) {
    Dictionary<int, PassiveOptionEffectEntry> map = new();
    if (entries == null) {
      return map;
    }

    for (int i = 0; i < entries.Count; i++) {
      PassiveOptionEffectEntry entry = entries[i];
      if (entry == null || entry.Milestone <= 0) {
        continue;
      }

      map[entry.Milestone] = CloneEntry(entry);
    }

    return map;
  }

  private static void WriteConfigFile(PassiveConfigFile config) {
    string json = JsonSerializer.Serialize(config, JsonOptions);
    File.WriteAllText(FilePath, json);
    Plugin.LogInstance?.LogInfo($"[ProfessionsPassives] Arquivo criado/regenerado em '{FilePath}'.");
  }

  private static PassiveConfigFile MergeWithDefaults(PassiveConfigFile current, PassiveConfigFile defaults, out bool changed) {
    changed = false;
    PassiveConfigFile merged = new() {
      Minerador = MergeProfessionConfig(current?.Minerador, defaults.Minerador, out bool minerChanged),
      Lenhador = MergeProfessionConfig(current?.Lenhador, defaults.Lenhador, out bool lenhadorChanged),
      Herbalista = MergeProfessionConfig(current?.Herbalista, defaults.Herbalista, out bool herbalistaChanged),
      Joalheiro = MergeProfessionConfig(current?.Joalheiro, defaults.Joalheiro, out bool joalheiroChanged),
      Alfaiate = MergeProfessionConfig(current?.Alfaiate, defaults.Alfaiate, out bool alfaiateChanged),
      Ferreiro = MergeProfessionConfig(current?.Ferreiro, defaults.Ferreiro, out bool ferreiroChanged),
      Alquimista = MergeProfessionConfig(current?.Alquimista, defaults.Alquimista, out bool alquimistaChanged),
      Cacador = MergeProfessionConfig(current?.Cacador, defaults.Cacador, out bool cacadorChanged),
      Pescador = MergeProfessionConfig(current?.Pescador, defaults.Pescador, out bool pescadorChanged)
    };

    changed = minerChanged || lenhadorChanged || herbalistaChanged || joalheiroChanged || alfaiateChanged || ferreiroChanged || alquimistaChanged || cacadorChanged || pescadorChanged;
    return merged;
  }

  private static PassiveProfessionConfig MergeProfessionConfig(PassiveProfessionConfig current, PassiveProfessionConfig defaults, out bool changed) {
    changed = false;
    List<PassiveOptionEffectEntry> mergedOption1 = MergeOptionEntries(current?.Option1, defaults.Option1, out bool changedOption1);
    List<PassiveOptionEffectEntry> mergedOption2 = MergeOptionEntries(current?.Option2, defaults.Option2, out bool changedOption2);
    changed = changedOption1 || changedOption2;

    return new PassiveProfessionConfig {
      Option1 = mergedOption1,
      Option2 = mergedOption2
    };
  }

  private static List<PassiveOptionEffectEntry> MergeOptionEntries(List<PassiveOptionEffectEntry> currentEntries, List<PassiveOptionEffectEntry> defaultEntries, out bool changed) {
    changed = false;
    Dictionary<int, PassiveOptionEffectEntry> currentByMilestone = new();
    if (currentEntries != null) {
      for (int i = 0; i < currentEntries.Count; i++) {
        PassiveOptionEffectEntry entry = currentEntries[i];
        if (entry == null || entry.Milestone <= 0) {
          changed = true;
          continue;
        }

        currentByMilestone[entry.Milestone] = entry;
      }
    }

    List<PassiveOptionEffectEntry> merged = new(defaultEntries.Count);
    for (int i = 0; i < defaultEntries.Count; i++) {
      PassiveOptionEffectEntry fallback = defaultEntries[i];
      if (!currentByMilestone.TryGetValue(fallback.Milestone, out PassiveOptionEffectEntry current)) {
        merged.Add(CloneEntry(fallback));
        changed = true;
        continue;
      }

      merged.Add(NormalizeEntry(current, fallback, out bool entryChanged));
      if (entryChanged) {
        changed = true;
      }
    }

    return merged;
  }

  private static PassiveOptionEffectEntry NormalizeEntry(PassiveOptionEffectEntry current, PassiveOptionEffectEntry fallback, out bool changed) {
    changed = false;
    PassiveOptionEffectEntry normalized = new() {
      Milestone = fallback.Milestone,
      Enabled = current.Enabled,
      ChancePercent = ClampPercent(current.ChancePercent),
      BonusPercent = ClampPercent(current.BonusPercent),
      Amount = Math.Max(1, current.Amount),
      RewardPrefabGUID = current.RewardPrefabGUID != 0 ? current.RewardPrefabGUID : fallback.RewardPrefabGUID,
      RewardName = !string.IsNullOrWhiteSpace(current.RewardName) ? current.RewardName.Trim() : fallback.RewardName,
      Regions = NormalizeRegions(current.Regions)
    };

    if (current.Milestone != fallback.Milestone) {
      changed = true;
    }

    if (Math.Abs(current.ChancePercent - normalized.ChancePercent) > double.Epsilon || Math.Abs(current.BonusPercent - normalized.BonusPercent) > double.Epsilon) {
      changed = true;
    }

    if (current.Amount != normalized.Amount) {
      changed = true;
    }

    if (current.RewardPrefabGUID == 0 && fallback.RewardPrefabGUID != 0) {
      changed = true;
    }

    if (string.IsNullOrWhiteSpace(current.RewardName) && !string.IsNullOrWhiteSpace(fallback.RewardName)) {
      changed = true;
    }

    if (current.Regions == null || (current.Regions.Count == 0 && fallback.Regions.Count > 0)) {
      normalized.Regions = [.. fallback.Regions];
      changed = true;
    } else if (normalized.Regions.Count == 0 && fallback.Regions.Count > 0) {
      normalized.Regions = [.. fallback.Regions];
      changed = true;
    }

    return normalized;
  }

  private static List<string> NormalizeRegions(List<string> regions) {
    if (regions == null || regions.Count == 0) {
      return [];
    }

    HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
    List<string> normalized = [];
    for (int i = 0; i < regions.Count; i++) {
      string region = regions[i];
      if (string.IsNullOrWhiteSpace(region)) {
        continue;
      }

      string trimmed = region.Trim();
      if (!unique.Add(trimmed)) {
        continue;
      }

      normalized.Add(trimmed);
    }

    return normalized;
  }

  private static double ClampPercent(double value) {
    return Math.Clamp(value, 0d, 1000d);
  }

  private static PassiveOptionEffectEntry CloneEntry(PassiveOptionEffectEntry source) {
    return new PassiveOptionEffectEntry {
      Milestone = source.Milestone,
      Enabled = source.Enabled,
      ChancePercent = source.ChancePercent,
      BonusPercent = source.BonusPercent,
      RewardPrefabGUID = source.RewardPrefabGUID,
      RewardName = source.RewardName,
      Amount = source.Amount,
      Regions = source.Regions != null ? [.. source.Regions] : []
    };
  }

  private static PassiveConfigFile BuildDefaultConfig() {
    return new PassiveConfigFile {
      Minerador = BuildMineradorDefaults(),
      Lenhador = BuildLenhadorDefaults(),
      Herbalista = BuildHerbalistaDefaults(),
      Joalheiro = BuildJoalheiroDefaults(),
      Alfaiate = BuildAlfaiateDefaults(),
      Ferreiro = BuildFerreiroDefaults(),
      Alquimista = BuildAlquimistaDefaults(),
      Cacador = BuildCacadorDefaults(),
      Pescador = BuildPescadorDefaults()
    };
  }

  private static PassiveProfessionConfig BuildMineradorDefaults() {
    return new PassiveProfessionConfig {
      Option1 = [
        BuildEntry(25, chancePercent: 5d, rewardPrefab: PrefabGUIDs.Item_Ingredient_Mineral_GoldOre, amount: 1),
        BuildEntry(50, chancePercent: 7d, rewardPrefab: PrefabGUIDs.Item_Ingredient_Mineral_GoldOre, amount: 1),
        BuildEntry(75, chancePercent: 9d, rewardPrefab: PrefabGUIDs.Item_Ingredient_Mineral_GoldOre, amount: 1),
        BuildEntry(100, chancePercent: 12d, rewardPrefab: PrefabGUIDs.Item_Ingredient_Mineral_GoldOre, amount: 1)
      ],
      Option2 = [
        BuildEntry(25, chancePercent: 10d, rewardPrefab: PrefabGUIDs.Item_Ingredient_Mineral_CopperOre, amount: 1),
        BuildEntry(50, chancePercent: 9d, rewardPrefab: PrefabGUIDs.Item_Ingredient_Mineral_IronOre, amount: 1),
        BuildEntry(75, chancePercent: 8d, rewardPrefab: PrefabGUIDs.Item_Ingredient_Mineral_Quartz, amount: 1),
        BuildEntry(100, chancePercent: 7d, rewardPrefab: PrefabGUIDs.Item_Ingredient_BloodCrystal, amount: 1)
      ]
    };
  }

  private static PassiveProfessionConfig BuildLenhadorDefaults() {
    return new PassiveProfessionConfig {
      Option1 = [
        BuildEntry(25, chancePercent: 8d),
        BuildEntry(50, chancePercent: 10d),
        BuildEntry(75, chancePercent: 12d),
        BuildEntry(100, chancePercent: 14d)
      ],
      Option2 = [
        BuildEntry(25, chancePercent: 8d),
        BuildEntry(50, chancePercent: 10d),
        BuildEntry(75, chancePercent: 12d),
        BuildEntry(100, chancePercent: 14d)
      ]
    };
  }

  private static PassiveProfessionConfig BuildHerbalistaDefaults() {
    return new PassiveProfessionConfig {
      Option1 = [
        BuildEntry(25, chancePercent: 8d),
        BuildEntry(50, chancePercent: 10d),
        BuildEntry(75, chancePercent: 12d),
        BuildEntry(100, chancePercent: 14d)
      ],
      Option2 = [
        BuildEntry(25, chancePercent: 8d),
        BuildEntry(50, chancePercent: 10d),
        BuildEntry(75, chancePercent: 12d),
        BuildEntry(100, chancePercent: 14d)
      ]
    };
  }

  private static PassiveProfessionConfig BuildJoalheiroDefaults() {
    return new PassiveProfessionConfig {
      Option1 = [
        BuildEntry(25, chancePercent: 6d),
        BuildEntry(50, chancePercent: 8d),
        BuildEntry(75, chancePercent: 10d),
        BuildEntry(100, chancePercent: 12d)
      ],
      Option2 = [
        BuildEntry(25, bonusPercent: 1d),
        BuildEntry(50, bonusPercent: 2d),
        BuildEntry(75, bonusPercent: 5d),
        BuildEntry(100, bonusPercent: 7d)
      ]
    };
  }
  private static PassiveProfessionConfig BuildAlfaiateDefaults() {
    return new PassiveProfessionConfig {
      Option1 = [
        BuildEntry(25, bonusPercent: 1d),
        BuildEntry(50, bonusPercent: 3d),
        BuildEntry(75, bonusPercent: 6d),
        BuildEntry(100, bonusPercent: 10d)
      ],
      Option2 = [
        BuildEntry(25, bonusPercent: 1d),
        BuildEntry(50, bonusPercent: 3d),
        BuildEntry(75, bonusPercent: 6d),
        BuildEntry(100, bonusPercent: 10d)
      ]
    };
  }
  private static PassiveProfessionConfig BuildFerreiroDefaults() {
    return new PassiveProfessionConfig {
      Option1 = [
        BuildEntry(25, bonusPercent: 2d),
        BuildEntry(50, bonusPercent: 5d),
        BuildEntry(75, bonusPercent: 8d),
        BuildEntry(100, bonusPercent: 15d)
      ],
      Option2 = [
        BuildEntry(25, bonusPercent: 2d),
        BuildEntry(50, bonusPercent: 5d),
        BuildEntry(75, bonusPercent: 8d),
        BuildEntry(100, bonusPercent: 15d)
      ]
    };
  }
  private static PassiveProfessionConfig BuildAlquimistaDefaults() {
    return new PassiveProfessionConfig {
      Option1 = [
        BuildEntry(25, bonusPercent: 5d),
        BuildEntry(50, bonusPercent: 8d),
        BuildEntry(75, bonusPercent: 13d),
        BuildEntry(100, bonusPercent: 17d)
      ],
      Option2 = [
        BuildEntry(25, bonusPercent: 1d),
        BuildEntry(50, bonusPercent: 2d),
        BuildEntry(75, bonusPercent: 5d),
        BuildEntry(100, bonusPercent: 7d)
      ]
    };
  }
  private static PassiveProfessionConfig BuildCacadorDefaults() {
    return new PassiveProfessionConfig {
      Option1 = [
        BuildEntry(25, chancePercent: 3d),
        BuildEntry(50, chancePercent: 5d),
        BuildEntry(75, chancePercent: 7d),
        BuildEntry(100, chancePercent: 10d)
      ],
      Option2 = [
        BuildEntry(25, chancePercent: 2d),
        BuildEntry(50, chancePercent: 3d),
        BuildEntry(75, chancePercent: 5d),
        BuildEntry(100, chancePercent: 7d)
      ]
    };
  }

  private static PassiveProfessionConfig BuildPescadorDefaults() {
    return new PassiveProfessionConfig {
      Option1 = [
        BuildEntry(25, chancePercent: 7d, rewardPrefab: PrefabGUIDs.Item_Ingredient_Fish_FatGoby_T01, amount: 1),
        BuildEntry(50, chancePercent: 9d, rewardPrefab: PrefabGUIDs.Item_Ingredient_Fish_RainbowTrout_T01, amount: 1),
        BuildEntry(75, chancePercent: 11d, rewardPrefab: PrefabGUIDs.Item_Ingredient_Fish_BloodSnapper_T02, amount: 1),
        BuildEntry(100, chancePercent: 13d, rewardPrefab: PrefabGUIDs.Item_Ingredient_Fish_SageFish_T02, amount: 1)
      ],
      Option2 = [
        BuildEntry(25, chancePercent: 10d, regions: ["Farbane", "Dunley"]),
        BuildEntry(50, chancePercent: 12d, regions: ["Cursed", "Gloomrot"]),
        BuildEntry(75, chancePercent: 15d, regions: ["Mortium", "Strongblade"]),
        BuildEntry(100, chancePercent: 18d, regions: ["Silverlight"])
      ]
    };
  }

  private static PassiveOptionEffectEntry BuildEntry(int milestone, double chancePercent = 0d, double bonusPercent = 0d, PrefabGUID rewardPrefab = default, int amount = 1, List<string> regions = null) {
    return new PassiveOptionEffectEntry {
      Milestone = milestone,
      Enabled = true,
      ChancePercent = chancePercent,
      BonusPercent = bonusPercent,
      RewardPrefabGUID = rewardPrefab.GuidHash,
      RewardName = rewardPrefab.GuidHash != 0 ? ResolvePrefabName(rewardPrefab) : string.Empty,
      Amount = Math.Max(1, amount),
      Regions = regions != null ? [.. regions] : []
    };
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





