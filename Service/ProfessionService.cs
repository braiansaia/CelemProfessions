using System;
using System.Collections.Generic;
using System.Globalization;
using CelemProfessions.Events;
using CelemProfessions.Models;
using ProjectM;
using ProjectM.Shared;
using ScarletCore;
using ScarletCore.Resources;
using ScarletCore.Services;
using ScarletCore.Systems;
using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace CelemProfessions.Service;

public static class ProfessionService
{
	public enum ResetPassivesStatus
	{
		Reset,
		NoPassivesChosen,
		MissingCostItem,
		ConsumeFailed,
		PlayerUnavailable
	}

	public sealed class ProfessionSummaryView
	{
		public ProfessionType Profession { get; set; }

		public string DisplayName { get; set; } = string.Empty;

		public int Level { get; set; }
	}

	public sealed class ProfessionPassiveView
	{
		public ProfessionMilestone Milestone { get; set; }

		public ProfessionPassiveOption Option1 { get; set; } = new ProfessionPassiveOption();

		public ProfessionPassiveOption Option2 { get; set; } = new ProfessionPassiveOption();

		public int SelectedOption { get; set; }

		public bool Unlocked { get; set; }
	}

	public sealed class ProfessionDetailsView
	{
		public ProfessionType Profession { get; set; }

		public string DisplayName { get; set; } = string.Empty;

		public string ColorHex { get; set; } = "#FFFFFF";

		public int Level { get; set; }

		public double TotalExperience { get; set; }

		public double CurrentLevelExperience { get; set; }

		public double RequiredExperience { get; set; }

		public double Percent { get; set; }

		public bool IsMaxLevel { get; set; }

		public List<ProfessionPassiveView> Passives { get; set; } = new List<ProfessionPassiveView>();
	}

	private const string PlayerPrefix = "professions/players/";

	private const double XpConstant = 0.1;

	private const double XpPower = 2.0;

	private const double MaxDisplayPercent = 99.999;

	private const string XpSctAssetGuid = "4210316d-23d4-4274-96f5-d6f0944bd0bb";

	private const double DurabilityCraftExperienceFactor = 0.0725;


	private static readonly CultureInfo PercentCulture = CultureInfo.GetCultureInfo("pt-BR");

	private static readonly PrefabGUID XpSctPrefab = new PrefabGUID(-1687715009);

	private static readonly float3 FallbackSctColor = new float3(1f, 0.78f, 0.15f);

	private static readonly ProfessionType[] ProfessionOrder = new ProfessionType[9]
	{
		ProfessionType.Minerador,
		ProfessionType.Lenhador,
		ProfessionType.Herbalista,
		ProfessionType.Joalheiro,
		ProfessionType.Alfaiate,
		ProfessionType.Ferreiro,
		ProfessionType.Alquimista,
		ProfessionType.Cacador,
		ProfessionType.Pescador
	};

	private static readonly System.Random Random = new System.Random();

	private static readonly Dictionary<ulong, PlayerProfessionsData> PlayerCache = new Dictionary<ulong, PlayerProfessionsData>();





	private static bool _initialized;

	public static void Initialize()
	{
		if (_initialized)
		{
			return;
		}
		PlayerCache.Clear();
		foreach (KeyValuePair<string, PlayerProfessionsData> item in Plugin.Database.GetAllByPrefix<PlayerProfessionsData>("professions/players/"))
		{
			PlayerProfessionsData value = item.Value;
			if (value == null)
			{
				continue;
			}
			ulong platformId = value.PlatformId;
			if (platformId != 0L || TryParsePlatformFromKey(item.Key, out platformId))
			{
				bool num = NormalizePlayerData(value, platformId);
				PlayerCache[platformId] = value;
				if (num)
				{
					SavePlayerData(value);
				}
			}
		}
		_initialized = true;
	}

	public static void Shutdown()
	{
		PlayerCache.Clear();
		_initialized = false;
	}

	public static void HandlePlayerJoined(PlayerData player)
	{
		if (player != null)
		{
			EnsurePlayerData(player.PlatformId);
		}
	}

	public static bool ToggleExperienceLog(ulong platformId, out bool enabled)
	{
		PlayerProfessionsData playerProfessionsData = EnsurePlayerData(platformId);
		playerProfessionsData.ExperienceLogEnabled = !playerProfessionsData.ExperienceLogEnabled;
		enabled = playerProfessionsData.ExperienceLogEnabled;
		SavePlayerData(playerProfessionsData);
		return true;
	}

	public static bool ToggleExperienceSct(ulong platformId, out bool enabled)
	{
		PlayerProfessionsData playerProfessionsData = EnsurePlayerData(platformId);
		playerProfessionsData.ExperienceSctEnabled = !playerProfessionsData.ExperienceSctEnabled;
		enabled = playerProfessionsData.ExperienceSctEnabled;
		SavePlayerData(playerProfessionsData);
		return true;
	}

	public static void GetDisplaySettings(ulong platformId, out bool experienceLogEnabled, out bool experienceSctEnabled)
	{
		PlayerProfessionsData playerProfessionsData = EnsurePlayerData(platformId);
		experienceLogEnabled = playerProfessionsData.ExperienceLogEnabled;
		experienceSctEnabled = playerProfessionsData.ExperienceSctEnabled;
	}

	public static IReadOnlyList<ProfessionSummaryView> GetProfessionSummaries(ulong platformId)
	{
		PlayerProfessionsData data = EnsurePlayerData(platformId);
		List<ProfessionSummaryView> list = new List<ProfessionSummaryView>(ProfessionOrder.Length);
		for (int i = 0; i < ProfessionOrder.Length; i++)
		{
			ProfessionType profession = ProfessionOrder[i];
			ProfessionProgressData professionProgress = GetProfessionProgress(data, profession);
			list.Add(new ProfessionSummaryView
			{
				Profession = profession,
				DisplayName = ProfessionCatalogService.GetDisplayName(profession),
				Level = professionProgress.Level
			});
		}
		return list;
	}

