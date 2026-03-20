using System.Collections.Generic;

namespace CelemProfessions.Models;

public sealed class LevelProgressionData {
  public Dictionary<int, double> RequiredExperienceByLevel { get; set; } = [];
}
