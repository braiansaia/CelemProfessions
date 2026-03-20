using System.Collections.Generic;

namespace CelemProfessions.Models;

public sealed class PassiveOptionEffectEntry {
  public int Milestone { get; set; }
  public bool Enabled { get; set; } = true;
  public double ChancePercent { get; set; }
  public double BonusPercent { get; set; }
  public int RewardPrefabGUID { get; set; }
  public string RewardName { get; set; } = string.Empty;
  public int Amount { get; set; } = 1;
  public List<string> Regions { get; set; } = [];
}

public sealed class PassiveProfessionConfig {
  public List<PassiveOptionEffectEntry> Option1 { get; set; } = [];
  public List<PassiveOptionEffectEntry> Option2 { get; set; } = [];
}

public sealed class PassiveConfigFile {
  public PassiveProfessionConfig Minerador { get; set; } = new();
  public PassiveProfessionConfig Lenhador { get; set; } = new();
  public PassiveProfessionConfig Herbalista { get; set; } = new();
  public PassiveProfessionConfig Joalheiro { get; set; } = new();
  public PassiveProfessionConfig Alfaiate { get; set; } = new();
  public PassiveProfessionConfig Ferreiro { get; set; } = new();
  public PassiveProfessionConfig Alquimista { get; set; } = new();
  public PassiveProfessionConfig Cacador { get; set; } = new();
  public PassiveProfessionConfig Pescador { get; set; } = new();
}