	public static ProfessionDetailsView GetProfessionDetails(ulong platformId, ProfessionType profession)
	{
		ProfessionProgressData professionProgress = GetProfessionProgress(EnsurePlayerData(platformId), profession);
		int level = professionProgress.Level;
		int num = 100;
		double num2 = ConvertLevelToXp(level);
		double num3 = ((level >= num) ? num2 : ConvertLevelToXp(level + 1));
		double num4 = ((level >= num) ? 0.0 : Math.Max(1.0, num3 - num2));
		double currentLevelExperience = ((level >= num) ? 0.0 : Math.Clamp(professionProgress.Experience - num2, 0.0, num4));
		double percent = ((level >= num) ? 0.0 : GetLevelPercent(professionProgress.Experience, level));
		ProfessionDetailsView professionDetailsView = new ProfessionDetailsView
		{
			Profession = profession,
			DisplayName = ProfessionCatalogService.GetDisplayName(profession),
			ColorHex = ProfessionCatalogService.GetColorHex(profession),
			Level = level,
			TotalExperience = professionProgress.Experience,
			CurrentLevelExperience = currentLevelExperience,
			RequiredExperience = num4,
			Percent = percent,
			IsMaxLevel = (level >= num)
		};
		IReadOnlyList<ProfessionPassiveMilestoneDefinition> milestones = ProfessionCatalogService.GetMilestones(profession);
		for (int i = 0; i < milestones.Count; i++)
		{
			ProfessionPassiveMilestoneDefinition professionPassiveMilestoneDefinition = milestones[i];
			professionProgress.PassiveChoices.TryGetValue((int)professionPassiveMilestoneDefinition.Milestone, out var value);
			professionDetailsView.Passives.Add(new ProfessionPassiveView
			{
				Milestone = professionPassiveMilestoneDefinition.Milestone,
				Option1 = professionPassiveMilestoneDefinition.Option1,
				Option2 = professionPassiveMilestoneDefinition.Option2,
				SelectedOption = value,
				Unlocked = (level >= (int)professionPassiveMilestoneDefinition.Milestone)
			});
		}
		return professionDetailsView;
	}

	public static bool TrySetLevel(ulong platformId, ProfessionType profession, int level, out string error)
	{
		error = string.Empty;
		if (level < 1 || level > 100)
		{
			error = $"O nivel deve estar entre 1 e {100}.";
			return false;
		}
		PlayerProfessionsData data = EnsurePlayerData(platformId);
		ProfessionProgressData professionProgress = GetProfessionProgress(data, profession);
		professionProgress.Level = level;
		professionProgress.Experience = ConvertLevelToXp(level);
		SavePlayerData(data);
		return true;
	}

	public static bool TrySetLevelPercent(ulong platformId, ProfessionType profession, double percent, out string error)
	{
		error = string.Empty;
		if (percent < 0.0 || percent > 99.999)
		{
			error = "O percentual deve estar entre 0 e " + 99.999.ToString("0.000", PercentCulture) + ".";
			return false;
		}
		PlayerProfessionsData data = EnsurePlayerData(platformId);
		ProfessionProgressData professionProgress = GetProfessionProgress(data, profession);
		if (professionProgress.Level >= 100)
		{
			professionProgress.Experience = ConvertLevelToXp(100);
			SavePlayerData(data);
			return true;
		}
		double num = ConvertLevelToXp(professionProgress.Level);
		double num2 = ConvertLevelToXp(professionProgress.Level + 1);
		double num3 = Math.Max(1.0, num2 - num);
		professionProgress.Experience = num + num3 * percent / 100.0;
		professionProgress.Level = ConvertXpToLevel(professionProgress.Experience);
		SavePlayerData(data);
		return true;
	}

	public static bool TryChoosePassive(ulong platformId, ProfessionType profession, ProfessionMilestone milestone, int option, out string error)
	{
		error = string.Empty;
		if (option != 1 && option != 2)
		{
			error = "A opcao de passiva deve ser 1 ou 2.";
			return false;
		}
		PlayerProfessionsData data = EnsurePlayerData(platformId);
		ProfessionProgressData professionProgress = GetProfessionProgress(data, profession);
		if (professionProgress.Level < (int)milestone)
		{
			error = $"Nivel insuficiente. Alcance nivel {milestone} em {ProfessionCatalogService.GetDisplayName(profession)}.";
			return false;
		}
		IReadOnlyList<ProfessionPassiveMilestoneDefinition> milestones = ProfessionCatalogService.GetMilestones(profession);
		bool flag = false;
		for (int i = 0; i < milestones.Count; i++)
		{
			if (milestones[i].Milestone == milestone)
			{
				flag = true;
				break;
			}
		}
		if (!flag)
		{
			error = "Marco de passiva invalido para esta profissao.";
			return false;
		}
		if (professionProgress.PassiveChoices.TryGetValue((int)milestone, out var value))
		{
			if (value == option)
			{
				error = "Essa passiva ja esta escolhida.";
			}
			else
			{
				error = "Esse marco ja possui passiva escolhida. Use o reset da profissao para trocar.";
			}
			return false;
		}
		professionProgress.PassiveChoices[(int)milestone] = option;
		SavePlayerData(data);
		return true;
	}

