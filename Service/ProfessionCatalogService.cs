using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CelemProfessions.Models;
using ScarletCore.Resources;
using Stunlock.Core;

namespace CelemProfessions.Service;

public static class ProfessionCatalogService {
  private static readonly Dictionary<ProfessionType, ProfessionDefinition> Definitions = BuildDefinitions();
  private static readonly Dictionary<string, ProfessionType> AliasLookup = BuildAliasLookup();
  private static readonly Dictionary<int, string> NormalizedPrefabNames = [];
  private static readonly Dictionary<int, int> TierMultipliers = [];

  private static readonly Dictionary<int, PrefabGUID> PerfectGemByGem = new() {
    { PrefabGUIDs.Item_Ingredient_Gem_Amethyst_T01.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Amethyst_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Amethyst_T02.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Amethyst_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Amethyst_T03.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Amethyst_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Emerald_T01.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Emerald_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Emerald_T02.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Emerald_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Emerald_T03.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Emerald_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Miststone_T01.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Miststone_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Miststone_T02.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Miststone_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Miststone_T03.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Miststone_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Ruby_T01.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Ruby_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Ruby_T02.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Ruby_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Ruby_T03.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Ruby_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Sapphire_T01.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Sapphire_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Sapphire_T02.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Sapphire_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Sapphire_T03.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Sapphire_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Topaz_T01.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Topaz_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Topaz_T02.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Topaz_T04 },
    { PrefabGUIDs.Item_Ingredient_Gem_Topaz_T03.GuidHash, PrefabGUIDs.Item_Ingredient_Gem_Topaz_T04 }
  };

  private static readonly List<PrefabGUID> TreeSaplings = [
    PrefabGUIDs.Item_Building_Sapling_AppleCursed_Seed,
    PrefabGUIDs.Item_Building_Sapling_AppleTree_Seed,
    PrefabGUIDs.Item_Building_Sapling_Aspen_Seed,
    PrefabGUIDs.Item_Building_Sapling_AspenAutum_Seed,
    PrefabGUIDs.Item_Building_Sapling_Birch_Seed,
    PrefabGUIDs.Item_Building_Sapling_BirchAutum_Seed,
    PrefabGUIDs.Item_Building_Sapling_Cypress_Seed,
    PrefabGUIDs.Item_Building_Sapling_GloomTree_Seed
  ];

  private static readonly List<PrefabGUID> PlantSeeds = [
    PrefabGUIDs.Item_Building_Plants_BleedingHeart_Seed,
    PrefabGUIDs.Item_Building_Plants_BloodRose_Seed,
    PrefabGUIDs.Item_Building_Plants_CorruptedFlower_Seed,
    PrefabGUIDs.Item_Building_Plants_Cotton_Seed,
    PrefabGUIDs.Item_Building_Plants_FireBlossom_Seed,
    PrefabGUIDs.Item_Building_Plants_GhostShroom_Seed,
    PrefabGUIDs.Item_Building_Plants_Grapes_Seed,
    PrefabGUIDs.Item_Building_Plants_HellsClarion_Seed,
    PrefabGUIDs.Item_Building_Plants_MourningLily_Seed,
    PrefabGUIDs.Item_Building_Plants_PlagueBrier_Seed,
    PrefabGUIDs.Item_Building_Plants_SnowFlower_Seed,
    PrefabGUIDs.Item_Building_Plants_Sunflower_Seed,
    PrefabGUIDs.Item_Building_Plants_Thistle_Seed,
    PrefabGUIDs.Item_Building_Plants_TrippyShroom_Seed
  ];

