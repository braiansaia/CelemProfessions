using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
  private static readonly Dictionary<int, int> LenhadorPassiveTierByTarget = [];
  private static readonly Dictionary<int, PrefabGUID> LenhadorPassiveDropByTarget = [];
  private static readonly Dictionary<int, int> HerbalistaPassiveTierByTarget = [];
  private static readonly Dictionary<int, PrefabGUID> HerbalistaPassiveDropByTarget = [];
  private static readonly Dictionary<int, bool> CacadorAggressiveByTarget = [];
  private static readonly HashSet<int> JoalheiroCraftItems = [];
  private static readonly HashSet<int> AlfaiateCraftItems = [];
  private static readonly HashSet<int> FerreiroCraftItems = [];

  private static readonly Dictionary<string, double> PescadorRegionExtraExperienceByRegion = new(StringComparer.Ordinal);

  private static readonly string[] FishingRegions = ["Farbane", "Dunley", "Gloomrot", "Cursed", "Silverlight", "Strongblade", "Mortium"];
  private static bool _initialized;

  private static string ConfigDirectory => Path.Combine(Paths.ConfigPath, "CelemProfessions");
  private static string MineradorFilePath => Path.Combine(ConfigDirectory, "minerador.json");
  private static string LenhadorFilePath => Path.Combine(ConfigDirectory, "lenhador.json");
  private static string HerbalistaFilePath => Path.Combine(ConfigDirectory, "herbalista.json");
  private static string AlquimistaFilePath => Path.Combine(ConfigDirectory, "alquimista.json");
  private static string CacadorFilePath => Path.Combine(ConfigDirectory, "cacador.json");
  private static string PescadorRegionFilePath => Path.Combine(ConfigDirectory, "pescador_regioes_xp.json");

  public static void Initialize() {
    if (_initialized) {
      return;
    }

    Directory.CreateDirectory(ConfigDirectory);

    List<PrefabSnapshot> prefabSnapshots = BuildPrefabSnapshots();
    List<GatherSnapshot> gatherSnapshots = BuildGatherSnapshots();

    bool needsMineradorConfig = ShouldGenerateGatherConfig(MineradorFilePath);
    bool needsLenhadorConfig = ShouldGenerateGatherConfig(LenhadorFilePath);
    bool needsHerbalistaConfig = ShouldGenerateGatherConfig(HerbalistaFilePath);
    bool needsAlquimistaConfig = !File.Exists(AlquimistaFilePath);
    bool needsCacadorConfig = !File.Exists(CacadorFilePath);
    bool needsPescadorRegionConfig = ShouldGenerateFishingRegionConfig(PescadorRegionFilePath);

    if (needsMineradorConfig) {
      WriteConfigFile(MineradorFilePath, BuildGatherEntries(gatherSnapshots, prefabSnapshots, ProfessionsTypes.Minerador), File.Exists(MineradorFilePath));
    }

    if (needsLenhadorConfig) {
      WriteConfigFile(LenhadorFilePath, BuildGatherEntries(gatherSnapshots, prefabSnapshots, ProfessionsTypes.Lenhador), File.Exists(LenhadorFilePath));
    }

    if (needsHerbalistaConfig) {
      WriteConfigFile(HerbalistaFilePath, BuildGatherEntries(gatherSnapshots, prefabSnapshots, ProfessionsTypes.Herbalista), File.Exists(HerbalistaFilePath));
    }

    if (needsAlquimistaConfig) {
      WriteConfigFile(AlquimistaFilePath, BuildAlquimistaEntries(prefabSnapshots), false);
    }

    if (needsCacadorConfig) {
      WriteConfigFile(CacadorFilePath, BuildCacadorEntries(prefabSnapshots), false);
    }

    if (needsPescadorRegionConfig) {
      WriteFishingRegionConfigFile(PescadorRegionFilePath, BuildFishingRegionEntries());
    }

    NormalizeGatherPassiveMetadataFile(LenhadorFilePath, ProfessionsTypes.Lenhador, gatherSnapshots, prefabSnapshots);
    NormalizeGatherPassiveMetadataFile(HerbalistaFilePath, ProfessionsTypes.Herbalista, gatherSnapshots, prefabSnapshots);
    NormalizeHunterAggressiveFile(CacadorFilePath, prefabSnapshots);

    LenhadorPassiveTierByTarget.Clear();
    LenhadorPassiveDropByTarget.Clear();
    HerbalistaPassiveTierByTarget.Clear();
    HerbalistaPassiveDropByTarget.Clear();
    CacadorAggressiveByTarget.Clear();

    LoadEnabledEntries(MineradorFilePath, MineradorGatherExperienceByTarget, MineradorExtraAtMaxByTarget, true);
    LoadEnabledEntries(LenhadorFilePath, LenhadorGatherExperienceByTarget, LenhadorExtraAtMaxByTarget, true, entry => RegisterGatherPassiveMetadata(entry, LenhadorPassiveTierByTarget, LenhadorPassiveDropByTarget));
    LoadEnabledEntries(HerbalistaFilePath, HerbalistaGatherExperienceByTarget, HerbalistaExtraAtMaxByTarget, true, entry => RegisterGatherPassiveMetadata(entry, HerbalistaPassiveTierByTarget, HerbalistaPassiveDropByTarget));
    LoadEnabledEntries(AlquimistaFilePath, AlquimistaCraftExperienceByItem, null, false);
    LoadEnabledEntries(CacadorFilePath, CacadorExperienceByTarget, CacadorExtraAtMaxByTarget, true, RegisterHunterAggressiveMetadata);

    NormalizeFishingRegionConfigFile(PescadorRegionFilePath);
    LoadFishingRegionEntries(PescadorRegionFilePath);

    BuildHunterLeatherLookup();
    BuildCraftClassificationLookups(prefabSnapshots);
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
    LenhadorPassiveTierByTarget.Clear();
    LenhadorPassiveDropByTarget.Clear();
    HerbalistaPassiveTierByTarget.Clear();
    HerbalistaPassiveDropByTarget.Clear();
    CacadorAggressiveByTarget.Clear();
    JoalheiroCraftItems.Clear();
    AlfaiateCraftItems.Clear();
    FerreiroCraftItems.Clear();
    PescadorRegionExtraExperienceByRegion.Clear();
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

  public static bool TryGetGatherPassiveConfiguration(ProfessionsTypes profession, PrefabGUID targetPrefab, out int passiveTier, out PrefabGUID passiveDropPrefab) {
    passiveTier = 0;
    passiveDropPrefab = PrefabGUID.Empty;
    int key = targetPrefab.GuidHash;

    Dictionary<int, int> passiveTierByTarget;
    Dictionary<int, PrefabGUID> passiveDropByTarget;
    switch (profession) {
      case ProfessionsTypes.Lenhador:
        passiveTierByTarget = LenhadorPassiveTierByTarget;
        passiveDropByTarget = LenhadorPassiveDropByTarget;
        break;
      case ProfessionsTypes.Herbalista:
        passiveTierByTarget = HerbalistaPassiveTierByTarget;
        passiveDropByTarget = HerbalistaPassiveDropByTarget;
        break;
      default:
        return false;
    }

    bool hasTier = passiveTierByTarget.TryGetValue(key, out passiveTier) && passiveTier > 0;
    bool hasDrop = passiveDropByTarget.TryGetValue(key, out passiveDropPrefab) && !passiveDropPrefab.IsEmpty();
    return hasTier || hasDrop;
  }

  public static bool TryGetAlchemyCraftExperience(PrefabGUID itemPrefab, out double experience) {
    return AlquimistaCraftExperienceByItem.TryGetValue(itemPrefab.GuidHash, out experience);
  }

  public static bool TryGetHunterConfiguration(PrefabGUID targetPrefab, out double experience, out PrefabGUID leatherDrop, out int extraAtMaxLevel, out bool aggressive) {
    leatherDrop = PrefabGUID.Empty;
    extraAtMaxLevel = 0;
    aggressive = true;
    int key = targetPrefab.GuidHash;
    if (!TryGetConfiguration(CacadorExperienceByTarget, CacadorExtraAtMaxByTarget, key, out experience, out extraAtMaxLevel)) {
      return false;
    }

    if (!CacadorLeatherDropByTarget.TryGetValue(key, out leatherDrop)) {
      return false;
    }

    if (CacadorAggressiveByTarget.TryGetValue(key, out bool isAggressive)) {
      aggressive = isAggressive;
    }

    return true;
  }

  public static bool IsAlchemyCraftConfigured(PrefabGUID itemPrefab) {
    return AlquimistaCraftExperienceByItem.ContainsKey(itemPrefab.GuidHash);
  }

  public static bool IsJewelerCraftConfigured(PrefabGUID itemPrefab) {
    return JoalheiroCraftItems.Contains(itemPrefab.GuidHash);
  }

  public static bool IsTailorCraftConfigured(PrefabGUID itemPrefab) {
    return AlfaiateCraftItems.Contains(itemPrefab.GuidHash);
  }

  public static bool IsBlacksmithCraftConfigured(PrefabGUID itemPrefab) {
    return FerreiroCraftItems.Contains(itemPrefab.GuidHash);
  }
  public static bool TryResolveGatherProfession(PrefabGUID targetPrefab, out ProfessionsTypes profession) {
    int key = targetPrefab.GuidHash;
    if (MineradorGatherExperienceByTarget.ContainsKey(key)) {
      profession = ProfessionsTypes.Minerador;
      return true;
    }

    if (LenhadorGatherExperienceByTarget.ContainsKey(key)) {
      profession = ProfessionsTypes.Lenhador;
      return true;
    }

    if (HerbalistaGatherExperienceByTarget.ContainsKey(key)) {
      profession = ProfessionsTypes.Herbalista;
      return true;
    }

    profession = default;
    return false;
  }

  public static bool TryGetFishingRegionExtraExperience(PrefabGUID fishingAreaPrefab, out double extraExperience) {
    extraExperience = 0d;
    if (!ProfessionCatalogService.TryResolveFishingRegion(fishingAreaPrefab, out string region)) {
      return false;
    }

    return PescadorRegionExtraExperienceByRegion.TryGetValue(region, out extraExperience);
  }

  private static void BuildCraftClassificationLookups(List<PrefabSnapshot> snapshots) {
    JoalheiroCraftItems.Clear();
    AlfaiateCraftItems.Clear();
    FerreiroCraftItems.Clear();

    for (int i = 0; i < snapshots.Count; i++) {
      PrefabSnapshot snapshot = snapshots[i];
      if (!snapshot.Name.StartsWith("Item_", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      PrefabGUID prefabGuid = snapshot.PrefabGuid;
      if (ProfessionCatalogService.IsNecklacePrefab(prefabGuid)) {
        JoalheiroCraftItems.Add(prefabGuid.GuidHash);
      }

      if (ProfessionCatalogService.IsArmorPrefab(prefabGuid)) {
        AlfaiateCraftItems.Add(prefabGuid.GuidHash);
      }

      if (ProfessionCatalogService.IsWeaponPrefab(prefabGuid)) {
        FerreiroCraftItems.Add(prefabGuid.GuidHash);
      }
    }
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
  private static List<ExperienceEntry> BuildGatherEntries(List<GatherSnapshot> snapshots, List<PrefabSnapshot> prefabSnapshots, ProfessionsTypes profession) {
    Dictionary<int, ExperienceEntry> entries = new();
    int defaultExtraAtMax = GetDefaultExtraAtMax(profession);

    List<PrefabSnapshot> saplingCandidates = BuildPassiveDropCandidates(prefabSnapshots, "item_building_sapling_");
    List<PrefabSnapshot> plantSeedCandidates = BuildPassiveDropCandidates(prefabSnapshots, "item_building_plants_");

    for (int i = 0; i < snapshots.Count; i++) {
      GatherSnapshot snapshot = snapshots[i];
      if (!MatchesGatherProfession(profession, snapshot.YieldPrefab)) {
        continue;
      }

      string dropName = ResolvePrefabName(snapshot.YieldPrefab);
      if (string.IsNullOrWhiteSpace(dropName)) {
        continue;
      }

      int passiveTier = ResolveGatherPassiveTier(profession, snapshot);
      PrefabGUID passiveDropPrefab = PrefabGUID.Empty;
      TryResolveGatherPassiveDropPrefab(profession, snapshot, saplingCandidates, plantSeedCandidates, out passiveDropPrefab);

      bool enabled = !(profession == ProfessionsTypes.Herbalista && IsPlantFiberDrop(snapshot.YieldPrefab));
      entries[snapshot.PrefabGuid.GuidHash] = new ExperienceEntry {
        Description = $"{snapshot.Name} drops {dropName}.",
        PrefabGUID = snapshot.PrefabGuid.GuidHash,
        Name = snapshot.Name,
        EXP = DefaultGatherBaseExperience,
        MaxResourceYield = defaultExtraAtMax,
        PassiveTier = passiveTier,
        PassiveDropPrefabGUID = passiveDropPrefab.IsEmpty() ? null : passiveDropPrefab.GuidHash,
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

      bool aggressive = ResolveHunterAggressive(prefabEntity, snapshot.Name);
      entries[snapshot.PrefabGuid.GuidHash] = new ExperienceEntry {
        Description = $"{snapshot.Name} drops {leatherName}.",
        PrefabGUID = snapshot.PrefabGuid.GuidHash,
        Name = snapshot.Name,
        EXP = Math.Floor(Math.Max(0d, unitLevel.Level._Value * HunterLevelExperienceFactor)),
        MaxResourceYield = defaultExtraAtMax,
        Aggressive = aggressive,
        Enabled = true
      };
    }

    return entries.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
  }

  private static bool ResolveHunterAggressive(Entity prefabEntity, string prefabName) {
    bool isVBlood = prefabEntity.Has<VBloodConsumeSource>() || prefabName.Contains("_VBlood", StringComparison.OrdinalIgnoreCase);
    if (!isVBlood && prefabEntity.TryGetComponent(out AggroConsumer aggroConsumer) && aggroConsumer.AlertDecayPerSecond == 99f) {
      return false;
    }

    return true;
  }

  private static void NormalizeGatherPassiveMetadataFile(string path, ProfessionsTypes profession, List<GatherSnapshot> gatherSnapshots, List<PrefabSnapshot> prefabSnapshots) {
    if (profession != ProfessionsTypes.Lenhador && profession != ProfessionsTypes.Herbalista) {
      return;
    }

    List<ExperienceEntry> currentEntries = ReadEntries(path);
    if (currentEntries.Count == 0) {
      return;
    }

    List<ExperienceEntry> defaults = BuildGatherEntries(gatherSnapshots, prefabSnapshots, profession);
    Dictionary<int, ExperienceEntry> defaultsByGuid = new();
    for (int i = 0; i < defaults.Count; i++) {
      ExperienceEntry entry = defaults[i];
      defaultsByGuid[entry.PrefabGUID] = entry;
    }

    bool changed = false;
    for (int i = 0; i < currentEntries.Count; i++) {
      ExperienceEntry entry = currentEntries[i];
      if (entry == null || entry.PrefabGUID == 0 || !defaultsByGuid.TryGetValue(entry.PrefabGUID, out ExperienceEntry fallback)) {
        continue;
      }

      if (entry.PassiveTier <= 0 && fallback.PassiveTier > 0) {
        entry.PassiveTier = fallback.PassiveTier;
        changed = true;
      }

      if ((!entry.PassiveDropPrefabGUID.HasValue || entry.PassiveDropPrefabGUID.Value == 0) && fallback.PassiveDropPrefabGUID.HasValue && fallback.PassiveDropPrefabGUID.Value != 0) {
        entry.PassiveDropPrefabGUID = fallback.PassiveDropPrefabGUID;
        changed = true;
      }
    }

    if (changed) {
      WriteConfigFile(path, currentEntries, overwriteExisting: true);
    }
  }

  private static void NormalizeHunterAggressiveFile(string path, List<PrefabSnapshot> prefabSnapshots) {
    List<ExperienceEntry> currentEntries = ReadEntries(path);
    if (currentEntries.Count == 0) {
      return;
    }

    List<ExperienceEntry> defaults = BuildCacadorEntries(prefabSnapshots);
    Dictionary<int, ExperienceEntry> defaultsByGuid = new();
    for (int i = 0; i < defaults.Count; i++) {
      ExperienceEntry entry = defaults[i];
      defaultsByGuid[entry.PrefabGUID] = entry;
    }

    bool changed = false;
    for (int i = 0; i < currentEntries.Count; i++) {
      ExperienceEntry entry = currentEntries[i];
      if (entry == null || entry.PrefabGUID == 0 || entry.Aggressive.HasValue || !defaultsByGuid.TryGetValue(entry.PrefabGUID, out ExperienceEntry fallback) || !fallback.Aggressive.HasValue) {
        continue;
      }

      entry.Aggressive = fallback.Aggressive.Value;
      changed = true;
    }

    if (changed) {
      WriteConfigFile(path, currentEntries, overwriteExisting: true);
    }
  }

  private static List<PrefabSnapshot> BuildPassiveDropCandidates(List<PrefabSnapshot> prefabSnapshots, string prefix) {
    List<PrefabSnapshot> candidates = [];
    for (int i = 0; i < prefabSnapshots.Count; i++) {
      PrefabSnapshot snapshot = prefabSnapshots[i];
      string normalized = snapshot.Name.ToLowerInvariant();
      if (!normalized.StartsWith(prefix, StringComparison.Ordinal) || !normalized.EndsWith("_seed", StringComparison.Ordinal)) {
        continue;
      }

      candidates.Add(snapshot);
    }

    return candidates;
  }

  private static int ResolveGatherPassiveTier(ProfessionsTypes profession, GatherSnapshot snapshot) {
    if (profession == ProfessionsTypes.Lenhador) {
      string yieldName = ProfessionCatalogService.GetNormalizedPrefabName(snapshot.YieldPrefab);
      if (yieldName.Contains("wood_standard", StringComparison.Ordinal) || yieldName.Contains("_standard", StringComparison.Ordinal)) {
        return 25;
      }

      if (yieldName.Contains("wood_hallow", StringComparison.Ordinal) || yieldName.Contains("wood_hollow", StringComparison.Ordinal) || yieldName.Contains("_hallow", StringComparison.Ordinal) || yieldName.Contains("_hollow", StringComparison.Ordinal)) {
        return 50;
      }

      if (yieldName.Contains("wood_cursed", StringComparison.Ordinal) || yieldName.Contains("_cursed", StringComparison.Ordinal)) {
        return 75;
      }

      if (yieldName.Contains("wood_gloom", StringComparison.Ordinal) || yieldName.Contains("_gloom", StringComparison.Ordinal) || yieldName.Contains("gloomroot", StringComparison.Ordinal)) {
        return 100;
      }

      return 0;
    }

    if (profession == ProfessionsTypes.Herbalista) {
      string source = (snapshot.Name + " " + ResolvePrefabName(snapshot.YieldPrefab)).ToLowerInvariant();
      if (ContainsAny(source, "sacredgrape", "grape", "ghostshroom", "plaguebrier")) {
        return 100;
      }

      if (ContainsAny(source, "fireblossom", "snowflower", "bleedingheart")) {
        return 75;
      }

      if (ContainsAny(source, "cotton", "sunflower", "thistle")) {
        return 50;
      }

      if (ContainsAny(source, "bloodrose", "mourninglily", "morninglily", "hellsclarion", "hellscarion")) {
        return 25;
      }
    }

    return 0;
  }

  private static bool TryResolveGatherPassiveDropPrefab(ProfessionsTypes profession, GatherSnapshot snapshot, List<PrefabSnapshot> saplingCandidates, List<PrefabSnapshot> plantSeedCandidates, out PrefabGUID dropPrefab) {
    dropPrefab = PrefabGUID.Empty;

    List<PrefabSnapshot> candidates = profession switch {
      ProfessionsTypes.Lenhador => saplingCandidates,
      ProfessionsTypes.Herbalista => plantSeedCandidates,
      _ => null
    };

    if (candidates == null || candidates.Count == 0) {
      return false;
    }

    HashSet<string> sourceTokens = BuildNameTokenSet(snapshot.Name, ResolvePrefabName(snapshot.YieldPrefab));
    if (sourceTokens.Count == 0) {
      return false;
    }

    int bestScore = 0;
    PrefabGUID bestPrefab = PrefabGUID.Empty;
    for (int i = 0; i < candidates.Count; i++) {
      PrefabSnapshot candidate = candidates[i];
      HashSet<string> candidateTokens = BuildNameTokenSet(candidate.Name);
      if (candidateTokens.Count == 0) {
        continue;
      }

      int score = 0;
      foreach (string token in candidateTokens) {
        if (sourceTokens.Contains(token)) {
          score++;
        }
      }

      if (score <= 0 || score < bestScore) {
        continue;
      }

      if (score == bestScore && !bestPrefab.IsEmpty()) {
        continue;
      }

      bestScore = score;
      bestPrefab = candidate.PrefabGuid;
    }

    if (bestScore <= 0 || bestPrefab.IsEmpty()) {
      return false;
    }

    dropPrefab = bestPrefab;
    return true;
  }

  private static HashSet<string> BuildNameTokenSet(params string[] names) {
    HashSet<string> tokens = new(StringComparer.Ordinal);
    for (int i = 0; i < names.Length; i++) {
      string name = names[i];
      if (string.IsNullOrWhiteSpace(name)) {
        continue;
      }

      string normalized = name.ToLowerInvariant();
      StringBuilder token = new();
      for (int j = 0; j < normalized.Length; j++) {
        char current = normalized[j];
        if (char.IsLetterOrDigit(current)) {
          token.Append(current);
          continue;
        }

        PushToken(tokens, token);
      }

      PushToken(tokens, token);
    }

    return tokens;
  }

  private static void PushToken(HashSet<string> tokens, StringBuilder token) {
    if (token.Length == 0) {
      return;
    }

    string value = token.ToString();
    token.Clear();
    if (value.Length <= 2) {
      return;
    }

    if (value is "item" or "ingredient" or "resource" or "building" or "seed" or "sapling" or "plants" or "plant" or "tree" or "wood" or "char" or "node" or "harvest" or "stone" or "drop" or "droptable" or "t01" or "t02" or "t03" or "t04" or "prefab") {
      return;
    }

    tokens.Add(value);
  }

  private static bool ContainsAny(string source, params string[] values) {
    for (int i = 0; i < values.Length; i++) {
      if (source.Contains(values[i], StringComparison.Ordinal)) {
        return true;
      }
    }

    return false;
  }

  private static void RegisterGatherPassiveMetadata(ExperienceEntry entry, Dictionary<int, int> passiveTierByTarget, Dictionary<int, PrefabGUID> passiveDropByTarget) {
    if (entry.PassiveTier > 0) {
      passiveTierByTarget[entry.PrefabGUID] = entry.PassiveTier;
    }

    if (entry.PassiveDropPrefabGUID.HasValue && entry.PassiveDropPrefabGUID.Value != 0) {
      passiveDropByTarget[entry.PrefabGUID] = new PrefabGUID(entry.PassiveDropPrefabGUID.Value);
    }
  }

  private static void RegisterHunterAggressiveMetadata(ExperienceEntry entry) {
    if (entry.Aggressive.HasValue) {
      CacadorAggressiveByTarget[entry.PrefabGUID] = entry.Aggressive.Value;
    }
  }
  private static List<FishingRegionExperienceEntry> BuildFishingRegionEntries() {
    List<FishingRegionExperienceEntry> entries = new(FishingRegions.Length);
    for (int i = 0; i < FishingRegions.Length; i++) {
      string region = FishingRegions[i];
      entries.Add(new FishingRegionExperienceEntry {
        Region = region,
        EXP = 0d,
        Enabled = true
      });
    }

    return entries;
  }

  private static void NormalizeFishingRegionConfigFile(string path) {
    if (!File.Exists(path)) {
      return;
    }

    try {
      string json = File.ReadAllText(path);
      if (string.IsNullOrWhiteSpace(json)) {
        return;
      }

      List<FishingRegionExperienceEntry> currentEntries = JsonSerializer.Deserialize<List<FishingRegionExperienceEntry>>(json) ?? [];
      List<FishingRegionExperienceEntry> normalizedEntries = BuildNormalizedFishingRegionEntries(currentEntries);
      if (IsNormalizedFishingRegionConfig(currentEntries, normalizedEntries)) {
        return;
      }

      WriteFishingRegionConfigFile(path, normalizedEntries);
      Plugin.LogInstance?.LogInfo($"[ProfessionsXPConfig] Arquivo regional de pescador normalizado em '{path}'.");
    } catch (Exception ex) {
      Plugin.LogInstance?.LogWarning($"[ProfessionsXPConfig] Failed to normalize '{Path.GetFileName(path)}': {ex.Message}");
    }
  }

  private static List<FishingRegionExperienceEntry> BuildNormalizedFishingRegionEntries(List<FishingRegionExperienceEntry> entries) {
    Dictionary<string, FishingRegionExperienceEntry> entriesByRegion = new(StringComparer.Ordinal);
    for (int i = 0; i < entries.Count; i++) {
      FishingRegionExperienceEntry entry = entries[i];
      if (entry == null || string.IsNullOrWhiteSpace(entry.Region)) {
        continue;
      }

      string normalizedRegion = NormalizeRegion(entry.Region);
      if (string.IsNullOrEmpty(normalizedRegion)) {
        continue;
      }

      entriesByRegion[normalizedRegion] = entry;
    }

    List<FishingRegionExperienceEntry> normalizedEntries = new(FishingRegions.Length);
    for (int i = 0; i < FishingRegions.Length; i++) {
      string canonicalRegion = FishingRegions[i];
      string normalizedRegion = NormalizeRegion(canonicalRegion);
      if (entriesByRegion.TryGetValue(normalizedRegion, out FishingRegionExperienceEntry existingEntry)) {
        normalizedEntries.Add(new FishingRegionExperienceEntry {
          Region = canonicalRegion,
          EXP = Math.Max(0d, Math.Floor(existingEntry.EXP)),
          Enabled = existingEntry.Enabled
        });
        continue;
      }

      normalizedEntries.Add(new FishingRegionExperienceEntry {
        Region = canonicalRegion,
        EXP = 0d,
        Enabled = true
      });
    }

    return normalizedEntries;
  }

  private static bool IsNormalizedFishingRegionConfig(List<FishingRegionExperienceEntry> currentEntries, List<FishingRegionExperienceEntry> normalizedEntries) {
    if (currentEntries.Count != normalizedEntries.Count) {
      return false;
    }

    for (int i = 0; i < normalizedEntries.Count; i++) {
      FishingRegionExperienceEntry currentEntry = currentEntries[i];
      FishingRegionExperienceEntry normalizedEntry = normalizedEntries[i];
      if (currentEntry == null) {
        return false;
      }

      string currentRegion = currentEntry.Region?.Trim() ?? string.Empty;
      double currentExp = Math.Max(0d, Math.Floor(currentEntry.EXP));
      if (!string.Equals(currentRegion, normalizedEntry.Region, StringComparison.Ordinal)
          || currentExp != normalizedEntry.EXP
          || currentEntry.Enabled != normalizedEntry.Enabled) {
        return false;
      }
    }

    return true;
  }

  private static void LoadFishingRegionEntries(string path) {
    PescadorRegionExtraExperienceByRegion.Clear();
    if (!File.Exists(path)) {
      return;
    }

    try {
      string json = File.ReadAllText(path);
      if (string.IsNullOrWhiteSpace(json)) {
        return;
      }

      List<FishingRegionExperienceEntry> entries = JsonSerializer.Deserialize<List<FishingRegionExperienceEntry>>(json) ?? [];
      for (int i = 0; i < entries.Count; i++) {
        FishingRegionExperienceEntry entry = entries[i];
        if (entry == null || !entry.Enabled || string.IsNullOrWhiteSpace(entry.Region)) {
          continue;
        }

        string normalizedRegion = NormalizeRegion(entry.Region);
        if (string.IsNullOrEmpty(normalizedRegion)) {
          continue;
        }

        double extraExperience = Math.Max(0d, Math.Floor(entry.EXP));
        PescadorRegionExtraExperienceByRegion[normalizedRegion] = extraExperience;
      }
    } catch (Exception ex) {
      Plugin.LogInstance?.LogWarning($"[ProfessionsXPConfig] Failed to parse '{Path.GetFileName(path)}': {ex.Message}");
    }
  }

  private static void WriteFishingRegionConfigFile(string path, List<FishingRegionExperienceEntry> entries) {
    string json = JsonSerializer.Serialize(entries, JsonOptions);
    File.WriteAllText(path, json);
    Plugin.LogInstance?.LogInfo($"[ProfessionsXPConfig] Arquivo criado em '{path}'.");
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

  private static void LoadEnabledEntries(string path, Dictionary<int, double> xpTarget, Dictionary<int, int> extraTarget, bool includeExtra, Action<ExperienceEntry> onLoadedEntry = null) {
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

      onLoadedEntry?.Invoke(entry);
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

  private static bool ShouldGenerateFishingRegionConfig(string path) {
    return !File.Exists(path) || IsBrokenEmptyConfig(path);
  }

  private static string NormalizeRegion(string region) {
    return string.IsNullOrWhiteSpace(region)
      ? string.Empty
      : region.Trim().ToLowerInvariant();
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
















