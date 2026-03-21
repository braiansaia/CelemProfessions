using System.Text.Json.Serialization;

namespace CelemProfessions.Models;

public sealed class ExperienceEntry {
  public string Description { get; set; } = string.Empty;
  public int PrefabGUID { get; set; }
  public string Name { get; set; } = string.Empty;
  public double EXP { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public int MaxResourceYield { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Yield { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Seed { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public bool? Gold { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public bool? Aggressive { get; set; }

  public bool Enabled { get; set; } = true;
}

public sealed class FishingRegionExperienceEntry {
  public string Region { get; set; } = string.Empty;
  public double EXP { get; set; }
  public bool Enabled { get; set; } = true;
}
