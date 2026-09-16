# BoatTracking

Name your boats with hold **E**, then find them on the map from anywhere - even across the ocean, even when nobody is near them.

**This is a server + client mod.** The dedicated server and every player need the same BoatTracking install. Client-only will not sync map pins.

## Install (multiplayer / dedicated)

1. Open r2modman / Thunderstore Mod Manager → Valheim.
2. Install **BepInExPack_Valheim** if you do not already have it.
3. Online → search `DevDonkey-BoatTracking` → Install on your **client** profile.
4. Install the **same** package on your **dedicated server** profile (or copy `BoatTracking.dll` into the server `BepInEx/plugins/` folder).
5. Restart the server, then launch the game through the mod manager.

If client and server builds get out of sync, you will see an in-game warning asking you (or the host) to update.

## Install (singleplayer)

Same package via r2modman is enough - the local host acts as the server.

Manual: put `BoatTracking.dll` in `BepInEx/plugins/` (needs BepInExPack_Valheim).

## How to use

- Look at a ship and **hold E** to open rename (tap E still boards / steers as normal).
- Names show on the minimap and full world map.
- Unnamed ships still appear as Raft, Karve, Longship, or Drakkar (config can hide unnamed ones).

## Ships tracked

Raft, Karve, Longship, and Drakkar (Ashlands).

## Notes

- Does not track carts.
- Source: [github.com/MattHB1/valheim-boat-tracking](https://github.com/MattHB1/valheim-boat-tracking)
- Other mods: [DevDonkey on Thunderstore](https://thunderstore.io/c/valheim/p/DevDonkey/)
- Changelog: [CHANGELOG.md](https://github.com/MattHB1/valheim-boat-tracking/blob/main/CHANGELOG.md)
