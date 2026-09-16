using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoatTracking;

internal static class ShipPins
{
  private static readonly Dictionary<ZDOID, MarkerState> Markers = new();
  private static readonly Dictionary<int, Sprite?> SpriteCache = new();
  private static Sprite? _fallbackSprite;
  private static int _lastLoggedCount = -1;

  private sealed class MarkerState
  {
    public Vector3 Pos;
    public string Name = string.Empty;
    public int PrefabHash;
    public GameObject? Marker;
  }

  internal static void Clear()
  {
    foreach (var state in Markers.Values)
      DestroyMarker(state);
    Markers.Clear();
    _lastLoggedCount = -1;
  }

  internal static void Apply(IReadOnlyList<ShipSnapshot> ships)
  {
    try
    {
      ApplyInternal(ships);
    }
    catch (System.Exception ex)
    {
      BoatTrackingPlugin.Log.LogError($"ShipPins.Apply failed: {ex}");
    }
  }

  private static void ApplyInternal(IReadOnlyList<ShipSnapshot> ships)
  {
    if (!BoatTrackingPlugin.ShowPins.Value)
    {
      Clear();
      return;
    }

    var seen = new HashSet<ZDOID>();
    var controlledId = GetControlledShipId();

    foreach (var ship in ships)
    {
      seen.Add(ship.Id);

      if (!controlledId.IsNone() && ship.Id == controlledId)
      {
        RemoveTracked(ship.Id);
        continue;
      }

      if (!IsSanePosition(ship.Position))
      {
        RemoveTracked(ship.Id);
        continue;
      }

      var label = ShipCatalog.DisplayName(ship.Name, ship.TypeLabel);
      if (!Markers.TryGetValue(ship.Id, out var state))
      {
        state = new MarkerState();
        Markers[ship.Id] = state;
      }

      state.Pos = ship.Position;
      state.Name = label;
      state.PrefabHash = ship.PrefabHash;
    }

    var stale = new List<ZDOID>();
    foreach (var id in Markers.Keys)
    {
      if (!seen.Contains(id))
        stale.Add(id);
    }
    foreach (var id in stale)
      RemoveTracked(id);

    if (Markers.Count != _lastLoggedCount)
    {
      _lastLoggedCount = Markers.Count;
      BoatTrackingPlugin.Log.LogInfo($"Map markers tracked: {Markers.Count}");
      foreach (var kv in Markers)
        BoatTrackingPlugin.Log.LogInfo($"  {kv.Value.Name} @ {kv.Value.Pos}");
    }
  }

  // Called every Minimap.UpdatePins frame — draws overlays without relying on AddPin.
  internal static void UpdateDrawnMarkers()
  {
    try
    {
      if (!BoatTrackingPlugin.ShowPins.Value || !Minimap.instance || Markers.Count == 0)
      {
        if (Markers.Count == 0)
          return;
        foreach (var state in Markers.Values)
          DestroyMarker(state);
        return;
      }

      var mm = Minimap.instance;
      var largeRoot = GetField<GameObject>(mm, "m_largeRoot");
      var mapImageLarge = GetField<RawImage>(mm, "m_mapImageLarge");
      var mapImageSmall = GetField<RawImage>(mm, "m_mapImageSmall");
      var pinRootLarge = GetField<RectTransform>(mm, "m_pinRootLarge");
      var pinRootSmall = GetField<RectTransform>(mm, "m_pinRootSmall");
      var pinPrefab = GetField<GameObject>(mm, "m_pinPrefab");
      if (!largeRoot || !mapImageLarge || !mapImageSmall || !pinRootLarge || !pinRootSmall || !pinPrefab)
        return;

      var large = largeRoot.activeSelf;
      var rawImage = large ? mapImageLarge : mapImageSmall;
      var parent = large ? pinRootLarge : pinRootSmall;
      var size = large
        ? GetField<float>(mm, "m_pinSizeLarge")
        : GetField<float>(mm, "m_pinSizeSmall");

      foreach (var state in Markers.Values)
      {
        if (!IsPointVisible(state.Pos, rawImage, mm))
        {
          DestroyMarker(state);
          continue;
        }

        DrawMarker(state, size, parent, rawImage, pinPrefab, large, mm);
      }
    }
    catch (System.Exception ex)
    {
      BoatTrackingPlugin.Log.LogWarning($"UpdateDrawnMarkers: {ex.Message}");
    }
  }

