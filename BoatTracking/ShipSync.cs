using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BoatTracking;

internal sealed class ShipSnapshot
{
  public ZDOID Id;
  public Vector3 Position;
  public string Name = string.Empty;
  public string TypeLabel = string.Empty;
  public int PrefabHash;
}

internal static class ShipSync
{
  private static float _timer;
  private static readonly List<ShipSnapshot> Latest = new();
  private static readonly object Gate = new();

  internal static IReadOnlyList<ShipSnapshot> GetLatest()
  {
    lock (Gate)
      return Latest.ToArray();
  }

  internal static void Tick(float dt)
  {
    if (ZNet.instance == null || ZDOMan.instance == null)
      return;
    if (!ZNet.instance.IsServer())
      return;

    var interval = Mathf.Clamp(BoatTrackingPlugin.SyncInterval.Value, 1f, 30f);
    _timer += dt;
    if (_timer < interval)
      return;
    _timer = 0f;

    BroadcastNow();
  }

  internal static void BroadcastNow()
  {
    if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null)
      return;

    var ships = CollectShips();
    var pkg = WritePackage(ships);

    lock (Gate)
    {
      Latest.Clear();
      Latest.AddRange(ships);
    }

    // Listen-server / singleplayer host draws pins locally.
    if (!ZNet.instance.IsDedicated() && Minimap.instance)
      ShipPins.Apply(ships);

