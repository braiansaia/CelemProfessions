using System.Collections.Generic;

namespace CelemProfessions.Models;

public sealed class ProfessionPassiveOption {
  public int Option { get; set; }
  public string Name { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
}

public sealed class ProfessionPassiveMilestoneDefinition {
  public ProfessionMilestone Milestone { get; set; }
  public ProfessionPassiveOption Option1 { get; set; } = new();
  public ProfessionPassiveOption Option2 { get; set; } = new();
}

public sealed class Definition {
  public ProfessionsTypes Type { get; set; }
  public string DisplayName { get; set; } = string.Empty;
  public string ColorHex { get; set; } = "#FFFFFF";
  public List<string> Aliases { get; set; } = [];
  public List<ProfessionPassiveMilestoneDefinition> PassiveMilestones { get; set; } = [];
}
