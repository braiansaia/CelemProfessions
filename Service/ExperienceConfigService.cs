using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;
using CelemProfessions.Models;
using ProjectM;
using ProjectM.Shared;
using ScarletCore.Systems;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace CelemProfessions.Service;

public static class ProfessionExperienceConfigService {
  private sealed class PrefabSnapshot {
    public PrefabGUID PrefabGuid { get; init; }
    public string Name { get; init; } = string.Empty;
  }

  private sealed class GatherSnapshot {
    public PrefabGUID PrefabGuid { get; init; }
    public string Name { get; init; } = string.Empty;
    public PrefabGUID YieldPrefab { get; init; }
  }

  private const double HunterLevelExperienceFactor = 7.25d;
  private const double DefaultGatherBaseExperience = 10d;
  private const double DefaultCraftBaseExperience = 50d;

  private static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = true
  };

  private static readonly Dictionary<int, double> MineradorGatherExperienceByTarget = [];
  private static readonly Dictionary<int, int> MineradorExtraAtMaxByTarget = [];
  private static readonly Dictionary<int, double> LenhadorGatherExperienceByTarget = [];
  private static readonly Dictionary<int, int> LenhadorExtraAtMaxByTarget = [];
  private static readonly Dictionary<int, double> HerbalistaGatherExperienceByTarget = [];
  private static readonly Dictionary<int, int> HerbalistaExtraAtMaxByTarget = [];
  private static readonly Dictionary<int, double> AlquimistaCraftExperienceByItem = [];
  private static readonly Dictionary<int, double> CacadorExperienceByTarget = [];
  private static readonly Dictionary<int, int> CacadorExtraAtMaxByTarget = [];
  private static readonly Dictionary<int, PrefabGUID> CacadorLeatherDropByTarget = [];

  private static bool _initialized;

  private static string ConfigDirectory => Path.Combine(Paths.ConfigPath, "CelemProfessions");
  private static string MineradorFilePath => Path.Combine(ConfigDirectory, "minerador.json");
  private static string LenhadorFilePath => Path.Combine(ConfigDirectory, "lenhador.json");
  private static string HerbalistaFilePath => Path.Combine(ConfigDirectory, "herbalista.json");
  private static string AlquimistaFilePath => Path.Combine(ConfigDirectory, "alquimista.json");
  private static string CacadorFilePath => Path.Combine(ConfigDirectory, "cacador.json");

  public static void Initialize() {
    if (_initialized) {
      return;
    }

    Directory.CreateDirectory(ConfigDirectory);

    bool needsMineradorConfig = ShouldGenerateGatherConfig(MineradorFilePath);
    bool needsLenhadorConfig = ShouldGenerateGatherConfig(LenhadorFilePath);
    bool needsHerbalistaConfig = ShouldGenerateGatherConfig(HerbalistaFilePath);
    bool needsAlquimistaConfig = !File.Exists(AlquimistaFilePath);
    bool needsCacadorConfig = !File.Exists(CacadorFilePath);

    List<GatherSnapshot> gatherSnapshots = [];
    if (needsMineradorConfig || needsLenhadorConfig || needsHerbalistaConfig) {
      gatherSnapshots = BuildGatherSnapshots();
    }

    List<PrefabSnapshot> prefabSnapshots = [];
    if (needsAlquimistaConfig || needsCacadorConfig) {
      prefabSnapshots = BuildPrefabSnapshots();
    }

    if (needsMineradorConfig) {
      WriteConfigFile(MineradorFilePath, BuildGatherEntries(gatherSnapshots, ProfessionsTypes.Minerador), File.Exists(MineradorFilePath));
    }

    if (needsLenhadorConfig) {
      WriteConfigFile(LenhadorFilePath, BuildGatherEntries(gatherSnapshots, ProfessionsTypes.Lenhador), File.Exists(LenhadorFilePath));
    }

    if (needsHerbalistaConfig) {
      WriteConfigFile(HerbalistaFilePath, BuildGatherEntries(gatherSnapshots, ProfessionsTypes.Herbalista), File.Exists(HerbalistaFilePath));
    }

    if (needsAlquimistaConfig) {
      WriteConfigFile(AlquimistaFilePath, BuildAlquimistaEntries(prefabSnapshots), false);
    }

    if (needsCacadorConfig) {
      WriteConfigFile(CacadorFilePath, BuildCacadorEntries(prefabSnapshots), false);
    }

    LoadEnabledEntries(MineradorFilePath, MineradorGatherExperienceByTarget, MineradorExtraAtMaxByTarget, true);
    LoadEnabledEntries(LenhadorFilePath, LenhadorGatherExperienceByTarget, LenhadorExtraAtMaxByTarget, true);
    LoadEnabledEntries(HerbalistaFilePath, HerbalistaGatherExperienceByTarget, HerbalistaExtraAtMaxByTarget, true);
    LoadEnabledEntries(AlquimistaFilePath, AlquimistaCraftExperienceByItem, null, false);
    LoadEnabledEntries(CacadorFilePath, CacadorExperienceByTarget, CacadorExtraAtMaxByTarget, true);

    BuildHunterLeatherLookup();
    _initialized = true;
  }

  public static void Shutdown() {
    MineradorGatherExperienceByTarget.Clear();
    MineradorExtraAtMaxByTarget.Clear();
    LenhadorGatherExperienceByTarget.Clear();
    LenhadorExtraAtMaxByTarget.Clear();
    HerbalistaGatherExperienceByTarget.Clear();
    HerbalistaExtraAtMaxByTarget.Clear();
    AlquimistaCraftExperienceByItem.Clear();
    CacadorExperienceByTarget.Clear();
    CacadorExtraAtMaxByTarget.Clear();
    CacadorLeatherDropByTarget.Clear();
    _initialized = false;
  }

  public static bool TryGetGatherConfiguration(ProfessionsTypes profession, PrefabGUID targetPrefab, out double experience, out int extraAtMaxLevel) {
    experience = 0d;
    extraAtMaxLevel = 0;
    int key = targetPrefab.GuidHash;

    return profession switch {
      ProfessionsTypes.Minerador => TryGetConfiguration(MineradorGatherExperienceByTarget, MineradorExtraAtMaxByTarget, key, out experience, out extraAtMaxLevel),
      ProfessionsTypes.Lenhador => TryGetConfiguration(LenhadorGatherExperienceByTarget, LenhadorExtraAtMaxByTarget, key, out experience, out extraAtMaxLevel),
      ProfessionsTypes.Herbalista => TryGetConfiguration(HerbalistaGatherExperienceByTarget, HerbalistaExtraAtMaxByTarget, key, out experience, out extraAtMaxLevel),
      _ => false
    };
  }

  public static bool TryGetAlchemyCraftExperience(PrefabGUID itemPrefab, out double experience) {
    return AlquimistaCraftExperienceByItem.TryGetValue(itemPrefab.GuidHash, out experience);
  }

  public static bool TryGetHunterConfiguration(PrefabGUID targetPrefab, out double experience, out PrefabGUID leatherDrop, out int extraAtMaxLevel) {
    leatherDrop = PrefabGUID.Empty;
    extraAtMaxLevel = 0;
    int key = targetPrefab.GuidHash;
    if (!TryGetConfiguration(CacadorExperienceByTarget, CacadorExtraAtMaxByTarget, key, out experience, out extraAtMaxLevel)) {
      return false;
    }

    return CacadorLeatherDropByTarget.TryGetValue(key, out leatherDrop);
  }

  private static List<PrefabSnapshot> BuildPrefabSnapshots() {
    Dictionary<int, PrefabSnapshot> snapshots = new();
    foreach (var pair in GameSystems.PrefabCollectionSystem._PrefabGuidToEntityMap) {
      PrefabGUID prefabGuid = pair.Key;
      if (prefabGuid.GuidHash == 0) {
        continue;
      }

      string prefabName = ResolvePrefabName(prefabGuid);
      if (string.IsNullOrWhiteSpace(prefabName)) {
        continue;
      }

      snapshots[prefabGuid.GuidHash] = new PrefabSnapshot {
        PrefabGuid = prefabGuid,
        Name = prefabName
      };
    }

    return snapshots.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
  }

  private static List<GatherSnapshot> BuildGatherSnapshots() {
    Dictionary<int, GatherSnapshot> snapshots = new();

    foreach (var pair in GameSystems.PrefabCollectionSystem._PrefabGuidToEntityMap) {
      PrefabGUID prefabGuid = pair.Key;
      if (!TryResolveGatherDrop(prefabGuid, out PrefabGUID droppedItem)) {
        continue;
      }

      string prefabName = ResolvePrefabName(prefabGuid);
      if (string.IsNullOrWhiteSpace(prefabName)) {
        continue;
      }

      snapshots[prefabGuid.GuidHash] = new GatherSnapshot {
        PrefabGuid = prefabGuid,
        Name = prefabName,
        YieldPrefab = droppedItem
      };
    }

    return snapshots.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
  }
  private static List<ExperienceEntry> BuildGatherEntries(List<GatherSnapshot> snapshots, ProfessionsTypes profession) {
    Dictionary<int, ExperienceEntry> entries = new();
    int defaultExtraAtMax = GetDefaultExtraAtMax(profession);

    for (int i = 0; i < snapshots.Count; i++) {
      GatherSnapshot snapshot = snapshots[i];
      if (!MatchesGatherProfession(profession, snapshot.YieldPrefab)) {
        continue;
      }

      string dropName = ResolvePrefabName(snapshot.YieldPrefab);
      if (string.IsNullOrWhiteSpace(dropName)) {
        continue;
      }

      bool enabled = !(profession == ProfessionsTypes.Herbalista && IsPlantFiberDrop(snapshot.YieldPrefab));
      entries[snapshot.PrefabGuid.GuidHash] = new ExperienceEntry {
        Description = $"{snapshot.Name} drops {dropName}.",
        PrefabGUID = snapshot.PrefabGuid.GuidHash,
        Name = snapshot.Name,
        EXP = DefaultGatherBaseExperience,
        MaxResourceYield = defaultExtraAtMax,
        Enabled = enabled
      };
    }

    return entries.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
  }

  private static List<ExperienceEntry> BuildAlquimistaEntries(List<PrefabSnapshot> snapshots) {
    Dictionary<int, ExperienceEntry> entries = new();
    for (int i = 0; i < snapshots.Count; i++) {
      PrefabSnapshot snapshot = snapshots[i];
      if (!snapshot.Name.StartsWith("Item_", StringComparison.OrdinalIgnoreCase) || !ProfessionCatalogService.IsConsumablePrefab(snapshot.PrefabGuid)) {
        continue;
      }

      entries[snapshot.PrefabGuid.GuidHash] = new ExperienceEntry {
        Description = $"{snapshot.Name} is a consumable crafted by Alquimista.",
        PrefabGUID = snapshot.PrefabGuid.GuidHash,
        Name = snapshot.Name,
        EXP = DefaultCraftBaseExperience,
        Enabled = true
      };
    }

    return entries.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
  }

  private static List<ExperienceEntry> BuildCacadorEntries(List<PrefabSnapshot> snapshots) {
    Dictionary<int, ExperienceEntry> entries = new();
    int defaultExtraAtMax = GetDefaultExtraAtMax(ProfessionsTypes.Cacador);

    for (int i = 0; i < snapshots.Count; i++) {
      PrefabSnapshot snapshot = snapshots[i];
      if (!snapshot.Name.StartsWith("CHAR_", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      if (!TryResolvePrefabEntity(snapshot.PrefabGuid, out Entity prefabEntity) || !prefabEntity.TryGetComponent(out UnitLevel unitLevel) || unitLevel.Level._Value <= 0) {
        continue;
      }

      if (!TryResolveLeatherDrop(prefabEntity, out PrefabGUID leatherDrop)) {
        continue;
      }

      string leatherName = ResolvePrefabName(leatherDrop);
      if (string.IsNullOrWhiteSpace(leatherName)) {
        continue;
      }

      entries[snapshot.PrefabGuid.GuidHash] = new ExperienceEntry {
        Description = $"{snapshot.Name} drops {leatherName}.",
        PrefabGUID = snapshot.PrefabGuid.GuidHash,
        Name = snapshot.Name,
        EXP = Math.Floor(Math.Max(0d, unitLevel.Level._Value * HunterLevelExperienceFactor)),
        MaxResourceYield = defaultExtraAtMax,
        Enabled = true
      };
    }

    return entries.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
  }

  private static void BuildHunterLeatherLookup() {
    CacadorLeatherDropByTarget.Clear();
    foreach (int targetPrefabGuid in CacadorExperienceByTarget.Keys) {
      PrefabGUID targetPrefab = new(targetPrefabGuid);
      if (!TryResolvePrefabEntity(targetPrefab, out Entity prefabEntity) || !TryResolveLeatherDrop(prefabEntity, out PrefabGUID leatherDrop)) {
        continue;
      }

      CacadorLeatherDropByTarget[targetPrefabGuid] = leatherDrop;
    }
  }

  private static void LoadEnabledEntries(string path, Dictionary<int, double> xpTarget, Dictionary<int, int> extraTarget, bool includeExtra) {
    xpTarget.Clear();
    extraTarget?.Clear();
    List<ExperienceEntry> entries = ReadEntries(path);
    for (int i = 0; i < entries.Count; i++) {
      ExperienceEntry entry = entries[i];
      if (entry == null || !entry.Enabled || entry.PrefabGUID == 0 || entry.EXP <= 0d) {
        continue;
      }

      xpTarget[entry.PrefabGUID] = Math.Floor(entry.EXP);
      if (includeExtra && extraTarget != null) {
        extraTarget[entry.PrefabGUID] = Math.Max(0, entry.MaxResourceYield);
      }
    }
  }

  private static List<ExperienceEntry> ReadEntries(string path) {
    if (!File.Exists(path)) {
      return [];
    }

    try {
      string json = File.ReadAllText(path);
      if (string.IsNullOrWhiteSpace(json)) {
        return [];
      }

      List<ExperienceEntry> entries = JsonSerializer.Deserialize<List<ExperienceEntry>>(json);
      return entries ?? [];
    } catch (Exception ex) {
      Plugin.LogInstance?.LogWarning($"[ProfessionsXPConfig] Failed to parse '{Path.GetFileName(path)}': {ex.Message}");
      return [];
    }
  }

  private static void WriteConfigFile(string path, List<ExperienceEntry> entries, bool overwriteExisting) {
    string json = JsonSerializer.Serialize(entries, JsonOptions);
    File.WriteAllText(path, json);
    string action = overwriteExisting ? "Arquivo regenerado" : "Arquivo criado";
    Plugin.LogInstance?.LogInfo($"[ProfessionsXPConfig] {action} em '{path}'.");
  }

  private static bool TryResolveGatherDrop(PrefabGUID prefabGuid, out PrefabGUID droppedItem) {
    droppedItem = PrefabGUID.Empty;
    if (!TryResolvePrefabEntity(prefabGuid, out Entity prefabEntity)) {
      return false;
    }

    if (TryResolveGatherDrop(prefabEntity, out droppedItem)) {
      return true;
    }

    return TryResolveGatherDropFromDropTables(prefabEntity, out droppedItem);
  }

  private static bool TryResolveGatherDrop(Entity prefabEntity, out PrefabGUID droppedItem) {
    droppedItem = PrefabGUID.Empty;
    if (!prefabEntity.TryGetBuffer(out DynamicBuffer<YieldResourcesOnDamageTaken> yields) || yields.IsEmpty) {
      return false;
    }

    droppedItem = yields[0].ItemType;
    return !droppedItem.IsEmpty();
  }

  private static bool TryResolveGatherDropFromDropTables(Entity prefabEntity, out PrefabGUID droppedItem) {
    droppedItem = PrefabGUID.Empty;
    if (!prefabEntity.TryGetBuffer(out DynamicBuffer<DropTableBuffer> dropTables) || dropTables.IsEmpty) {
      return false;
    }

    for (int i = 0; i < dropTables.Length; i++) {
      DropTableBuffer dropTable = dropTables[i];
      if (dropTable.DropTrigger != DropTriggerType.YieldResourceOnDamageTaken) {
        continue;
      }

      if (!TryResolvePrefabEntity(dropTable.DropTableGuid, out Entity dropTableEntity)) {
        continue;
      }

      DynamicBuffer<DropTableDataBuffer> dropItems;
      try {
        if (!dropTableEntity.TryGetBuffer(out dropItems) || dropItems.IsEmpty) {
          continue;
        }
      } catch {
        continue;
      }

      for (int j = 0; j < dropItems.Length; j++) {
        PrefabGUID candidate = dropItems[j].ItemGuid;
        if (candidate.IsEmpty() || !IsGatherRelevantDrop(candidate)) {
          continue;
        }

        droppedItem = candidate;
        return true;
      }

      PrefabGUID first = dropItems[0].ItemGuid;
      if (!first.IsEmpty()) {
        droppedItem = first;
        return true;
      }
    }

    return false;
  }
  private static bool TryResolveLeatherDrop(Entity prefabEntity, out PrefabGUID leatherDrop) {
    leatherDrop = PrefabGUID.Empty;
    if (!prefabEntity.TryGetBuffer(out DynamicBuffer<DropTableBuffer> dropTables) || dropTables.IsEmpty) {
      return false;
    }

    for (int i = 0; i < dropTables.Length; i++) {
      PrefabGUID dropTableGuid = dropTables[i].DropTableGuid;
      if (!TryResolvePrefabEntity(dropTableGuid, out Entity dropTableEntity)) {
        continue;
      }

      DynamicBuffer<DropTableDataBuffer> dropItems;
      try {
        if (!dropTableEntity.TryGetBuffer(out dropItems) || dropItems.IsEmpty) {
          continue;
        }
      } catch {
        continue;
      }

      for (int j = 0; j < dropItems.Length; j++) {
        PrefabGUID itemGuid = dropItems[j].ItemGuid;
        if (!ProfessionCatalogService.IsLeatherPrefab(itemGuid)) {
          continue;
        }

        leatherDrop = itemGuid;
        return true;
      }
    }

    return false;
  }

  private static bool IsGatherRelevantDrop(PrefabGUID itemGuid) {
    return ProfessionCatalogService.IsOrePrefab(itemGuid)
      || ProfessionCatalogService.IsWoodPrefab(itemGuid)
      || ProfessionCatalogService.IsPlantPrefab(itemGuid)
      || ProfessionCatalogService.IsGemPrefab(itemGuid);
  }
  private static bool MatchesGatherProfession(ProfessionsTypes profession, PrefabGUID droppedItem) {
    return profession switch {
      ProfessionsTypes.Minerador => ProfessionCatalogService.IsOrePrefab(droppedItem),
      ProfessionsTypes.Lenhador => ProfessionCatalogService.IsWoodPrefab(droppedItem),
      ProfessionsTypes.Herbalista => ProfessionCatalogService.IsPlantPrefab(droppedItem),
      _ => false
    };
  }

  private static bool IsPlantFiberDrop(PrefabGUID droppedItem) {
    return droppedItem.GuidHash == ScarletCore.Resources.PrefabGUIDs.Item_Ingredient_Plant_PlantFiber.GuidHash
      || ProfessionCatalogService.GetNormalizedPrefabName(droppedItem).Contains("plantfiber", StringComparison.Ordinal);
  }

  private static bool TryResolvePrefabEntity(PrefabGUID prefabGuid, out Entity prefabEntity) {
    prefabEntity = Entity.Null;
    if (!GameSystems.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(prefabGuid, out Entity candidate) || candidate == Entity.Null) {
      return false;
    }

    prefabEntity = candidate;
    return true;
  }

  private static bool TryGetConfiguration(Dictionary<int, double> experienceMap, Dictionary<int, int> extraMap, int key, out double experience, out int extraAtMaxLevel) {
    experience = 0d;
    extraAtMaxLevel = 0;
    if (!experienceMap.TryGetValue(key, out experience)) {
      return false;
    }

    extraMap?.TryGetValue(key, out extraAtMaxLevel);
    return true;
  }

  private static int GetDefaultExtraAtMax(ProfessionsTypes profession) {
    return profession switch {
      ProfessionsTypes.Minerador => CalculateDefaultExtraAtMax(ProfessionSettingsService.MineradorYieldMultiplier),
      ProfessionsTypes.Lenhador => CalculateDefaultExtraAtMax(ProfessionSettingsService.LenhadorYieldMultiplier),
      ProfessionsTypes.Herbalista => CalculateDefaultExtraAtMax(ProfessionSettingsService.HerbalistaYieldMultiplier),
      ProfessionsTypes.Cacador => CalculateDefaultExtraAtMax(ProfessionSettingsService.CacadorLeatherYieldMultiplier),
      _ => 0
    };
  }

  private static int CalculateDefaultExtraAtMax(double multiplier) {
    if (multiplier <= 0d) {
      return 0;
    }

    return Math.Max(0, (int)Math.Floor(5d * multiplier));
  }

  private static bool ShouldGenerateGatherConfig(string path) {
    return !File.Exists(path) || IsBrokenEmptyConfig(path);
  }

  private static bool IsBrokenEmptyConfig(string path) {
    try {
      FileInfo fileInfo = new(path);
      if (!fileInfo.Exists || fileInfo.Length > 4) {
        return false;
      }

      string json = File.ReadAllText(path).Trim();
      return string.IsNullOrEmpty(json) || string.Equals(json, "[]", StringComparison.Ordinal);
    } catch {
      return false;
    }
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

