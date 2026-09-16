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
  private static int _lastLoggedCount = -1;
  private static bool _loggedProtocolMismatch;

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

    try
    {
      BroadcastNow();
    }
    catch (System.Exception ex)
    {
      BoatTrackingPlugin.Log.LogError($"Ship sync failed: {ex}");
    }
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

    if (ships.Count != _lastLoggedCount)
    {
      _lastLoggedCount = ships.Count;
      BoatTrackingPlugin.Log.LogInfo($"Broadcasting {ships.Count} ship pin(s) to clients.");
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
    var serverId = GetServerPeerIdSafe();
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

    try
    {
      pkg.SetPos(0);
      if (!TryReadPackage(pkg, out var ships))
        return;

      lock (Gate)
      {
        Latest.Clear();
        Latest.AddRange(ships);
      }
      if (ships.Count != _lastLoggedCount)
      {
        _lastLoggedCount = ships.Count;
        BoatTrackingPlugin.Log.LogInfo($"Received {ships.Count} ship pin(s) from server.");
      }
      ShipPins.Apply(ships);
    }
    catch (System.Exception ex)
    {
      BoatTrackingPlugin.Log.LogError($"Ship sync receive failed: {ex}");
    }
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
    var serverId = GetServerPeerIdSafe();
    if (serverId == 0L)
    {
      BoatTrackingPlugin.Log.LogWarning("Rename skipped - no server peer id yet.");
      return;
    }
    ZRoutedRpc.instance.InvokeRoutedRPC(serverId, BoatTrackingPlugin.RpcRename, pkg);
  }

  // Live GetServerPeerID is non-public; publicized libs lie. Call via reflection.
  private static long GetServerPeerIdSafe()
  {
    try
    {
      var method = AccessTools.Method(typeof(ZRoutedRpc), "GetServerPeerID");
      if (method != null && ZRoutedRpc.instance != null)
      {
        var result = method.Invoke(ZRoutedRpc.instance, null);
        if (result is long id)
          return id;
      }
    }
    catch (System.Exception ex)
    {
      BoatTrackingPlugin.Log.LogWarning($"GetServerPeerID reflection failed: {ex.Message}");
    }

    try
    {
      var peer = ZNet.instance != null ? ZNet.instance.GetServerPeer() : null;
      if (peer != null)
      {
        var uidField = AccessTools.Field(peer.GetType(), "m_uid");
        if (uidField != null && uidField.GetValue(peer) is long uid)
          return uid;
      }
    }
    catch (System.Exception ex)
    {
      BoatTrackingPlugin.Log.LogWarning($"GetServerPeer fallback failed: {ex.Message}");
    }

    return 0L;
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

    var seen = new HashSet<ZDOID>();

    void Consider(ZDO? zdo)
    {
      if (zdo == null || !zdo.IsValid())
        return;
      if (!seen.Add(zdo.m_uid))
        return;
      if (!ShipCatalog.TryGet(zdo.GetPrefab(), out var info))
        return;

      var custom = zdo.GetString(BoatTrackingPlugin.ZdoNameKey, string.Empty);
      if (!BoatTrackingPlugin.ShowUnnamed.Value && string.IsNullOrWhiteSpace(custom))
        return;

      var pos = zdo.GetPosition();
      result.Add(new ShipSnapshot
      {
        Id = zdo.m_uid,
        Position = pos,
        Name = custom?.Trim() ?? string.Empty,
        TypeLabel = info.Label,
        PrefabHash = info.Hash,
      });
    }

    // Reflection: live m_objectsByID is private (publicized libs lie), but FieldInfo works.
    CollectFromObjectsById(zdoMan, Consider);

    // Fallback if dictionary scan found nothing.
    if (result.Count == 0)
      CollectFromPrefabIterative(zdoMan, Consider);

    return result;
  }

  private static void CollectFromObjectsById(ZDOMan zdoMan, System.Action<ZDO?> consider)
  {
    try
    {
      var field = AccessTools.Field(typeof(ZDOMan), "m_objectsByID");
      var value = field?.GetValue(zdoMan);
      if (value is System.Collections.IDictionary dict)
      {
        foreach (var entry in dict.Values)
        {
          if (entry is ZDO zdo)
            consider(zdo);
        }
        return;
      }

      // Some builds use a custom map type that still enumerates values.
      if (value is System.Collections.IEnumerable enumerable)
      {
        foreach (var entry in enumerable)
        {
          if (entry is ZDO zdo)
            consider(zdo);
          else if (entry is System.Collections.DictionaryEntry de && de.Value is ZDO zdo2)
            consider(zdo2);
        }
      }
    }
    catch (System.Exception ex)
    {
      BoatTrackingPlugin.Log.LogWarning($"ZDO id scan failed: {ex.Message}");
    }
  }

  private static void CollectFromPrefabIterative(ZDOMan zdoMan, System.Action<ZDO?> consider)
  {
    var buffer = new List<ZDO>();
    foreach (var info in ShipCatalog.Prefabs)
    {
      buffer.Clear();
      var index = 0;
      var guard = 0;
      while (!zdoMan.GetAllZDOsWithPrefabIterative(info.Prefab, buffer, ref index))
      {
        if (++guard > 100000)
        {
          BoatTrackingPlugin.Log.LogWarning($"Prefab scan aborted for {info.Prefab}");
          break;
        }
      }

      foreach (var zdo in buffer)
        consider(zdo);
    }
  }

  private static ZPackage WritePackage(List<ShipSnapshot> ships)
  {
    var pkg = new ZPackage();
    pkg.Write(BoatTrackingPlugin.ProtocolVersion);
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

  private static bool TryReadPackage(ZPackage pkg, out List<ShipSnapshot> ships)
  {
    ships = new List<ShipSnapshot>();
    var version = pkg.ReadInt();
    if (version != BoatTrackingPlugin.ProtocolVersion)
    {
      NotifyProtocolMismatch(version);
      return false;
    }

    var count = pkg.ReadInt();
    if (count < 0 || count > 512)
    {
      BoatTrackingPlugin.Log.LogWarning($"Ship sync rejected: unreasonable ship count {count}.");
      return false;
    }

    ships = new List<ShipSnapshot>(count);
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
    return true;
  }

  private static void NotifyProtocolMismatch(int serverVersion)
  {
    if (_loggedProtocolMismatch)
      return;
    _loggedProtocolMismatch = true;

    var clientVersion = BoatTrackingPlugin.ProtocolVersion;
    string playerMsg;
    if (serverVersion < clientVersion)
    {
      playerMsg =
        $"BoatTracking: server mod is outdated (server protocol {serverVersion}, you have {clientVersion}). Ask the host to update BoatTracking.";
    }
    else
    {
      playerMsg =
        $"BoatTracking: your mod is outdated (server protocol {serverVersion}, you have {clientVersion}). Update BoatTracking to match the server.";
    }

    BoatTrackingPlugin.Log.LogWarning(playerMsg);
    try
    {
      if (MessageHud.instance)
        MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, playerMsg);
    }
    catch (System.Exception ex)
    {
      BoatTrackingPlugin.Log.LogWarning($"Could not show protocol mismatch HUD: {ex.Message}");
    }

    try
    {
      if (Chat.instance)
        Chat.instance.AddString("BoatTracking", playerMsg, Talker.Type.Normal);
    }
    catch
    {
      // optional — HUD is enough
    }
  }
}
