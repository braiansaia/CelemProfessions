namespace CelemProfessions.Models;

public class RewardEntry {
  public int PrefabGUID { get; set; }
  public string Name { get; set; } = string.Empty;
  public int Level { get; set; } = 1;
  public bool Enabled { get; set; } = true;
}

public sealed class FishingRewardEntry : RewardEntry {
  public string Region { get; set; } = string.Empty;
}