  private static readonly Dictionary<string, List<PrefabGUID>> FishingAreaDrops = new() {
    {
      "farbane",
      [
        PrefabGUIDs.Item_Ingredient_Fish_FatGoby_T01
      ]
    },
    {
      "dunley",
      [
        PrefabGUIDs.Item_Ingredient_Fish_FatGoby_T01,
        PrefabGUIDs.Item_Ingredient_Fish_FierceStinger_T01,
        PrefabGUIDs.Item_Ingredient_Fish_RainbowTrout_T01
      ]
    },
    {
      "gloomrot",
      [
        PrefabGUIDs.Item_Ingredient_Fish_FatGoby_T01,
        PrefabGUIDs.Item_Ingredient_Fish_FierceStinger_T01,
        PrefabGUIDs.Item_Ingredient_Fish_RainbowTrout_T01,
        PrefabGUIDs.Item_Ingredient_Fish_SageFish_T02,
        PrefabGUIDs.Item_Ingredient_Fish_BloodSnapper_T02
      ]
    },
    {
      "cursed",
      [
        PrefabGUIDs.Item_Ingredient_Fish_FatGoby_T01,
        PrefabGUIDs.Item_Ingredient_Fish_FierceStinger_T01,
        PrefabGUIDs.Item_Ingredient_Fish_RainbowTrout_T01,
        PrefabGUIDs.Item_Ingredient_Fish_SageFish_T02,
        PrefabGUIDs.Item_Ingredient_Fish_BloodSnapper_T02,
        PrefabGUIDs.Item_Ingredient_Fish_SwampDweller_T03
      ]
    },
    {
      "silverlight",
      [
        PrefabGUIDs.Item_Ingredient_Fish_FatGoby_T01,
        PrefabGUIDs.Item_Ingredient_Fish_FierceStinger_T01,
        PrefabGUIDs.Item_Ingredient_Fish_RainbowTrout_T01,
        PrefabGUIDs.Item_Ingredient_Fish_SageFish_T02,
        PrefabGUIDs.Item_Ingredient_Fish_BloodSnapper_T02,
        PrefabGUIDs.Item_Ingredient_Fish_GoldenRiverBass_T03
      ]
    },
    {
      "strongblade",
      [
        PrefabGUIDs.Item_Ingredient_Fish_FatGoby_T01,
        PrefabGUIDs.Item_Ingredient_Fish_FierceStinger_T01,
        PrefabGUIDs.Item_Ingredient_Fish_RainbowTrout_T01,
        PrefabGUIDs.Item_Ingredient_Fish_SageFish_T02,
        PrefabGUIDs.Item_Ingredient_Fish_BloodSnapper_T02,
        PrefabGUIDs.Item_Ingredient_Fish_GoldenRiverBass_T03,
        PrefabGUIDs.Item_Ingredient_Fish_Corrupted_T03
      ]
    }
  };

  public static IReadOnlyList<PrefabGUID> TreeSaplingRewards => TreeSaplings;

  public static IReadOnlyList<PrefabGUID> PlantSeedRewards => PlantSeeds;

  public static bool TryResolveProfession(string input, out ProfessionType profession, out string error) {
    profession = default;
    error = string.Empty;

    if (string.IsNullOrWhiteSpace(input)) {
      error = "Profissao nao informada.";
      return false;
    }

    string normalized = Normalize(input);

    if (AliasLookup.TryGetValue(normalized, out profession)) {
      return true;
    }

    var matches = new HashSet<ProfessionType>();

    foreach (var pair in AliasLookup) {
      if (pair.Key.StartsWith(normalized, StringComparison.Ordinal)) {
        matches.Add(pair.Value);
      }
    }

    foreach (var definition in Definitions.Values) {
      string displayNormalized = Normalize(definition.DisplayName);
      if (displayNormalized.StartsWith(normalized, StringComparison.Ordinal)) {
        matches.Add(definition.Type);
      }
    }

    if (matches.Count == 1) {
      profession = matches.First();
      return true;
    }

    if (matches.Count == 0) {
      error = "Profissao invalida.";
      return false;
    }

    string options = string.Join(", ", matches.Select(GetDisplayName));
    error = $"Profissao ambigua. Opcoes: {options}.";
    return false;
  }

  public static string GetDisplayName(ProfessionType profession) {
    return Definitions.TryGetValue(profession, out ProfessionDefinition definition) ? definition.DisplayName : profession.ToString();
  }

  public static string GetColorHex(ProfessionType profession) {
    return Definitions.TryGetValue(profession, out ProfessionDefinition definition) ? definition.ColorHex : "#FFFFFF";
  }

  public static IReadOnlyList<ProfessionPassiveMilestoneDefinition> GetMilestones(ProfessionType profession) {
    return Definitions.TryGetValue(profession, out ProfessionDefinition definition) ? definition.PassiveMilestones : [];
  }

  public static bool IsOrePrefab(PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);

    if (name.Contains("stone") && !name.Contains("ore") && !name.Contains("gem") && !name.Contains("quartz") && !name.Contains("bloodcrystal")) {
      return false;
    }