	public static ResetPassivesStatus ResetProfessionPassives(ulong platformId, ProfessionType profession, out int removedCount)
	{
		removedCount = 0;
		PlayerProfessionsData data = EnsurePlayerData(platformId);
		ProfessionProgressData professionProgress = GetProfessionProgress(data, profession);
		if (professionProgress.PassiveChoices.Count == 0)
		{
			return ResetPassivesStatus.NoPassivesChosen;
		}
		if (!TryResolveOnlinePlayer(platformId, out var player))
		{
			return ResetPassivesStatus.PlayerUnavailable;
		}
		PrefabGUID resetPassiveCostItem = ProfessionSettingsService.ResetPassiveCostItem;
		int resetPassiveCostAmount = ProfessionSettingsService.ResetPassiveCostAmount;
		if (!InventoryService.HasAmount(player.CharacterEntity, resetPassiveCostItem, resetPassiveCostAmount))
		{
			return ResetPassivesStatus.MissingCostItem;
		}
		if (!InventoryService.RemoveItem(player.CharacterEntity, resetPassiveCostItem, resetPassiveCostAmount))
		{
			return ResetPassivesStatus.ConsumeFailed;
		}
		removedCount = professionProgress.PassiveChoices.Count;
		professionProgress.PassiveChoices.Clear();
		SavePlayerData(data);
		return ResetPassivesStatus.Reset;
	}

	public static void HandleGatherFromEntity(PlayerData player, Entity target, PrefabGUID targetPrefab)
	{
		if (player != null && target.Exists() && target.TryGetBuffer<YieldResourcesOnDamageTaken>(out DynamicBuffer<YieldResourcesOnDamageTaken> dynamicBuffer) && !dynamicBuffer.IsEmpty)
		{
			PrefabGUID itemType = dynamicBuffer[0].ItemType;
			if (TryResolveGatherProfession(itemType, out var profession))
			{
				HandleGatherEvent(new GatherEventData(player, target, targetPrefab, itemType, profession));
			}
		}
	}

	public static void HandleGatherEvent(in GatherEventData gatherEvent)
	{
		if (gatherEvent.Player != null && gatherEvent.Player.CharacterEntity.Exists())
		{
			double baseValue = ResolveGatherBaseExperience(gatherEvent);
			if (baseValue <= 0.0)
			{
				return;
			}
			double xpMultiplier = ProfessionSettingsService.GetXpMultiplier(gatherEvent.Profession);
			double calculatedXp = Math.Floor(Math.Max(0.0, baseValue) * xpMultiplier);
			AddExperience(gatherEvent.Player, gatherEvent.Profession, baseValue, out var progress, out var gainedExperience, out var _);
			LogExperienceGain(gatherEvent.Player, gatherEvent.Profession, "Gather", gatherEvent.TargetPrefab, gatherEvent.YieldPrefab, baseValue, xpMultiplier, calculatedXp, gainedExperience, progress.Level);
			switch (gatherEvent.Profession)
			{
			case ProfessionType.Minerador:
				HandleMinerRewards(gatherEvent.Player, gatherEvent.YieldPrefab, progress.Level);
				break;
			case ProfessionType.Lenhador:
				HandleWoodRewards(gatherEvent.Player, gatherEvent.YieldPrefab, progress.Level);
				break;
			case ProfessionType.Herbalista:
				HandleHerbalRewards(gatherEvent.Player, gatherEvent.YieldPrefab, progress.Level);
				break;
			case ProfessionType.Joalheiro:
				HandleJewelGatherRewards(gatherEvent.Player, gatherEvent.YieldPrefab, progress.Level);
				break;
			}
		}
	}

	public static void HandleHunterKillEvent(in HunterKillEventData hunterEvent)
	{
		if (hunterEvent.Player != null && hunterEvent.Target.Exists() && !hunterEvent.Target.IsPlayer() && ProfessionExperienceConfigService.TryGetHunterExperience(hunterEvent.TargetPrefab, out var baseValue, out var leatherPrefab))
		{
			double xpMultiplier = ProfessionSettingsService.GetXpMultiplier(ProfessionType.Cacador);
			double calculatedXp = Math.Floor(Math.Max(0.0, baseValue) * xpMultiplier);
			AddExperience(hunterEvent.Player, ProfessionType.Cacador, baseValue, out var progress, out var gainedExperience, out var _);
			LogExperienceGain(hunterEvent.Player, ProfessionType.Cacador, "Hunter", hunterEvent.TargetPrefab, leatherPrefab, baseValue, xpMultiplier, calculatedXp, gainedExperience, progress.Level);
			int num = CalculateYieldBonus(progress.Level, ProfessionSettingsService.CacadorLeatherYieldMultiplier);
			if (num > 0)
			{
				GiveReward(hunterEvent.Player, ProfessionType.Cacador, leatherPrefab, num);
			}
		}
	}

	public static void HandleFishingEvent(in FishingEventData fishingEvent)
	{
		if (fishingEvent.Player == null || !fishingEvent.Player.CharacterEntity.Exists())
		{
			return;
		}
		double baseValue = ProfessionSettingsService.FishingBaseXp;
		double xpMultiplier = ProfessionSettingsService.GetXpMultiplier(ProfessionType.Pescador);
		double calculatedXp = Math.Floor(Math.Max(0.0, baseValue) * xpMultiplier);
		AddExperience(fishingEvent.Player, ProfessionType.Pescador, baseValue, out var progress, out var gainedExperience, out var _);
		LogExperienceGain(fishingEvent.Player, ProfessionType.Pescador, "Fishing", fishingEvent.FishingAreaPrefab, PrefabGUID.Empty, baseValue, xpMultiplier, calculatedXp, gainedExperience, progress.Level);
		if (RollChance(ProfessionSettingsService.PescadorExtraFishChanceAtMax * (double)progress.Level / 100.0))
		{
			List<PrefabGUID> fishingAreaDrops = ProfessionCatalogService.GetFishingAreaDrops(fishingEvent.FishingAreaPrefab);
			if (fishingAreaDrops.Count != 0)
			{
				PrefabGUID itemPrefab = fishingAreaDrops[Random.Next(0, fishingAreaDrops.Count)];
				GiveReward(fishingEvent.Player, ProfessionType.Pescador, itemPrefab, ProfessionSettingsService.PescadorExtraFishAmount);
			}
		}
	}

