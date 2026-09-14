# Torch Golem

A Valheim mod that adds a buildable **Torch Golem**: a little friendly helper that takes fuel from nearby chests and wanders your base keeping torches, sconces, braziers, campfires and hearths topped up.

## Features

- Built with the Hammer (Misc tab) near a **Forge**: 2 Surtling core, 5 Greydwarf eye, 1 Ectoplasm.
- Refuels any player-built fire within 50 m of where it was placed, once it drops to half fuel.
- Carries up to a stack of each fuel type, restocking from nearby chests. Fuel types are detected automatically (wood, resin, greydwarf eyes, guck, coal, and fires added by other mods).
- **Never** touches production stations: furnaces, kilns, blast furnaces, eitr refineries, spinning wheels, windmills, cooking stations, etc.
- Respects private chests, and skips chests another player may have open.
- Walks using Valheim's own navmesh pathfinding; hops next to its target if it gets stuck.
- Hover to see what it's carrying; press **E** to toggle rest/work.
- Deconstructing it returns its build cost plus any fuel it was carrying.

## Installation

Requires [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/). Copy `TorchGolem.dll` into `BepInEx/plugins`. In multiplayer, every player needs the mod.

## Configuration

`BepInEx/config/jtboyd.torchgolem.cfg`, or in game with [ConfigurationManager](https://thunderstore.io/c/valheim/p/Azumatt/Official_BepInEx_ConfigurationManager/) (F1).

| Section | Setting | Default | |
|---|---|---|---|
| Behaviour | `WorkRadius` | 50 | Range from the golem's build spot |
| Behaviour | `RefuelBelowPercent` | 0.5 | Refuel once a fire is this empty |
| Behaviour | `CarryLimit` | 0 | Max per fuel type (0 = one stack) |
| Behaviour | `MoveSpeed` | 2.2 | Walking speed |
| Fuel | `FuelItems` | Auto | `Auto` or a list like `Wood,Resin,Guck` |
| Fuel | `ExcludedFuel` | | e.g. `Coal` to keep it for smelting |
| Fuel | `ExcludedPieces` | | e.g. `piece_bathtub` |
| Building | `Recipe` | `SurtlingCore:2,GreydwarfEye:5,Ectoplasm:1` | |
| Building | `CraftingStation` | forge | |
| Visual | `VisualPrefab` | Greyling | Model the golem borrows |

## Building from source

Requires the .NET SDK and Valheim with BepInEx installed.

```
dotnet build -c Release
```

If Valheim isn't in the default Steam location, pass `-p:ValheimDir="D:\path\to\Valheim"`.

### Dev profile

`tools/setup-dev-profile.ps1` creates a **TorchGolem Dev** r2modman profile with Server_devcommands, Infinity_Hammer, ConfigurationManager and UnityExplorer. Once it exists, every build copies the DLL into it automatically.

`tools/launch-dev.ps1` builds, deploys and launches Valheim on that profile with the console enabled.