    return name.Contains("ore")
      || name.Contains("mineral")
      || name.Contains("bloodcrystal")
      || name.Contains("quartz")
      || name.Contains("sulphur")
      || name.Contains("techscrap")
      || name.Contains("emery");
  }

  public static bool IsWoodPrefab(PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("wood") || name.Contains("lumber") || name.Contains("plank");
  }

  public static bool IsPlantPrefab(PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("plant")
      || name.Contains("fiber")
      || name.Contains("flower")
      || name.Contains("herb")
      || name.Contains("mushroom")
      || name.Contains("trippyshroom");
  }

  public static bool IsGemPrefab(PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("gem") || name.Contains("jewel") || name.Contains("magicsource");
  }

  public static bool IsLeatherPrefab(PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("hide") || name.Contains("leather") || name.Contains("skin");
  }

  public static bool IsWeaponPrefab(PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("weapon") || name.Contains("onyxtear");
  }

  public static bool IsArmorPrefab(PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("armor")
      || name.Contains("cloak")
      || name.Contains("bag")
      || name.Contains("cloth")
      || name.Contains("chest")
      || name.Contains("boots")
      || name.Contains("gloves")
      || name.Contains("legs");
  }

  public static bool IsNecklacePrefab(PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("necklace") || name.Contains("pendant") || name.Contains("amulet") || name.Contains("magicsource");
  }

  public static bool IsConsumablePrefab(PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("canteen")
      || name.Contains("potion")
      || name.Contains("bottle")
      || name.Contains("flask")
      || name.Contains("consumable")
      || name.Contains("duskcaller")
      || name.Contains("elixir")
      || name.Contains("coating")
      || name.Contains("salve")
      || name.Contains("brew")
      || name.Contains("gruel");
  }

  public static int GetTierMultiplier(PrefabGUID prefab) {
    if (TierMultipliers.TryGetValue(prefab.GuidHash, out int tierMultiplier)) {
      return tierMultiplier;
    }

    string name = GetNormalizedPrefabName(prefab);
    if (name.Contains("t09")) {
      tierMultiplier = 9;
    } else if (name.Contains("t08")) {
      tierMultiplier = 8;
    } else if (name.Contains("t07")) {
      tierMultiplier = 7;
    } else if (name.Contains("t06")) {
      tierMultiplier = 6;
    } else if (name.Contains("t05")) {
      tierMultiplier = 5;
    } else if (name.Contains("t04")) {
      tierMultiplier = 4;
    } else if (name.Contains("t03")) {
      tierMultiplier = 3;
    } else if (name.Contains("t02")) {
      tierMultiplier = 2;
    } else {
      tierMultiplier = 1;
    }

    TierMultipliers[prefab.GuidHash] = tierMultiplier;
    return tierMultiplier;
  }

  public static bool TryGetPerfectGem(PrefabGUID collectedGem, out PrefabGUID perfectGem) {
    return PerfectGemByGem.TryGetValue(collectedGem.GuidHash, out perfectGem);
  }

  public static List<PrefabGUID> GetFishingAreaDrops(PrefabGUID fishingAreaPrefab) {
    string name = GetNormalizedPrefabName(fishingAreaPrefab);

    foreach (KeyValuePair<string, List<PrefabGUID>> pair in FishingAreaDrops) {
      if (name.Contains(pair.Key)) {
        return pair.Value;
      }
    }

    return FishingAreaDrops["farbane"];
  }

  internal static string GetNormalizedPrefabName(PrefabGUID prefab) {
    int key = prefab.GuidHash;
    if (key == 0) {
      return string.Empty;
    }

    if (NormalizedPrefabNames.TryGetValue(key, out string name)) {
      return name;
    }

    try {
      name = prefab.GetName().ToLowerInvariant();
    } catch {
      name = string.Empty;
    }

    NormalizedPrefabNames[key] = name;
    return name;
  }

  private static Dictionary<ProfessionType, ProfessionDefinition> BuildDefinitions() {
    return new Dictionary<ProfessionType, ProfessionDefinition> {
      {
        ProfessionType.Minerador,
        new ProfessionDefinition {
          Type = ProfessionType.Minerador,
          DisplayName = "Minerador",
          ColorHex = "#B0B7C3",
          Aliases = ["minerador", "mineracao", "mining", "miner"],
          PassiveMilestones = BuildMinerPassiveMilestones()
        }
      },
      {
        ProfessionType.Lenhador,
        new ProfessionDefinition {
          Type = ProfessionType.Lenhador,
          DisplayName = "Lenhador",
          ColorHex = "#8B5A2B",
          Aliases = ["lenhador", "woodcutting", "madeira", "wood"],
          PassiveMilestones = BuildPlaceholderMilestones("Lenhador")
        }
      },
      {
        ProfessionType.Herbalista,
        new ProfessionDefinition {
          Type = ProfessionType.Herbalista,
          DisplayName = "Herbalista",
          ColorHex = "#3FA34D",
          Aliases = ["herbalista", "herbalism", "herb", "coleta", "harvesting"],
          PassiveMilestones = BuildPlaceholderMilestones("Herbalista")
        }
      },
      {
        ProfessionType.Joalheiro,
        new ProfessionDefinition {
          Type = ProfessionType.Joalheiro,
          DisplayName = "Joalheiro",
          ColorHex = "#C77DFF",
          Aliases = ["joalheiro", "jewel", "jeweler", "jewelcrafting", "encantamento", "enchanting"],
          PassiveMilestones = BuildPlaceholderMilestones("Joalheiro")
        }
      },
      {
        ProfessionType.Alfaiate,
        new ProfessionDefinition {
          Type = ProfessionType.Alfaiate,
          DisplayName = "Alfaiate",
          ColorHex = "#E5989B",
          Aliases = ["alfaiate", "alfaiataria", "tailor", "tailoring"],
          PassiveMilestones = BuildPlaceholderMilestones("Alfaiate")
        }
      },
      {
        ProfessionType.Ferreiro,
        new ProfessionDefinition {
          Type = ProfessionType.Ferreiro,
          DisplayName = "Ferreiro",
          ColorHex = "#6C757D",
          Aliases = ["ferreiro", "blacksmith", "blacksmithing", "smith"],
          PassiveMilestones = BuildPlaceholderMilestones("Ferreiro")
        }
      },
      {
        ProfessionType.Alquimista,
        new ProfessionDefinition {
          Type = ProfessionType.Alquimista,
          DisplayName = "Alquimista",
          ColorHex = "#2EC4B6",
          Aliases = ["alquimista", "alquimia", "alchemy", "alchemist"],
          PassiveMilestones = BuildPlaceholderMilestones("Alquimista")
        }
      },
      {
        ProfessionType.Cacador,
        new ProfessionDefinition {
          Type = ProfessionType.Cacador,
          DisplayName = "Cacador",
          ColorHex = "#C08552",
          Aliases = ["cacador", "caçador", "hunter", "hunting"],
          PassiveMilestones = BuildPlaceholderMilestones("Cacador")
        }
      },
      {
        ProfessionType.Pescador,
        new ProfessionDefinition {
          Type = ProfessionType.Pescador,
          DisplayName = "Pescador",
          ColorHex = "#4D9DE0",
          Aliases = ["pescador", "pesca", "fishing", "fisher"],
          PassiveMilestones = BuildPlaceholderMilestones("Pescador")
        }
      }
    };
  }

  private static List<ProfessionPassiveMilestoneDefinition> BuildMinerPassiveMilestones() {
    return [
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level25,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Pepita Dourada", Description = "Chance de obter ouro ao coletar veias de cobre." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Cobre Extra", Description = "Chance de obter cobre extra ao coletar veias de cobre." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level50,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Pepita Dourada", Description = "Chance de obter ouro ao coletar veias de ferro." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Ferro Extra", Description = "Chance de obter ferro extra ao coletar veias de ferro." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level75,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Pepita Dourada", Description = "Chance de obter ouro ao coletar veias de quartzo." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Quartzo Extra", Description = "Chance de obter quartzo extra ao coletar veias de quartzo." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level100,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Pepita Dourada", Description = "Chance de obter ouro ao coletar cristal de sangue." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Cristal de Sangue Extra", Description = "Chance de obter cristal de sangue extra ao coletar cristal de sangue." }
      }
    ];
  }

  private static List<ProfessionPassiveMilestoneDefinition> BuildPlaceholderMilestones(string professionName) {
    return [
      BuildPlaceholderMilestone(ProfessionMilestone.Level25, professionName),
      BuildPlaceholderMilestone(ProfessionMilestone.Level50, professionName),
      BuildPlaceholderMilestone(ProfessionMilestone.Level75, professionName),
      BuildPlaceholderMilestone(ProfessionMilestone.Level100, professionName)
    ];
  }

  private static ProfessionPassiveMilestoneDefinition BuildPlaceholderMilestone(ProfessionMilestone milestone, string professionName) {
    return new ProfessionPassiveMilestoneDefinition {
      Milestone = milestone,
      Option1 = new ProfessionPassiveOption {
        Option = 1,
        Name = $"{professionName} Passiva A",
        Description = "Estrutura pronta para efeito futuro."
      },
      Option2 = new ProfessionPassiveOption {
        Option = 2,
        Name = $"{professionName} Passiva B",
        Description = "Estrutura pronta para efeito futuro."
      }
    };
  }

  private static Dictionary<string, ProfessionType> BuildAliasLookup() {
    var lookup = new Dictionary<string, ProfessionType>(StringComparer.Ordinal);

    foreach (ProfessionDefinition definition in Definitions.Values) {
      foreach (string alias in definition.Aliases) {
        lookup[Normalize(alias)] = definition.Type;
      }

      lookup[Normalize(definition.DisplayName)] = definition.Type;
    }

    return lookup;
  }

  private static string Normalize(string input) {
    if (string.IsNullOrWhiteSpace(input)) {
      return string.Empty;
    }

    string lower = input.Trim().ToLowerInvariant();
    string formD = lower.Normalize(NormalizationForm.FormD);
    var sb = new StringBuilder(formD.Length);

    foreach (char c in formD) {
      UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
      if (category == UnicodeCategory.NonSpacingMark) {
        continue;
      }

      if (char.IsLetterOrDigit(c)) {
        sb.Append(c);
      }
    }

    return sb.ToString();
  }
}
