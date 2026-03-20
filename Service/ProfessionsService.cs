using System;
using System.Collections.Generic;
using System.Globalization;
using CelemProfessions.Models;
using ProjectM;
using ScarletCore.Resources;
using ScarletCore.Services;
using Stunlock.Core;
using Unity.Mathematics;

namespace CelemProfessions.Service;

public static partial class ProfessionService {
  internal const int CurrentDataVersion = 2;

  public enum ResetPassivesStatus {
    Reset,
    NoPassivesChosen,
    MissingCostItem,
    ConsumeFailed,
    PlayerUnavailable
  }

  public sealed class ProfessionSummaryView {
    public ProfessionsTypes Profession { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int Level { get; set; }
  }

  public sealed class ProfessionPassiveView {
    public ProfessionMilestone Milestone { get; set; }
    public ProfessionPassiveOption Option1 { get; set; } = new();
    public ProfessionPassiveOption Option2 { get; set; } = new();
    public int SelectedOption { get; set; }
    public bool Unlocked { get; set; }
  }

  public sealed class ProfessionDetailsView {
    public ProfessionsTypes Profession { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#FFFFFF";
    public int Level { get; set; }
    public double TotalExperience { get; set; }
    public double CurrentLevelExperience { get; set; }
    public double RequiredExperience { get; set; }
    public double Percent { get; set; }
    public bool IsMaxLevel { get; set; }
    public List<ProfessionPassiveView> Passives { get; set; } = new();
  }

  private static readonly CultureInfo PercentCulture = CultureInfo.GetCultureInfo("pt-BR");
  private static readonly PrefabGUID XpSctPrefab = new(-1687715009);
  private static readonly float3 FallbackSctColor = new(1f, 0.78f, 0.15f);
  private static readonly ProfessionsTypes[] ProfessionOrder = {
    ProfessionsTypes.Minerador,
    ProfessionsTypes.Lenhador,
    ProfessionsTypes.Herbalista,
    ProfessionsTypes.Joalheiro,
    ProfessionsTypes.Alfaiate,
    ProfessionsTypes.Ferreiro,
    ProfessionsTypes.Alquimista,
    ProfessionsTypes.Cacador,
    ProfessionsTypes.Pescador
  };

  private static readonly System.Random Random = new();
  private static readonly Dictionary<ulong, PlayerProfessionsData> PlayerCache = new();

  private static bool _initialized;

  internal static PlayerProfessionsData EnsurePlayerDataInternal(ulong platformId) {
    return EnsurePlayerData(platformId);
  }

  internal static ProfessionProgressData GetProfessionProgressInternal(PlayerProfessionsData data, ProfessionsTypes profession) {
    return GetProfessionProgress(data, profession);
  }

  internal static void SavePlayerDataInternal(PlayerProfessionsData data) {
    SavePlayerData(data);
  }

  internal static bool TryResolveOnlinePlayerInternal(ulong platformId, out PlayerData player) {
    return TryResolveOnlinePlayer(platformId, out player);
  }
}
