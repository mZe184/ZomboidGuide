# Companion Sync (Desktop + Mod)

This repository now contains:

1. Desktop app support in `ZomboidGuide`:
- New HTTP endpoint: `POST /api/multi-base/scan`.
- Multi-base snapshot persistence in app state.
- Mandatory player inventory payload requirement for snapshot ingest.
- Merge logic: inventory + tracked bases are compared against books/magazines during session sync.
- Settings UI for tracked bases:
  - endpoint URL display,
  - snapshot status,
  - rename selected base,
  - clear active-run bases.

2. Mod package in this repository:
- Path: `mod/ZomboidGuideMultiBaseMod/Contents/mods/ZomboidGuideMultiBase`
- Includes:
  - `mod.info`
  - `media/lua/client/ZomboidGuideMultiBase.lua`
- See `mod/ZomboidGuideMultiBaseMod/README.md` for setup and controls.

## API Endpoint

`POST http://<host>:<port>/api/multi-base/scan`

Required:
- `baseId`
- `playerInventoryItems` (can be empty array, but must exist)

Response:
- `200` on accepted snapshot
- `400` on invalid payload
- `405` if method is not POST

## Validation

1. Endpoint smoke test (without game):
- Start app and overlay server.
- Run:
  - `powershell -ExecutionPolicy Bypass -File .\deployment\Test-MultiBaseEndpoint.ps1`
- Expect: HTTP `200`, connection badge turns green.

2. Negative contract test:
- Run:
  - `powershell -ExecutionPolicy Bypass -File .\deployment\Test-MultiBaseEndpoint.ps1 -NegativePayload`
- Expect: HTTP `400` because `playerInventoryItems` is mandatory.

3. In-game E2E:
- Install mod from `mod/ZomboidGuideMultiBaseMod`.
- Press `F8` in a building, set a base name.
- Verify in app settings:
  - connection badge green,
  - last POST timestamp updates,
  - tracked base appears with name and item/structure counts.
