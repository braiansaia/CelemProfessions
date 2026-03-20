using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using CelemProfessions.Patches;
using CelemProfessions.Service;
using ScarletCore.Commanding;
using ScarletCore.Data;
using ScarletCore.Events;
using ScarletCore.Systems;

namespace CelemProfessions;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("markvaaz.ScarletCore")]
[BepInDependency("gg.deca.VampireCommandFramework")]
public class Plugin : BasePlugin {
  public static Plugin Instance { get; private set; }
  public static ManualLogSource LogInstance { get; private set; }
  public static Settings Settings { get; private set; }
  public static Database Database { get; private set; }

  public override void Load() {
    Instance = this;
    LogInstance = Log;

    Settings = new Settings(MyPluginInfo.PLUGIN_GUID, Instance);
    Database = new Database(MyPluginInfo.PLUGIN_GUID);
    Database.EnableAutoBackup();

    ProfessionSettingsService.Configure();
    CommandHandler.RegisterAll();
    GameSystems.OnInitialize(OnInitialize);
  }

  private void OnInitialize() {
    RunInitializationStep("ProgressionService", ProgressionService.Initialize);
    RunInitializationStep("RewardConfigService", RewardConfigService.Initialize);
    RunInitializationStep("ExperienceConfigService", ProfessionExperienceConfigService.Initialize);
    RunInitializationStep("PassiveConfigService", PassiveConfigService.Initialize);
    RunInitializationStep("ProfessionService", ProfessionService.Initialize);
    RunInitializationStep("EventPatch", EventPatch.Initialize);
  }

  public override bool Unload() {
    CraftTrackingService.Shutdown();
    RewardConfigService.Shutdown();
    ProfessionExperienceConfigService.Shutdown();
    PassiveConfigService.Shutdown();
    ProgressionService.Shutdown();
    ProfessionService.Shutdown();
    Database?.UnregisterAssembly();
    CommandHandler.UnregisterAssembly();
    EventManager.UnregisterAssembly();
    return true;
  }

  private static void RunInitializationStep(string stepName, Action initializer) {
    try {
      initializer();
    } catch (Exception ex) {
      LogInstance?.LogError($"[CelemProfessions] Failed while initializing {stepName}: {ex}");
      throw;
    }
  }
}
