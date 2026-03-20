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
    PlayerProfessionsData playerProfessionsData = Plugin.Database.GetOrCreate(key, () => CreateDefaultPlayerData(platformId));
    if (playerProfessionsData == null) {
      playerProfessionsData = CreateDefaultPlayerData(platformId);
    }

    bool normalized = NormalizePlayerData(playerProfessionsData, platformId);
    PlayerCache[platformId] = playerProfessionsData;
    if (normalized) {
      SavePlayerData(playerProfessionsData);
    }

    return playerProfessionsData;
  }

  private static bool NormalizePlayerData(PlayerProfessionsData data, ulong platformId) {
    bool changed = false;
    if (data.PlatformId != platformId) {
      data.PlatformId = platformId;
      changed = true;
    }

    if (data.Professions == null) {
      data.Professions = new Dictionary<string, ProfessionProgressData>();
      changed = true;
    }

    for (int i = 0; i < ProfessionOrder.Length; i++) {
      ProfessionType profession = ProfessionOrder[i];
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

      int clampedLevel = Math.Clamp(value.Level, 1, 100);
      if (value.Level != clampedLevel) {
        value.Level = clampedLevel;
        changed = true;
      }

      double maxExperience = ConvertLevelToXp(100);
      double clampedExperience = Math.Clamp(value.Experience, 0d, maxExperience);
      if (Math.Abs(value.Experience - clampedExperience) > double.Epsilon) {
        value.Experience = clampedExperience;
        changed = true;
      }

      double minimumExperience = ConvertLevelToXp(value.Level);
      if (value.Experience < minimumExperience) {
        value.Experience = minimumExperience;
        changed = true;
      }

      int resolvedLevel = ConvertXpToLevel(value.Experience);
      if (value.Level != resolvedLevel) {
        value.Level = resolvedLevel;
        changed = true;
      }

      IReadOnlyList<ProfessionPassiveMilestoneDefinition> milestones = ProfessionCatalogService.GetMilestones(profession);
      List<int> invalidChoices = new();
      foreach (KeyValuePair<int, int> passiveChoice in value.PassiveChoices) {
        bool validMilestone = false;
        for (int j = 0; j < milestones.Count; j++) {
          if (milestones[j].Milestone == (ProfessionMilestone)passiveChoice.Key) {
            validMilestone = true;
            break;
          }
        }

        if (!validMilestone || (passiveChoice.Value != 1 && passiveChoice.Value != 2)) {
          invalidChoices.Add(passiveChoice.Key);
        }
      }

      for (int i2 = 0; i2 < invalidChoices.Count; i2++) {
        value.PassiveChoices.Remove(invalidChoices[i2]);
        changed = true;
      }
    }

    return changed;
  }

  private static PlayerProfessionsData CreateDefaultPlayerData(ulong platformId) {
    PlayerProfessionsData playerProfessionsData = new() {
      PlatformId = platformId,
      ExperienceLogEnabled = true,
      ExperienceSctEnabled = true,
      UpdatedAtUtc = DateTime.UtcNow
    };

    for (int i = 0; i < ProfessionOrder.Length; i++) {
      ProfessionType professionType = ProfessionOrder[i];
      playerProfessionsData.Professions[professionType.ToString()] = CreateDefaultProgressData();
    }

    return playerProfessionsData;
  }

  private static ProfessionProgressData CreateDefaultProgressData() {
    return new ProfessionProgressData {
      Level = 1,
      Experience = ConvertLevelToXp(1),
      PassiveChoices = new Dictionary<int, int>()
    };
  }

  private static ProfessionProgressData GetProfessionProgress(PlayerProfessionsData data, ProfessionType profession) {
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
    PlayerCache[data.PlatformId] = data;
    Plugin.Database.Set(BuildPlayerKey(data.PlatformId), data);
  }

  private static string BuildPlayerKey(ulong platformId) {
    return $"professions/players/{platformId}";
  }

}