	private static double ResolveGatherBaseExperience(in GatherEventData gatherEvent)
	{
		switch (gatherEvent.Profession)
		{
		case ProfessionType.Minerador:
		case ProfessionType.Lenhador:
		case ProfessionType.Herbalista:
			if (!ProfessionExperienceConfigService.TryGetGatherExperience(gatherEvent.Profession, gatherEvent.TargetPrefab, out var configuredExperience))
			{
				return 0.0;
			}
			return Math.Max(0.0, configuredExperience);
		default:
			return ProfessionSettingsService.GatherBaseXp * (double)ProfessionCatalogService.GetTierMultiplier(gatherEvent.YieldPrefab);
		}
	}

	private static bool TryResolveCraftBaseExperience(ProfessionType profession, PrefabGUID itemPrefab, out double baseValue)
	{
		baseValue = 0.0;
		switch (profession)
		{
		case ProfessionType.Joalheiro:
		case ProfessionType.Alfaiate:
		case ProfessionType.Ferreiro:
			return TryResolveDurabilityBasedCraftExperience(itemPrefab, out baseValue);
		case ProfessionType.Alquimista:
			return ProfessionExperienceConfigService.TryGetAlchemyCraftExperience(itemPrefab, out baseValue);
		default:
			baseValue = ProfessionSettingsService.CraftBaseXp * (double)ProfessionCatalogService.GetTierMultiplier(itemPrefab);
			return baseValue > 0.0;
		}
	}

	private static bool TryResolveDurabilityBasedCraftExperience(PrefabGUID itemPrefab, out double baseValue)
	{
		baseValue = 0.0;
		if (!GameSystems.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(itemPrefab, out Entity prefabEntity) || !prefabEntity.Exists() || !prefabEntity.TryGetComponent(out Durability durability) || durability.MaxDurability <= 0f)
		{
			return false;
		}
		baseValue = Math.Max(0.0, (double)durability.MaxDurability * DurabilityCraftExperienceFactor);
		return baseValue > 0.0;
	}
	public static void HandleCraftedItem(ulong platformId, PlayerData player, Entity workstation, Entity itemEntity, PrefabGUID itemPrefab, int amount)
	{
		if (itemPrefab.IsEmpty() || amount <= 0)
		{
			if (Plugin.LogInstance != null)
			{
				Plugin.LogInstance.LogInfo($"[ProfessionsXP][CraftSkip] platform={platformId} reason=invalid-item amount={amount} item={FormatPrefabForLog(itemPrefab)} station={workstation.Index}:{workstation.Version}");
			}
			return;
		}

		if (!TryResolveCraftProfession(itemPrefab, out var profession))
		{
			if (Plugin.LogInstance != null)
			{
				Plugin.LogInstance.LogInfo($"[ProfessionsXP][CraftSkip] platform={platformId} reason=no-profession item={FormatPrefabForLog(itemPrefab)} amount={amount} station={workstation.Index}:{workstation.Version}");
			}
			return;
		}

		if (player == null && !TryResolveOnlinePlayer(platformId, out player))
		{
			TryResolveCachedPlayer(platformId, out player);
		}

		if (player == null)
		{
			if (Plugin.LogInstance != null)
			{
				Plugin.LogInstance.LogInfo($"[ProfessionsXP][CraftSkip] platform={platformId} reason=player-not-found item={FormatPrefabForLog(itemPrefab)} amount={amount} station={workstation.Index}:{workstation.Version}");
			}
			return;
		}

		if (!TryResolveCraftBaseExperience(profession, itemPrefab, out double baseValue))
		{
			return;
		}

		double xpMultiplier = ProfessionSettingsService.GetXpMultiplier(profession);
		double calculatedXp = Math.Floor(Math.Max(0.0, baseValue) * xpMultiplier);
		AddExperience(player, profession, baseValue, out var progress, out var gainedExperience, out var _);
		PrefabGUID prefabGuid = workstation.Exists() ? workstation.GetPrefabGuid() : PrefabGUID.Empty;
		string sourceContext = (profession == ProfessionType.Alquimista) ? "CraftConsumable" : "Craft";
		LogExperienceGain(player, profession, sourceContext, prefabGuid, itemPrefab, baseValue, xpMultiplier, calculatedXp, gainedExperience, progress.Level);

		switch (profession)
		{
			case ProfessionType.Joalheiro:
				ApplyDurabilityBonus(itemEntity, itemPrefab, progress.Level, ProfessionSettingsService.JoalheiroDurabilityBonusAtMax);
				break;
			case ProfessionType.Alfaiate:
				ApplyDurabilityBonus(itemEntity, itemPrefab, progress.Level, ProfessionSettingsService.AlfaiateDurabilityBonusAtMax);
				break;
			case ProfessionType.Ferreiro:
				ApplyDurabilityBonus(itemEntity, itemPrefab, progress.Level, ProfessionSettingsService.FerreiroDurabilityBonusAtMax);
				break;
		}
	}

