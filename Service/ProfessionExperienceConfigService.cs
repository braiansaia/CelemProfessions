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

  private static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = true
  };

  private static readonly Dictionary<int, double> MineradorGatherExperienceByTarget = [];
  private static readonly Dictionary<int, double> LenhadorGatherExperienceByTarget = [];
  private static readonly Dictionary<int, double> HerbalistaGatherExperienceByTarget = [];
  private static readonly Dictionary<int, double> AlquimistaCraftExperienceByItem = [];
  private static readonly Dictionary<int, double> CacadorExperienceByTarget = [];
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

    List<GatherSnapshot> gatherSnapshots = BuildGatherSnapshots();
    List<PrefabSnapshot> snapshots = BuildPrefabSnapshots();

    LogConfig($"Initialize gatherSnapshots={gatherSnapshots.Count} prefabSnapshots={snapshots.Count}");

    List<ProfessionExperienceEntry> mineradorEntries = BuildGatherEntries(gatherSnapshots, ProfessionType.Minerador);
    List<ProfessionExperienceEntry> lenhadorEntries = BuildGatherEntries(gatherSnapshots, ProfessionType.Lenhador);
    List<ProfessionExperienceEntry> herbalistaEntries = BuildGatherEntries(gatherSnapshots, ProfessionType.Herbalista);
    List<ProfessionExperienceEntry> alquimistaEntries = BuildAlquimistaEntries(snapshots);
    List<ProfessionExperienceEntry> cacadorEntries = BuildCacadorEntries(snapshots);

    EnsureConfigFile(MineradorFilePath, mineradorEntries);
    EnsureConfigFile(LenhadorFilePath, lenhadorEntries);
    EnsureConfigFile(HerbalistaFilePath, herbalistaEntries);
    EnsureConfigFile(AlquimistaFilePath, alquimistaEntries);
    EnsureConfigFile(CacadorFilePath, cacadorEntries);

    LoadEnabledEntries(MineradorFilePath, MineradorGatherExperienceByTarget);
    LoadEnabledEntries(LenhadorFilePath, LenhadorGatherExperienceByTarget);
    LoadEnabledEntries(HerbalistaFilePath, HerbalistaGatherExperienceByTarget);
    LoadEnabledEntries(AlquimistaFilePath, AlquimistaCraftExperienceByItem);
    LoadEnabledEntries(CacadorFilePath, CacadorExperienceByTarget);

    BuildHunterLeatherLookup(snapshots);

    _initialized = true;
    LogConfig("Initialize completed");
  }

  public static bool TryGetGatherExperience(ProfessionType profession, PrefabGUID targetPrefab, out double experience) {
    experience = 0d;
    int key = targetPrefab.GuidHash;

    return profession switch {
      ProfessionType.Minerador => MineradorGatherExperienceByTarget.TryGetValue(key, out experience),
      ProfessionType.Lenhador => LenhadorGatherExperienceByTarget.TryGetValue(key, out experience),
      ProfessionType.Herbalista => HerbalistaGatherExperienceByTarget.TryGetValue(key, out experience),
      _ => false
    };
  }

  public static bool TryGetAlchemyCraftExperience(PrefabGUID itemPrefab, out double experience) {
    return AlquimistaCraftExperienceByItem.TryGetValue(itemPrefab.GuidHash, out experience);
  }

  public static bool TryGetHunterExperience(PrefabGUID targetPrefab, out double experience, out PrefabGUID leatherDrop) {
    leatherDrop = PrefabGUID.Empty;
    int key = targetPrefab.GuidHash;

    if (!CacadorExperienceByTarget.TryGetValue(key, out experience)) {
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
    int total = 0;
    int named = 0;

    try {
      NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
      try {
        for (int i = 0; i < entities.Length; i++) {
          total++;
          Entity entity = entities[i];
          if (!entity.TryGetComponent(out PrefabGUID prefabGuid)) {
            continue;
          }

          string prefabName = ResolvePrefabName(prefabGuid);
          if (string.IsNullOrWhiteSpace(prefabName)) {
            continue;
          }

          named++;
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

    LogConfig($"BuildPrefabSnapshots total={total} named={named} unique={snapshots.Count}");
    return snapshots.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
  }

  private static List<GatherSnapshot> BuildGatherSnapshots() {
    Dictionary<int, GatherSnapshot> snapshots = new();
    int total = 0;
    int resolvedDrop = 0;
    int named = 0;

    foreach (var pair in GameSystems.PrefabCollectionSystem._PrefabGuidToEntityMap) {
      total++;
      PrefabGUID prefabGuid = pair.Key;
      if (!TryResolveGatherDrop(prefabGuid, out PrefabGUID droppedItem)) {
        continue;
      }

      resolvedDrop++;
      string prefabName = ResolvePrefabName(prefabGuid);
      if (string.IsNullOrWhiteSpace(prefabName)) {
        continue;
      }

      named++;
      snapshots[prefabGuid.GuidHash] = new GatherSnapshot {
        PrefabGuid = prefabGuid,
        Name = prefabName,
        YieldPrefab = droppedItem
      };
    }

    LogConfig($"BuildGatherSnapshots total={total} resolvedDrop={resolvedDrop} named={named} unique={snapshots.Count}");
    return snapshots.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
  }

  private static List<ProfessionExperienceEntry> BuildGatherEntries(List<GatherSnapshot> snapshots, ProfessionType profession) {
    Dictionary<int, ProfessionExperienceEntry> entries = new();

    int professionMismatch = 0;
    int missingDropName = 0;

    for (int i = 0; i < snapshots.Count; i++) {
      GatherSnapshot snapshot = snapshots[i];
      PrefabGUID droppedItem = snapshot.YieldPrefab;

      if (!MatchesGatherProfession(profession, droppedItem)) {
        professionMismatch++;
        continue;
      }

      bool enabled = !(profession == ProfessionType.Herbalista && IsPlantFiberDrop(droppedItem));
      double exp = Math.Floor(Math.Max(0d, ProfessionSettingsService.GatherBaseXp * (double)ProfessionCatalogService.GetTierMultiplier(droppedItem)));
      string dropName = ResolvePrefabName(droppedItem);
      if (string.IsNullOrWhiteSpace(dropName)) {
        missingDropName++;
        continue;
      }

      string typeName = profession switch {
        ProfessionType.Minerador => "Ore",
        ProfessionType.Lenhador => "Tree",
        ProfessionType.Herbalista => "Plant",
        _ => "Resource"
      };

      entries[snapshot.PrefabGuid.GuidHash] = new ProfessionExperienceEntry {
        Description = $"{snapshot.Name} is a {typeName} that drops {dropName} upon collected",
        PrefabGUID = snapshot.PrefabGuid.GuidHash,
        Name = snapshot.Name,
        EXP = exp,
        Enabled = enabled
      };
    }

    List<ProfessionExperienceEntry> result = entries.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
    LogConfig($"BuildGatherEntries profession={profession} snapshots={snapshots.Count} entries={result.Count} mismatch={professionMismatch} missingDropName={missingDropName}");
    return result;
  }

  private static List<ProfessionExperienceEntry> BuildAlquimistaEntries(List<PrefabSnapshot> snapshots) {
    Dictionary<int, ProfessionExperienceEntry> entries = new();

    int itemPrefix = 0;
    int consumables = 0;

    for (int i = 0; i < snapshots.Count; i++) {
      PrefabSnapshot snapshot = snapshots[i];
      if (!snapshot.Name.StartsWith("Item_", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      itemPrefix++;
      if (!ProfessionCatalogService.IsConsumablePrefab(snapshot.PrefabGuid)) {
        continue;
      }

      consumables++;
      double exp = Math.Floor(Math.Max(0d, ProfessionSettingsService.CraftBaseXp * (double)ProfessionCatalogService.GetTierMultiplier(snapshot.PrefabGuid)));

      entries[snapshot.PrefabGuid.GuidHash] = new ProfessionExperienceEntry {
        Description = $"{snapshot.Name} is a Consumable that grants experience upon crafted",
        PrefabGUID = snapshot.PrefabGuid.GuidHash,
        Name = snapshot.Name,
        EXP = exp,
        Enabled = true
      };
    }

    List<ProfessionExperienceEntry> result = entries.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
    LogConfig($"BuildAlquimistaEntries snapshots={snapshots.Count} itemPrefix={itemPrefix} consumables={consumables} entries={result.Count}");
    return result;
  }

  private static List<ProfessionExperienceEntry> BuildCacadorEntries(List<PrefabSnapshot> snapshots) {
    Dictionary<int, ProfessionExperienceEntry> entries = new();

    int charPrefabs = 0;
    int unitLevelOk = 0;
    int leatherResolved = 0;
    int missingLeatherName = 0;

    for (int i = 0; i < snapshots.Count; i++) {
      PrefabSnapshot snapshot = snapshots[i];
      if (!snapshot.Name.StartsWith("CHAR_", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      charPrefabs++;
      if (!TryResolvePrefabEntity(snapshot.PrefabGuid, out Entity prefabEntity)) {
        continue;
      }

      if (!prefabEntity.TryGetComponent(out UnitLevel unitLevel) || unitLevel.Level._Value <= 0) {
        continue;
      }

      unitLevelOk++;
      if (!TryResolveLeatherDrop(prefabEntity, out PrefabGUID leatherDrop)) {
        continue;
      }

      leatherResolved++;
      string leatherName = ResolvePrefabName(leatherDrop);
      if (string.IsNullOrWhiteSpace(leatherName)) {
        missingLeatherName++;
        continue;
      }

      double exp = Math.Floor(Math.Max(0d, unitLevel.Level._Value * HunterLevelExperienceFactor));
      entries[snapshot.PrefabGuid.GuidHash] = new ProfessionExperienceEntry {
        Description = $"{snapshot.Name} is a Hunter target that drops {leatherName} upon killed",
        PrefabGUID = snapshot.PrefabGuid.GuidHash,
        Name = snapshot.Name,
        EXP = exp,
        Enabled = true
      };
    }

    List<ProfessionExperienceEntry> result = entries.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
    LogConfig($"BuildCacadorEntries snapshots={snapshots.Count} charPrefabs={charPrefabs} unitLevelOk={unitLevelOk} leatherResolved={leatherResolved} missingLeatherName={missingLeatherName} entries={result.Count}");
    return result;
  }

  private static void BuildHunterLeatherLookup(List<PrefabSnapshot> snapshots) {
    CacadorLeatherDropByTarget.Clear();

    int mapped = 0;
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
      mapped++;
    }

    LogConfig($"BuildHunterLeatherLookup mapped={mapped}");
  }

  private static void LoadEnabledEntries(string path, Dictionary<int, double> target) {
    target.Clear();

    List<ProfessionExperienceEntry> entries = ReadEntries(path);
    int enabled = 0;
    for (int i = 0; i < entries.Count; i++) {
      ProfessionExperienceEntry entry = entries[i];
      if (entry == null || !entry.Enabled || entry.PrefabGUID == 0 || entry.EXP <= 0d) {
        continue;
      }

      target[entry.PrefabGUID] = entry.EXP;
      enabled++;
    }

    LogConfig($"LoadEnabledEntries file={Path.GetFileName(path)} total={entries.Count} enabled={enabled} cache={target.Count}");
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
      LogConfig($"EnsureConfigFile skip-existing file={Path.GetFileName(path)} generatedEntries={entries.Count}");
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
        if (candidate.IsEmpty()) {
          continue;
        }

        if (!IsGatherRelevantDrop(candidate)) {
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

  private static bool MatchesGatherProfession(ProfessionType profession, PrefabGUID droppedItem) {
    return profession switch {
      ProfessionType.Minerador => ProfessionCatalogService.IsOrePrefab(droppedItem),
      ProfessionType.Lenhador => ProfessionCatalogService.IsWoodPrefab(droppedItem),
      ProfessionType.Herbalista => ProfessionCatalogService.IsPlantPrefab(droppedItem),
      _ => false
    };
  }

  private static bool IsPlantFiberDrop(PrefabGUID droppedItem) {
    return droppedItem.GuidHash == PrefabGUIDs.Item_Ingredient_Plant_PlantFiber.GuidHash
      || ResolvePrefabName(droppedItem).Contains("plantfiber", StringComparison.OrdinalIgnoreCase);
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
