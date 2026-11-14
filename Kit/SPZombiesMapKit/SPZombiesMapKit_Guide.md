# SPZombies Map Kit: making a zombies map

The SPZombies Map Kit turns a map you build with the **Tushonka Mapping SDK** into a zombies map for **Dr K's SPZombies**. You place the zombies things in Unity: barricades, zombie spawns, perk machines, the mystery box, wall buys, doors and debris, zones, the power switch and the exfil. You see the real models where they'll stand. The kit then writes the layout file SPZombies reads.

- Kit version 0.1.0, for SPZombies 0.1.2 or newer on SPT 4.1.x.
- Made by Kenner. MIT licence.

## What you need

| What | Why |
|---|---|
| The **Tushonka Mapping SDK** Unity project (Unity 2022.3.43f1) | You build the map itself with it, and its CustomMapLoader loads the map in SPT. The kit goes into this project. |
| An **SPT 4.1.x** install with **Dr K's SPZombies** and the mods it needs (Pack-a-Punch, Perk Beverages, Ray Gun, Thundergun), plus the SDK's **CustomMapLoader** | The kit reads SPZombies' models, gun list and decals from it to show them in Unity. You test the map there. |

Nothing of SPZombies is copied into your project or your map. The models in the Scene view come straight from your install.

## Installing the kit

1. In your SDK project: **Assets > Import Package > Custom Package...**, pick `SPZombiesMapKit_v0.1.0.unitypackage`, and import everything.
2. An **SPZombies** menu appears. Open **SPZombies > Map Kit**.
3. **Setup** tab: check the SPT folder. The kit fills it in from your Map Definition's SPT Root; otherwise press **Browse...** and pick the folder with `BepInEx` and `SPT_Runtime`. "SPZombies", "Perk Beverages" and "Pack-a-Punch" should say *found*.

## Where your map comes from

Every zombies map starts in the **SDK's Unity project**, whatever it began as. From there the steps are the same.

| You have | Start here |
|---|---|
| A free or bought map (the Unity Asset Store or elsewhere) | Check its licence first: it must let you use it in something you give away, built into a game's files (the map's bundles). Credit its maker in your release. Import it into the SDK project, then go through the checklist below. |
| Your own map, made in another Unity project | In that project, **Assets > Export Package...** with its scenes and *Include dependencies* ticked. Import that package into the SDK project, then go through the checklist below. |
| Nothing yet | Build it straight in the SDK project, keeping the checklist below in mind as you go. |

### Making a map fit the game

Go through this before anything else. A map that fails one of these won't load right, or zombies won't move on it.

- **Shaders:** the game uses Unity's built-in render pipeline. Materials made for URP or HDRP show pink or black: switch them to the **Standard** shader. The SDK's **Quick Fixes** (**Fix Shiny Materials**, **Fix Night Lighting**) sort out maps that look too bright or shiny in the game.
- **Scripts:** the map's own scripts don't run in the game. Doors, lifts or triggers that work through code won't work. Use the kit's doors and debris for anything players buy open, and remove the rest.
- **Colliders and layers:** everything players walk on or bump into needs a collider on the right layer. Walls and floors go on the `Terrain` layer with a `BallisticCollider` (a collider left on `Default` is ignored by bullets and bots). Without colliders, players fall through and the navmesh has nothing to bake on. The SDK's tools do this for you: select the map's objects and use its **Tools > Geometry > Convert Selected Model**, **Make Selected Static Geometry** and **Add Low + High Poly Colliders To Selected**, then **Quick Fixes > Apply Tags, Layers and Physics**.
- **Time of day:** a map with no time of day renders black in the game. **Build Map** adds the SDK's simple one if there's none. For the kit's **Freeze the weather** to do anything, pick the SDK's **full weather system** instead (its **Generation > Time > Time of Day / Weather** window).
- **Scale:** 1 Unity unit is 1 metre. A doorway is about 2 m tall, a window about 1.4 m wide.
- **Size:** keep the play area compact. The navmesh has to stay within about 2.4 km² and 150 m from bottom to top.
- **Window openings:** zombies climb in through real holes in the walls. For a clean vault, the sill is **0.75-1.0 m** above the outside ground and the wall **at most 0.41 m** deep. The kit's Check tab measures each one later.
- **Leftovers:** cameras, players, game managers and UI from a demo scene aren't needed. Delete them.

Then follow **The kit and the Tushonka Mapping SDK together** and **Making a zombies map, step by step** below.

