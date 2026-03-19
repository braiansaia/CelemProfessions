using CelemProfessions.Models;
using ProjectM;
using ScarletCore.Services;
using Stunlock.Core;
using Unity.Entities;

namespace CelemProfessions.Events;

public readonly record struct GatherEventData(PlayerData Player, Entity Target, PrefabGUID TargetPrefab, PrefabGUID YieldPrefab, ProfessionType Profession);

public readonly record struct CraftedItemEventData(PlayerData Player, Entity Workstation, Entity ItemEntity, PrefabGUID ItemPrefab, ProfessionType Profession);

public readonly record struct FishingEventData(PlayerData Player, Entity FishingTarget, PrefabGUID FishingAreaPrefab);

public readonly record struct HunterKillEventData(PlayerData Player, Entity Target, PrefabGUID TargetPrefab);
