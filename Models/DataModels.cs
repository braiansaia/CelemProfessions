using System;
using System.Collections.Generic;

namespace CelemProfessions.Models;

public sealed class ProfessionProgressData {
  public int Level { get; set; } = 1;
  public double Experience { get; set; }
  public Dictionary<int, int> PassiveChoices { get; set; } = [];
}

public sealed class PlayerProfessionsData {
  public int DataVersion { get; set; } = 2;
  public ulong PlatformId { get; set; }
  public Dictionary<string, ProfessionProgressData> Professions { get; set; } = [];
  public bool ExperienceLogEnabled { get; set; } = true;
  public bool ExperienceSctEnabled { get; set; } = true;
  public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