	public static void HandleBuffSpawn(Entity buffEntity)
	{
		if (!buffEntity.Exists() || !buffEntity.TryGetComponent<Buff>(out Buff componentData) || !componentData.Target.Exists() || !componentData.Target.IsPlayer() || !buffEntity.TryGetComponent<PrefabGUID>(out PrefabGUID componentData2) || !IsConsumableBuff(componentData2))
		{
			return;
		}
		PlayerData playerData = componentData.Target.GetPlayerData();
		if (playerData == null)
		{
			return;
		}
		ProfessionProgressData professionProgress = GetProfessionProgress(EnsurePlayerData(playerData.PlatformId), ProfessionType.Alquimista);
		double powerMultiplier = 1.0 + ProfessionSettingsService.AlquimistaPowerBonusAtMax * (double)professionProgress.Level / 100.0;
		double durationMultiplier = 1.0 + ProfessionSettingsService.AlquimistaDurationBonusAtMax * (double)professionProgress.Level / 100.0;
		if (powerMultiplier <= 1.0 && durationMultiplier <= 1.0)
		{
			return;
		}
		ApplyConsumableBuffBonus(buffEntity, powerMultiplier, durationMultiplier);
		LogAlchemyBuffApplied(playerData.PlatformId, componentData2, powerMultiplier, durationMultiplier, professionProgress.Level);
		MessageService.SendInfo(playerData, $"Consumivel aprimorado aplicado: poder x{powerMultiplier:0.###} | duracao x{durationMultiplier:0.###}.");
	}

	public static string FormatPercent(double percent)
	{
		return Math.Clamp(percent, 0.0, 99.999).ToString("0.000", PercentCulture);
	}

	public static string FormatExperience(double value)
	{
		return Math.Round(Math.Max(0.0, value), 0).ToString("N0", CultureInfo.InvariantCulture);
	}

	private static void AddExperience(PlayerData player, ProfessionType profession, double baseValue, out ProfessionProgressData progress, out double gainedExperience, out bool leveledUp)
	{
		gainedExperience = 0.0;
		leveledUp = false;
		if (player == null)
		{
			progress = new ProfessionProgressData();
			return;
		}
		PlayerProfessionsData playerProfessionsData = EnsurePlayerData(player.PlatformId);
		progress = GetProfessionProgress(playerProfessionsData, profession);
		int level = progress.Level;
		double experience = progress.Experience;
		if (progress.Level >= 100)
		{
			double num = ConvertLevelToXp(100);
			if (progress.Experience != num)
			{
				progress.Experience = num;
				SavePlayerData(playerProfessionsData);
			}
			return;
		}
		double xpMultiplier = ProfessionSettingsService.GetXpMultiplier(profession);
		double num2 = Math.Floor(Math.Max(0.0, baseValue) * xpMultiplier);
		if (!(num2 <= 0.0))
		{
			double num3 = ConvertLevelToXp(100);
			progress.Experience = Math.Min(num3, experience + num2);
			progress.Level = ConvertXpToLevel(progress.Experience);
			if (progress.Level >= 100)
			{
				progress.Level = 100;
				progress.Experience = num3;
			}
			gainedExperience = Math.Max(0.0, progress.Experience - experience);
			leveledUp = progress.Level > level;
			SavePlayerData(playerProfessionsData);
			NotifyExperienceGain(player, playerProfessionsData, profession, progress, gainedExperience, leveledUp);
		}
	}

	private static void NotifyExperienceGain(PlayerData player, PlayerProfessionsData playerData, ProfessionType profession, ProfessionProgressData progress, double gainedExperience, bool leveledUp)
	{
		if (player == null || gainedExperience <= 0.0)
		{
			return;
		}
		string displayName = ProfessionCatalogService.GetDisplayName(profession);
		if (leveledUp)
		{
			MessageService.SendSuccess(player, $"{displayName} subiu para o nivel {progress.Level}.");
		}
		if (playerData.ExperienceLogEnabled)
		{
			if (progress.Level >= 100)
			{
				MessageService.SendInfo(player, $"+{FormatExperience(gainedExperience)} XP em {displayName}.");
			}
			else
			{
				double levelPercent = GetLevelPercent(progress.Experience, progress.Level);
				MessageService.SendInfo(player, $"+{FormatExperience(gainedExperience)} XP em {displayName} ({FormatPercent(levelPercent)}%).");
			}
		}
		if (playerData.ExperienceSctEnabled)
		{
			float3 color = ParseHexColor(ProfessionCatalogService.GetColorHex(profession), FallbackSctColor);
			MessageService.SendSCT(player, XpSctPrefab, "4210316d-23d4-4274-96f5-d6f0944bd0bb", color, ToDisplayExperienceValue(gainedExperience));
		}
	}

	private static void HandleMinerRewards(PlayerData player, PrefabGUID yieldPrefab, int professionLevel)
	{
		int num = CalculateYieldBonus(professionLevel, ProfessionSettingsService.MineradorYieldMultiplier);
		if (num > 0)
		{
			GiveReward(player, ProfessionType.Minerador, yieldPrefab, num);
		}
		if (RollChance(ProfessionSettingsService.MineradorGoldChanceAtMax * (double)professionLevel / 100.0))
		{
			GiveReward(player, ProfessionType.Minerador, PrefabGUIDs.Item_Ingredient_Mineral_GoldOre, ProfessionSettingsService.MineradorGoldAmount);
		}
	}

