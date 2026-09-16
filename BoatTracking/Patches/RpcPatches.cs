using System;
using HarmonyLib;
using UnityEngine;

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

// Draw custom ship markers each pin-update frame (does not rely on AddPin).
[HarmonyPatch(typeof(Minimap), "UpdatePins")]
internal static class MinimapUpdatePinsPatch
{
  private static void Postfix() => ShipPins.UpdateDrawnMarkers();
}

// Keep snapshot applied if something else wiped state.
[HarmonyPatch(typeof(Minimap), "UpdateMap")]
internal static class MinimapUpdateRefreshPatch
{
  private static float _next;
  private static void Postfix()
  {
    if (Time.time < _next)
      return;
    _next = Time.time + 5f;
    var latest = ShipSync.GetLatest();
    if (latest.Count > 0)
      ShipPins.Apply(latest);
  }
}
