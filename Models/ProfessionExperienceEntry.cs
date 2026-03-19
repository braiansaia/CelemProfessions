namespace CelemProfessions.Models;

public sealed class ProfessionExperienceEntry {
  public string Description { get; set; } = string.Empty;
  public int PrefabGUID { get; set; }
  public string Name { get; set; } = string.Empty;
  public double EXP { get; set; }
  public bool Enabled { get; set; } = true;
}