	private static void HandleWoodRewards(PlayerData player, PrefabGUID yieldPrefab, int professionLevel)
	{
		int num = CalculateYieldBonus(professionLevel, ProfessionSettingsService.LenhadorYieldMultiplier);
		if (num > 0)
		{
			GiveReward(player, ProfessionType.Lenhador, yieldPrefab, num);
		}
		if (RollChance(ProfessionSettingsService.LenhadorSaplingChanceAtMax * (double)professionLevel / 100.0))
		{
			IReadOnlyList<PrefabGUID> treeSaplingRewards = ProfessionCatalogService.TreeSaplingRewards;
			if (treeSaplingRewards.Count != 0)
			{
				PrefabGUID itemPrefab = treeSaplingRewards[Random.Next(0, treeSaplingRewards.Count)];
				GiveReward(player, ProfessionType.Lenhador, itemPrefab, ProfessionSettingsService.LenhadorSaplingAmount);
			}
		}
	}

	private static void HandleHerbalRewards(PlayerData player, PrefabGUID yieldPrefab, int professionLevel)
	{
		int num = CalculateYieldBonus(professionLevel, ProfessionSettingsService.HerbalistaYieldMultiplier);
		if (num > 0)
		{
			GiveReward(player, ProfessionType.Herbalista, yieldPrefab, num);
		}
		if (RollChance(ProfessionSettingsService.HerbalistaSeedChanceAtMax * (double)professionLevel / 100.0))
		{
			IReadOnlyList<PrefabGUID> plantSeedRewards = ProfessionCatalogService.PlantSeedRewards;
			if (plantSeedRewards.Count != 0)
			{
				PrefabGUID itemPrefab = plantSeedRewards[Random.Next(0, plantSeedRewards.Count)];
				GiveReward(player, ProfessionType.Herbalista, itemPrefab, ProfessionSettingsService.HerbalistaSeedAmount);
			}
		}
	}

	private static void HandleJewelGatherRewards(PlayerData player, PrefabGUID yieldPrefab, int professionLevel)
	{
		if (RollChance(ProfessionSettingsService.JoalheiroPerfectGemChanceAtMax * (double)professionLevel / 100.0) && ProfessionCatalogService.TryGetPerfectGem(yieldPrefab, out var perfectGem))
		{
			GiveReward(player, ProfessionType.Joalheiro, perfectGem, ProfessionSettingsService.JoalheiroPerfectGemAmount);
		}
	}

	private static int CalculateYieldBonus(int professionLevel, double multiplier)
	{
		if (professionLevel <= 0 || multiplier <= 0.0)
		{
			return 0;
		}
		int num = professionLevel / 20;
		return Math.Max(0, (int)Math.Floor((double)num * multiplier));
	}

	private static void ApplyDurabilityBonus(Entity itemEntity, PrefabGUID itemPrefab, int level, double bonusAtMax)
	{
		Entity entity = default(Entity);
		if (!itemEntity.Exists() || !itemEntity.Has<Durability>() || bonusAtMax <= 0.0 || !GameSystems.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(itemPrefab, out entity) || !entity.Exists() || !entity.Has<Durability>())
		{
			return;
		}
		Durability val = itemEntity.Read<Durability>();
		Durability val2 = entity.Read<Durability>();
		if (val.MaxDurability > val2.MaxDurability)
		{
			return;
		}
		double num = 1.0 + bonusAtMax * (double)level / 100.0;
		if (!(num <= 1.0))
		{
			float adjustedMax = (float)((double)val.MaxDurability * num);
			itemEntity.With<Durability>((ECSExtensions.WithRefHandler<Durability>)delegate(ref Durability value)
			{
				value.MaxDurability = adjustedMax;
				value.Value = adjustedMax;
			});
		}
	}

	private static void ApplyConsumableBuffBonus(Entity buffEntity, double powerMultiplier, double durationMultiplier)
	{
		if (powerMultiplier > 1.0 && buffEntity.TryGetBuffer<ModifyUnitStatBuff_DOTS>(out DynamicBuffer<ModifyUnitStatBuff_DOTS> dynamicBuffer) && !dynamicBuffer.IsEmpty)
		{
			for (int i = 0; i < dynamicBuffer.Length; i++)
			{
				ModifyUnitStatBuff_DOTS val = dynamicBuffer[i];
				val.Value = (float)((double)val.Value * powerMultiplier);
				dynamicBuffer[i] = val;
			}
		}
		if (durationMultiplier > 1.0 && buffEntity.Has<LifeTime>())
		{
			buffEntity.With<LifeTime>((ECSExtensions.WithRefHandler<LifeTime>)delegate(ref LifeTime lifeTime)
			{
				if (lifeTime.Duration > 0f)
				{
					lifeTime.Duration = (float)((double)lifeTime.Duration * durationMultiplier);
				}
			});
		}
		if (durationMultiplier > 1.0 && buffEntity.Has<HealOnGameplayEvent>() && buffEntity.TryGetBuffer<CreateGameplayEventsOnTick>(out DynamicBuffer<CreateGameplayEventsOnTick> dynamicBuffer2) && !dynamicBuffer2.IsEmpty)
		{
			for (int num = 0; num < dynamicBuffer2.Length; num++)
			{
				CreateGameplayEventsOnTick val2 = dynamicBuffer2[num];
				val2.MaxTicks = Math.Max(1, (int)Math.Round((double)val2.MaxTicks * durationMultiplier));
				dynamicBuffer2[num] = val2;
			}
		}
	}

	private static void GiveReward(PlayerData player, ProfessionType profession, PrefabGUID itemPrefab, int amount)
	{
		if (player == null || !player.CharacterEntity.Exists() || amount <= 0)
		{
			return;
		}
		bool flag = InventoryService.AddItem(player.CharacterEntity, itemPrefab, amount);
		if (!flag)
		{
			InventoryService.CreateDropItem(player.CharacterEntity, itemPrefab, amount);
		}
			string displayName = ProfessionCatalogService.GetDisplayName(profession);
			string value = itemPrefab.LocalizedName(player.Language);
			if (flag)
			{
				MessageService.SendSuccess(player, $"{amount}x {value} extra recebido de {displayName}.");
			}
			else
			{
				MessageService.SendWarning(player, $"{amount}x {value} extra de {displayName} caiu no chao (inventario cheio).");
			}
	}

