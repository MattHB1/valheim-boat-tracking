# BoatTracking

Name your Valheim ships with hold E and see them on the world map from anywhere. Works on dedicated servers (mod on **server + clients**).

## Install

1. Open [r2modman](https://r2modman.com/) (or Thunderstore Mod Manager) → Valheim → your profile.
2. Online → search `DevDonkey-BoatTracking`, or install from the package page once published.
3. For multiplayer: install the same mod on the dedicated server.
4. Launch through the mod manager.

Manual install: put `BoatTracking.dll` in `BepInEx/plugins/` (requires [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)).

## Use

- **Hold E** while looking at a ship → rename it.
- Tap E still boards / steers.
- Ships show on the minimap and full map (custom name, or Raft / Karve / Longship / Drakkar if unnamed).

## Notes

- Server syncs ship positions so pins work even when the boat is unloaded.
- Does not track carts (yet).
- Other mods: [DevDonkey on Thunderstore](https://thunderstore.io/c/valheim/p/DevDonkey/)

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## Build

Requires a .NET SDK with the net4.8 targeting pack, plus publicized Valheim / BepInEx reference assemblies (see HintPaths in `BoatTracking/BoatTracking.csproj`).

```powershell
.\scripts\build-deploy.ps1
.\scripts\create-release.ps1
```

`build-deploy.ps1` defaults to the r2modman `testing` profile. Use `-Profile Default` for multiplayer testing after singleplayer signoff.
