using System;
using System.Collections.Generic;
using System.Globalization;
using CelemProfessions.Events;
using CelemProfessions.Models;
using ProjectM;
using ProjectM.Shared;
using ScarletCore;
using ScarletCore.Resources;
using ScarletCore.Services;
using ScarletCore.Systems;
using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace CelemProfessions.Service;

public static partial class ProfessionService {
  public enum ResetPassivesStatus {
    Reset,
    NoPassivesChosen,
    MissingCostItem,
    ConsumeFailed,
    PlayerUnavailable
  }

  public sealed class ProfessionSummaryView {
    public ProfessionType Profession { get; set; }
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
    public ProfessionType Profession { get; set; }
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

  private const double DurabilityCraftExperienceFactor = 0.0725;

  private static readonly CultureInfo PercentCulture = CultureInfo.GetCultureInfo("pt-BR");
  private static readonly PrefabGUID XpSctPrefab = new(-1687715009);
  private static readonly float3 FallbackSctColor = new(1f, 0.78f, 0.15f);
  private static readonly ProfessionType[] ProfessionOrder = {
    ProfessionType.Minerador,
    ProfessionType.Lenhador,
    ProfessionType.Herbalista,
    ProfessionType.Joalheiro,
    ProfessionType.Alfaiate,
    ProfessionType.Ferreiro,
    ProfessionType.Alquimista,
    ProfessionType.Cacador,
    ProfessionType.Pescador
  };

  private static readonly System.Random Random = new();
  private static readonly Dictionary<ulong, PlayerProfessionsData> PlayerCache = new();

  private static bool _initialized;
}

