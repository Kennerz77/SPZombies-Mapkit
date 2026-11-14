# SPZombies Map Kit

A Unity editor add-on for making zombies maps for **Dr K's SPZombies** (SPT 4.1.x). You build the map with the **Tushonka Mapping SDK**, then place the zombies side in the same Unity project: barricades, zombie spawns, perk machines, the mystery box, wall buys, doors, debris, zones, the power and the exfil, seeing SPZombies' real models where they'll stand. The kit writes the layout SPZombies reads, installs it into SPT to test, and makes the release zip.

Version **0.1.0**. Made by **Kenner**. MIT licence.

## Requirements

| What | Version |
|---|---|
| Unity | 2022.3.43f1 |
| [Tushonka Mapping SDK](https://sp-mod.com/mod/3193/tushonka-mapping-sdk) (its Unity project, and CustomMapLoader in SPT) | 1.9.3 or newer |
| [Dr K's SPZombies](https://github.com/Kennerz77/KennerZombiesSPT) and the mods it needs, in your SPT folder | 0.1.2 or newer |

The kit shows SPZombies' models from your own SPT install: nothing of SPZombies is copied into your project or your map.

## Getting started

1. Download the latest release and import `SPZombiesMapKit_v<version>.unitypackage` into your Tushonka Mapping SDK project.
2. Open **SPZombies > Map Kit**.
3. Follow the guide: **SPZombies > Guide** in Unity, or [SPZombiesMapKit_Guide.md](Kit/SPZombiesMapKit/SPZombiesMapKit_Guide.md) here. It covers starting from a free map, your own map or nothing, using the kit together with the SDK, every map setting, testing and releasing.

Maps made with the kit install to `BepInEx\plugins\SPZombies_Maps\ZM_<MapName>`.

## In this repository

| Path | What |
|---|---|
| `Kit/SPZombiesMapKit/` | The kit as it goes into a Unity project, with its `.meta` files (the script GUIDs must never change, or maps made with an older kit lose their markers). |
| `tools/build_package.sh` | Builds `SPZombiesMapKit_v<version>.unitypackage` into `build/` from a copy of the SDK project (`BuildProject/`, not in git). |
| `tools/selftest.sh`, `tools/KitSelfTest.cs` | The self-test: imports a real layout, builds every preview from an SPT install, exports and compares, converts test sounds, runs the check, the paths and the SDK helpers, and renders pictures. |

## Credits

- **Tushonka Mapping SDK** by **mizmii**, with **Manimal1920** (and thanks to **elgreco505**): https://sp-mod.com/mod/3193/tushonka-mapping-sdk. Every zombies map is built with it and loaded by its CustomMapLoader. Not included.
- Made by Kenner.
