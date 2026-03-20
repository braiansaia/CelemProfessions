using System;
using System.Collections.Generic;
using CelemProfessions.Models;
using CelemProfessions.Service;
using ScarletCore.Commanding;
using ScarletCore.Localization;
using ScarletCore.Services;

namespace CelemProfessions.Commands;

[CommandGroup("profissao", Language.English, aliases: ["prof"])]
public static class Commands {
  private const int PageSize = 5;

  [Command("detalhes", Language.English, aliases: ["d"], description: "Mostra os detalhes da profissao.", usage: ".prof d [profissao]")]
  public static void Details(CommandContext context, string professionInput) {
    if (context.Sender == null) {
      context.ReplyError("Dados do jogador nao foram encontrados.");
      return;
    }

    if (!ProfessionCatalogService.TryResolveProfession(professionInput, out ProfessionsTypes profession, out string error)) {
      context.ReplyError(error);
      return;
    }

    ProfessionService.ProfessionDetailsView details = ProfessionService.GetProfessionDetails(context.Sender.PlatformId, profession);
    ReplyProfessionDetails(context, details, context.Sender.Name);
  }

  [Command("reset", Language.English, aliases: ["r"], description: "Reseta as passivas escolhidas de uma profissao.", usage: ".prof r [profissao]")]
  public static void Reset(CommandContext context, string professionInput) {
    if (context.Sender == null) {
      context.ReplyError("Dados do jogador nao foram encontrados.");
      return;
    }

    if (!ProfessionCatalogService.TryResolveProfession(professionInput, out ProfessionsTypes profession, out string error)) {
      context.ReplyError(error);
      return;
    }

    ProfessionService.ResetPassivesStatus status = ProfessionService.ResetProfessionPassives(context.Sender.PlatformId, profession, out int removedCount);
    switch (status) {
      case ProfessionService.ResetPassivesStatus.Reset:
        context.ReplySuccess($"{removedCount} passiva(s) de {ProfessionCatalogService.GetDisplayName(profession)} foram resetadas.");
        return;
      case ProfessionService.ResetPassivesStatus.NoPassivesChosen:
        context.ReplyWarning("Nao existe passiva escolhida nessa profissao.");
        return;
      case ProfessionService.ResetPassivesStatus.MissingCostItem:
        context.ReplyError($"Voce precisa de {ProfessionSettingsService.ResetPassiveCostAmount}x {ProfessionSettingsService.ResetPassiveCostItem.LocalizedName(context.Sender.Language)} para resetar.");
        return;
      case ProfessionService.ResetPassivesStatus.ConsumeFailed:
        context.ReplyError("Nao foi possivel consumir o item de reset.");
        return;
      case ProfessionService.ResetPassivesStatus.PlayerUnavailable:
        context.ReplyError("Nao foi possivel validar o jogador para resetar.");
        return;
      default:
        context.ReplyError("Falha ao resetar passivas.");
        return;
    }
  }

  [Command("listar", Language.English, aliases: ["l"], description: "Lista as profissoes disponiveis.", usage: ".prof l [pagina]")]
  public static void List(CommandContext context, int page = 1) {
    if (context.Sender == null) {
      context.ReplyError("Dados do jogador nao foram encontrados.");
      return;
    }

    IReadOnlyList<ProfessionService.ProfessionSummaryView> summaries = ProfessionService.GetProfessionSummaries(context.Sender.PlatformId);
    int total = summaries.Count;
    int totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
    int currentPage = Math.Clamp(page, 1, totalPages);
    int start = (currentPage - 1) * PageSize;
    int end = Math.Min(start + PageSize, total);

    context.ReplyRaw($"[ PROFISSOES ] Pagina {currentPage}/{totalPages}");
    for (int i = start; i < end; i++) {
      ProfessionService.ProfessionSummaryView summary = summaries[i];
      context.ReplyRaw($"[{i + 1}] <color={ProfessionCatalogService.GetColorHex(summary.Profession)}>{summary.DisplayName}</color> | N: <color=#8FFD50>{summary.Level}</color>");
    }
  }

  [Command("passiva", Language.English, aliases: ["p"], description: "Escolhe uma passiva para o marco da profissao.", usage: ".prof p [profissao] [25|50|75|100] [1|2]")]
  public static void Passive(CommandContext context, string professionInput, int milestoneLevel, int option) {
    if (context.Sender == null) {
      context.ReplyError("Dados do jogador nao foram encontrados.");
      return;
    }

    if (!ProfessionCatalogService.TryResolveProfession(professionInput, out ProfessionsTypes profession, out string professionError)) {
      context.ReplyError(professionError);
      return;
    }

    if (!TryParseMilestone(milestoneLevel, out ProfessionMilestone milestone, out string milestoneError)) {
      context.ReplyError(milestoneError);
      return;
    }

    if (!ProfessionService.TryChoosePassive(context.Sender.PlatformId, profession, milestone, option, out string error)) {
      context.ReplyError(error);
      return;
    }

    context.ReplySuccess($"Passiva {option} escolhida para {ProfessionCatalogService.GetDisplayName(profession)} no nivel {milestoneLevel}.");
  }