	private static bool TryResolveGatherProfession(PrefabGUID yieldPrefab, out ProfessionType profession)
	{
		profession = ProfessionType.Minerador;
		if (ProfessionCatalogService.IsGemPrefab(yieldPrefab))
		{
			profession = ProfessionType.Joalheiro;
			return true;
		}
		if (ProfessionCatalogService.IsOrePrefab(yieldPrefab))
		{
			profession = ProfessionType.Minerador;
			return true;
		}
		if (ProfessionCatalogService.IsWoodPrefab(yieldPrefab))
		{
			profession = ProfessionType.Lenhador;
			return true;
		}
		if (ProfessionCatalogService.IsPlantPrefab(yieldPrefab))
		{
			profession = ProfessionType.Herbalista;
			return true;
		}
		return false;
	}

	private static bool TryResolveCraftProfession(PrefabGUID itemPrefab, out ProfessionType profession)
	{
		profession = ProfessionType.Minerador;
		if (ProfessionCatalogService.IsNecklacePrefab(itemPrefab))
		{
			profession = ProfessionType.Joalheiro;
			return true;
		}
		if (ProfessionCatalogService.IsArmorPrefab(itemPrefab))
		{
			profession = ProfessionType.Alfaiate;
			return true;
		}
		if (ProfessionCatalogService.IsWeaponPrefab(itemPrefab))
		{
			profession = ProfessionType.Ferreiro;
			return true;
		}
		if (ProfessionCatalogService.IsConsumablePrefab(itemPrefab))
		{
			profession = ProfessionType.Alquimista;
			return true;
		}
		return false;
	}


	private static bool RollChance(double chance)
	{
		if (chance <= 0.0)
		{
			return false;
		}
		if (chance >= 1.0)
		{
			return true;
		}
		return Random.NextDouble() <= chance;
	}

	private static bool TryResolveOnlinePlayer(ulong platformId, out PlayerData player)
	{
		if (platformId.TryGetPlayerData(out player))
		{
			return player != null;
		}
		return false;
	}

	private static bool TryResolveCachedPlayer(ulong platformId, out PlayerData player)
	{
		if (PlayerService.TryGetById(platformId, out player))
		{
			return player != null;
		}
		return false;
	}

	private static bool IsConsumableBuff(PrefabGUID buffPrefab)
	{
		string text = buffPrefab.GetName().ToLowerInvariant();
		if (!text.Contains("consumable") && !text.Contains("potion") && !text.Contains("elixir") && !text.Contains("coating") && !text.Contains("salve") && !text.Contains("brew"))
		{
			return text.Contains("canteen");
		}
		return true;
	}

	private static void LogExperienceGain(PlayerData player, ProfessionType profession, string sourceContext, PrefabGUID sourcePrefab, PrefabGUID resultPrefab, double baseValue, double xpMultiplier, double calculatedXp, double gainedExperience, int level)
	{
		if (Plugin.LogInstance == null)
		{
			return;
		}

		ulong platformId = (player != null) ? player.PlatformId : 0uL;
		Plugin.LogInstance.LogInfo($"[ProfessionsXP] Context={sourceContext} Player={platformId} Profession={profession} BaseXP={baseValue:0.###} Multiplier={xpMultiplier:0.###} CalculatedXP={calculatedXp:0.###} GainedXP={gainedExperience:0.###} Level={level} Source={FormatPrefabForLog(sourcePrefab)} Result={FormatPrefabForLog(resultPrefab)}");
	}

	private static void LogAlchemyBuffApplied(ulong platformId, PrefabGUID buffPrefab, double powerMultiplier, double durationMultiplier, int level)
	{
		if (Plugin.LogInstance == null)
		{
			return;
		}

		Plugin.LogInstance.LogInfo($"[ProfessionsXP][AlchemyBuff] Player={platformId} Profession=Alquimista Level={level} Buff={FormatPrefabForLog(buffPrefab)} PowerMult={powerMultiplier:0.###} DurationMult={durationMultiplier:0.###}");
	}

	private static string FormatPrefabForLog(PrefabGUID prefab)
	{
		if (prefab.IsEmpty())
		{
			return "Empty(0)";
		}

		string name;
		try
		{
			name = prefab.GetName();
		}
		catch
		{
			name = "Unknown";
		}

		return $"{name}({prefab.GuidHash})";
	}

	private static float3 ParseHexColor(string hex, float3 fallback)
	{
		if (string.IsNullOrWhiteSpace(hex))
		{
			return fallback;
		}
		string text = hex.Trim().TrimStart('#');
		if (text.Length != 6)
		{
			return fallback;
		}
		if (!byte.TryParse(text.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result) || !byte.TryParse(text.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result2) || !byte.TryParse(text.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result3))
		{
			return fallback;
		}
		return new float3((float)(int)result / 255f, (float)(int)result2 / 255f, (float)(int)result3 / 255f);
	}

	private static int ToDisplayExperienceValue(double value)
	{
		return (int)Math.Max(1.0, Math.Round(Math.Max(0.0, value)));
	}


	private static double GetLevelPercent(double experience, int level)
	{
		if (level >= 100)
		{
			return 0.0;
		}
		double num = ConvertLevelToXp(level);
		double num2 = ConvertLevelToXp(level + 1);
		double num3 = Math.Max(1.0, num2 - num);
		double num4 = Math.Clamp(experience - num, 0.0, num3);
		return Math.Round(Math.Min(99.999, num4 / num3 * 100.0), 3);
	}

