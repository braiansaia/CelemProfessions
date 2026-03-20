using System;
using System.Collections.Generic;
using CelemProfessions.Models;
using ProjectM;
using ScarletCore.Services;
using Stunlock.Core;

namespace CelemProfessions.Service;

public static partial class ProfessionService {
  public static void Initialize() {
    if (_initialized) {
      return;
    }

    PlayerCache.Clear();
    foreach (KeyValuePair<string, PlayerProfessionsData> item in Plugin.Database.GetAllByPrefix<PlayerProfessionsData>("professions/players/")) {
      PlayerProfessionsData value = item.Value;
      if (value == null) {
        continue;
      }

      ulong platformId = value.PlatformId;
      if (platformId == 0) {
        continue;
      }

      bool normalized = NormalizePlayerData(value, platformId);
      PlayerCache[platformId] = value;
      if (normalized) {
        SavePlayerData(value);
      }
    }

    _initialized = true;
  }

  public static void Shutdown() {
    PlayerCache.Clear();
    _initialized = false;
  }

  public static void HandlePlayerJoined(PlayerData player) {
    if (player != null) {
      EnsurePlayerData(player.PlatformId);
    }
  }

  public static bool ToggleExperienceLog(ulong platformId, out bool enabled) {
    PlayerProfessionsData playerProfessionsData = EnsurePlayerData(platformId);
    playerProfessionsData.ExperienceLogEnabled = !playerProfessionsData.ExperienceLogEnabled;
    enabled = playerProfessionsData.ExperienceLogEnabled;
    SavePlayerData(playerProfessionsData);
    return true;
  }

  public static bool ToggleExperienceSct(ulong platformId, out bool enabled) {
    PlayerProfessionsData playerProfessionsData = EnsurePlayerData(platformId);
    playerProfessionsData.ExperienceSctEnabled = !playerProfessionsData.ExperienceSctEnabled;
    enabled = playerProfessionsData.ExperienceSctEnabled;
    SavePlayerData(playerProfessionsData);
    return true;
  }

  public static IReadOnlyList<ProfessionSummaryView> GetProfessionSummaries(ulong platformId) {
    PlayerProfessionsData data = EnsurePlayerData(platformId);
    List<ProfessionSummaryView> list = new(ProfessionOrder.Length);
    for (int i = 0; i < ProfessionOrder.Length; i++) {
      ProfessionType profession = ProfessionOrder[i];
      ProfessionProgressData professionProgress = GetProfessionProgress(data, profession);
      list.Add(new ProfessionSummaryView {
        Profession = profession,
        DisplayName = ProfessionCatalogService.GetDisplayName(profession),
        Level = professionProgress.Level
      });
    }

    return list;
  }

  public static ProfessionDetailsView GetProfessionDetails(ulong platformId, ProfessionType profession) {
    ProfessionProgressData professionProgress = GetProfessionProgress(EnsurePlayerData(platformId), profession);
    int level = professionProgress.Level;
    double currentLevelBase = ConvertLevelToXp(level);
    double nextLevelBase = level >= 100 ? currentLevelBase : ConvertLevelToXp(level + 1);
    double requiredExperience = level >= 100 ? 0d : Math.Max(1d, nextLevelBase - currentLevelBase);
    double currentLevelExperience = level >= 100 ? 0d : Math.Clamp(professionProgress.Experience - currentLevelBase, 0d, requiredExperience);
    double percent = level >= 100 ? 0d : GetLevelPercent(professionProgress.Experience, level);

    ProfessionDetailsView details = new() {
      Profession = profession,
      DisplayName = ProfessionCatalogService.GetDisplayName(profession),
      ColorHex = ProfessionCatalogService.GetColorHex(profession),
      Level = level,
      TotalExperience = professionProgress.Experience,
      CurrentLevelExperience = currentLevelExperience,
      RequiredExperience = requiredExperience,
      Percent = percent,
      IsMaxLevel = level >= 100
    };

    IReadOnlyList<ProfessionPassiveMilestoneDefinition> milestones = ProfessionCatalogService.GetMilestones(profession);
    for (int i = 0; i < milestones.Count; i++) {
      ProfessionPassiveMilestoneDefinition milestone = milestones[i];
      professionProgress.PassiveChoices.TryGetValue((int)milestone.Milestone, out int value);
      details.Passives.Add(new ProfessionPassiveView {
        Milestone = milestone.Milestone,
        Option1 = milestone.Option1,
        Option2 = milestone.Option2,
        SelectedOption = value,
        Unlocked = level >= (int)milestone.Milestone
      });
    }

    return details;
  }

