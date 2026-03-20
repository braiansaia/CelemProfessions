using CelemProfessions.Models;
using ProjectM;
using ScarletCore.Services;
using Stunlock.Core;
using Unity.Entities;

namespace CelemProfessions.Events;

public readonly record struct GatherEventData(PlayerData Player, PrefabGUID TargetPrefab, PrefabGUID YieldPrefab, ProfessionsTypes Profession);

public readonly record struct FishingEventData(PlayerData Player, PrefabGUID FishingAreaPrefab);

public readonly record struct HunterKillEventData(PlayerData Player, Entity Target, PrefabGUID TargetPrefab);
