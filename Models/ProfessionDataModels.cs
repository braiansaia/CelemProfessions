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





