using System;
using System.Collections.Generic;
using Stunlock.Core;

namespace CelemProfessions.Models;

public sealed class ProfessionProgressData {
  public int Level { get; set; } = 1;
  public double Experience { get; set; } = 100d;
  public Dictionary<int, int> PassiveChoices { get; set; } = [];
}

public sealed class PlayerProfessionsData {
  public ulong PlatformId { get; set; }
  public Dictionary<string, ProfessionProgressData> Professions { get; set; } = [];
  public bool ExperienceLogEnabled { get; set; } = true;
  public bool ExperienceSctEnabled { get; set; } = true;
  public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AlchemyConsumableBonusData {
  public ulong CrafterPlatformId { get; set; }
  public double PowerMultiplier { get; set; } = 1d;
  public double DurationMultiplier { get; set; } = 1d;
  public int RemainingCharges { get; set; } = 1;
  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PendingConsumableBonusData {
  public PrefabGUID ConsumablePrefab { get; set; }
  public double PowerMultiplier { get; set; } = 1d;
  public double DurationMultiplier { get; set; } = 1d;
  public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow;
}

