using System;
using System.Collections.Generic;
using CelemProfessions.Models;
using ProjectM;
using ScarletCore.Services;

namespace CelemProfessions.Service;

public static partial class ProfessionService {
  public static void Initialize() {
    if (_initialized) {
      return;
    }

    PlayerCache.Clear();
    foreach (KeyValuePair<string, PlayerProfessionsData> item in Plugin.Database.GetAllByPrefix<PlayerProfessionsData>("professions/players/")) {
      PlayerProfessionsData value = item.Value;
      if (value == null || value.PlatformId == 0) {
        continue;
      }

      bool normalized = NormalizePlayerData(value, value.PlatformId);
      PlayerCache[value.PlatformId] = value;
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
    PlayerProfessionsData playerData = EnsurePlayerData(platformId);
    playerData.ExperienceLogEnabled = !playerData.ExperienceLogEnabled;
    enabled = playerData.ExperienceLogEnabled;
    SavePlayerData(playerData);
    return true;
  }

  public static bool ToggleExperienceSct(ulong platformId, out bool enabled) {
    PlayerProfessionsData playerData = EnsurePlayerData(platformId);
    playerData.ExperienceSctEnabled = !playerData.ExperienceSctEnabled;
    enabled = playerData.ExperienceSctEnabled;
    SavePlayerData(playerData);
    return true;
  }

  public static IReadOnlyList<ProfessionSummaryView> GetProfessionSummaries(ulong platformId) {
    PlayerProfessionsData data = EnsurePlayerData(platformId);
    List<ProfessionSummaryView> summaries = new(ProfessionOrder.Length);
    for (int i = 0; i < ProfessionOrder.Length; i++) {
      ProfessionsTypes profession = ProfessionOrder[i];
      ProfessionProgressData progress = GetProfessionProgress(data, profession);
      summaries.Add(new ProfessionSummaryView {
        Profession = profession,
        DisplayName = ProfessionCatalogService.GetDisplayName(profession),
        Level = progress.Level
      });
    }

    return summaries;
  }

  public static ProfessionDetailsView GetProfessionDetails(ulong platformId, ProfessionsTypes profession) {
    ProfessionProgressData progress = GetProfessionProgress(EnsurePlayerData(platformId), profession);
    int level = progress.Level;
    double currentLevelBase = ProgressionService.GetLevelStartExperience(level);
    double requiredExperience = level >= ProgressionService.GetMaxLevel()
      ? 0d
      : Math.Max(1d, ProgressionService.GetRequiredExperienceForLevel(level));
    double currentLevelExperience = level >= ProgressionService.GetMaxLevel()
      ? 0d
      : Math.Clamp(progress.Experience - currentLevelBase, 0d, requiredExperience);
    double percent = level >= ProgressionService.GetMaxLevel()
      ? 0d
      : ProgressionService.GetLevelPercent(progress.Experience, level);

    return new ProfessionDetailsView {
      Profession = profession,
      DisplayName = ProfessionCatalogService.GetDisplayName(profession),
      ColorHex = ProfessionCatalogService.GetColorHex(profession),
      Level = level,
      TotalExperience = progress.Experience,
      CurrentLevelExperience = currentLevelExperience,
      RequiredExperience = requiredExperience,
      Percent = percent,
      IsMaxLevel = level >= ProgressionService.GetMaxLevel(),
      Passives = PassivesService.BuildViews(profession, level, progress.PassiveChoices)
    };
  }

  public static bool TrySetLevel(ulong platformId, ProfessionsTypes profession, int level, out string error) {
    error = string.Empty;
    if (level < ProgressionService.GetStartingLevel() || level > ProgressionService.GetMaxLevel()) {
      error = $"O nivel deve estar entre {ProgressionService.GetStartingLevel()} e {ProgressionService.GetMaxLevel()}.";
      return false;
    }

    PlayerProfessionsData data = EnsurePlayerData(platformId);
    ProfessionProgressData progress = GetProfessionProgress(data, profession);
    progress.Level = level;
    progress.Experience = ProgressionService.GetLevelStartExperience(level);
    SavePlayerData(data);
    return true;
  }

  public static bool TrySetLevelPercent(ulong platformId, ProfessionsTypes profession, double percent, out string error) {
    error = string.Empty;
    if (percent < 0d || percent > 99.999d) {
      error = "O percentual deve estar entre 0 e " + 99.999d.ToString("0.000", PercentCulture) + ".";
      return false;
    }

    PlayerProfessionsData data = EnsurePlayerData(platformId);
    ProfessionProgressData progress = GetProfessionProgress(data, profession);
    if (progress.Level >= ProgressionService.GetMaxLevel()) {
      progress.Experience = ProgressionService.GetLevelStartExperience(ProgressionService.GetMaxLevel());
      SavePlayerData(data);
      return true;
    }

    double currentLevelBase = ProgressionService.GetLevelStartExperience(progress.Level);
    double requiredExperience = Math.Max(1d, ProgressionService.GetRequiredExperienceForLevel(progress.Level));
    progress.Experience = currentLevelBase + (requiredExperience * percent / 100d);
    progress.Level = ProgressionService.GetLevelFromExperience(progress.Experience);
    SavePlayerData(data);
    return true;
  }

  public static bool TryChoosePassive(ulong platformId, ProfessionsTypes profession, ProfessionMilestone milestone, int option, out string error) {
    return PassivesService.TryChoosePassive(platformId, profession, milestone, option, out error);
  }

  public static ResetPassivesStatus ResetProfessionPassives(ulong platformId, ProfessionsTypes profession, out int removedCount) {
    return PassivesService.ResetProfessionPassives(platformId, profession, out removedCount);
  }
}