	private static int ConvertXpToLevel(double xp)
	{
		return Math.Clamp((int)(0.1 * Math.Sqrt(Math.Max(0.0, xp))), 1, 100);
	}

	private static double ConvertLevelToXp(int level)
	{
		return Math.Pow((double)Math.Clamp(level, 1, 100) / 0.1, 2.0);
	}

	private static PlayerProfessionsData EnsurePlayerData(ulong platformId)
	{
		if (PlayerCache.TryGetValue(platformId, out var value) && value != null)
		{
			return value;
		}
		string key = BuildPlayerKey(platformId);
		PlayerProfessionsData playerProfessionsData = Plugin.Database.GetOrCreate(key, () => CreateDefaultPlayerData(platformId));
		if (playerProfessionsData == null)
		{
			playerProfessionsData = CreateDefaultPlayerData(platformId);
		}
		bool num = NormalizePlayerData(playerProfessionsData, platformId);
		PlayerCache[platformId] = playerProfessionsData;
		if (num)
		{
			SavePlayerData(playerProfessionsData);
		}
		return playerProfessionsData;
	}

	private static bool NormalizePlayerData(PlayerProfessionsData data, ulong platformId)
	{
		bool result = false;
		if (data.PlatformId != platformId)
		{
			data.PlatformId = platformId;
			result = true;
		}
		if (data.Professions == null)
		{
			data.Professions = new Dictionary<string, ProfessionProgressData>();
			result = true;
		}
		for (int i = 0; i < ProfessionOrder.Length; i++)
		{
			ProfessionType profession = ProfessionOrder[i];
			string key = profession.ToString();
			if (!data.Professions.TryGetValue(key, out var value) || value == null)
			{
				data.Professions[key] = CreateDefaultProgressData();
				result = true;
				continue;
			}
			if (value.PassiveChoices == null)
			{
				value.PassiveChoices = new Dictionary<int, int>();
				result = true;
			}
			int num = Math.Clamp(value.Level, 1, 100);
			if (value.Level != num)
			{
				value.Level = num;
				result = true;
			}
			double max = ConvertLevelToXp(100);
			double num2 = Math.Clamp(value.Experience, 0.0, max);
			if (Math.Abs(value.Experience - num2) > double.Epsilon)
			{
				value.Experience = num2;
				result = true;
			}
			double num3 = ConvertLevelToXp(value.Level);
			if (value.Experience < num3)
			{
				value.Experience = num3;
				result = true;
			}
			int num4 = ConvertXpToLevel(value.Experience);
			if (value.Level != num4)
			{
				value.Level = num4;
				result = true;
			}
			IReadOnlyList<ProfessionPassiveMilestoneDefinition> milestones = ProfessionCatalogService.GetMilestones(profession);
			List<int> list = new List<int>();
			foreach (KeyValuePair<int, int> passiveChoice in value.PassiveChoices)
			{
				bool flag = false;
				for (int j = 0; j < milestones.Count; j++)
				{
					if (milestones[j].Milestone == (ProfessionMilestone)passiveChoice.Key)
					{
						flag = true;
						break;
					}
				}
				if (!flag || (passiveChoice.Value != 1 && passiveChoice.Value != 2))
				{
					list.Add(passiveChoice.Key);
				}
			}
			for (int k = 0; k < list.Count; k++)
			{
				value.PassiveChoices.Remove(list[k]);
				result = true;
			}
		}
		return result;
	}

	private static PlayerProfessionsData CreateDefaultPlayerData(ulong platformId)
	{
		PlayerProfessionsData playerProfessionsData = new PlayerProfessionsData
		{
			PlatformId = platformId,
			ExperienceLogEnabled = true,
			ExperienceSctEnabled = true,
			UpdatedAtUtc = DateTime.UtcNow
		};
		for (int i = 0; i < ProfessionOrder.Length; i++)
		{
			ProfessionType professionType = ProfessionOrder[i];
			playerProfessionsData.Professions[professionType.ToString()] = CreateDefaultProgressData();
		}
		return playerProfessionsData;
	}

	private static ProfessionProgressData CreateDefaultProgressData()
	{
		return new ProfessionProgressData
		{
			Level = 1,
			Experience = ConvertLevelToXp(1),
			PassiveChoices = new Dictionary<int, int>()
		};
	}

	private static ProfessionProgressData GetProfessionProgress(PlayerProfessionsData data, ProfessionType profession)
	{
		string key = profession.ToString();
		if (!data.Professions.TryGetValue(key, out var value) || value == null)
		{
			value = CreateDefaultProgressData();
			data.Professions[key] = value;
			SavePlayerData(data);
		}
		return value;
	}

	private static void SavePlayerData(PlayerProfessionsData data)
	{
		if (data != null && data.PlatformId != 0L)
		{
			data.UpdatedAtUtc = DateTime.UtcNow;
			PlayerCache[data.PlatformId] = data;
			Plugin.Database.Set(BuildPlayerKey(data.PlatformId), data);
		}
	}

	private static string BuildPlayerKey(ulong platformId)
	{
		return $"{"professions/players/"}{platformId}";
	}

	private static bool TryParsePlatformFromKey(string key, out ulong platformId)
	{
		platformId = 0uL;
		if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("professions/players/", StringComparison.Ordinal))
		{
			return false;
		}
		if (ulong.TryParse(key.Substring("professions/players/".Length), out platformId))
		{
			return platformId != 0;
		}
		return false;
	}
}






