  [Command("escolhidas", Language.English, aliases: ["es", "sel"], description: "Mostra apenas as passivas escolhidas da profissao.", usage: ".prof es [profissao]")]
  public static void SelectedPassives(CommandContext context, string professionInput) {
    if (context.Sender == null) {
      context.ReplyError("Dados do jogador nao foram encontrados.");
      return;
    }

    if (!ProfessionCatalogService.TryResolveProfession(professionInput, out ProfessionsTypes profession, out string error)) {
      context.ReplyError(error);
      return;
    }

    ProfessionService.ProfessionDetailsView details = ProfessionService.GetProfessionDetails(context.Sender.PlatformId, profession);
    ReplySelectedPassives(context, details);
  }

  [Command("passivas", Language.English, aliases: ["pa", "all"], description: "Mostra todas as passivas da profissao, incluindo bloqueadas e escolhidas.", usage: ".prof passivas [profissao]")]
  public static void AllPassives(CommandContext context, string professionInput) {
    if (context.Sender == null) {
      context.ReplyError("Dados do jogador nao foram encontrados.");
      return;
    }

    if (!ProfessionCatalogService.TryResolveProfession(professionInput, out ProfessionsTypes profession, out string error)) {
      context.ReplyError(error);
      return;
    }

    ProfessionService.ProfessionDetailsView details = ProfessionService.GetProfessionDetails(context.Sender.PlatformId, profession);
    ReplyAllPassives(context, details);
  }

  [Command("setnivel", Language.English, aliases: ["sn"], adminOnly: true, description: "Define o nivel de uma profissao do jogador.", usage: ".prof sn [jogador] [profissao] [1-100]")]
  public static void SetLevel(CommandContext context, string playerNameOrId, string professionInput, int level) {
    if (!TryResolvePlayer(playerNameOrId, out ulong platformId, out string resolvedName)) {
      context.ReplyError("Nao foi possivel encontrar o jogador.");
      return;
    }

    if (!ProfessionCatalogService.TryResolveProfession(professionInput, out ProfessionsTypes profession, out string professionError)) {
      context.ReplyError(professionError);
      return;
    }

    if (!ProfessionService.TrySetLevel(platformId, profession, level, out string error)) {
      context.ReplyError(error);
      return;
    }

    context.ReplySuccess($"{ProfessionCatalogService.GetDisplayName(profession)} de {resolvedName} ajustada para nivel {level}.");
  }

  [Command("setporcento", Language.English, aliases: ["sp"], adminOnly: true, description: "Define o percentual do nivel atual de uma profissao.", usage: ".prof sp [jogador] [profissao] [0-99.999]")]
  public static void SetPercent(CommandContext context, string playerNameOrId, string professionInput, double percent) {
    if (!TryResolvePlayer(playerNameOrId, out ulong platformId, out string resolvedName)) {
      context.ReplyError("Nao foi possivel encontrar o jogador.");
      return;
    }

    if (!ProfessionCatalogService.TryResolveProfession(professionInput, out ProfessionsTypes profession, out string professionError)) {
      context.ReplyError(professionError);
      return;
    }

    if (!ProfessionService.TrySetLevelPercent(platformId, profession, percent, out string error)) {
      context.ReplyError(error);
      return;
    }

    context.ReplySuccess($"Percentual de {ProfessionCatalogService.GetDisplayName(profession)} de {resolvedName} ajustado para {ProfessionService.FormatPercent(percent)}%.");
  }

  [Command("info", Language.English, aliases: ["i"], adminOnly: true, description: "Mostra detalhes de uma profissao de um jogador.", usage: ".prof i [jogador] [profissao]")]
  public static void Info(CommandContext context, string playerNameOrId, string professionInput) {
    if (!TryResolvePlayer(playerNameOrId, out ulong platformId, out string resolvedName)) {
      context.ReplyError("Nao foi possivel encontrar o jogador.");
      return;
    }

    if (!ProfessionCatalogService.TryResolveProfession(professionInput, out ProfessionsTypes profession, out string professionError)) {
      context.ReplyError(professionError);
      return;
    }

    ProfessionService.ProfessionDetailsView details = ProfessionService.GetProfessionDetails(platformId, profession);
    ReplyProfessionDetails(context, details, resolvedName);
  }

  [Command("log", Language.English, description: "Ativa ou desativa o log de XP de profissao no chat.", usage: ".prof log")]
  public static void ToggleLog(CommandContext context) {
    if (context.Sender == null) {
      context.ReplyError("Dados do jogador nao foram encontrados.");
      return;
    }

    ProfessionService.ToggleExperienceLog(context.Sender.PlatformId, out bool enabled);
    context.ReplySuccess($"Log de XP de profissao {(enabled ? "ativado" : "desativado")}.");
  }

  [Command("sct", Language.English, description: "Ativa ou desativa o popup de XP de profissao na tela.", usage: ".prof sct")]
  public static void ToggleSct(CommandContext context) {
    if (context.Sender == null) {
      context.ReplyError("Dados do jogador nao foram encontrados.");
      return;
    }

    ProfessionService.ToggleExperienceSct(context.Sender.PlatformId, out bool enabled);
    context.ReplySuccess($"Popup SCT de XP de profissao {(enabled ? "ativado" : "desativado")}.");
  }

