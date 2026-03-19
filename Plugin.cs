using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using CelemProfessions.Patches;
using CelemProfessions.Service;
using HarmonyLib;
using ScarletCore.Commanding;
using ScarletCore.Data;
using ScarletCore.Events;
using ScarletCore.Systems;

namespace CelemProfessions;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("markvaaz.ScarletCore")]
[BepInDependency("gg.deca.VampireCommandFramework")]
public class Plugin : BasePlugin {
  private static Harmony _harmony;

  public static Harmony Harmony => _harmony;
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

    _harmony = ScarletCore.Plugin.Harmony;
    if (_harmony == null) {
      throw new InvalidOperationException("ScarletCore Harmony is not initialized.");
    }

    _harmony.PatchAll(Assembly.GetExecutingAssembly());

    CommandHandler.RegisterAll();
    GameSystems.OnInitialize(OnInitialize);
  }

  private void OnInitialize() {
    ProfessionService.Initialize();
    ProfessionsEventPatch.Initialize();
  }

  public override bool Unload() {
    ProfessionService.Shutdown();
    Database?.UnregisterAssembly();
    UnpatchAssemblyPatches();
    CommandHandler.UnregisterAssembly();
    EventManager.UnregisterAssembly();
    return true;
  }

  private static void UnpatchAssemblyPatches() {
    if (_harmony == null) {
      return;
    }

    Assembly assembly = Assembly.GetExecutingAssembly();
    foreach (MethodBase original in Harmony.GetAllPatchedMethods()) {
      HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(original);
      if (patchInfo == null) {
        continue;
      }

      UnpatchCollection(original, patchInfo.Prefixes, assembly);
      UnpatchCollection(original, patchInfo.Postfixes, assembly);
      UnpatchCollection(original, patchInfo.Transpilers, assembly);
      UnpatchCollection(original, patchInfo.Finalizers, assembly);
    }
  }

  private static void UnpatchCollection(MethodBase original, IEnumerable<Patch> patches, Assembly assembly) {
    foreach (Patch patch in patches) {
      MethodInfo patchMethod = patch.PatchMethod;
      if (patchMethod?.DeclaringType?.Assembly != assembly) {
        continue;
      }

      _harmony.Unpatch(original, patchMethod);
    }
  }
}