using HarmonyLib;
using UnityEngine;

namespace BoatTracking.Patches;

[HarmonyPatch(typeof(Player), nameof(Player.Interact))]
internal static class InteractRenamePatch
{
  // Returning false from this Prefix skips Player.Interact's 0.2s hold throttle,
  // so without a latch hold-E would reopen the dialog every frame.
  internal static bool OpenedThisHold;

  private static bool Prefix(Player __instance, GameObject go, bool hold, bool alt)
  {
    if (__instance != Player.m_localPlayer)
      return true;

    if (!hold)
      return true;

    // Still holding after we already opened (or dialog is up): swallow so we don't
    // re-fire, and so board/steer hold-handlers don't also run.
    if (OpenedThisHold || TextInput.IsVisible())
    {
      if (IsShipHover(go))
        return false;
      return true;
    }

    if (!go)
      return true;
    // Let chest hold-E (auto deposit / similar) alone.
    if (go.GetComponentInParent<Container>())
      return true;

    var ship = ShipCatalog.FindShipFrom(go.GetComponent<Component>());
    if (!ship)
      return true;

    var nview = ship.GetComponent<ZNetView>();
    if (!nview || !nview.IsValid())
      return true;

    var max = Mathf.Clamp(BoatTrackingPlugin.MaxNameLength.Value, 1, 40);
    if (!TextInput.instance)
      return true;

    OpenedThisHold = true;
    TextInput.instance.RequestText(new ShipNameReceiver(nview.GetZDO().m_uid), "Rename ship", max);
    return false;
  }

  private static bool IsShipHover(GameObject? go)
  {
    if (!go || go.GetComponentInParent<Container>())
      return false;
    return ShipCatalog.FindShipFrom(go.GetComponent<Component>());
  }

  private sealed class ShipNameReceiver : TextReceiver
  {
    private readonly ZDOID _id;

    public ShipNameReceiver(ZDOID id) => _id = id;

    public string GetText()
    {
      var zdo = ZDOMan.instance?.GetZDO(_id);
      return zdo != null && zdo.IsValid()
        ? zdo.GetString(BoatTrackingPlugin.ZdoNameKey, string.Empty)
        : string.Empty;
    }

    public void SetText(string text) => ShipSync.SubmitRename(_id, text);
  }
}

[HarmonyPatch(typeof(Player), "Update")]
internal static class InteractRenameLatchResetPatch
{
  private static void Postfix(Player __instance)
  {
    if (__instance != Player.m_localPlayer)
      return;
    if (ZInput.GetButton("Use") || ZInput.GetButton("JoyUse"))
      return;
    InteractRenamePatch.OpenedThisHold = false;
  }
}

[HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.GetHoverText))]
internal static class ShipControllsHoverPatch
{
  private static void Postfix(ShipControlls __instance, ref string __result)
  {
    HoverText.AppendShipName(__instance, ref __result);
  }
}

internal static class HoverText
{
  internal static void AppendShipName(Component component, ref string result)
  {
    var ship = ShipCatalog.FindShipFrom(component);
    if (!ship)
      return;
    var nview = ship.GetComponent<ZNetView>();
    if (!nview || !nview.IsValid())
      return;

    var zdo = nview.GetZDO();
    if (!ShipCatalog.TryGet(zdo.GetPrefab(), out var info))
      return;

    var name = ShipCatalog.DisplayName(zdo, info);
    const string renameHint = "\n[<color=yellow><b>Hold $KEY_Use</b></color>] Rename ship";
    if (string.IsNullOrEmpty(result))
      result = $"<color=orange>{name}</color>{renameHint}";
    else if (!result.Contains(name))
      result = $"<color=orange>{name}</color>\n{result}{renameHint}";
    else if (!result.Contains("Rename ship"))
      result += renameHint;
  }
}
