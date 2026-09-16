using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoatTracking.Patches;

[HarmonyPatch(typeof(Player), nameof(Player.Interact))]
internal static class InteractRenamePatch
{
  // Every Use press sends hold=false (tap) first, then hold=true while held.
  // Open rename after a sustained hold, but ignore typed characters until Use is
  // released so held E does not spam into the box.
  private const float RenameHoldSeconds = 0.75f;

  internal static bool OpenedThisHold;
  private static float UsePressStartedAt = -1f;
  private static bool SuppressTypingUntilUseReleased;
  private static string SeedText = string.Empty;

  private static bool Prefix(Player __instance, GameObject go, bool hold, bool alt)
  {
    if (__instance != Player.m_localPlayer)
      return true;

    if (ShouldLeaveUseAlone(__instance, go))
    {
      OpenedThisHold = false;
      return true;
    }

    if (!hold)
      return true;

    if (OpenedThisHold || TextInput.IsVisible())
    {
      if (IsRenameTarget(go))
        return false;
      return true;
    }

    if (!IsRenameTarget(go))
      return true;

    if (UsePressStartedAt < 0f || Time.time - UsePressStartedAt < RenameHoldSeconds)
      return true;

    var ship = ShipCatalog.FindShipFrom(go.GetComponent<Component>());
    if (!ship)
      return true;

    var nview = ship.GetComponent<ZNetView>();
    if (!nview || !nview.IsValid())
      return true;

    if (!TextInput.instance)
      return true;

    var max = Mathf.Clamp(BoatTrackingPlugin.MaxNameLength.Value, 1, 40);
    var receiver = new ShipNameReceiver(nview.GetZDO().m_uid);
    SeedText = receiver.GetText() ?? string.Empty;
    SuppressTypingUntilUseReleased = true;
    OpenedThisHold = true;
    TextInput.instance.RequestText(receiver, "Rename ship", max);
    ForceTextInputText(SeedText);
    return false;
  }

  internal static void OnPlayerUpdate(Player player)
  {
    if (player != Player.m_localPlayer)
      return;

    var useHeld = ZInput.GetButton("Use") || ZInput.GetButton("JoyUse");
    if (useHeld)
    {
      if (UsePressStartedAt < 0f)
        UsePressStartedAt = Time.time;

      // Keep wiping held-E key repeats out of the open rename box.
      if (SuppressTypingUntilUseReleased && TextInput.IsVisible())
        ForceTextInputText(SeedText);
      return;
    }

    UsePressStartedAt = -1f;
    OpenedThisHold = false;

    if (SuppressTypingUntilUseReleased)
    {
      SuppressTypingUntilUseReleased = false;
      if (TextInput.IsVisible())
        ForceTextInputText(SeedText);
    }
  }

  private static void ForceTextInputText(string text)
  {
    var ti = TextInput.instance;
    if (!ti)
      return;

    // Prefer publicized/private text widgets via reflection (live fields may be private).
    foreach (var fieldName in new[] { "m_textField", "m_inputField", "m_input" })
    {
      var field = AccessTools.Field(typeof(TextInput), fieldName);
      if (field == null)
        continue;
      var widget = field.GetValue(ti);
      if (widget == null)
        continue;

      if (widget is string)
      {
        field.SetValue(ti, text);
        continue;
      }

      var textProp = widget.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
      if (textProp != null && textProp.CanWrite)
      {
        textProp.SetValue(widget, text, null);
        return;
      }
    }
  }

  internal static bool ShouldLeaveUseAlone(Player player, GameObject? go)
  {
    if (!player)
      return false;

    if (IsRudderHover(go))
      return true;

    if (go && go.GetComponentInParent<Ladder>())
      return true;

    if (IsLocalPlayerSteering(player))
      return true;

    return false;
  }

  internal static bool IsLocalPlayerSteering(Player player)
  {
    if (!player)
      return false;
    if (player.GetControlledShip())
      return true;
    var doodad = player.GetDoodadController();
    return doodad != null && doodad.IsValid() && doodad.GetControlledComponent() is Ship;
  }

  internal static bool IsRudderHover(GameObject? go) =>
    go && go.GetComponentInParent<ShipControlls>();

  private static bool IsRenameTarget(GameObject? go)
  {
    if (!go)
      return false;
    if (go.GetComponentInParent<Container>())
      return false;
    if (go.GetComponentInParent<Ladder>())
      return false;
    if (go.GetComponentInParent<ShipControlls>())
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
internal static class InteractRenameHoldTrackPatch
{
  private static void Postfix(Player __instance) => InteractRenamePatch.OnPlayerUpdate(__instance);
}

[HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.GetHoverText))]
internal static class ShipControllsHoverPatch
{
  private static void Postfix(ShipControlls __instance, ref string __result)
  {
    HoverText.AppendShipName(__instance, ref __result, showRenameHint: false);
  }
}

[HarmonyPatch(typeof(Chair), nameof(Chair.GetHoverText))]
internal static class ChairHoverPatch
{
  private static void Postfix(Chair __instance, ref string __result)
  {
    if (!ShipCatalog.FindShipFrom(__instance))
      return;
    var showRename = Player.m_localPlayer &&
                     !InteractRenamePatch.ShouldLeaveUseAlone(Player.m_localPlayer, __instance.gameObject);
    HoverText.AppendShipName(__instance, ref __result, showRename);
  }
}

internal static class HoverText
{
  internal static void AppendShipName(Component component, ref string result, bool showRenameHint)
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
    var renameHint = string.Empty;
    if (showRenameHint)
    {
      renameHint = Localization.instance.Localize(
        "\n[<color=yellow><b>Hold $KEY_Use</b></color>] Rename ship");
    }

    if (string.IsNullOrEmpty(result))
      result = $"<color=orange>{name}</color>{renameHint}";
    else if (!result.Contains(name))
      result = $"<color=orange>{name}</color>\n{result}{renameHint}";
    else if (renameHint.Length > 0 && !result.Contains("Rename ship"))
      result += renameHint;
  }
}
