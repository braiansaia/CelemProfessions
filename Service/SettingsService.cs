using System;
using CelemProfessions.Models;
using ScarletCore.Resources;
using Stunlock.Core;

namespace CelemProfessions.Service;

public static class ProfessionSettingsService {
  public static void Configure() {
    Plugin.Settings.Section("Professions")
      .Add("FishingBaseXp", 100.0, "EXP base por pescaria bem-sucedida.")
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
      .Add("LenhadorSpecialDropChanceAtMax", 0.35, "Chance maxima de drop especial do Lenhador no nivel 100.")
      .Add("HerbalistaYieldMultiplier", 1.0, "Escala da recompensa de planta extra.")
      .Add("HerbalistaSpecialDropChanceAtMax", 0.35, "Chance maxima de drop especial do Herbalista no nivel 100.")
      .Add("JoalheiroDurabilityBonusAtMax", 0.50, "Bonus maximo de durabilidade de colares no nivel 100.")
      .Add("JoalheiroGemChanceAtMax", 0.25, "Chance maxima de gema extra no nivel 100.")
      .Add("AlfaiateDurabilityBonusAtMax", 0.50, "Bonus maximo de durabilidade de armaduras no nivel 100.")
      .Add("FerreiroDurabilityBonusAtMax", 0.50, "Bonus maximo de durabilidade de armas no nivel 100.")
      .Add("AlquimistaPowerBonusAtMax", 0.30, "Bonus maximo de poder no nivel 100 para consumiveis criados.")
      .Add("AlquimistaDurationBonusAtMax", 0.30, "Bonus maximo de duracao no nivel 100 para consumiveis criados.")
      .Add("CacadorLeatherYieldMultiplier", 1.0, "Escala da recompensa de couro extra.")
      .Add("PescadorFishChanceAtMax", 0.45, "Chance maxima de peixe extra no nivel 100.");
  }

  public static double FishingBaseXp => Math.Max(0d, Plugin.Settings.Get<double>("FishingBaseXp"));
  public static PrefabGUID ResetPassiveCostItem => new(Plugin.Settings.Get<int>("ResetPassivesCostItem"));
  public static int ResetPassiveCostAmount => Math.Max(1, Plugin.Settings.Get<int>("ResetPassivesCostAmount"));
  public static double MineradorYieldMultiplier => Math.Max(0d, Plugin.Settings.Get<double>("MineradorYieldMultiplier"));
  public static double MineradorGoldChanceAtMax => ClampChance(Plugin.Settings.Get<double>("MineradorGoldChanceAtMax"));
  public static int MineradorGoldAmount => Math.Max(1, Plugin.Settings.Get<int>("MineradorGoldAmount"));
  public static double LenhadorYieldMultiplier => Math.Max(0d, Plugin.Settings.Get<double>("LenhadorYieldMultiplier"));
  public static double LenhadorSpecialDropChanceAtMax => ClampChance(Plugin.Settings.Get<double>("LenhadorSpecialDropChanceAtMax"));
  public static double HerbalistaYieldMultiplier => Math.Max(0d, Plugin.Settings.Get<double>("HerbalistaYieldMultiplier"));
  public static double HerbalistaSpecialDropChanceAtMax => ClampChance(Plugin.Settings.Get<double>("HerbalistaSpecialDropChanceAtMax"));
  public static double JoalheiroDurabilityBonusAtMax => Math.Max(0d, Plugin.Settings.Get<double>("JoalheiroDurabilityBonusAtMax"));
  public static double JoalheiroGemChanceAtMax => ClampChance(Plugin.Settings.Get<double>("JoalheiroGemChanceAtMax"));
  public static double AlfaiateDurabilityBonusAtMax => Math.Max(0d, Plugin.Settings.Get<double>("AlfaiateDurabilityBonusAtMax"));
  public static double FerreiroDurabilityBonusAtMax => Math.Max(0d, Plugin.Settings.Get<double>("FerreiroDurabilityBonusAtMax"));
  public static double AlquimistaPowerBonusAtMax => Math.Max(0d, Plugin.Settings.Get<double>("AlquimistaPowerBonusAtMax"));
  public static double AlquimistaDurationBonusAtMax => Math.Max(0d, Plugin.Settings.Get<double>("AlquimistaDurationBonusAtMax"));
  public static double CacadorLeatherYieldMultiplier => Math.Max(0d, Plugin.Settings.Get<double>("CacadorLeatherYieldMultiplier"));
  public static double PescadorFishChanceAtMax => ClampChance(Plugin.Settings.Get<double>("PescadorFishChanceAtMax"));

  public static double GetXpMultiplier(ProfessionsTypes profession) {
    return profession switch {
      ProfessionsTypes.Minerador => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierMinerador")),
      ProfessionsTypes.Lenhador => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierLenhador")),
      ProfessionsTypes.Herbalista => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierHerbalista")),
      ProfessionsTypes.Joalheiro => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierJoalheiro")),
      ProfessionsTypes.Alfaiate => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierAlfaiate")),
      ProfessionsTypes.Ferreiro => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierFerreiro")),
      ProfessionsTypes.Alquimista => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierAlquimista")),
      ProfessionsTypes.Cacador => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierCacador")),
      ProfessionsTypes.Pescador => Math.Max(0d, Plugin.Settings.Get<double>("XpMultiplierPescador")),
      _ => 1d
    };
  }

  private static double ClampChance(double value) {
    return Math.Clamp(value, 0d, 1d);
  }
}
