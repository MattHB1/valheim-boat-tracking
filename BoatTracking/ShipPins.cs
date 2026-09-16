using System.Collections.Generic;
using UnityEngine;

namespace BoatTracking;

internal static class ShipPins
{
  private static readonly Dictionary<ZDOID, Minimap.PinData> Pins = new();
  private static readonly Dictionary<int, Sprite> Icons = new();

  internal static void Clear()
  {
    if (Minimap.instance)
    {
      foreach (var pin in Pins.Values)
      {
        if (pin != null)
          Minimap.instance.RemovePin(pin);
      }
    }
    Pins.Clear();
  }

  internal static void Apply(IReadOnlyList<ShipSnapshot> ships)
  {
    if (!BoatTrackingPlugin.ShowPins.Value || Minimap.instance == null)
    {
      Clear();
      return;
    }

    EnsureIcons();

    var seen = new HashSet<ZDOID>();
    var player = Player.m_localPlayer;
    var controlled = player ? player.GetControlledShip() : null;
    var controlledPos = controlled ? controlled.transform.position : Vector3.zero;

    foreach (var ship in ships)
    {
      seen.Add(ship.Id);

      // Hide pin for the ship you are currently steering.
      if (controlled && Vector3.Distance(controlledPos, ship.Position) < 0.5f)
      {
        if (Pins.TryGetValue(ship.Id, out var hidePin))
        {
          Minimap.instance.RemovePin(hidePin);
          Pins.Remove(ship.Id);
        }
        continue;
      }

      var label = ShipCatalog.DisplayName(ship.Name, ship.TypeLabel);
      if (!Pins.TryGetValue(ship.Id, out var pin) || pin == null)
      {
        pin = Minimap.instance.AddPin(ship.Position, Minimap.PinType.Icon3, label, false, false);
        if (Icons.TryGetValue(ship.PrefabHash, out var sprite) && sprite)
          pin.m_icon = sprite;
        pin.m_doubleSize = true;
        Pins[ship.Id] = pin;
      }
      else
      {
        pin.m_pos = ship.Position;
        pin.m_name = label;
        if (Icons.TryGetValue(ship.PrefabHash, out var sprite) && sprite)
          pin.m_icon = sprite;
      }
    }

    var stale = new List<ZDOID>();
    foreach (var id in Pins.Keys)
    {
      if (!seen.Contains(id))
        stale.Add(id);
    }
    foreach (var id in stale)
    {
      Minimap.instance.RemovePin(Pins[id]);
      Pins.Remove(id);
    }
  }

  private static void EnsureIcons()
  {
    if (Icons.Count > 0 || ObjectDB.instance == null)
      return;

    GameObject? hammer = null;
    if (ObjectDB.instance.m_itemByHash != null &&
        ObjectDB.instance.m_itemByHash.TryGetValue("Hammer".GetStableHashCode(), out var byHash))
      hammer = byHash;

    if (!hammer && ObjectDB.instance.m_items != null)
    {
      foreach (var item in ObjectDB.instance.m_items)
      {
        if (item && item.name == "Hammer")
        {
          hammer = item;
          break;
        }
      }
    }

    if (!hammer)
      return;

    var drop = hammer.GetComponent<ItemDrop>();
    var table = drop?.m_itemData?.m_shared?.m_buildPieces;
    if (table?.m_pieces == null)
      return;

    foreach (var pieceObj in table.m_pieces)
    {
      if (!pieceObj)
        continue;
      // Piece table entries are sometimes named "VikingShip" and sometimes have suffixes.
      var piece = pieceObj.GetComponent<Piece>();
      if (!piece || !piece.m_icon)
        continue;
      var rawName = pieceObj.name;
      var hash = rawName.GetStableHashCode();
      if (ShipCatalog.TryGet(hash, out _))
      {
        Icons[hash] = piece.m_icon;
        continue;
      }
      foreach (var info in ShipCatalog.Prefabs)
      {
        if (rawName.StartsWith(info.Prefab))
          Icons[info.Hash] = piece.m_icon;
      }
    }
  }
}
