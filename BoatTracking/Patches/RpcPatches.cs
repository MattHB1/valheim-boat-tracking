using System;
using HarmonyLib;

namespace BoatTracking.Patches;

[HarmonyPatch(typeof(Game), nameof(Game.Start))]
internal static class GameStartPatch
{
  private static void Prefix()
  {
    ZRoutedRpc.instance.Register(BoatTrackingPlugin.RpcRequestSync, new Action<long>(ShipSync.HandleRequestSync));
    ZRoutedRpc.instance.Register<ZPackage>(BoatTrackingPlugin.RpcSync, ShipSync.HandleSync);
    ZRoutedRpc.instance.Register<ZPackage>(BoatTrackingPlugin.RpcRename, ShipSync.HandleRename);
  }
}

[HarmonyPatch(typeof(Game), nameof(Game.Logout))]
internal static class GameLogoutPatch
{
  private static void Prefix() => ShipPins.Clear();
}

[HarmonyPatch(typeof(Minimap), nameof(Minimap.OnDestroy))]
internal static class MinimapDestroyPatch
{
  private static void Postfix() => ShipPins.Clear();
}

[HarmonyPatch(typeof(Minimap), nameof(Minimap.Awake))]
internal static class MinimapAwakePatch
{
  private static void Postfix() => ShipSync.RequestFromServer();
}
