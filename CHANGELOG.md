# Changelog

## 2.1.0
- Golems are kept flagged asleep so they stop making their periodic creature noises, which quiets them for players without the mod too (`SilenceIdleSounds`).
- Players running the mod also have the golem's own audio switched off locally (`MuteSounds`).
- Renaming: Shift+E opens the same dialog tamed animals use, and each golem keeps its own name.
- Dismiss moved to holding E, since Shift+E now renames.

## 2.0.0
- Modded players: E rests or wakes a golem.
- **Rewritten to be server-authoritative.** Only the server (and players who want to build golems) need the mod; everyone else sees and interacts with golems in a vanilla game.
- The golem is now a vanilla ghost puppeted by the server. It flies straight to fires and chests through walls and returns above its build spot when idle.
- Refuels are confirmed before carried fuel is spent; chest edits happen only while the server holds the chest.
- Recipe: 2 Surtling core, 30 Bone fragments, 5 Ectoplasm at a Forge.
- Removed the Jötunn dependency and config sync (settings live on the server).
- Golems placed with 1.x no longer appear, since their custom object no longer exists. Dismantle them with 1.x first to get their materials back.

## 1.1.0
- Server and all clients must have the mod (enforced by Jötunn, major.minor version must match).
- Gameplay settings are synced from the server; only admins can change them in game.
- Fuel types are detected automatically (`FuelItems = Auto`), including guck for green torches and coal for braziers.
- Production stations (furnaces, kilns, eitr refinery, windmills, etc.) are always excluded.
- Only player-built fires and chests are used.
- Golem returns fuel no fire in range uses anymore to a chest.
- Recipe: 2 Surtling core, 5 Greydwarf eye, 1 Ectoplasm at a Forge. Work radius 50 m.

## 1.0.0
- Initial release.
