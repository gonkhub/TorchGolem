# Torch Golem

A Valheim mod that adds a **Torch Golem**: a friendly ghost that takes fuel from nearby chests and drifts around your base keeping torches, sconces, braziers, campfires and hearths lit.

**Server-side first.** The golem is a vanilla ghost run entirely by the server, so players who don't install the mod still see it working and enjoy lit torches. Only players who want to *build* golems need the mod.

## How it works

- The server owns every golem and moves it itself, so no player's game ever runs its AI.
- It flies in a straight line to each fire or chest, through walls, and floats back above the spot it was built whenever it has nothing to do.
- Refuelling uses the same network message a player's own refuel sends, and carried fuel is only spent once the fire's fuel is seen to rise.
- Chests are edited by the server only while it holds ownership of them, and never while someone has them open.

## Features

- Built with the Hammer (Misc tab) near a **Forge**: 2 Surtling core, 30 Bone fragments, 5 Ectoplasm.
- Refuels any player-built fire within 50 m of where it was built, once it drops to half fuel.
- Carries up to a stack of each fuel type that a fire in range actually uses, and returns fuel nothing needs anymore. Fuel types are detected automatically (wood, resin, greydwarf eyes, guck, coal, and modded fires).
- **Never** touches production stations: furnaces, kilns, blast furnaces, eitr refineries, spinning wheels, windmills, cooking stations, etc.
- Respects private chests.
- Modded players: **E** rests/wakes it, **Shift+E** renames it (the same dialog tamed animals use), and **holding E** dismisses it, returning its build cost and carried fuel (owner or admin).
- Its creature sounds are silenced for players running the mod (`MuteSounds`). Sounds play locally in each game, so players without the mod still hear the ghost.
- Golems can't be hurt, and players' tames and turrets leave them alone.

## Installation

Requires [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/). Copy `TorchGolem.dll` into `BepInEx/plugins`.

| Who | Needs the mod? |
|---|---|
| Server | **Yes.** All golem behaviour runs here. |
| Players who build golems | Yes |
| Everyone else | No |

A modded player on a server without the mod simply doesn't get the hammer piece.

## Configuration

`BepInEx/config/gonkhub.torchgolem.cfg` on the server. Recipe and crafting station are sent to modded players when they join.

| Section | Setting | Default | |
|---|---|---|---|
| Behaviour | `WorkRadius` | 50 | Range from the golem's build spot |
| Behaviour | `RefuelBelowPercent` | 0.5 | Refuel once a fire is this empty |
| Behaviour | `CarryLimit` | 0 | Max per fuel type (0 = one stack) |
| Behaviour | `MoveSpeed` | 3 | Flying speed |
| Behaviour | `HoverHeight` | 1.2 | Float height above home, fires and chests |
| Behaviour | `MaxGolemsPerPlayer` | 3 | 0 = unlimited |
| Fuel | `FuelItems` | Auto | `Auto` or a list like `Wood,Resin,Guck` |
| Fuel | `ExcludedFuel` | | e.g. `Coal` to keep it for smelting |
| Fuel | `ExcludedPieces` | | e.g. `piece_bathtub` |
| Appearance | `GolemPrefab` | Ghost | Must be a vanilla creature |
| Appearance | `GolemName` | Torch Golem | Starting name; rename in game with Shift+E |
| Client | `MuteSounds` | true | Silence the golem in your own game |
| Building | `Recipe` | `SurtlingCore:2,BoneFragments:30,Ectoplasm:5` | |
| Building | `CraftingStation` | forge | |

## Building from source

Requires the .NET SDK and Valheim with BepInEx installed.

```
dotnet build -c Release
```

If Valheim isn't in the default Steam location, pass `-p:ValheimDir="D:\path\to\Valheim"`.

### Dev profile

`tools/setup-dev-profile.ps1` creates a **TorchGolem Dev** r2modman profile with Server_devcommands, Infinity_Hammer, ConfigurationManager and UnityExplorer. Once it exists, every build copies the DLL into it automatically. `tools/launch-dev.ps1` builds, deploys and launches Valheim on that profile.

`tools/package.ps1` builds the Thunderstore zip into `dist/`.