  public static bool TrySetLevel(ulong platformId, ProfessionType profession, int level, out string error) {
    error = string.Empty;
    if (level < 1 || level > 100) {
      error = "O nivel deve estar entre 1 e 100.";
      return false;
    }

    PlayerProfessionsData data = EnsurePlayerData(platformId);
    ProfessionProgressData professionProgress = GetProfessionProgress(data, profession);
    professionProgress.Level = level;
    professionProgress.Experience = ConvertLevelToXp(level);
    SavePlayerData(data);
    return true;
  }

  public static bool TrySetLevelPercent(ulong platformId, ProfessionType profession, double percent, out string error) {
    error = string.Empty;
    if (percent < 0d || percent > 99.999) {
      error = "O percentual deve estar entre 0 e " + 99.999.ToString("0.000", PercentCulture) + ".";
      return false;
    }

    PlayerProfessionsData data = EnsurePlayerData(platformId);
    ProfessionProgressData professionProgress = GetProfessionProgress(data, profession);
    if (professionProgress.Level >= 100) {
      professionProgress.Experience = ConvertLevelToXp(100);
      SavePlayerData(data);
      return true;
    }

    double currentLevelBase = ConvertLevelToXp(professionProgress.Level);
    double nextLevelBase = ConvertLevelToXp(professionProgress.Level + 1);
    double requiredExperience = Math.Max(1d, nextLevelBase - currentLevelBase);
    professionProgress.Experience = currentLevelBase + requiredExperience * percent / 100d;
    professionProgress.Level = ConvertXpToLevel(professionProgress.Experience);
    SavePlayerData(data);
    return true;
  }

  public static bool TryChoosePassive(ulong platformId, ProfessionType profession, ProfessionMilestone milestone, int option, out string error) {
    error = string.Empty;
    if (option != 1 && option != 2) {
      error = "A opcao de passiva deve ser 1 ou 2.";
      return false;
    }

    PlayerProfessionsData data = EnsurePlayerData(platformId);
    ProfessionProgressData professionProgress = GetProfessionProgress(data, profession);
    if (professionProgress.Level < (int)milestone) {
      error = $"Nivel insuficiente. Alcance nivel {milestone} em {ProfessionCatalogService.GetDisplayName(profession)}.";
      return false;
    }

    IReadOnlyList<ProfessionPassiveMilestoneDefinition> milestones = ProfessionCatalogService.GetMilestones(profession);
    bool milestoneExists = false;
    for (int i = 0; i < milestones.Count; i++) {
      if (milestones[i].Milestone == milestone) {
        milestoneExists = true;
        break;
      }
    }

    if (!milestoneExists) {
      error = "Marco de passiva invalido para esta profissao.";
      return false;
    }

    if (professionProgress.PassiveChoices.TryGetValue((int)milestone, out int value)) {
      error = value == option
        ? "Essa passiva ja esta escolhida."
        : "Esse marco ja possui passiva escolhida. Use o reset da profissao para trocar.";
      return false;
    }

    professionProgress.PassiveChoices[(int)milestone] = option;
    SavePlayerData(data);
    return true;
  }

  public static ResetPassivesStatus ResetProfessionPassives(ulong platformId, ProfessionType profession, out int removedCount) {
    removedCount = 0;
    PlayerProfessionsData data = EnsurePlayerData(platformId);
    ProfessionProgressData professionProgress = GetProfessionProgress(data, profession);
    if (professionProgress.PassiveChoices.Count == 0) {
      return ResetPassivesStatus.NoPassivesChosen;
    }

    if (!TryResolveOnlinePlayer(platformId, out PlayerData player)) {
      return ResetPassivesStatus.PlayerUnavailable;
    }

    PrefabGUID resetPassiveCostItem = ProfessionSettingsService.ResetPassiveCostItem;
    int resetPassiveCostAmount = ProfessionSettingsService.ResetPassiveCostAmount;
    if (!InventoryService.HasAmount(player.CharacterEntity, resetPassiveCostItem, resetPassiveCostAmount)) {
      return ResetPassivesStatus.MissingCostItem;
    }

    if (!InventoryService.RemoveItem(player.CharacterEntity, resetPassiveCostItem, resetPassiveCostAmount)) {
      return ResetPassivesStatus.ConsumeFailed;
    }

    removedCount = professionProgress.PassiveChoices.Count;
    professionProgress.PassiveChoices.Clear();
    SavePlayerData(data);
    return ResetPassivesStatus.Reset;
  }
}

