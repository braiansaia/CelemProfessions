using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ScarletCore.Commanding;
using ScarletCore.Data;

namespace CelemTemplate;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("markvaaz.ScarletCore")]
[BepInDependency("gg.deca.VampireCommandFramework")]
public class Plugin : BasePlugin {
  // Descomente estas linhas quando o plugin precisar aplicar patches Harmony.
  // static Harmony _harmony;
  // public static Harmony Harmony => _harmony;

  public static Plugin Instance { get; private set; }
  public static ManualLogSource LogInstance { get; private set; }

  // Descomente estas linhas quando o plugin precisar de configuracao persistida ou banco via ScarletCore.
  // public static Settings Settings { get; private set; }
  // public static Database Database { get; private set; }

  public override void Load() {
    Instance = this;
    LogInstance = Log;

    Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} version {MyPluginInfo.PLUGIN_VERSION} is loaded!");

    // Ative este bloco se o plugin tiver classes em Patches/ ou qualquer patch manual com Harmony.
    // _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
    // _harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());

    // Ative estas linhas se o plugin precisar salvar configuracoes ou dados persistentes.
    // Settings = new Settings(MyPluginInfo.PLUGIN_GUID, Instance);
    // Database = new Database(MyPluginInfo.PLUGIN_GUID);

    CommandHandler.RegisterAll();
  }

  public override bool Unload() {
    // Ative se Harmony tiver sido inicializado no Load().
    // _harmony?.UnpatchSelf();
    CommandHandler.UnregisterAssembly();
    return true;
  }
}
