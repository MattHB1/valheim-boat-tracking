using System.Collections.Generic;
using UnityEngine;

namespace BoatTracking;

internal static class ShipCatalog
{
  internal readonly struct PrefabInfo
  {
    public readonly string Prefab;
    public readonly string Label;
    public readonly int Hash;

    public PrefabInfo(string prefab, string label)
    {
      Prefab = prefab;
      Label = label;
      Hash = prefab.GetStableHashCode();
    }
  }

  // Vanilla ship prefabs. VikingShip = Longship; VikingShip_Ashlands = Drakkar.
  internal static readonly PrefabInfo[] Prefabs =
  {
    new("Raft", "Raft"),
    new("Karve", "Karve"),
    new("VikingShip", "Longship"),
    new("VikingShip_Ashlands", "Drakkar"),
  };

  private static readonly Dictionary<int, PrefabInfo> ByHash = BuildLookup();

  private static Dictionary<int, PrefabInfo> BuildLookup()
  {
    var map = new Dictionary<int, PrefabInfo>();
    foreach (var info in Prefabs)
      map[info.Hash] = info;
    return map;
  }

  internal static bool TryGet(int prefabHash, out PrefabInfo info) =>
    ByHash.TryGetValue(prefabHash, out info);

  internal static string DisplayName(ZDO zdo, PrefabInfo info)
  {
    var custom = zdo.GetString(BoatTrackingPlugin.ZdoNameKey, string.Empty);
    if (!string.IsNullOrWhiteSpace(custom))
      return custom.Trim();
    return info.Label;
  }

  internal static string DisplayName(string custom, string typeLabel)
  {
    if (!string.IsNullOrWhiteSpace(custom))
      return custom.Trim();
    return typeLabel;
  }

  internal static Ship? FindShipFrom(Component? component)
  {
    if (!component)
      return null;
    if (component is Ship ship)
      return ship;
    var parent = component.GetComponentInParent<Ship>();
    if (parent)
      return parent;
    var root = component.transform.root;
    return root ? root.GetComponentInChildren<Ship>() : null;
  }
}
