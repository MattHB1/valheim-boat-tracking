using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace BoatTracking;

[BepInPlugin(GUID, NAME, VERSION)]
public class BoatTrackingPlugin : BaseUnityPlugin
{
  public const string GUID = "matthb1.boattracking";
  public const string NAME = "BoatTracking";
  public const string VERSION = "1.0.1";

  // Wire-format version for sync packages (independent of Thunderstore VERSION).
  public const int ProtocolVersion = 1;

  public const string ZdoNameKey = "BoatTracking.Name";
  public const string RpcSync = "BoatTracking.Sync";
  public const string RpcRename = "BoatTracking.Rename";
  public const string RpcRequestSync = "BoatTracking.RequestSync";

  internal static BoatTrackingPlugin Instance = null!;
  internal static ConfigEntry<bool> ShowPins = null!;
  internal static ConfigEntry<bool> ShowUnnamed = null!;
  internal static ConfigEntry<float> SyncInterval = null!;
  internal static ConfigEntry<int> MaxNameLength = null!;

  internal static ManualLogSource Log = null!;

  private void Awake()
  {
    Instance = this;
    Log = Logger;
    ShowPins = Config.Bind("General", "ShowPins", true, "Show ship pins on the minimap and world map.");
    ShowUnnamed = Config.Bind("General", "ShowUnnamed", true, "Show pins for ships that have not been renamed (label = ship type).");
    SyncInterval = Config.Bind("General", "SyncInterval", 3f, "Seconds between server ship-position syncs (1-30).");
    MaxNameLength = Config.Bind("General", "MaxNameLength", 24, "Max characters for a ship name (1-40).");

    Logger.LogInfo($"{NAME} {VERSION} loaded - name ships with hold E; server syncs map pins.");
    new Harmony(GUID).PatchAll();
  }

  private void Update()
  {
    ShipSync.Tick(UnityEngine.Time.deltaTime);
  }
}
