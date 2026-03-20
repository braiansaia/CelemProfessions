using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CelemProfessions.Models;

namespace CelemProfessions.Service;

public static class ProfessionCatalogService {
  private static readonly Dictionary<ProfessionsTypes, Definition> Definitions = BuildDefinitions();
  private static readonly Dictionary<string, ProfessionsTypes> AliasLookup = BuildAliasLookup();
  private static readonly Dictionary<int, string> NormalizedPrefabNames = [];

  public static bool TryResolveProfession(string input, out ProfessionsTypes profession, out string error) {
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

    HashSet<ProfessionsTypes> matches = [];
    foreach (KeyValuePair<string, ProfessionsTypes> pair in AliasLookup) {
      if (pair.Key.StartsWith(normalized, StringComparison.Ordinal)) {
        matches.Add(pair.Value);
      }
    }

    foreach (Definition definition in Definitions.Values) {
      if (Normalize(definition.DisplayName).StartsWith(normalized, StringComparison.Ordinal)) {
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

    error = $"Profissao ambigua. Opcoes: {string.Join(", ", matches.Select(GetDisplayName))}.";
    return false;
  }

  public static string GetDisplayName(ProfessionsTypes profession) {
    return Definitions.TryGetValue(profession, out Definition definition)
      ? definition.DisplayName
      : profession.ToString();
  }

  public static string GetColorHex(ProfessionsTypes profession) {
    return Definitions.TryGetValue(profession, out Definition definition)
      ? definition.ColorHex
      : "#FFFFFF";
  }

  public static IReadOnlyList<ProfessionPassiveMilestoneDefinition> GetMilestones(ProfessionsTypes profession) {
    return Definitions.TryGetValue(profession, out Definition definition)
      ? definition.PassiveMilestones
      : [];
  }

  public static bool IsOrePrefab(Stunlock.Core.PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    if (name.Contains("stone", StringComparison.Ordinal) && !name.Contains("ore", StringComparison.Ordinal) && !name.Contains("gem", StringComparison.Ordinal) && !name.Contains("quartz", StringComparison.Ordinal) && !name.Contains("bloodcrystal", StringComparison.Ordinal)) {
      return false;
    }

    return name.Contains("ore", StringComparison.Ordinal)
      || name.Contains("mineral", StringComparison.Ordinal)
      || name.Contains("bloodcrystal", StringComparison.Ordinal)
      || name.Contains("quartz", StringComparison.Ordinal)
      || name.Contains("sulphur", StringComparison.Ordinal)
      || name.Contains("techscrap", StringComparison.Ordinal)
      || name.Contains("emery", StringComparison.Ordinal);
  }

  public static bool IsWoodPrefab(Stunlock.Core.PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("wood", StringComparison.Ordinal) || name.Contains("lumber", StringComparison.Ordinal) || name.Contains("plank", StringComparison.Ordinal);
  }

  public static bool IsPlantPrefab(Stunlock.Core.PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("plant", StringComparison.Ordinal)
      || name.Contains("fiber", StringComparison.Ordinal)
      || name.Contains("flower", StringComparison.Ordinal)
      || name.Contains("herb", StringComparison.Ordinal)
      || name.Contains("mushroom", StringComparison.Ordinal)
      || name.Contains("trippyshroom", StringComparison.Ordinal);
  }

  public static bool IsGemPrefab(Stunlock.Core.PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("gem", StringComparison.Ordinal) || name.Contains("jewel", StringComparison.Ordinal) || name.Contains("magicsource", StringComparison.Ordinal);
  }

  public static bool IsLeatherPrefab(Stunlock.Core.PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("hide", StringComparison.Ordinal) || name.Contains("leather", StringComparison.Ordinal) || name.Contains("skin", StringComparison.Ordinal);
  }

  public static bool IsWeaponPrefab(Stunlock.Core.PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("weapon", StringComparison.Ordinal) || name.Contains("onyxtear", StringComparison.Ordinal);
  }

  public static bool IsArmorPrefab(Stunlock.Core.PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("armor", StringComparison.Ordinal)
      || name.Contains("cloak", StringComparison.Ordinal)
      || name.Contains("bag", StringComparison.Ordinal)
      || name.Contains("cloth", StringComparison.Ordinal)
      || name.Contains("chest", StringComparison.Ordinal)
      || name.Contains("boots", StringComparison.Ordinal)
      || name.Contains("gloves", StringComparison.Ordinal)
      || name.Contains("legs", StringComparison.Ordinal);
  }

  public static bool IsNecklacePrefab(Stunlock.Core.PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("necklace", StringComparison.Ordinal) || name.Contains("pendant", StringComparison.Ordinal) || name.Contains("amulet", StringComparison.Ordinal) || name.Contains("magicsource", StringComparison.Ordinal);
  }

  public static bool IsConsumablePrefab(Stunlock.Core.PrefabGUID prefab) {
    string name = GetNormalizedPrefabName(prefab);
    return name.Contains("canteen", StringComparison.Ordinal)
      || name.Contains("potion", StringComparison.Ordinal)
      || name.Contains("bottle", StringComparison.Ordinal)
      || name.Contains("flask", StringComparison.Ordinal)
      || name.Contains("consumable", StringComparison.Ordinal)
      || name.Contains("duskcaller", StringComparison.Ordinal)
      || name.Contains("elixir", StringComparison.Ordinal)
      || name.Contains("coating", StringComparison.Ordinal)
      || name.Contains("salve", StringComparison.Ordinal)
      || name.Contains("brew", StringComparison.Ordinal)
      || name.Contains("gruel", StringComparison.Ordinal);
  }

  public static bool TryResolveFishingRegion(Stunlock.Core.PrefabGUID fishingAreaPrefab, out string region) {
    region = string.Empty;
    string name = GetNormalizedPrefabName(fishingAreaPrefab);
    if (string.IsNullOrEmpty(name)) {
      return false;
    }

    if (name.Contains("farbane", StringComparison.Ordinal)) {
      region = "farbane";
      return true;
    }

    if (name.Contains("dunley", StringComparison.Ordinal)) {
      region = "dunley";
      return true;
    }

    if (name.Contains("gloomrot", StringComparison.Ordinal)) {
      region = "gloomrot";
      return true;
    }

    if (name.Contains("cursed", StringComparison.Ordinal)) {
      region = "cursed";
      return true;
    }

    if (name.Contains("silverlight", StringComparison.Ordinal)) {
      region = "silverlight";
      return true;
    }

    if (name.Contains("strongblade", StringComparison.Ordinal)) {
      region = "strongblade";
      return true;
    }

    if (name.Contains("mortium", StringComparison.Ordinal)) {
      region = "mortium";
      return true;
    }

    return false;
  }

  internal static string GetNormalizedPrefabName(Stunlock.Core.PrefabGUID prefab) {
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

  private static Dictionary<ProfessionsTypes, Definition> BuildDefinitions() {
    return new Dictionary<ProfessionsTypes, Definition> {
      {
        ProfessionsTypes.Minerador,
        new Definition {
          Type = ProfessionsTypes.Minerador,
          DisplayName = "Minerador",
          ColorHex = "#B0B7C3",
          Aliases = ["minerador", "mineracao", "mining", "miner"],
          PassiveMilestones = BuildMinerPassiveMilestones()
        }
      },
      {
        ProfessionsTypes.Lenhador,
        new Definition {
          Type = ProfessionsTypes.Lenhador,
          DisplayName = "Lenhador",
          ColorHex = "#8B5A2B",
          Aliases = ["lenhador", "woodcutting", "madeira", "wood"],
          PassiveMilestones = BuildPlaceholderMilestones("Lenhador")
        }
      },
      {
        ProfessionsTypes.Herbalista,
        new Definition {
          Type = ProfessionsTypes.Herbalista,
          DisplayName = "Herbalista",
          ColorHex = "#3FA34D",
          Aliases = ["herbalista", "herbalism", "herb", "coleta", "harvesting"],
          PassiveMilestones = BuildPlaceholderMilestones("Herbalista")
        }
      },
      {
        ProfessionsTypes.Joalheiro,
        new Definition {
          Type = ProfessionsTypes.Joalheiro,
          DisplayName = "Joalheiro",
          ColorHex = "#C77DFF",
          Aliases = ["joalheiro", "jewel", "jeweler", "jewelcrafting", "encantamento", "enchanting"],
          PassiveMilestones = BuildPlaceholderMilestones("Joalheiro")
        }
      },
      {
        ProfessionsTypes.Alfaiate,
        new Definition {
          Type = ProfessionsTypes.Alfaiate,
          DisplayName = "Alfaiate",
          ColorHex = "#E5989B",
          Aliases = ["alfaiate", "alfaiataria", "tailor", "tailoring"],
          PassiveMilestones = BuildPlaceholderMilestones("Alfaiate")
        }
      },
      {
        ProfessionsTypes.Ferreiro,
        new Definition {
          Type = ProfessionsTypes.Ferreiro,
          DisplayName = "Ferreiro",
          ColorHex = "#6C757D",
          Aliases = ["ferreiro", "blacksmith", "blacksmithing", "smith"],
          PassiveMilestones = BuildPlaceholderMilestones("Ferreiro")
        }
      },
      {
        ProfessionsTypes.Alquimista,
        new Definition {
          Type = ProfessionsTypes.Alquimista,
          DisplayName = "Alquimista",
          ColorHex = "#2EC4B6",
          Aliases = ["alquimista", "alquimia", "alchemy", "alchemist"],
          PassiveMilestones = BuildPlaceholderMilestones("Alquimista")
        }
      },
      {
        ProfessionsTypes.Cacador,
        new Definition {
          Type = ProfessionsTypes.Cacador,
          DisplayName = "Cacador",
          ColorHex = "#C08552",
          Aliases = ["cacador", "caçador", "hunter", "hunting"],
          PassiveMilestones = BuildPlaceholderMilestones("Cacador")
        }
      },
      {
        ProfessionsTypes.Pescador,
        new Definition {
          Type = ProfessionsTypes.Pescador,
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

  private static Dictionary<string, ProfessionsTypes> BuildAliasLookup() {
    Dictionary<string, ProfessionsTypes> lookup = new(StringComparer.Ordinal);
    foreach (Definition definition in Definitions.Values) {
      for (int i = 0; i < definition.Aliases.Count; i++) {
        lookup[Normalize(definition.Aliases[i])] = definition.Type;
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
    StringBuilder builder = new(formD.Length);
    for (int i = 0; i < formD.Length; i++) {
      char current = formD[i];
      UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(current);
      if (category == UnicodeCategory.NonSpacingMark) {
        continue;
      }

      if (char.IsLetterOrDigit(current)) {
        builder.Append(current);
      }
    }

    return builder.ToString();
  }
}
