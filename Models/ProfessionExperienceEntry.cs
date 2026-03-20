using System.Text.Json.Serialization;

namespace CelemProfessions.Models;

public sealed class ProfessionExperienceEntry {
  public string Description { get; set; } = string.Empty;
  public int PrefabGUID { get; set; }
  public string Name { get; set; } = string.Empty;
  public double EXP { get; set; }
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public int MaxResourceYield { get; set; }

  public bool Enabled { get; set; } = true;
}