  private static void DrawMarker(
    MarkerState state,
    float size,
    RectTransform parent,
    RawImage rawImage,
    GameObject pinPrefab,
    bool large,
    Minimap mm)
  {
    var go = state.Marker;
    if (!go || go.transform.parent != parent)
    {
      if (go)
        Object.Destroy(go);

      go = Object.Instantiate(pinPrefab);
      state.Marker = go;
      go.transform.SetParent(parent, false);

      var image = go.GetComponent<Image>();
      if (image)
        image.sprite = ResolveSprite(state.PrefabHash) ?? image.sprite;

      var rt = go.transform as RectTransform;
      if (rt)
      {
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size * 1.25f);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size * 1.25f);
      }
    }

    WorldToMapPoint(state.Pos, mm, out var mx, out var my);
    var anchored = MapPointToLocalGuiPos(mx, my, rawImage);
    var rect = go.transform as RectTransform;
    if (rect)
      rect.anchoredPosition = anchored;

    var checkedGo = go.transform.Find("Checked");
    if (checkedGo)
      checkedGo.gameObject.SetActive(false);

    // Own label — Valheim pin prefabs no longer expose a reliable UI.Text "Name" child
    // (TMP / CreateMapNamePin), so Find("Name") was a no-op.
    SetMarkerName(go, state.Name, large, size);
  }

  private static Font? _mapFont;
  private static bool _loggedLabelOnce;

  private static void SetMarkerName(GameObject marker, string name, bool large, float pinSize)
  {
    // Hide vanilla name child if present (may be TMP / unused).
    var vanilla = marker.transform.Find("Name");
    if (vanilla)
      vanilla.gameObject.SetActive(false);

    const string labelName = "BoatTrackingName";
    var labelTf = marker.transform.Find(labelName);
    Text? text;

    if (!labelTf)
    {
      if (!_loggedLabelOnce)
      {
        _loggedLabelOnce = true;
        var kids = new System.Text.StringBuilder("Pin prefab children:");
        foreach (Transform child in marker.transform)
        {
          kids.Append(' ').Append(child.name);
          foreach (var c in child.GetComponents<Component>())
          {
            if (c)
              kids.Append('[').Append(c.GetType().Name).Append(']');
          }
        }
        BoatTrackingPlugin.Log.LogInfo(kids.ToString());
      }

      var labelGo = new GameObject(labelName);
      labelTf = labelGo.transform;
      labelTf.SetParent(marker.transform, false);

      var rt = labelGo.AddComponent<RectTransform>();
      rt.sizeDelta = new Vector2(220f, 48f);
      rt.anchoredPosition = new Vector2(0f, -pinSize * 0.75f);
      rt.localScale = Vector3.one;

      text = labelGo.AddComponent<Text>();
      text.font = GetMapFont();
      text.fontSize = Mathf.Clamp(Mathf.RoundToInt(pinSize * 0.35f), 10, 14);
      text.fontStyle = FontStyle.Bold;
      text.alignment = TextAnchor.UpperCenter;
      text.color = new Color(1f, 0.95f, 0.75f, 1f);
      text.horizontalOverflow = HorizontalWrapMode.Overflow;
      text.verticalOverflow = VerticalWrapMode.Overflow;
      text.raycastTarget = false;
      text.supportRichText = false;

      var outline = labelGo.AddComponent<Outline>();
      outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
      outline.effectDistance = new Vector2(1.2f, -1.2f);
    }
    else
    {
      text = labelTf.GetComponent<Text>();
      var rt = labelTf as RectTransform;
      if (rt)
        rt.anchoredPosition = new Vector2(0f, -pinSize * 0.75f);
    }

    labelTf.gameObject.SetActive(large && !string.IsNullOrWhiteSpace(name));
    if (!large || text == null)
      return;

    text.text = Localization.instance != null
      ? Localization.instance.Localize(name)
      : name;
  }

  private static Font GetMapFont()
  {
    if (_mapFont)
      return _mapFont;

    try
    {
      if (Minimap.instance)
      {
        var existing = Minimap.instance.GetComponentsInChildren<Text>(true);
        foreach (var t in existing)
        {
          if (t && t.font)
          {
            _mapFont = t.font;
            return _mapFont;
          }
        }
      }
    }
    catch
    {
      // fall through
    }

    _mapFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
    if (!_mapFont)
      _mapFont = Font.CreateDynamicFontFromOSFont("Arial", 16);
    return _mapFont;
  }

  private static Sprite? ResolveSprite(int prefabHash)
  {
    if (SpriteCache.TryGetValue(prefabHash, out var cached))
      return cached ?? GetFallbackSprite();

    Sprite? sprite = null;
    try
    {
      if (ZNetScene.instance != null)
      {
        var prefab = ZNetScene.instance.GetPrefab(prefabHash);
        if (prefab)
        {
          var piece = prefab.GetComponent<Piece>();
          if (piece)
          {
            var iconField = AccessTools.Field(typeof(Piece), "m_icon");
            sprite = iconField?.GetValue(piece) as Sprite;
          }
        }
      }
    }
    catch
    {
      // ignored — fall back below
    }

    SpriteCache[prefabHash] = sprite;
    return sprite ?? GetFallbackSprite();
  }

  private static Sprite? GetFallbackSprite()
  {
    if (_fallbackSprite)
      return _fallbackSprite;
    try
    {
      var mm = Minimap.instance;
      if (!mm)
        return null;
      // Prefer Death icon — always present and high contrast on the map.
      var icons = GetField<object>(mm, "m_icons");
      if (icons is System.Collections.IList list)
      {
        foreach (var entry in list)
        {
          if (entry == null)
            continue;
          var name = entry.GetType().GetField("m_name")?.GetValue(entry)?.ToString()
                     ?? entry.GetType().GetField("m_type")?.GetValue(entry)?.ToString();
          var sprite = entry.GetType().GetField("m_icon")?.GetValue(entry) as Sprite
                       ?? entry.GetType().GetField("m_sprite")?.GetValue(entry) as Sprite;
          if (sprite == null)
            continue;
          if (name != null && (name.Contains("Death") || name.Contains("Icon0")))
          {
            _fallbackSprite = sprite;
            return _fallbackSprite;
          }
          _fallbackSprite ??= sprite;
        }
      }
    }
    catch
    {
      // ignored
    }
    return _fallbackSprite;
  }

  private static bool IsPointVisible(Vector3 p, RawImage map, Minimap mm)
  {
    WorldToMapPoint(p, mm, out var mx, out var my);
    var uv = map.uvRect;
    return mx > uv.xMin && mx < uv.xMax && my > uv.yMin && my < uv.yMax;
  }

  private static void WorldToMapPoint(Vector3 p, Minimap mm, out float mx, out float my)
  {
    var textureSize = GetField<int>(mm, "m_textureSize");
    var pixelSize = GetField<float>(mm, "m_pixelSize");
    var half = textureSize / 2;
    mx = p.x / pixelSize + half;
    my = p.z / pixelSize + half;
    mx /= textureSize;
    my /= textureSize;
  }

  private static Vector2 MapPointToLocalGuiPos(float mx, float my, RawImage img)
  {
    var uv = img.uvRect;
    var result = new Vector2(
      (mx - uv.xMin) / uv.width,
      (my - uv.yMin) / uv.height);
    var rect = img.rectTransform.rect;
    result.x *= rect.width;
    result.y *= rect.height;
    return result;
  }

  private static T? GetField<T>(object obj, string name)
  {
    var field = AccessTools.Field(obj.GetType(), name);
    if (field == null)
      return default;
    var value = field.GetValue(obj);
    if (value is T typed)
      return typed;
    if (typeof(T).IsValueType && value != null)
      return (T)value;
    return default;
  }

  private static void RemoveTracked(ZDOID id)
  {
    if (!Markers.TryGetValue(id, out var state))
      return;
    DestroyMarker(state);
    Markers.Remove(id);
  }

  private static void DestroyMarker(MarkerState state)
  {
    if (state.Marker)
    {
      Object.Destroy(state.Marker);
      state.Marker = null;
    }
  }

  private static ZDOID GetControlledShipId()
  {
    var player = Player.m_localPlayer;
    if (!player)
      return ZDOID.None;
    var ship = player.GetControlledShip();
    if (!ship)
      return ZDOID.None;
    var nview = ship.GetComponent<ZNetView>();
    if (!nview || !nview.IsValid())
      return ZDOID.None;
    var zdo = nview.GetZDO();
    if (zdo == null || !zdo.IsValid())
      return ZDOID.None;
    return zdo.m_uid;
  }

  private static bool IsSanePosition(Vector3 pos)
  {
    if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z))
      return false;
    if (float.IsInfinity(pos.x) || float.IsInfinity(pos.y) || float.IsInfinity(pos.z))
      return false;
    if (pos.sqrMagnitude < 0.01f)
      return false;
    return true;
  }
}