    // Dedicated + listen: push to connected clients.
    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, BoatTrackingPlugin.RpcSync, pkg);
  }

  internal static void RequestFromServer()
  {
    if (ZNet.instance == null || ZRoutedRpc.instance == null)
      return;
    if (ZNet.instance.IsServer())
    {
      BroadcastNow();
      return;
    }
    // Minimap can awake before we are fully connected.
    var serverId = ZRoutedRpc.instance.GetServerPeerID();
    if (serverId == 0L)
      return;
    ZRoutedRpc.instance.InvokeRoutedRPC(serverId, BoatTrackingPlugin.RpcRequestSync);
  }

  internal static void HandleRequestSync(long sender)
  {
    if (ZNet.instance == null || !ZNet.instance.IsServer())
      return;
    var ships = CollectShips();
    var pkg = WritePackage(ships);
    ZRoutedRpc.instance.InvokeRoutedRPC(sender, BoatTrackingPlugin.RpcSync, pkg);
  }

  internal static void HandleSync(long sender, ZPackage pkg)
  {
    if (ZNet.instance == null)
      return;
    // Ignore echoes of our own server broadcast on a dedicated process.
    if (ZNet.instance.IsDedicated())
      return;
    if (pkg == null || pkg.Size() == 0)
      return;

    pkg.SetPos(0);
    var ships = ReadPackage(pkg);
    lock (Gate)
    {
      Latest.Clear();
      Latest.AddRange(ships);
    }
    ShipPins.Apply(ships);
  }

  internal static void HandleRename(long sender, ZPackage pkg)
  {
    if (ZNet.instance == null || !ZNet.instance.IsServer() || pkg == null)
      return;

    pkg.SetPos(0);
    var id = pkg.ReadZDOID();
    var name = pkg.ReadString() ?? string.Empty;
    ApplyRename(id, name);
  }

  internal static void SubmitRename(ZDOID id, string name)
  {
    name = SanitizeName(name);
    if (ZNet.instance == null)
      return;

    if (ZNet.instance.IsServer())
    {
      ApplyRename(id, name);
      return;
    }

    var pkg = new ZPackage();
    pkg.Write(id);
    pkg.Write(name);
    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), BoatTrackingPlugin.RpcRename, pkg);
  }

  private static void ApplyRename(ZDOID id, string name)
  {
    var zdo = ZDOMan.instance?.GetZDO(id);
    if (zdo == null || !zdo.IsValid())
      return;

    if (!ShipCatalog.TryGet(zdo.GetPrefab(), out _))
      return;

    zdo.SetOwner(ZDOMan.GetSessionID());
    zdo.Set(BoatTrackingPlugin.ZdoNameKey, name);
    BroadcastNow();
  }

  internal static string SanitizeName(string? name)
  {
    if (name == null || string.IsNullOrWhiteSpace(name))
      return string.Empty;
    var trimmed = name.Trim();
    var max = Mathf.Clamp(BoatTrackingPlugin.MaxNameLength.Value, 1, 40);
    if (trimmed.Length > max)
      trimmed = trimmed.Substring(0, max);
    return trimmed;
  }

  private static List<ShipSnapshot> CollectShips()
  {
    var result = new List<ShipSnapshot>();
    var zdoMan = ZDOMan.instance;
    if (zdoMan == null)
      return result;

    void Consider(ZDO? zdo)
    {
      if (zdo == null || !zdo.IsValid())
        return;
      if (!ShipCatalog.TryGet(zdo.GetPrefab(), out var info))
        return;
      var custom = zdo.GetString(BoatTrackingPlugin.ZdoNameKey, string.Empty);
      if (!BoatTrackingPlugin.ShowUnnamed.Value && string.IsNullOrWhiteSpace(custom))
        return;

      result.Add(new ShipSnapshot
      {
        Id = zdo.m_uid,
        Position = zdo.GetPosition(),
        Name = custom?.Trim() ?? string.Empty,
        TypeLabel = info.Label,
        PrefabHash = info.Hash,
      });
    }

    var bySector = zdoMan.m_objectsBySector;
    if (bySector != null)
    {
      foreach (var list in bySector)
      {
        if (list == null)
          continue;
        foreach (var zdo in list)
          Consider(zdo);
      }
    }

    // Newer Valheim builds also keep some ZDOs in chunk buckets.
    CollectFromChunkField(zdoMan, Consider);

    return result;
  }

  private static void CollectFromChunkField(ZDOMan zdoMan, System.Action<ZDO?> consider)
  {
    var field = AccessTools.Field(typeof(ZDOMan), "m_objectsByChunk");
    if (field == null)
      return;
    var value = field.GetValue(zdoMan);
    if (value == null)
      return;

    // Expected shapes: Dictionary<*, List<ZDO>> or similar enumerable of lists.
    if (value is System.Collections.IDictionary dict)
    {
      foreach (var entry in dict.Values)
        ConsiderZdoList(entry, consider);
      return;
    }

    if (value is System.Collections.IEnumerable enumerable)
    {
      foreach (var entry in enumerable)
        ConsiderZdoList(entry, consider);
    }
  }

  private static void ConsiderZdoList(object? entry, System.Action<ZDO?> consider)
  {
    if (entry is System.Collections.IEnumerable list)
    {
      foreach (var item in list)
      {
        if (item is ZDO zdo)
          consider(zdo);
      }
    }
  }

  private static ZPackage WritePackage(List<ShipSnapshot> ships)
  {
    var pkg = new ZPackage();
    pkg.Write(ships.Count);
    foreach (var ship in ships)
    {
      pkg.Write(ship.Id);
      pkg.Write(ship.Position);
      pkg.Write(ship.Name ?? string.Empty);
      pkg.Write(ship.TypeLabel ?? string.Empty);
      pkg.Write(ship.PrefabHash);
    }
    return pkg;
  }

  private static List<ShipSnapshot> ReadPackage(ZPackage pkg)
  {
    var count = pkg.ReadInt();
    var ships = new List<ShipSnapshot>(count);
    for (var i = 0; i < count; i++)
    {
      ships.Add(new ShipSnapshot
      {
        Id = pkg.ReadZDOID(),
        Position = pkg.ReadVector3(),
        Name = pkg.ReadString() ?? string.Empty,
        TypeLabel = pkg.ReadString() ?? string.Empty,
        PrefabHash = pkg.ReadInt(),
      });
    }
    return ships;
  }
}
