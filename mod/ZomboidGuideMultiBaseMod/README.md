# ZomboidGuide Companion Mod (Client)

Client-side Project Zomboid companion mod for live snapshots to ZomboidGuide.

## Features
- Multiple bases per run.
- Base naming when setting a base (F8).
- Remove base at current building (F7).
- Periodic snapshots with:
  - player inventory items (always included),
  - tracked base items (last-seen snapshot),
  - simple structure index (object names).
- HTTP POST to the desktop app endpoint:
  - `http://127.0.0.1:8765/api/multi-base/scan`

## Install
1. Copy folder `Contents/mods/ZomboidGuideMultiBase` into your local PZ mods folder.
2. Enable the mod in-game.
3. Start ZomboidGuide desktop app and ensure Overlay server runs on matching port (default `8765`).

## In-game controls
- `F8`: set or rename the base for the building where the player is currently standing.
- `F7`: remove the base for the current building.

## Snapshot payload
The mod sends payloads in this shape:

```json
{
  "source": "zomboidguide-companion-mod",
  "runKey": "Sandbox::Muldraugh",
  "saveId": "Sandbox",
  "playerName": "Player",
  "baseId": "10:20:16:28",
  "baseName": "Main Base",
  "buildingId": "10:20:16:28",
  "timestampUtc": "2026-02-19T18:44:30Z",
  "playerInventoryItems": [{"fullType":"Base.BookCarpentry1","count":1,"container":"player"}],
  "baseItems": [{"fullType":"Base.ElectronicsMag1","count":1,"container":"counter"}],
  "structures": [{"type":"IsoDoor","x":10624,"y":9410,"z":0}]
}
```

## Notes
- Remote/offloaded areas are represented by last-seen snapshots (when scanned while loaded around player).
- The desktop app enforces `playerInventoryItems` presence in each snapshot for reliable compare flow.