## The kit and the Tushonka Mapping SDK together

Zombies maps stand on the shoulders of the **Tushonka Mapping SDK** by **mizmii**, with **Manimal1920** (thanks also to **elgreco505**). It's what makes custom maps possible in SPT at all: its Unity tools build the map, and its CustomMapLoader loads it in the game. Get it, and say thanks, at https://sp-mod.com/mod/3193/tushonka-mapping-sdk. The kit is built to sit next to it, not replace it.

You make one map with two tools, in the same Unity project. The **SDK** makes the map itself: everything a normal custom map needs to load and play in SPT. The **kit** adds the zombies side on top. Neither replaces the other.

### Getting started with the SDK

The SDK comes with its own instructions: its `README.md`, and a **Help** menu inside Unity (in the SDK's menu in the menu bar) with how-tos by topic, troubleshooting, and what every Build Map message means. In short:

1. Install CustomMapLoader (client and server) into your SPT, and open the SDK's Unity project in **Unity 2022.3.43f1**. It sets up the game's layers, tags, physics and navmesh settings when it opens.
2. **Map Settings > New Map...** (in the SDK's menu) makes your map's folder with a scene and a **Map Definition**. Give it an id starting `zm_`.
3. **Edit Server Data > Create From sdk_example Template** makes the map's server data with your id.
4. Select the Map Definition, tick **Deploy To Game**, check **Spt Root**, and press **Build Map**. Start SPT: the map is on the map screen.

Once that works, add the kit (see **Installing the kit**) and carry on below.

### What to use from each


| Job | Tool |
|---|---|
| The map itself: ground, buildings, props, lighting | SDK (and normal Unity work) |
| The map id, its scenes and its Map Definition | SDK |
| Baking the navmesh (where zombies can walk) | SDK. The kit reads it for the Paths tab and the check. |
| Player spawns, the bot zone, the AI doors holder | SDK parts. The kit's **Setup** tab can add them for you. |
| Server data (`base.json`, `locale.json`) | SDK's **Edit Server Data**. The kit's **Setup** tab makes it zombies-ready. |
| Building the map (its bundles) | SDK's **Build Map** (**Deploy To Game** to test, **Create Package** to release) |
| Barricades, zombie spawns, perks, the box, wall buys, doors, debris, zones, power, exfil | Kit (**Place** tab) |
| Time, weather, power, perk limit and prices, starting gun, sounds, pictures, tips | Kit (**Map** tab) |
| Zombie routes and the map check | Kit (**Paths** and **Check** tabs) |
| The zombies layout file | Kit (**Export** tab) |
| Testing in SPT | SDK **Deploy To Game**, then kit **Install into SPT** |
| The release zip | SDK **Create Package**, then kit **Make release zip** (both go in one zip) |

**The usual loop:**

1. **SDK:** build your rooms, bake the navmesh, then **Build Map** with **Deploy To Game** ticked.
2. **Kit:** place the zombies parts, then look at the **Paths** and **Check** tabs.
3. **Kit:** **Install into SPT (test it)**. Restart the SPT server and the game, and play it.
4. Change things and go round again:
   - You moved walls, openings or floors: bake the navmesh again (SDK), work the paths out again (kit), and **Build Map** again (SDK).
   - You only moved zombies parts or changed map settings: **Install into SPT** again (kit). No new SDK build is needed: the kit's markers aren't part of the map build, SPZombies reads them from the layout file.
   - You added a custom look or used **Turn Selected Into**: that changes the map scene, so save it and **Build Map** again (SDK).

**Where each tool's files go in SPT** (they find each other by the map id):

- SDK: `BepInEx\plugins\CustomMapLoader\bundles\maps` (the map's bundles) and `SPT_Runtime\user\mods\CustomMapLoader\maps\<mapid>` (its server data).
- Kit: `BepInEx\plugins\SPZombies_Maps\ZM_<name>` (the zombies layout, sounds, pictures and tips).

**From the SDK, for a zombies map:**

- Use: the geometry tools, the time of day or full weather, **Ambient Sounds**, **Spatial Audio**, indoor triggers, lamps and lights, fog volumes, decoration, and the **Help** menu. They all work in a zombies match.
- Needed: a bot zone, spawn points and an AI doors holder. The kit's **Setup** tab adds them with the SDK's own parts.
- Leave out: the SDK's own doors and switches between play areas (players would open them for free, and zombies walk through): use the kit's doors and debris there. Its extracts, bot spawns, loot and world events aren't used either: SPZombies runs its own exfil and zombies, the zombies server settings turn bots off, and zombies matches have no loot.

**The kit never changes the SDK.** It doesn't edit, copy or ship any SDK file. Its markers are editor-only, so **Build Map** leaves them out. The only things it adds to your map build are the SDK's own spawn points, bot zone and AI doors holder (when you press those **Setup** buttons), and copies of your custom looks under `SPZombies Custom Looks`.

## Making a zombies map, step by step

### 1. Build the map with the SDK

Make your map the SDK's usual way: a new map, your scenes and a Map Definition. Give it an id starting with `zm_` (for example `zm_asylum`). The server then knows it's a zombies map even before it reads the layout.

Keep the play area compact. Zombies maps are about holding rooms, not roaming.

### 2. Add the map settings

**Map** tab > **Add map settings**. This puts an editor-only object called `SPZombies` in the active scene. All the kit's markers live under it.

Hover over any setting in the Map tab for a fuller explanation.

| Setting | What it does |
|---|---|
| Map id | Must be the Map Definition's Map Id (the button next to it copies it). |
| Map name | Names the map's folder: `BepInEx\plugins\SPZombies_Maps\ZM_<name>`. |
| Map screen spot | Where your map's icon appears on the map screen for solo players: on the spot of one of the normal maps (that map is hidden in zombies mode). Stronghold uses `bigmap`, so pick another one. "Its own place" uses the icon spot from your map's server settings. |
| Map needs power | On: players must turn the power switch on before perk machines, Pack-a-Punch, power lights and power doors work (place a power switch). Off: everything works from the start. |
| Perks a player can hold | How many perks one player can have at once (4 is normal). |
| Starting gun | The gun players start and respawn with, with four mags in reserve. The M1911 is the normal one. |
| Custom perk prices | Your own price for each perk. Quick Revive's price is its co-op price: solo it always costs 500, then 1500, then 2500. If you changed prices after adding starter tips, check the tips: the Map tab and the Check tab flag tips with an old price. |
| Freeze the time of day / Time of day | On: the clock stays at that hour for the whole match. Off: the raid's own clock runs. |
| Moonlit nights | A bright moonlit night: players can see at night. |
| Freeze the weather | On: clouds, rain, fog and wind stay at the sliders' values for the whole match. Off: the raid's own weather runs. Needs the SDK's full weather system in the map (its **Time of Day / Weather** window); with the simple time of day there is no weather to freeze. |
| Loading screen music, Map ambience, Easter egg song | Your own sounds. **Choose file...** takes a .wav, .mp3, .ogg or .aiff from anywhere on your PC, copies it into `Assets/SPZombiesMapData/<map id>` and makes it a 16-bit PCM .wav (what SPZombies plays) when it isn't one. A sound dragged in that SPZombies can't play gets a **Convert to 16-bit .wav** button. |
| Map screen picture, Loading screen picture | **Choose file...** takes a .png (or .jpg for the loading picture) from anywhere. 16:9 pictures fit best: 1920 x 1080 looks sharp on the loading screen. |
| Loading tips | One tip per line, shown while the map loads, mixed in with SPZombies' own general tips. **Add starter tips** adds ready-made tips about what's on your map (its perks and prices, wall buys, the power, box spots, 115 rocks, the starting gun). Place your things first, then edit the tips as you like. |

Maps made with this kit need **SPZombies 0.1.2 or newer**: older versions don't look in the `SPZombies_Maps` folder.

### 3. Player spawns and the raid's must-haves

**Setup** tab:

- **Add player spawns 1-4 where the Scene view looks** makes the SDK spawn points `Spawn_Player_1` to `Spawn_Player_4` (and a bot zone if the map has none: the raid needs one). Move each to its spot. Player *N* starts at `Spawn_Player_N`, and a solo player starts at `Spawn_Player_1`.
- **Add an AI doors holder**: without one, the popular Waypoints mod stops the raid from starting.

### 4. Place the zombies things

**Place** tab: pick a tab and a picture, look at the spot in the Scene view, then press **Place where the Scene view looks** (or double-click the picture). The thing lands where the Scene view's centre points, facing you. Wall buys and decals go flat on the wall you're looking at. Then move, turn and scale it like any object.

- **Arrows** show which way a thing faces. Yellow is the side players use (machines, box, wall buys, doors). Green is the way a spawn faces. **Red points into the building** at a barricade: zombies come from the window's front.
- **Scale = size in metres** for windows, doors, debris, zones, blockers and exfil areas. The Inspector also has a *Size* field.
- The **Inspector** of a marker has the choices for its kind (which perk, which gun, which look), its price, its zone, and buttons for **Drop to ground** and **Face the camera**.

| Thing | How to use it |
|---|---|
| **Zombie spawn** | Where zombies come up out of the ground. Put them outside, where they can walk to a window. Spread several round the map. |
| **Barricade (window)** | Boards zombies tear down and climb through. Cut a real opening in your wall and put the barricade in it, with the red arrow pointing inside. Players can't pass it, but zombies climb in. For a clean vault the sill should be **0.75-1.0 m** above the outside ground and the wall **at most 0.41 m deep**. Deeper walls make zombies climb onto the sill and step down; too high or too low makes them use a scripted climb. The Check tab measures every window. |
| **Door / Debris** | Bought with points (Price), or opened by the power (tick *Opens with the power*). Pick one of SPZombies' looks, or make one of your map's own objects into it: select the object, then **SPZombies > Turn Selected Into > Buyable Door / Buyable Debris** (or right-click it in the Hierarchy > SPZombies). The kit puts the marker exactly on it, sized to it, and keeps the original out of the map build. In a match SPZombies puts its copy there and takes it away when it's bought. **Put the original back** in the marker's Inspector undoes it. Bake the navmesh again afterwards so the way through is walkable. |
| **Zone** (box or outline) | Areas of the map. Zones **1 and 2 are open from the start**. Any other zone opens when a door or debris **touching it (within 2.5 m)** is bought. Zombie spawns in a closed zone wait until it opens. A map without zones has every spawn working from round 1. |
| **Perk machine** | One of: Jugger-Nog, Speed Cola, Double Tap, Deadshot, Stamin-Up, Quick Revive, Mule Kick, Widow's Wine, PhD Flopper. Needs the power. |
| **Pack-a-Punch** | Needs the power. |
| **Mystery box spot** | Where the box can be. Place several so it can move. Tick *Box starts here* on one to always start there. |
| **Wall buy** | A gun (or grenades) bought off the wall. Its chalk outline shows until the first buy. Put the marker on the wall's face. |
| **Power switch** | Thrown once, the power is on. Needed unless the map is always powered. |
| **Power light** | A light that comes on with the power. Select a light fixture of your map and use **SPZombies > Turn Selected Into > Power Light**: the fixture lights up when the power comes on. |
| **Exfil lever, hold area, exit zone** | From round 15 a player throws the lever. The team defends the hold area until the timer runs out, then everyone in the exit zone gets out. |
| **Blocker / Play area wall** | Invisible walls (bullets pass): seal gaps, keep players in. |
| **115 rock** | Easter egg rocks: find them all and the easter egg song plays. Lore rocks only glow and hum. |
| **Wall decal** | Blood, scrawl and chalk on walls. |
| **Lightning** | Crackling arcs (crackle) or big strikes (storm). |
| **Game over shot** | Camera shots for the game over screen. The shot is placed where the Scene view camera is, looking where it looks. SPZombies moves the camera itself: each shot lasts 7 s, drifting 2.5 m sideways (the red line) while it looks at the first thing ahead (the yellow point). Press **Play it** in its Inspector to watch it in the Scene view. With none placed, SPZombies picks its own shots. |
| **Nav link** | A walk-across link where the navmesh has a gap, or a one-way drop through a floor hole. |

### 5. Bake the navmesh

Zombies walk on the map's navmesh. Bake it with the SDK after the map's shape is done. Zombie spawns, the outside of every window and the floor inside must all be on it.

### 6. See the zombie paths

**Paths** tab > **Work out the zombie paths**. The kit works out how zombies will move, the same way SPZombies decides it in a match. It draws the result in the Scene view and lists it as sentences in the tab.

- **Each zombie spawn's route** to the barricade it feeds (the nearest within 35 m whose outside faces it), with arrows:
  - **green**: they walk there;
  - **orange**: the route stops short, at the **X** (a gap in the navmesh);
  - **red**: no route at all, or the spawn is off the navmesh;
  - **blue**: no barricade faces the spawn, so its zombies start inside and go straight for the players.
- **At each barricade**:
  - a big dot where zombies stand to tear the boards (red if there's no navmesh there);
  - a small cyan dot where they land inside;
  - a bar along the sill, coloured by the vault check;
  - how many spawns feed it. "no zombies come here" means no spawn feeds it.
- **The walk areas**, tinted on the floor with every door, debris pile and barricade shut (as at the start of a match):
  - orange: the zombie side;
  - green: the player side;
  - grey: shut;
  - blue: reached by nobody (navmesh no one can get to).
- **Leaks**, as red dashes: a way zombies can walk in to the players without a barricade, door or debris. Close the gap, or put a barricade, door, debris or blocker in it.
- A **legend** with the counts sits in the Scene view's bottom left.
- **Select** a spawn or a barricade to bring its own routes forward.
- **Only problems** hides the good routes.
- **Update as I move things** works the paths out again a moment after you move a marker. Work them out again yourself after each navmesh bake.

The Check tab includes the same results.

### 7. Check the map

**Check** tab > **Check map**. Errors break the map, warnings are worth a look, and notes are for information. **Select** shows the thing in the Scene view, and **Fix** fixes it where the kit can. It checks player spawns, the bot zone, the AI doors holder, zones, the navmesh, window sills, the power, perks, guns, the exfil, things floating over the ground, your sound files and the server settings.

### 8. Server settings

The SDK's **Edit Server Data** makes the map's `base.json`. A zombies map wants no bots or waves of its own, a long raid timer and the old spawn system. **Setup > Make the server settings zombies-ready** (or the Fix button in the check) sets them. A backup of the old file is kept beside it.

### 9. Test it

1. Build the map with the SDK (**Deploy To Game** ticked on the Map Definition).
2. **Export** tab > **Install into SPT (test it)**.
3. Restart the SPT server and the game. Switch the main menu to zombies mode, press **Solo**, and pick your map.

### 10. Release it

1. Build the map with **Create Package** ticked on the Map Definition.
2. **Export** tab > **Make release zip**. You get one zip with the map (bundles and server files), the zombies layout and a read-me. Players extract it into their SPT folder.
3. In your release, list SPZombies and CustomMapLoader as requirements.

## How it works

- **Markers are editor only.** Every marker is tagged `EditorOnly`, so it never goes into your map build. The models you see are hidden previews made from your SPZombies install. They aren't saved in the scene.
- **Export** writes `BepInEx\plugins\SPZombies_Maps\ZM_<name>\<mapid>zombies.json`, plus your sounds, pictures, tips, nav links and game over shots beside it, into the project's `SPZombiesExport` folder. SPZombies (0.1.2 and newer) ships the `SPZombies_Maps` folder and finds every map in it whose layout is marked as a zombies map. Nothing else needs registering.
- **Custom looks**: on export the kit keeps a copy of each custom look under `SPZombies Custom Looks`, far under the map. SPZombies copies the look from there in a match, so keep that object and save the scene and build the map after the first export with a new look.
- **Import a layout file** (Export tab) reads a layout back in as markers: to move a layout between projects, or to start from another map's.

## Troubleshooting

| Problem | Fix |
|---|---|
| The raid doesn't start | Check tab: spawn points, bot zone and AI doors holder. The game's log is in `Logs`. |
| The previews are only coloured boxes | Set the SPT folder (Setup), then **Reload models**. Perk machines need Perk Beverages, and Pack-a-Punch needs the Pack-a-Punch mod. |
| Zombies stand outside and never come in | The window's outside isn't on the navmesh, or its arrow points the wrong way. Look at the Paths tab: a red dot at the window, or an orange or red route. |
| Zombies walk in without breaking a barricade | The Paths tab shows a leak (red dashes): close that gap. |
| Zombies float over a window | The sill is too deep or too high to vault (see the window rules above). |
| A sound is silent | It must be a 16-bit PCM .wav file: pick it again with **Choose file...**, or press **Convert to 16-bit .wav**. |
| The map is black in the game | It has no time of day. Build Map adds the SDK's simple one; or pick one in the SDK's **Time of Day / Weather** window. |
| The weather sliders do nothing | The map uses the SDK's simple time of day. Switch to its full weather system. |
| Anything about building or loading the map itself | The SDK's **Help** menu and its README cover every Build Map message. |
| The map isn't on the map screen in zombies mode | Install it again (Export tab) and restart the server and the game. Check the map id matches the Map Definition. |
| After updating SPZombies the models are old | Setup > **Reload models**. |

## Credits

- **Tushonka Mapping SDK** by **mizmii**, with **Manimal1920** (and thanks to **elgreco505**): https://sp-mod.com/mod/3193/tushonka-mapping-sdk. Every zombies map is built with it and loaded by its CustomMapLoader: none of this works without their work. Not included: get it from their page.
- Made by Kenner.
