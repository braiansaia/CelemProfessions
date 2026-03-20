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
          PassiveMilestones = BuildLenhadorPassiveMilestones()
        }
      },
      {
        ProfessionsTypes.Herbalista,
        new Definition {
          Type = ProfessionsTypes.Herbalista,
          DisplayName = "Herbalista",
          ColorHex = "#3FA34D",
          Aliases = ["herbalista", "herbalism", "herb", "coleta", "harvesting"],
          PassiveMilestones = BuildHerbalistaPassiveMilestones()
        }
      },
      {
        ProfessionsTypes.Joalheiro,
        new Definition {
          Type = ProfessionsTypes.Joalheiro,
          DisplayName = "Joalheiro",
          ColorHex = "#C77DFF",
          Aliases = ["joalheiro", "jewel", "jeweler", "jewelcrafting", "encantamento", "enchanting"],
          PassiveMilestones = BuildJoalheiroPassiveMilestones()
        }
      },
      {
        ProfessionsTypes.Alfaiate,
        new Definition {
          Type = ProfessionsTypes.Alfaiate,
          DisplayName = "Alfaiate",
          ColorHex = "#E5989B",
          Aliases = ["alfaiate", "alfaiataria", "tailor", "tailoring"],
          PassiveMilestones = BuildAlfaiatePassiveMilestones()
        }
      },
      {
        ProfessionsTypes.Ferreiro,
        new Definition {
          Type = ProfessionsTypes.Ferreiro,
          DisplayName = "Ferreiro",
          ColorHex = "#6C757D",
          Aliases = ["ferreiro", "blacksmith", "blacksmithing", "smith"],
          PassiveMilestones = BuildFerreiroPassiveMilestones()
        }
      },
      {
        ProfessionsTypes.Alquimista,
        new Definition {
          Type = ProfessionsTypes.Alquimista,
          DisplayName = "Alquimista",
          ColorHex = "#2EC4B6",
          Aliases = ["alquimista", "alquimia", "alchemy", "alchemist"],
          PassiveMilestones = BuildAlquimistaPassiveMilestones()
        }
      },
      {
        ProfessionsTypes.Cacador,
        new Definition {
          Type = ProfessionsTypes.Cacador,
          DisplayName = "Cacador",
          ColorHex = "#C08552",
          Aliases = ["cacador", "caçador", "hunter", "hunting"],
          PassiveMilestones = BuildCacadorPassiveMilestones()
        }
      },
      {
        ProfessionsTypes.Pescador,
        new Definition {
          Type = ProfessionsTypes.Pescador,
          DisplayName = "Pescador",
          ColorHex = "#4D9DE0",
          Aliases = ["pescador", "pesca", "fishing", "fisher"],
          PassiveMilestones = BuildPescadorPassiveMilestones()
        }
      }
    };
  }

  private static List<ProfessionPassiveMilestoneDefinition> BuildMinerPassiveMilestones() {
    return [
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level25,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Ouro Extra", Description = "Chance de coletar ouro ao quebrar qualquer pedra." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Cobre Extra", Description = "Chance de coletar cobre ao quebrar qualquer veia de cobre." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level50,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Ouro Extra", Description = "Chance de coletar ouro ao quebrar qualquer pedra." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Ferro Extra", Description = "Chance de coletar ferro ao quebrar qualquer veia de ferro." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level75,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Ouro Extra", Description = "Chance de coletar ouro ao quebrar qualquer pedra." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Quartzo Extra", Description = "Chance de coletar quartzo ao quebrar qualquer veia de quartzo." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level100,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Ouro Extra", Description = "Chance de coletar ouro ao quebrar qualquer pedra." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Cristal de Sangue Extra", Description = "Chance de coletar cristal de sangue ao quebrar cristal de sangue." }
      }
    ];
  }

  private static List<ProfessionPassiveMilestoneDefinition> BuildLenhadorPassiveMilestones() {
    return [
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level25,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Semente de Madeira Normal", Description = "Chance de coletar a semente da arvore ao quebrar arvore de madeira normal." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Madeira Normal Extra", Description = "Chance de aumentar a quantidade de Item_Ingredient_Wood_Standard." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level50,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Semente de Madeira Hollow", Description = "Chance de coletar a semente da arvore ao quebrar arvore de madeira hollow." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Madeira Hollow Extra", Description = "Chance de aumentar a quantidade de Item_Ingredient_Wood_Hallow." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level75,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Semente de Madeira Cursed", Description = "Chance de coletar a semente da arvore ao quebrar arvore de madeira cursed." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Madeira Cursed Extra", Description = "Chance de aumentar a quantidade de Item_Ingredient_Wood_Cursed." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level100,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Semente de Madeira Gloom", Description = "Chance de coletar a semente da arvore ao quebrar arvore de madeira gloom." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Madeira Gloom Extra", Description = "Chance de aumentar a quantidade de Item_Ingredient_Wood_Gloom." }
      }
    ];
  }

  private static List<ProfessionPassiveMilestoneDefinition> BuildHerbalistaPassiveMilestones() {
    return [
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level25,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Sementes Iniciais", Description = "Chance de coletar sementes de Blood Rose, Mourning Lily e Hell's Clarion." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Colheita Inicial", Description = "Chance de aumentar a quantidade de Blood Rose, Mourning Lily e Hell's Clarion." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level50,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Sementes Intermediarias", Description = "Chance de coletar sementes de Cotton, Sunflower e Thistle." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Colheita Intermediaria", Description = "Chance de aumentar a quantidade de Cotton, Sunflower e Thistle." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level75,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Sementes Avancadas", Description = "Chance de coletar sementes de Fire Blossom, Snow Flower e Bleeding Heart." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Colheita Avancada", Description = "Chance de aumentar a quantidade de Fire Blossom, Snow Flower e Bleeding Heart." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level100,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Sementes Finais", Description = "Chance de coletar sementes de Sacred Grape, Ghost Shroom e Plague Brier." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Colheita Final", Description = "Chance de aumentar a quantidade de Sacred Grape, Ghost Shroom e Plague Brier." }
      }
    ];
  }

  private static List<ProfessionPassiveMilestoneDefinition> BuildJoalheiroPassiveMilestones() {
    return [
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level25,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Gema Perfeita", Description = "Chance de coletar gema perfeita ao quebrar pedra de gema." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Durabilidade de Colar", Description = "+1% de durabilidade em colares criados." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level50,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Gema Perfeita", Description = "Chance de coletar gema perfeita ao quebrar pedra de gema." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Durabilidade de Colar", Description = "+2% de durabilidade em colares criados." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level75,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Gema Perfeita", Description = "Chance de coletar gema perfeita ao quebrar pedra de gema." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Durabilidade de Colar", Description = "+5% de durabilidade em colares criados." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level100,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Gema Perfeita", Description = "Chance de coletar gema perfeita ao quebrar pedra de gema." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Durabilidade de Colar", Description = "+7% de durabilidade em colares criados." }
      }
    ];
  }

  private static List<ProfessionPassiveMilestoneDefinition> BuildAlfaiatePassiveMilestones() {
    return [
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level25,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Durabilidade Fisica", Description = "+1% de durabilidade em armaduras Rogue e Warrior." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Durabilidade Magica", Description = "+1% de durabilidade em armaduras Brute e Scholar." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level50,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Durabilidade Fisica", Description = "+3% de durabilidade em armaduras Rogue e Warrior." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Durabilidade Magica", Description = "+3% de durabilidade em armaduras Brute e Scholar." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level75,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Durabilidade Fisica", Description = "+6% de durabilidade em armaduras Rogue e Warrior." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Durabilidade Magica", Description = "+6% de durabilidade em armaduras Brute e Scholar." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level100,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Durabilidade Fisica", Description = "+10% de durabilidade em armaduras Rogue e Warrior." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Durabilidade Magica", Description = "+10% de durabilidade em armaduras Brute e Scholar." }
      }
    ];
  }

  private static List<ProfessionPassiveMilestoneDefinition> BuildFerreiroPassiveMilestones() {
    return [
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level25,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Durabilidade Curto Alcance", Description = "+2% de durabilidade em armas de curto alcance." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Durabilidade Longo Alcance", Description = "+2% de durabilidade em armas de longo alcance." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level50,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Durabilidade Curto Alcance", Description = "+5% de durabilidade em armas de curto alcance." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Durabilidade Longo Alcance", Description = "+5% de durabilidade em armas de longo alcance." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level75,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Durabilidade Curto Alcance", Description = "+8% de durabilidade em armas de curto alcance." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Durabilidade Longo Alcance", Description = "+8% de durabilidade em armas de longo alcance." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level100,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Durabilidade Curto Alcance", Description = "+15% de durabilidade em armas de curto alcance." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Durabilidade Longo Alcance", Description = "+15% de durabilidade em armas de longo alcance." }
      }
    ];
  }

  private static List<ProfessionPassiveMilestoneDefinition> BuildAlquimistaPassiveMilestones() {
    return [
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level25,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Duracao de Pocoes", Description = "+5% na duracao de todas as pocoes." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Eficiencia de Pocoes", Description = "+1% na eficiencia de todas as pocoes." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level50,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Duracao de Pocoes", Description = "+8% na duracao de todas as pocoes." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Eficiencia de Pocoes", Description = "+2% na eficiencia de todas as pocoes." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level75,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Duracao de Pocoes", Description = "+13% na duracao de todas as pocoes." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Eficiencia de Pocoes", Description = "+5% na eficiencia de todas as pocoes." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level100,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Duracao de Pocoes", Description = "+17% na duracao de todas as pocoes." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Eficiencia de Pocoes", Description = "+7% na eficiencia de todas as pocoes." }
      }
    ];
  }

  private static List<ProfessionPassiveMilestoneDefinition> BuildCacadorPassiveMilestones() {
    return [
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level25,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Couro Passivo", Description = "+3% de couro em animais passivos." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Couro Agressivo", Description = "+2% de couro em animais agressivos." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level50,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Couro Passivo", Description = "+5% de couro em animais passivos." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Couro Agressivo", Description = "+3% de couro em animais agressivos." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level75,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Couro Passivo", Description = "+7% de couro em animais passivos." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Couro Agressivo", Description = "+5% de couro em animais agressivos." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level100,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Couro Passivo", Description = "+10% de couro em animais passivos." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Couro Agressivo", Description = "+7% de couro em animais agressivos." }
      }
    ];
  }

  private static List<ProfessionPassiveMilestoneDefinition> BuildPescadorPassiveMilestones() {
    return [
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level25,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Peixe Especifico", Description = "Chance de coletar Item_Ingredient_Fish_FatGoby_T01 em qualquer regiao." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Bonus Regional", Description = "Chance de +1 peixe em FARBANE e DUNLEY." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level50,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Peixe Especifico", Description = "Chance de coletar Item_Ingredient_Fish_RainbowTrout_T01 em qualquer regiao." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Bonus Regional", Description = "Chance de +1 peixe em CURSED e GLOOMROT." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level75,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Peixe Especifico", Description = "Chance de coletar Item_Ingredient_Fish_BloodSnapper_T02 em qualquer regiao." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Bonus Regional", Description = "Chance de +1 peixe em MORTIUM e STRONGBLADE." }
      },
      new ProfessionPassiveMilestoneDefinition {
        Milestone = ProfessionMilestone.Level100,
        Option1 = new ProfessionPassiveOption { Option = 1, Name = "Peixe Especifico", Description = "Chance de coletar Item_Ingredient_Fish_SageFish_T02 em qualquer regiao." },
        Option2 = new ProfessionPassiveOption { Option = 2, Name = "Bonus Regional", Description = "Chance de +1 peixe em SILVERLIGHT." }
      }
    ];
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


