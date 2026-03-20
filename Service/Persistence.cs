using System;
using System.Collections.Generic;
using CelemProfessions.Models;

namespace CelemProfessions.Service;

public static partial class ProfessionService {
  private static PlayerProfessionsData EnsurePlayerData(ulong platformId) {
    if (PlayerCache.TryGetValue(platformId, out PlayerProfessionsData value) && value != null) {
      return value;
    }

    string key = BuildPlayerKey(platformId);
    PlayerProfessionsData playerData = Plugin.Database.GetOrCreate(key, () => CreateDefaultPlayerData(platformId));
    if (playerData == null) {
      playerData = CreateDefaultPlayerData(platformId);
    }

    bool normalized = NormalizePlayerData(playerData, platformId);
    PlayerCache[platformId] = playerData;
    if (normalized) {
      SavePlayerData(playerData);
    }

    return playerData;
  }

  private static bool NormalizePlayerData(PlayerProfessionsData data, ulong platformId) {
    bool changed = false;
    bool migrateLegacyProgression = data.DataVersion < CurrentDataVersion;

    if (data.PlatformId != platformId) {
      data.PlatformId = platformId;
      changed = true;
    }

    if (data.Professions == null) {
      data.Professions = new Dictionary<string, ProfessionProgressData>();
      changed = true;
    }

    for (int i = 0; i < ProfessionOrder.Length; i++) {
      ProfessionsTypes profession = ProfessionOrder[i];
      string key = profession.ToString();
      if (!data.Professions.TryGetValue(key, out ProfessionProgressData value) || value == null) {
        data.Professions[key] = CreateDefaultProgressData();
        changed = true;
        continue;
      }

      if (value.PassiveChoices == null) {
        value.PassiveChoices = new Dictionary<int, int>();
        changed = true;
      }

      int clampedLevel = Math.Clamp(value.Level, ProgressionService.GetStartingLevel(), ProgressionService.GetMaxLevel());
      if (value.Level != clampedLevel) {
        value.Level = clampedLevel;
        changed = true;
      }

      if (migrateLegacyProgression) {
        MigrateLegacyProgress(value);
        changed = true;
      }

      double minimumExperience = ProgressionService.GetLevelStartExperience(value.Level);
      double maximumExperience = ProgressionService.GetLevelStartExperience(ProgressionService.GetMaxLevel());
      double clampedExperience = Math.Clamp(value.Experience, ProgressionService.GetLevelStartExperience(ProgressionService.GetStartingLevel()), maximumExperience);
      if (Math.Abs(value.Experience - clampedExperience) > double.Epsilon) {
        value.Experience = clampedExperience;
        changed = true;
      }

      if (value.Level >= ProgressionService.GetMaxLevel()) {
        if (Math.Abs(value.Experience - maximumExperience) > double.Epsilon) {
          value.Experience = maximumExperience;
          changed = true;
        }
      } else if (value.Experience < minimumExperience) {
        value.Experience = minimumExperience;
        changed = true;
      }

      int resolvedLevel = ProgressionService.GetLevelFromExperience(value.Experience);
      if (value.Level != resolvedLevel) {
        value.Level = resolvedLevel;
        changed = true;
      }

      if (PassivesService.NormalizeChoices(profession, value.PassiveChoices)) {
        changed = true;
      }
    }

    if (data.DataVersion != CurrentDataVersion) {
      data.DataVersion = CurrentDataVersion;
      changed = true;
    }

    return changed;
  }

  private static void MigrateLegacyProgress(ProfessionProgressData progress) {
    int level = Math.Clamp(progress.Level, ProgressionService.GetStartingLevel(), ProgressionService.GetMaxLevel());
    if (level >= ProgressionService.GetMaxLevel()) {
      progress.Experience = ProgressionService.GetLevelStartExperience(ProgressionService.GetMaxLevel());
      return;
    }

    double legacyLevelStart = ProgressionService.GetLegacyLevelStartExperience(level);
    double legacyRequired = Math.Max(1d, ProgressionService.GetLegacyRequiredExperienceForLevel(level));
    double legacyPercent = Math.Clamp((progress.Experience - legacyLevelStart) / legacyRequired, 0d, 0.99999d);

    double newLevelStart = ProgressionService.GetLevelStartExperience(level);
    double newRequired = Math.Max(1d, ProgressionService.GetRequiredExperienceForLevel(level));
    progress.Experience = newLevelStart + (newRequired * legacyPercent);
  }

  private static PlayerProfessionsData CreateDefaultPlayerData(ulong platformId) {
    PlayerProfessionsData playerData = new() {
      DataVersion = CurrentDataVersion,
      PlatformId = platformId,
      ExperienceLogEnabled = true,
      ExperienceSctEnabled = true,
      UpdatedAtUtc = DateTime.UtcNow
    };

    for (int i = 0; i < ProfessionOrder.Length; i++) {
      ProfessionsTypes profession = ProfessionOrder[i];
      playerData.Professions[profession.ToString()] = CreateDefaultProgressData();
    }

    return playerData;
  }

  private static ProfessionProgressData CreateDefaultProgressData() {
    return new ProfessionProgressData {
      Level = ProgressionService.GetStartingLevel(),
      Experience = ProgressionService.GetLevelStartExperience(ProgressionService.GetStartingLevel()),
      PassiveChoices = new Dictionary<int, int>()
    };
  }

  private static ProfessionProgressData GetProfessionProgress(PlayerProfessionsData data, ProfessionsTypes profession) {
    string key = profession.ToString();
    if (!data.Professions.TryGetValue(key, out ProfessionProgressData value) || value == null) {
      value = CreateDefaultProgressData();
      data.Professions[key] = value;
      SavePlayerData(data);
    }

    return value;
  }

  private static void SavePlayerData(PlayerProfessionsData data) {
    if (data == null || data.PlatformId == 0) {
      return;
    }

    data.UpdatedAtUtc = DateTime.UtcNow;
    data.DataVersion = CurrentDataVersion;
    PlayerCache[data.PlatformId] = data;
    Plugin.Database.Set(BuildPlayerKey(data.PlatformId), data);
  }

  private static string BuildPlayerKey(ulong platformId) {
    return $"professions/players/{platformId}";
  }
}