  private static void ReplyProfessionDetails(CommandContext context, ProfessionService.ProfessionDetailsView details, string playerName) {
    context.ReplyRaw($"[ PROFISSAO ] Jogador: <color=#56B5E1>{playerName}</color>");
    context.ReplyRaw($"Nome: <color={details.ColorHex}>{details.DisplayName}</color> | Nivel: <color=#8FFD50>{details.Level}</color>");
    context.ReplyRaw($"XP Total: <color=#EEDE0E>{ProfessionService.FormatExperience(details.TotalExperience)}</color>");

    if (!details.IsMaxLevel) {
      context.ReplyRaw($"Progresso: <color=#FFFFFF>{ProfessionService.FormatExperience(details.CurrentLevelExperience)}/{ProfessionService.FormatExperience(details.RequiredExperience)}</color> ({ProfessionService.FormatPercent(details.Percent)}%)");
    } else {
      context.ReplyRaw("Progresso: <color=#8FFD50>NIVEL MAXIMO</color>");
    }

    int selectedCount = 0;
    for (int i = 0; i < details.Passives.Count; i++) {
      if (details.Passives[i].SelectedOption == 1 || details.Passives[i].SelectedOption == 2) {
        selectedCount++;
      }
    }

    context.ReplyRaw($"Passivas escolhidas: <color=#8FFD50>{selectedCount}</color>/{details.Passives.Count}. Use <color=#56B5E1>.prof passivas {details.DisplayName}</color> para ver todas e <color=#56B5E1>.prof es {details.DisplayName}</color> para ver apenas as escolhidas.");
  }

  private static void ReplySelectedPassives(CommandContext context, ProfessionService.ProfessionDetailsView details) {
    context.ReplyRaw($"[ PASSIVAS ] <color={details.ColorHex}>{details.DisplayName}</color>");

    bool hasSelection = false;
    for (int i = 0; i < details.Passives.Count; i++) {
      ProfessionService.ProfessionPassiveView passive = details.Passives[i];
      if (passive.SelectedOption != 1 && passive.SelectedOption != 2) {
        continue;
      }

      hasSelection = true;
      ProfessionPassiveOption option = passive.SelectedOption == 1 ? passive.Option1 : passive.Option2;
      context.ReplyRaw($"- N{(int)passive.Milestone}: [OPCAO {passive.SelectedOption}] {option.Name} - {option.Description}");
    }

    if (!hasSelection) {
      context.ReplyWarning("Nenhuma passiva foi escolhida nessa profissao ainda.");
    }
  }

  private static void ReplyAllPassives(CommandContext context, ProfessionService.ProfessionDetailsView details) {
    context.ReplyRaw($"[ PASSIVAS COMPLETAS ] <color={details.ColorHex}>{details.DisplayName}</color>");

    for (int i = 0; i < details.Passives.Count; i++) {
      ProfessionService.ProfessionPassiveView passive = details.Passives[i];
      int milestone = (int)passive.Milestone;
      string status = passive.Unlocked
        ? "<color=#8FFD50>DESBLOQUEADA</color>"
        : "<color=#FFB347>BLOQUEADA</color>";

      context.ReplyRaw($"N{milestone} | {status}");
      context.ReplyRaw($"  [1]{(passive.SelectedOption == 1 ? " <color=#8FFD50>[ESCOLHIDA]</color>" : string.Empty)} {passive.Option1.Name} - {passive.Option1.Description}");
      context.ReplyRaw($"  [2]{(passive.SelectedOption == 2 ? " <color=#8FFD50>[ESCOLHIDA]</color>" : string.Empty)} {passive.Option2.Name} - {passive.Option2.Description}");
    }
  }

  private static bool TryResolvePlayer(string playerNameOrId, out ulong platformId, out string resolvedName) {
    platformId = 0;
    resolvedName = playerNameOrId;

    if (ulong.TryParse(playerNameOrId, out ulong parsedId)) {
      platformId = parsedId;

      if (PlayerService.TryGetById(parsedId, out PlayerData byId)) {
        resolvedName = byId.Name;
      }

      return true;
    }

    if (!PlayerService.TryGetByName(playerNameOrId, out PlayerData byName)) {
      return false;
    }

    platformId = byName.PlatformId;
    resolvedName = byName.Name;
    return true;
  }

  private static bool TryParseMilestone(int level, out ProfessionMilestone milestone, out string error) {
    milestone = default;
    error = string.Empty;

    switch (level) {
      case 25:
        milestone = ProfessionMilestone.Level25;
        return true;
      case 50:
        milestone = ProfessionMilestone.Level50;
        return true;
      case 75:
        milestone = ProfessionMilestone.Level75;
        return true;
      case 100:
        milestone = ProfessionMilestone.Level100;
        return true;
      default:
        error = "Marco de passiva invalido. Use 25, 50, 75 ou 100.";
        return false;
    }
  }
}
