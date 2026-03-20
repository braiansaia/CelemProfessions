using System;
using System.Collections.Generic;
using CelemProfessions.Models;
using ProjectM;
using ScarletCore.Services;
using Stunlock.Core;

namespace CelemProfessions.Service;

public static class PassivesService {
  public static List<ProfessionService.ProfessionPassiveView> BuildViews(ProfessionsTypes profession, int level, Dictionary<int, int> passiveChoices) {
    List<ProfessionService.ProfessionPassiveView> views = new();
    IReadOnlyList<ProfessionPassiveMilestoneDefinition> milestones = ProfessionCatalogService.GetMilestones(profession);
    for (int i = 0; i < milestones.Count; i++) {
      ProfessionPassiveMilestoneDefinition milestone = milestones[i];
      passiveChoices.TryGetValue((int)milestone.Milestone, out int selectedOption);
      views.Add(new ProfessionService.ProfessionPassiveView {
        Milestone = milestone.Milestone,
        Option1 = milestone.Option1,
        Option2 = milestone.Option2,
        SelectedOption = selectedOption,
        Unlocked = level >= (int)milestone.Milestone
      });
    }

    return views;
  }

  public static bool NormalizeChoices(ProfessionsTypes profession, Dictionary<int, int> passiveChoices) {
    if (passiveChoices == null || passiveChoices.Count == 0) {
      return false;
    }

    bool changed = false;
    IReadOnlyList<ProfessionPassiveMilestoneDefinition> milestones = ProfessionCatalogService.GetMilestones(profession);
    List<int> invalidChoices = new();
    foreach (KeyValuePair<int, int> passiveChoice in passiveChoices) {
      bool validMilestone = false;
      for (int i = 0; i < milestones.Count; i++) {
        if (milestones[i].Milestone == (ProfessionMilestone)passiveChoice.Key) {
          validMilestone = true;
          break;
        }
      }

      if (!validMilestone || (passiveChoice.Value != 1 && passiveChoice.Value != 2)) {
        invalidChoices.Add(passiveChoice.Key);
      }
    }

    for (int i = 0; i < invalidChoices.Count; i++) {
      passiveChoices.Remove(invalidChoices[i]);
      changed = true;
    }

    return changed;
  }

  public static bool HasMilestone(ProfessionsTypes profession, ProfessionMilestone milestone) {
    IReadOnlyList<ProfessionPassiveMilestoneDefinition> milestones = ProfessionCatalogService.GetMilestones(profession);
    for (int i = 0; i < milestones.Count; i++) {
      if (milestones[i].Milestone == milestone) {
        return true;
      }
    }

    return false;
  }

  public static bool TryChoosePassive(ulong platformId, ProfessionsTypes profession, ProfessionMilestone milestone, int option, out string error) {
    error = string.Empty;
    if (option != 1 && option != 2) {
      error = "A opcao de passiva deve ser 1 ou 2.";
      return false;
    }

    PlayerProfessionsData data = ProfessionService.EnsurePlayerDataInternal(platformId);
    ProfessionProgressData progress = ProfessionService.GetProfessionProgressInternal(data, profession);
    if (progress.Level < (int)milestone) {
      error = $"Nivel insuficiente. Alcance nivel {milestone} em {ProfessionCatalogService.GetDisplayName(profession)}.";
      return false;
    }

    if (!HasMilestone(profession, milestone)) {
      error = "Marco de passiva invalido para esta profissao.";
      return false;
    }

    if (progress.PassiveChoices.TryGetValue((int)milestone, out int selectedOption)) {
      error = selectedOption == option
        ? "Essa passiva ja esta escolhida."
        : "Esse marco ja possui passiva escolhida. Use o reset da profissao para trocar.";
      return false;
    }

    progress.PassiveChoices[(int)milestone] = option;
    ProfessionService.SavePlayerDataInternal(data);
    return true;
  }

  public static ProfessionService.ResetPassivesStatus ResetProfessionPassives(ulong platformId, ProfessionsTypes profession, out int removedCount) {
    removedCount = 0;
    PlayerProfessionsData data = ProfessionService.EnsurePlayerDataInternal(platformId);
    ProfessionProgressData progress = ProfessionService.GetProfessionProgressInternal(data, profession);
    if (progress.PassiveChoices.Count == 0) {
      return ProfessionService.ResetPassivesStatus.NoPassivesChosen;
    }

    if (!ProfessionService.TryResolveOnlinePlayerInternal(platformId, out PlayerData player)) {
      return ProfessionService.ResetPassivesStatus.PlayerUnavailable;
    }

    PrefabGUID resetPassiveCostItem = ProfessionSettingsService.ResetPassiveCostItem;
    int resetPassiveCostAmount = ProfessionSettingsService.ResetPassiveCostAmount;
    if (!InventoryService.HasAmount(player.CharacterEntity, resetPassiveCostItem, resetPassiveCostAmount)) {
      return ProfessionService.ResetPassivesStatus.MissingCostItem;
    }

    if (!InventoryService.RemoveItem(player.CharacterEntity, resetPassiveCostItem, resetPassiveCostAmount)) {
      return ProfessionService.ResetPassivesStatus.ConsumeFailed;
    }

    removedCount = progress.PassiveChoices.Count;
    progress.PassiveChoices.Clear();
    ProfessionService.SavePlayerDataInternal(data);
    return ProfessionService.ResetPassivesStatus.Reset;
  }
}
