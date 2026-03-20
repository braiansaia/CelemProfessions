using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;
using CelemProfessions.Models;
using ProjectM;
using ProjectM.Shared;
using ScarletCore.Resources;
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
  private static readonly Dictionary<int, double> JoalheiroGatherExperienceByTarget = [];
  private static readonly Dictionary<int, int> JoalheiroExtraAtMaxByTarget = [];
  private static readonly Dictionary<int, double> AlquimistaCraftExperienceByItem = [];
  private static readonly Dictionary<int, double> CacadorExperienceByTarget = [];
  private static readonly Dictionary<int, int> CacadorExtraAtMaxByTarget = [];
  private static readonly Dictionary<int, PrefabGUID> CacadorLeatherDropByTarget = [];

  private static bool _initialized;

  private static string ConfigDirectory => Path.Combine(Paths.ConfigPath, "CelemProfessions");
  private static string MineradorFilePath => Path.Combine(ConfigDirectory, "minerador.json");
  private static string LenhadorFilePath => Path.Combine(ConfigDirectory, "lenhador.json");
  private static string HerbalistaFilePath => Path.Combine(ConfigDirectory, "herbalista.json");
  private static string JoalheiroFilePath => Path.Combine(ConfigDirectory, "joalheiro.json");
  private static string AlquimistaFilePath => Path.Combine(ConfigDirectory, "alquimista.json");
  private static string CacadorFilePath => Path.Combine(ConfigDirectory, "cacador.json");

  public static void Initialize() {
    if (_initialized) {
      return;
    }

    LogConfig("Initialize start");
    Directory.CreateDirectory(ConfigDirectory);

    List<GatherSnapshot> gatherSnapshots = BuildGatherSnapshots();
    List<PrefabSnapshot> snapshots = BuildPrefabSnapshots();

    List<ProfessionExperienceEntry> mineradorEntries = BuildGatherEntries(gatherSnapshots, ProfessionType.Minerador);
    List<ProfessionExperienceEntry> lenhadorEntries = BuildGatherEntries(gatherSnapshots, ProfessionType.Lenhador);
    List<ProfessionExperienceEntry> herbalistaEntries = BuildGatherEntries(gatherSnapshots, ProfessionType.Herbalista);
    List<ProfessionExperienceEntry> joalheiroEntries = BuildGatherEntries(gatherSnapshots, ProfessionType.Joalheiro);
    List<ProfessionExperienceEntry> alquimistaEntries = BuildAlquimistaEntries(snapshots);
    List<ProfessionExperienceEntry> cacadorEntries = BuildCacadorEntries(snapshots);

    EnsureConfigFile(MineradorFilePath, mineradorEntries);
    EnsureConfigFile(LenhadorFilePath, lenhadorEntries);
    EnsureConfigFile(HerbalistaFilePath, herbalistaEntries);
    EnsureConfigFile(JoalheiroFilePath, joalheiroEntries);
    EnsureConfigFile(AlquimistaFilePath, alquimistaEntries);
    EnsureConfigFile(CacadorFilePath, cacadorEntries);

    LoadEnabledEntries(MineradorFilePath, MineradorGatherExperienceByTarget, MineradorExtraAtMaxByTarget, true);
    LoadEnabledEntries(LenhadorFilePath, LenhadorGatherExperienceByTarget, LenhadorExtraAtMaxByTarget, true);
    LoadEnabledEntries(HerbalistaFilePath, HerbalistaGatherExperienceByTarget, HerbalistaExtraAtMaxByTarget, true);
    LoadEnabledEntries(JoalheiroFilePath, JoalheiroGatherExperienceByTarget, JoalheiroExtraAtMaxByTarget, false);
    LoadEnabledEntries(AlquimistaFilePath, AlquimistaCraftExperienceByItem, null, false);
    LoadEnabledEntries(CacadorFilePath, CacadorExperienceByTarget, CacadorExtraAtMaxByTarget, true);

    BuildHunterLeatherLookup(snapshots);

    _initialized = true;
    LogConfig("Initialize completed");
  }

  public static void Shutdown() {
    MineradorGatherExperienceByTarget.Clear();
    MineradorExtraAtMaxByTarget.Clear();
    LenhadorGatherExperienceByTarget.Clear();
    LenhadorExtraAtMaxByTarget.Clear();
    HerbalistaGatherExperienceByTarget.Clear();
    HerbalistaExtraAtMaxByTarget.Clear();
    JoalheiroGatherExperienceByTarget.Clear();
    JoalheiroExtraAtMaxByTarget.Clear();
    AlquimistaCraftExperienceByItem.Clear();
    CacadorExperienceByTarget.Clear();
    CacadorExtraAtMaxByTarget.Clear();
    CacadorLeatherDropByTarget.Clear();
    _initialized = false;
  }

  public static bool TryGetGatherConfiguration(ProfessionType profession, PrefabGUID targetPrefab, out double experience, out int extraAtMaxLevel) {
    experience = 0d;
    extraAtMaxLevel = 0;
    int key = targetPrefab.GuidHash;

    return profession switch {
      ProfessionType.Minerador => TryGetConfiguration(MineradorGatherExperienceByTarget, MineradorExtraAtMaxByTarget, key, out experience, out extraAtMaxLevel),
      ProfessionType.Lenhador => TryGetConfiguration(LenhadorGatherExperienceByTarget, LenhadorExtraAtMaxByTarget, key, out experience, out extraAtMaxLevel),
      ProfessionType.Herbalista => TryGetConfiguration(HerbalistaGatherExperienceByTarget, HerbalistaExtraAtMaxByTarget, key, out experience, out extraAtMaxLevel),
      ProfessionType.Joalheiro => TryGetConfiguration(JoalheiroGatherExperienceByTarget, JoalheiroExtraAtMaxByTarget, key, out experience, out extraAtMaxLevel),
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
    EntityQueryBuilder queryBuilder = new(Allocator.Temp);
    queryBuilder.AddAll(ComponentType.ReadOnly<Prefab>());
    queryBuilder.AddAll(ComponentType.ReadOnly<PrefabGUID>());
    queryBuilder.WithOptions(EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab);

    EntityQuery query = GameSystems.EntityManager.CreateEntityQuery(ref queryBuilder);
    Dictionary<int, PrefabSnapshot> snapshots = new();

    try {
      NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
      try {
        for (int i = 0; i < entities.Length; i++) {
          Entity entity = entities[i];
          if (!entity.TryGetComponent(out PrefabGUID prefabGuid)) {
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
      } finally {
        entities.Dispose();
      }
    } finally {
      query.Dispose();
      queryBuilder.Dispose();
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

  private static List<ProfessionExperienceEntry> BuildGatherEntries(List<GatherSnapshot> snapshots, ProfessionType profession) {
    Dictionary<int, ProfessionExperienceEntry> entries = new();
    int defaultExtraAtMax = GetDefaultExtraAtMax(profession);

    for (int i = 0; i < snapshots.Count; i++) {
      GatherSnapshot snapshot = snapshots[i];
      PrefabGUID droppedItem = snapshot.YieldPrefab;

      if (!MatchesGatherProfession(profession, droppedItem)) {
        continue;
      }

      bool enabled = !(profession == ProfessionType.Herbalista && IsPlantFiberDrop(droppedItem));
      double exp = Math.Floor(Math.Max(0d, DefaultGatherBaseExperience * (double)ProfessionCatalogService.GetTierMultiplier(droppedItem)));
      string dropName = ResolvePrefabName(droppedItem);
      if (string.IsNullOrWhiteSpace(dropName)) {
        continue;
      }

      string typeName = profession switch {
        ProfessionType.Minerador => "Ore",
        ProfessionType.Lenhador => "Tree",
        ProfessionType.Herbalista => "Plant",
        ProfessionType.Joalheiro => "Gem",
        _ => "Resource"
      };

      entries[snapshot.PrefabGuid.GuidHash] = new ProfessionExperienceEntry {
        Description = $"{snapshot.Name} is a {typeName} that drops {dropName} upon collected",
        PrefabGUID = snapshot.PrefabGuid.GuidHash,
        Name = snapshot.Name,
        EXP = exp,
        MaxResourceYield = defaultExtraAtMax,
        Enabled = enabled
      };
    }

    return entries.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
  }

  private static List<ProfessionExperienceEntry> BuildAlquimistaEntries(List<PrefabSnapshot> snapshots) {
    Dictionary<int, ProfessionExperienceEntry> entries = new();

    for (int i = 0; i < snapshots.Count; i++) {
      PrefabSnapshot snapshot = snapshots[i];
      if (!snapshot.Name.StartsWith("Item_", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      if (!ProfessionCatalogService.IsConsumablePrefab(snapshot.PrefabGuid)) {
        continue;
      }

      double exp = Math.Floor(Math.Max(0d, DefaultCraftBaseExperience * (double)ProfessionCatalogService.GetTierMultiplier(snapshot.PrefabGuid)));

      entries[snapshot.PrefabGuid.GuidHash] = new ProfessionExperienceEntry {
        Description = $"{snapshot.Name} is a Consumable that grants experience upon crafted",
        PrefabGUID = snapshot.PrefabGuid.GuidHash,
        Name = snapshot.Name,
        EXP = exp,
        Enabled = true
      };
    }

    return entries.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
  }

  private static List<ProfessionExperienceEntry> BuildCacadorEntries(List<PrefabSnapshot> snapshots) {
    Dictionary<int, ProfessionExperienceEntry> entries = new();
    int defaultExtraAtMax = GetDefaultExtraAtMax(ProfessionType.Cacador);

    for (int i = 0; i < snapshots.Count; i++) {
      PrefabSnapshot snapshot = snapshots[i];
      if (!snapshot.Name.StartsWith("CHAR_", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      if (!TryResolvePrefabEntity(snapshot.PrefabGuid, out Entity prefabEntity)) {
        continue;
      }

      if (!prefabEntity.TryGetComponent(out UnitLevel unitLevel) || unitLevel.Level._Value <= 0) {
        continue;
      }

      if (!TryResolveLeatherDrop(prefabEntity, out PrefabGUID leatherDrop)) {
        continue;
      }

      string leatherName = ResolvePrefabName(leatherDrop);
      if (string.IsNullOrWhiteSpace(leatherName)) {
        continue;
      }

      double exp = Math.Floor(Math.Max(0d, unitLevel.Level._Value * HunterLevelExperienceFactor));
      entries[snapshot.PrefabGuid.GuidHash] = new ProfessionExperienceEntry {
        Description = $"{snapshot.Name} is a Hunter target that drops {leatherName} upon killed",
        PrefabGUID = snapshot.PrefabGuid.GuidHash,
        Name = snapshot.Name,
        EXP = exp,
        MaxResourceYield = defaultExtraAtMax,
        Enabled = true
      };
    }

    return entries.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
  }

  private static void BuildHunterLeatherLookup(List<PrefabSnapshot> snapshots) {
    CacadorLeatherDropByTarget.Clear();

    for (int i = 0; i < snapshots.Count; i++) {
      PrefabSnapshot snapshot = snapshots[i];
      if (!snapshot.Name.StartsWith("CHAR_", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      if (!TryResolvePrefabEntity(snapshot.PrefabGuid, out Entity prefabEntity)) {
        continue;
      }

      if (!TryResolveLeatherDrop(prefabEntity, out PrefabGUID leatherDrop)) {
        continue;
      }

      CacadorLeatherDropByTarget[snapshot.PrefabGuid.GuidHash] = leatherDrop;
    }
  }

  private static void LoadEnabledEntries(string path, Dictionary<int, double> xpTarget, Dictionary<int, int> extraTarget, bool includeExtra) {
    xpTarget.Clear();
    extraTarget?.Clear();

    List<ProfessionExperienceEntry> entries = ReadEntries(path);

    for (int i = 0; i < entries.Count; i++) {
      ProfessionExperienceEntry entry = entries[i];
      if (entry == null || !entry.Enabled || entry.PrefabGUID == 0 || entry.EXP <= 0d) {
        continue;
      }

      xpTarget[entry.PrefabGUID] = entry.EXP;

      if (includeExtra && extraTarget != null) {
        int extraAtMax = Math.Max(0, entry.MaxResourceYield);
        extraTarget[entry.PrefabGUID] = extraAtMax;
      }
    }
  }

  private static List<ProfessionExperienceEntry> ReadEntries(string path) {
    if (!File.Exists(path)) {
      return [];
    }

    try {
      string json = File.ReadAllText(path);
      if (string.IsNullOrWhiteSpace(json)) {
        return [];
      }

      List<ProfessionExperienceEntry> entries = JsonSerializer.Deserialize<List<ProfessionExperienceEntry>>(json);
      return entries ?? [];
    } catch (Exception ex) {
      Plugin.LogInstance?.LogWarning($"[ProfessionsXPConfig] Failed to parse '{Path.GetFileName(path)}': {ex.Message}");
      return [];
    }
  }

  private static void EnsureConfigFile(string path, List<ProfessionExperienceEntry> entries) {
    if (File.Exists(path)) {
      return;
    }

    string json = JsonSerializer.Serialize(entries, JsonOptions);
    File.WriteAllText(path, json);
    LogConfig($"EnsureConfigFile created file={Path.GetFileName(path)} entries={entries.Count}");
  }

  private static bool TryResolveGatherDrop(PrefabGUID prefabGuid, out PrefabGUID droppedItem) {
    droppedItem = PrefabGUID.Empty;
    if (!TryResolvePrefabEntity(prefabGuid, out Entity prefabEntity)) {
      return false;
    }

    return TryResolveGatherDrop(prefabEntity, out droppedItem);
  }

  private static bool TryResolveGatherDrop(Entity prefabEntity, out PrefabGUID droppedItem) {
    droppedItem = PrefabGUID.Empty;

    if (!prefabEntity.TryGetBuffer(out DynamicBuffer<YieldResourcesOnDamageTaken> yields) || yields.IsEmpty) {
      return false;
    }

    droppedItem = yields[0].ItemType;
    return !droppedItem.IsEmpty();
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

  private static bool MatchesGatherProfession(ProfessionType profession, PrefabGUID droppedItem) {
    return profession switch {
      ProfessionType.Minerador => ProfessionCatalogService.IsOrePrefab(droppedItem),
      ProfessionType.Lenhador => ProfessionCatalogService.IsWoodPrefab(droppedItem),
      ProfessionType.Herbalista => ProfessionCatalogService.IsPlantPrefab(droppedItem),
      ProfessionType.Joalheiro => ProfessionCatalogService.IsGemPrefab(droppedItem),
      _ => false
    };
  }

  private static bool IsPlantFiberDrop(PrefabGUID droppedItem) {
    return droppedItem.GuidHash == PrefabGUIDs.Item_Ingredient_Plant_PlantFiber.GuidHash
      || ProfessionCatalogService.GetNormalizedPrefabName(droppedItem).Contains("plantfiber", StringComparison.Ordinal);
  }

  private static bool TryResolvePrefabEntity(PrefabGUID prefabGuid, out Entity prefabEntity) {
    prefabEntity = Entity.Null;
    if (!GameSystems.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(prefabGuid, out Entity candidate)) {
      return false;
    }

    if (candidate == Entity.Null) {
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

    extraMap.TryGetValue(key, out extraAtMaxLevel);
    return true;
  }

  private static int GetDefaultExtraAtMax(ProfessionType profession) {
    return profession switch {
      ProfessionType.Minerador => CalculateDefaultExtraAtMax(ProfessionSettingsService.MineradorYieldMultiplier),
      ProfessionType.Lenhador => CalculateDefaultExtraAtMax(ProfessionSettingsService.LenhadorYieldMultiplier),
      ProfessionType.Herbalista => CalculateDefaultExtraAtMax(ProfessionSettingsService.HerbalistaYieldMultiplier),
      ProfessionType.Cacador => CalculateDefaultExtraAtMax(ProfessionSettingsService.CacadorLeatherYieldMultiplier),
      _ => 0
    };
  }

  private static int CalculateDefaultExtraAtMax(double multiplier) {
    if (multiplier <= 0d) {
      return 0;
    }

    return Math.Max(0, (int)Math.Floor(5d * multiplier));
  }

  private static string ResolvePrefabName(PrefabGUID prefabGuid) {
    try {
      string name = prefabGuid.GetName();
      return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
    } catch {
      return string.Empty;
    }
  }

  private static void LogConfig(string message) {
    Plugin.LogInstance?.LogInfo($"[ProfessionsXPConfig] {message}");
  }
}

