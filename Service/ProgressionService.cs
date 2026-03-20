using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BepInEx;
using CelemProfessions.Models;

namespace CelemProfessions.Service;

public static class ProgressionService {
  private const int StartingLevel = 1;
  private const int MaxLevel = 100;
  private const string FileName = "required_experience.json";

  private static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = true
  };

  private static readonly Dictionary<int, double> RequiredExperienceByLevel = [];
  private static double[] _levelStartExperience = Array.Empty<double>();
  private static bool _initialized;

  private static string ConfigDirectory => Path.Combine(Paths.ConfigPath, "CelemProfessions");
  private static string FilePath => Path.Combine(ConfigDirectory, FileName);

  public static void Initialize() {
    if (_initialized) {
      return;
    }

    Directory.CreateDirectory(ConfigDirectory);
    _initialized = true;

    try {
      EnsureConfigFile();
      LoadProgressionData();
    } catch {
      Shutdown();
      throw;
    }
  }

  public static void Shutdown() {
    RequiredExperienceByLevel.Clear();
    _levelStartExperience = Array.Empty<double>();
    _initialized = false;
  }

  public static int GetStartingLevel() {
    return StartingLevel;
  }

  public static int GetMaxLevel() {
    return MaxLevel;
  }

  public static double GetRequiredExperienceForLevel(int level) {
    EnsureInitialized();
    int clampedLevel = Math.Clamp(level, StartingLevel, MaxLevel);
    return RequiredExperienceByLevel.TryGetValue(clampedLevel, out double requiredExperience)
      ? requiredExperience
      : 1d;
  }

  public static double GetLevelStartExperience(int level) {
    EnsureInitialized();
    int clampedLevel = Math.Clamp(level, StartingLevel, MaxLevel);
    return _levelStartExperience[clampedLevel];
  }

  public static int GetLevelFromExperience(double totalExperience) {
    EnsureInitialized();
    double clampedExperience = Math.Max(0d, totalExperience);
    if (clampedExperience <= _levelStartExperience[StartingLevel]) {
      return StartingLevel;
    }

    int low = StartingLevel;
    int high = MaxLevel;
    while (low <= high) {
      int middle = (low + high) / 2;
      if (_levelStartExperience[middle] <= clampedExperience) {
        low = middle + 1;
      } else {
        high = middle - 1;
      }
    }

    return Math.Clamp(high, StartingLevel, MaxLevel);
  }

  public static double GetLevelPercent(double totalExperience, int level) {
    EnsureInitialized();
    int clampedLevel = Math.Clamp(level, StartingLevel, MaxLevel);
    if (clampedLevel >= MaxLevel) {
      return 0d;
    }

    double levelStart = GetLevelStartExperience(clampedLevel);
    double requiredExperience = Math.Max(1d, GetRequiredExperienceForLevel(clampedLevel));
    double currentLevelExperience = Math.Clamp(totalExperience - levelStart, 0d, requiredExperience);
    return Math.Round(Math.Min(99.999d, currentLevelExperience / requiredExperience * 100d), 3);
  }

  public static double GetLegacyLevelStartExperience(int level) {
    return Math.Pow(Math.Clamp(level, StartingLevel, MaxLevel) / 0.1d, 2d);
  }

  public static double GetLegacyRequiredExperienceForLevel(int level) {
    int clampedLevel = Math.Clamp(level, StartingLevel, MaxLevel);
    double current = GetLegacyLevelStartExperience(clampedLevel);
    double next = Math.Pow((clampedLevel + 1) / 0.1d, 2d);
    return Math.Max(1d, next - current);
  }

  private static void EnsureInitialized() {
    if (!_initialized) {
      Initialize();
    }
  }

  private static void EnsureConfigFile() {
    if (File.Exists(FilePath)) {
      return;
    }

    string json = JsonSerializer.Serialize(BuildDefaultProgressionData(), JsonOptions);
    File.WriteAllText(FilePath, json);
    Plugin.LogInstance?.LogInfo($"[ProfessionsProgression] Arquivo criado em '{FilePath}'.");
  }

  private static void LoadProgressionData() {
    LevelProgressionData progressionData = ReadProgressionData();
    RequiredExperienceByLevel.Clear();
    for (int level = StartingLevel; level <= MaxLevel; level++) {
      double requiredExperience = progressionData.RequiredExperienceByLevel.TryGetValue(level, out double configuredExperience) && configuredExperience > 0d
        ? Math.Floor(configuredExperience)
        : BuildDefaultRequiredExperienceForLevel(level);
      RequiredExperienceByLevel[level] = Math.Max(1d, requiredExperience);
    }

    RebuildCache();
  }

  private static LevelProgressionData ReadProgressionData() {
    try {
      string json = File.ReadAllText(FilePath);
      LevelProgressionData progressionData = JsonSerializer.Deserialize<LevelProgressionData>(json);
      if (progressionData?.RequiredExperienceByLevel == null || progressionData.RequiredExperienceByLevel.Count == 0) {
        throw new InvalidOperationException("JSON vazio ou sem niveis.");
      }

      return progressionData;
    } catch (Exception ex) {
      Plugin.LogInstance?.LogWarning($"[ProfessionsProgression] Falha ao ler '{Path.GetFileName(FilePath)}': {ex.Message}. Valores padrao em memoria serao usados.");
      return BuildDefaultProgressionData();
    }
  }

  private static void RebuildCache() {
    _levelStartExperience = new double[MaxLevel + 2];
    _levelStartExperience[StartingLevel] = 0d;
    for (int level = StartingLevel; level < MaxLevel; level++) {
      double requiredExperience = RequiredExperienceByLevel.TryGetValue(level, out double configuredExperience)
        ? configuredExperience
        : 1d;
      _levelStartExperience[level + 1] = _levelStartExperience[level] + Math.Max(1d, requiredExperience);
    }
  }

  private static LevelProgressionData BuildDefaultProgressionData() {
    LevelProgressionData progressionData = new();
    for (int level = StartingLevel; level <= MaxLevel; level++) {
      progressionData.RequiredExperienceByLevel[level] = BuildDefaultRequiredExperienceForLevel(level);
    }

    return progressionData;
  }

  private static double BuildDefaultRequiredExperienceForLevel(int level) {
    return Math.Floor(GetLegacyRequiredExperienceForLevel(level));
  }
}
