using System;
using CelemProfessions.Models;
using ScarletCore.Resources;
using Stunlock.Core;

namespace CelemProfessions.Service;

public static class ProfessionSettingsService {
  public const int MaxProfessionLevel = 100;

  public static void Configure() {
    Plugin.Settings.Section("Professions")
      .Add("GatherBaseXp", 10.0, "EXP base para eventos de coleta de recursos.")
      .Add("CraftBaseXp", 50.0, "EXP base para eventos de craft.")
      .Add("FishingBaseXp", 100.0, "EXP base por pescaria bem-sucedida.")
      .Add("HunterBaseXp", 60.0, "EXP base por eliminacao de alvo com drop de couro.")
      .Add("ResetPassivesCostItem", PrefabGUIDs.Item_Ingredient_Bone.GuidHash, "PrefabGUID consumido no reset de passivas.")
      .Add("ResetPassivesCostAmount", 1, "Quantidade do item consumido no reset de passivas.")
      .Add("XpMultiplierMinerador", 1.0, "Multiplicador de XP do Minerador.")
      .Add("XpMultiplierLenhador", 1.0, "Multiplicador de XP do Lenhador.")
      .Add("XpMultiplierHerbalista", 1.0, "Multiplicador de XP do Herbalista.")
      .Add("XpMultiplierJoalheiro", 1.0, "Multiplicador de XP do Joalheiro.")
      .Add("XpMultiplierAlfaiate", 1.0, "Multiplicador de XP do Alfaiate.")
      .Add("XpMultiplierFerreiro", 1.0, "Multiplicador de XP do Ferreiro.")
      .Add("XpMultiplierAlquimista", 1.0, "Multiplicador de XP do Alquimista.")
      .Add("XpMultiplierCacador", 1.0, "Multiplicador de XP do Cacador.")
      .Add("XpMultiplierPescador", 1.0, "Multiplicador de XP do Pescador.")
      .Add("MineradorYieldMultiplier", 1.0, "Escala da recompensa de minerio extra.")
      .Add("MineradorGoldChanceAtMax", 0.30, "Chance maxima de ouro extra no nivel 100.")
      .Add("MineradorGoldAmount", 1, "Quantidade de ouro extra por proc.")
      .Add("LenhadorYieldMultiplier", 1.0, "Escala da recompensa de madeira extra.")
      .Add("LenhadorSaplingChanceAtMax", 0.35, "Chance maxima de muda extra no nivel 100.")
      .Add("LenhadorSaplingAmount", 1, "Quantidade de mudas por proc.")
      .Add("HerbalistaYieldMultiplier", 1.0, "Escala da recompensa de planta extra.")
      .Add("HerbalistaSeedChanceAtMax", 0.35, "Chance maxima de sementes extras no nivel 100.")
      .Add("HerbalistaSeedAmount", 1, "Quantidade de sementes por proc.")
      .Add("JoalheiroDurabilityBonusAtMax", 0.50, "Bonus maximo de durabilidade de colares no nivel 100.")
      .Add("JoalheiroPerfectGemChanceAtMax", 0.25, "Chance maxima de gema perfeita no nivel 100.")
      .Add("JoalheiroPerfectGemAmount", 1, "Quantidade de gema perfeita por proc.")
      .Add("AlfaiateDurabilityBonusAtMax", 0.50, "Bonus maximo de durabilidade de armaduras no nivel 100.")
      .Add("FerreiroDurabilityBonusAtMax", 0.50, "Bonus maximo de durabilidade de armas no nivel 100.")
      .Add("AlquimistaPowerBonusAtMax", 0.30, "Bonus maximo de poder no nivel 100 para consumiveis criados.")
      .Add("AlquimistaDurationBonusAtMax", 0.30, "Bonus maximo de duracao no nivel 100 para consumiveis criados.")
      .Add("CacadorLeatherYieldMultiplier", 1.0, "Escala da recompensa de couro extra.")
      .Add("PescadorExtraFishChanceAtMax", 0.45, "Chance maxima de peixe extra no nivel 100.")
      .Add("PescadorExtraFishAmount", 1, "Quantidade de peixes extras por proc.");
  }

  public static double GatherBaseXp => Math.Max(0d, Plugin.Settings.Get<double>("GatherBaseXp"));

  public static double CraftBaseXp => Math.Max(0d, Plugin.Settings.Get<double>("CraftBaseXp"));

  public static double FishingBaseXp => Math.Max(0d, Plugin.Settings.Get<double>("FishingBaseXp"));

  public static double HunterBaseXp => Math.Max(0d, Plugin.Settings.Get<double>("HunterBaseXp"));

  public static PrefabGUID ResetPassiveCostItem => new(Plugin.Settings.Get<int>("ResetPassivesCostItem"));

  public static int ResetPassiveCostAmount => Math.Max(1, Plugin.Settings.Get<int>("ResetPassivesCostAmount"));

  public static double GetXpMultiplier(ProfessionType profession) {
    return profession switch {
      ProfessionType.Minerador => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierMinerador")),
      ProfessionType.Lenhador => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierLenhador")),
      ProfessionType.Herbalista => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierHerbalista")),
      ProfessionType.Joalheiro => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierJoalheiro")),
      ProfessionType.Alfaiate => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierAlfaiate")),
      ProfessionType.Ferreiro => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierFerreiro")),
      ProfessionType.Alquimista => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierAlquimista")),
      ProfessionType.Cacador => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierCacador")),
      ProfessionType.Pescador => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierPescador")),
      _ => 1d
    };
  }

  public static double MineradorYieldMultiplier => Math.Max(0d, Plugin.Settings.Get<double>("MineradorYieldMultiplier"));

  public static double MineradorGoldChanceAtMax => ClampChance(Plugin.Settings.Get<double>("MineradorGoldChanceAtMax"));

  public static int MineradorGoldAmount => Math.Max(1, Plugin.Settings.Get<int>("MineradorGoldAmount"));

  public static double LenhadorYieldMultiplier => Math.Max(0d, Plugin.Settings.Get<double>("LenhadorYieldMultiplier"));

  public static double LenhadorSaplingChanceAtMax => ClampChance(Plugin.Settings.Get<double>("LenhadorSaplingChanceAtMax"));

  public static int LenhadorSaplingAmount => Math.Max(1, Plugin.Settings.Get<int>("LenhadorSaplingAmount"));

  public static double HerbalistaYieldMultiplier => Math.Max(0d, Plugin.Settings.Get<double>("HerbalistaYieldMultiplier"));

  public static double HerbalistaSeedChanceAtMax => ClampChance(Plugin.Settings.Get<double>("HerbalistaSeedChanceAtMax"));

  public static int HerbalistaSeedAmount => Math.Max(1, Plugin.Settings.Get<int>("HerbalistaSeedAmount"));

  public static double JoalheiroDurabilityBonusAtMax => Math.Max(0d, Plugin.Settings.Get<double>("JoalheiroDurabilityBonusAtMax"));

  public static double JoalheiroPerfectGemChanceAtMax => ClampChance(Plugin.Settings.Get<double>("JoalheiroPerfectGemChanceAtMax"));

  public static int JoalheiroPerfectGemAmount => Math.Max(1, Plugin.Settings.Get<int>("JoalheiroPerfectGemAmount"));

  public static double AlfaiateDurabilityBonusAtMax => Math.Max(0d, Plugin.Settings.Get<double>("AlfaiateDurabilityBonusAtMax"));

  public static double FerreiroDurabilityBonusAtMax => Math.Max(0d, Plugin.Settings.Get<double>("FerreiroDurabilityBonusAtMax"));

  public static double AlquimistaPowerBonusAtMax => Math.Max(0d, Plugin.Settings.Get<double>("AlquimistaPowerBonusAtMax"));

  public static double AlquimistaDurationBonusAtMax => Math.Max(0d, Plugin.Settings.Get<double>("AlquimistaDurationBonusAtMax"));

  public static double CacadorLeatherYieldMultiplier => Math.Max(0d, Plugin.Settings.Get<double>("CacadorLeatherYieldMultiplier"));

  public static double PescadorExtraFishChanceAtMax => ClampChance(Plugin.Settings.Get<double>("PescadorExtraFishChanceAtMax"));

  public static int PescadorExtraFishAmount => Math.Max(1, Plugin.Settings.Get<int>("PescadorExtraFishAmount"));

  private static double ClampChance(double value) {
    return Math.Clamp(value, 0d, 1d);
  }
}

