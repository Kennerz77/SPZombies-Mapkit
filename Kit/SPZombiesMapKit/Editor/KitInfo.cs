using System.Collections.Generic;
using UnityEngine;

namespace SPZombies.MapKit.Editor
{
    /// <summary>What the kit knows about each kind of thing: names, colours, default sizes and prices, and the choices.</summary>
    internal static class KitInfo
    {
        public const string KitVersion = "0.1.0";

        // The perks SPZombies has machines for (Dr K's Perk Beverages)
        public static readonly string[] PerkIds = { "jugg", "speed", "dtap", "deadshot", "stamin", "revive", "mule", "widow", "phd" };

        public static string PerkName(string id)
        {
            switch (id)
            {
                case "jugg": return "Jugger-Nog";
                case "speed": return "Speed Cola";
                case "dtap": return "Double Tap";
                case "deadshot": return "Deadshot";
                case "stamin": return "Stamin-Up";
                case "revive": return "Quick Revive";
                case "mule": return "Mule Kick";
                case "widow": return "Widow's Wine";
                case "phd": return "PhD Flopper";
                default: return id;
            }
        }

        /// <summary>SPZombies' own price for a perk (Quick Revive: its co-op price; solo it's 500, then 1500, then 2500).</summary>
        public static int PerkDefaultPrice(string id)
        {
            switch (id)
            {
                case "jugg": return 2500;
                case "speed": return 3000;
                case "dtap": return 2000;
                case "deadshot": return 1500;
                case "stamin": return 2000;
                case "revive": return 1500;
                case "mule": return 4000;
                case "widow": return 4000;
                case "phd": return 2000;
                default: return 0;
            }
        }

        /// <summary>A perk's full name as players see it in a match (for tips).</summary>
        public static string PerkGameName(string id)
        {
            switch (id)
            {
                case "dtap": return "Double Tap Root Beer";
                case "deadshot": return "Deadshot Daiquiri";
                case "phd": return "PHD Flopper";
                default: return PerkName(id);
            }
        }

        /// <summary>The gun players start with when the map doesn't choose one.</summary>
        public const string DefaultStartGun = "m1911";

        /// <summary>The SPZombies version that reads SPZombies_ map folders, starting guns and perk prices.</summary>
        public static readonly System.Version CoreNeeded = new System.Version(0, 1, 2);

        // The door and debris looks the kit offers (the user's pick, 2026-11-13)
        public static readonly string[] DoorLooks = { "metal_rusted", "kino_ornate", "kino_wood", "wood_church" };
        public static readonly string[] DebrisLooks = { "concrete_rubble", "mud_rocks", "gorod_rubble", "trash_pile" };

        // Every look SPZombies has (a layout using another one still works; the kit just doesn't offer it)
        public static readonly string[] AllDoorLooks = { "metal_rusted", "wood_church", "cellar", "kino_wood", "kino_ornate", "bunker", "barbed_gate", "metal_rustic", "sandbags", "bookcase" };
        public static readonly string[] AllDebrisLooks = { "concrete_rubble", "wood_planks", "barbed_wire", "brick_pile", "junk_pile", "cave_rocks", "concrete_pile", "mud_rocks", "burnt_wagon", "church_bricks", "mud_sandbags", "burnt_wood", "trash_pile", "plank_heap", "gorod_rubble", "barbed_wire_2", "kino_barbed_wire", "corrugated_metal" };
        public static readonly string[] RockLooks = { "easter_egg", "lore" };
        public static readonly string[] LightningLooks = { "crackle", "storm" };
        public static readonly string[] DecalLooks = { "blood_hand_01", "blood_hand_03", "blood_hand_05", "blood_drips_01", "blood_drips_02", "blood_drips_03", "blood_splatter_02", "blood_splatter_03", "blood_smear_02", "blood_spray_01", "wall_scrawl", "chalk_wishing", "chalk_follow_steps", "scratch_marks", "decal_115_splatter" };

        public const string GrenadesWallId = "m67_grenades";

        // Map ids whose place on the map screen a zombies map can take
        public static readonly string[] MapSelectSpots = { "", "bigmap", "woods", "shoreline", "interchange", "rezervbase", "factory4_day", "laboratory", "lighthouse", "sandbox" };
        public static readonly string[] MapSelectLabels = { "Its own place (from the map's server settings)", "bigmap (Stronghold uses this one)", "woods", "shoreline", "interchange", "rezervbase", "factory4_day", "laboratory", "lighthouse", "sandbox" };

        public static string Nice(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            var parts = s.Split('_');
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length > 0)
                    parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            return string.Join(" ", parts);
        }

        public static string KindName(ZombiesKind k)
        {
            switch (k)
            {
                case ZombiesKind.PlayerStart: return "Player start";
                case ZombiesKind.ZombieSpawn: return "Zombie spawn";
                case ZombiesKind.Window: return "Barricade (window)";
                case ZombiesKind.BoxSpot: return "Mystery box spot";
                case ZombiesKind.PerkMachine: return "Perk machine";
                case ZombiesKind.PackAPunch: return "Pack-a-Punch";
                case ZombiesKind.WallBuy: return "Wall buy";
                case ZombiesKind.Door: return "Door";
                case ZombiesKind.Debris: return "Debris";
                case ZombiesKind.Blocker: return "Blocker (invisible wall)";
                case ZombiesKind.PlayAreaWall: return "Play area wall";
                case ZombiesKind.Prop: return "Prop";
                case ZombiesKind.PowerLight: return "Power light";
                case ZombiesKind.PowerSwitch: return "Power switch";
                case ZombiesKind.Zone: return "Zone (box)";
                case ZombiesKind.Rock: return "115 rock";
                case ZombiesKind.Lightning: return "Lightning";
                case ZombiesKind.Lamp: return "Lamp (always on)";
                case ZombiesKind.Decal: return "Wall decal";
                case ZombiesKind.ExfilActivator: return "Exfil lever";
                case ZombiesKind.ExfilHold: return "Exfil hold area";
                case ZombiesKind.ExfilZone: return "Exfil exit zone";
                default: return k.ToString();
            }
        }

        /// <summary>One line on what the kind does and how to place it.</summary>
        public static string Help(ZombiesKind k)
        {
            switch (k)
            {
                case ZombiesKind.PlayerStart: return "Where players start, facing its arrow. Prefer the SDK's player spawn points named Spawn_Player_1..4 (one per player; Setup tab): with those, Player starts are not needed.";
                case ZombiesKind.ZombieSpawn: return "Where zombies come up out of the ground. Put them outside, in reach of a window's outside on the navmesh. A spawn in a closed zone waits until its zone opens.";
                case ZombiesKind.Window: return "A boarded window zombies tear open and climb through. Its arrow points INTO the building (zombies come from its front, the outside). Scale = the opening's size. For a clean climb: sill 0.75-1.0 m above the outside ground, wall at most 0.41 m deep.";
                case ZombiesKind.BoxSpot: return "A spot the mystery box can be at (it moves between them). Its arrow points to the side players buy from. Tick 'Box starts here' on one to always start there.";
                case ZombiesKind.PerkMachine: return "A perk machine. Its arrow points to the side players buy from. Needs the power (unless the map is always powered).";
                case ZombiesKind.PackAPunch: return "Pack-a-Punch. Its arrow points to the side players use. Needs the power (unless the map is always powered).";
                case ZombiesKind.WallBuy: return "A gun (or grenades) bought off a wall: the chalk outline shows until the first buy. Put the marker on the wall's face, its arrow out of the wall.";
                case ZombiesKind.Door: return "A door bought with points (or opened by the power). Scale = its size. Buying it opens every zone it touches (within 2.5 m).";
                case ZombiesKind.Debris: return "Debris cleared with points (or by the power). Scale = its size. Clearing it opens every zone it touches (within 2.5 m).";
                case ZombiesKind.Blocker: return "An invisible solid wall: seals gaps players or zombies shouldn't get through (bullets pass). Scale = its size.";
                case ZombiesKind.PlayAreaWall: return "An invisible wall round the play area. Scale = its size.";
                case ZombiesKind.Prop: return "A copy of one of the map's own props. Better: place props in Unity as normal map objects.";
                case ZombiesKind.PowerLight: return "A light that comes on with the power (with a fixture of your map as its look, Custom look).";
                case ZombiesKind.PowerSwitch: return "The power switch: thrown once, the power is on for the match (perks and Pack-a-Punch work). Not needed on an always-powered map.";
                case ZombiesKind.Zone: return "An area of the map. Zones 1 and 2 are open from the start; the others open when a door or debris touching them is bought. Zombie spawns and windows inside belong to it. Scale = its size.";
                case ZombiesKind.Rock: return "A glowing 115 rock. Easter egg rocks: find them all and the easter egg song plays. Lore rocks only glow and hum.";
                case ZombiesKind.Lightning: return "Arcs of lightning crackling (crackle: small and often, storm: big now and then).";
                case ZombiesKind.Lamp: return "A light that's always on (with a fixture of your map as its look). Plain Unity lights work too.";
                case ZombiesKind.Decal: return "A picture on a wall (blood, scrawl, chalk). The marker's feet are the picture's bottom middle; its arrow out of the wall. Scale = width and height.";
                case ZombiesKind.ExfilActivator: return "The exfil lever: from round 15 a player throws it to start the exfil (defend the hold area, then reach the exit zone).";
                case ZombiesKind.ExfilHold: return "The exfil hold area: the defend timer only runs while a player stands in it. Several boxes make one area. Scale = its size.";
                case ZombiesKind.ExfilZone: return "The exfil exit: everyone standing in it when the exfil closes gets out. Scale = its size.";
                default: return "";
            }
        }

        /// <summary>The colours SPZombies' own editor shows each kind in.</summary>
        public static Color Colour(ZombiesKind kind)
        {
            switch (kind)
            {
                case ZombiesKind.PlayerStart: return new Color(0.2f, 1f, 0.3f, 0.7f);
                case ZombiesKind.ZombieSpawn: return new Color(1f, 0.15f, 0.1f, 0.6f);
                case ZombiesKind.BoxSpot: return new Color(1f, 0.85f, 0.2f, 0.6f);
                case ZombiesKind.PerkMachine: return new Color(0.2f, 0.5f, 1f, 0.6f);
                case ZombiesKind.PackAPunch: return new Color(0.7f, 0.2f, 1f, 0.6f);
                case ZombiesKind.WallBuy: return new Color(1f, 0.55f, 0.1f, 0.8f);
                case ZombiesKind.Window: return new Color(0.55f, 0.35f, 0.18f, 0.35f);
                case ZombiesKind.Door: return new Color(0.55f, 0.35f, 0.15f, 0.7f);
                case ZombiesKind.Debris: return new Color(0.6f, 0.6f, 0.55f, 0.7f);
                case ZombiesKind.Blocker: return new Color(0.3f, 0.3f, 0.35f, 0.35f);
                case ZombiesKind.PowerLight: return new Color(1f, 0.95f, 0.7f, 0.7f);
                case ZombiesKind.PowerSwitch: return new Color(1f, 0.8f, 0.1f, 0.8f);
                case ZombiesKind.ExfilActivator: return new Color(0.2f, 1f, 0.6f, 0.8f);
                case ZombiesKind.ExfilHold: return new Color(0.2f, 1f, 0.4f, 0.12f);
                case ZombiesKind.ExfilZone: return new Color(0.1f, 1f, 0.9f, 0.25f);
                case ZombiesKind.Zone: return new Color(0.3f, 0.6f, 1f, 0.12f);
                case ZombiesKind.Rock: return new Color(0.3f, 0.7f, 1f, 0.7f);
                case ZombiesKind.Lightning: return new Color(0.6f, 0.8f, 1f, 0.5f);
                case ZombiesKind.Lamp: return new Color(1f, 0.85f, 0.5f, 0.7f);
                case ZombiesKind.Decal: return new Color(0.8f, 0.1f, 0.1f, 0.5f);
                default: return new Color(0.2f, 0.9f, 1f, 0.25f);
            }
        }

        /// <summary>A zone's colour by its number (as SPZombies' editor draws it).</summary>
        public static Color ZoneColour(int id) => Color.HSVToRGB(Mathf.Repeat(id * 0.137f, 1f), 0.75f, 1f);

        /// <summary>A new sized thing's size in metres (width, height, depth).</summary>
        public static Vector3 DefaultSize(ZombiesKind kind)
        {
            switch (kind)
            {
                case ZombiesKind.Window: return new Vector3(1.4f, 1.6f, 0.3f);
                case ZombiesKind.Door: return new Vector3(1.2f, 2.2f, 0.15f);
                case ZombiesKind.Debris: return new Vector3(2.5f, 1.6f, 1.5f);
                case ZombiesKind.Blocker: return new Vector3(2f, 2.5f, 0.3f);
                case ZombiesKind.PlayAreaWall: return new Vector3(4f, 3f, 0.1f);
                case ZombiesKind.Zone: return new Vector3(12f, 5f, 12f);
                case ZombiesKind.ExfilHold: return new Vector3(12f, 4f, 12f);
                case ZombiesKind.ExfilZone: return new Vector3(4f, 3f, 4f);
                case ZombiesKind.Decal: return new Vector3(1f, 1f, 0.02f);
                default: return Vector3.one;
            }
        }

        /// <summary>The size of the stand-in box for a kind with no model (as SPZombies' editor draws it).</summary>
        public static Vector3 MarkerSize(ZombiesKind kind)
        {
            switch (kind)
            {
                case ZombiesKind.PlayerStart: return new Vector3(0.6f, 1.8f, 0.6f);
                case ZombiesKind.ZombieSpawn: return new Vector3(0.6f, 1.8f, 0.6f);
                case ZombiesKind.BoxSpot: return new Vector3(2.1f, 0.7f, 0.6f);
                case ZombiesKind.PerkMachine: return new Vector3(0.9f, 2f, 0.7f);
                case ZombiesKind.PackAPunch: return new Vector3(1.2f, 1.8f, 1f);
                case ZombiesKind.WallBuy: return new Vector3(0.8f, 0.4f, 0.05f);
                case ZombiesKind.PowerLight: return new Vector3(1.2f, 0.15f, 0.2f);
                case ZombiesKind.PowerSwitch: return new Vector3(0.6f, 1.1f, 0.3f);
                case ZombiesKind.ExfilActivator: return new Vector3(0.6f, 1.1f, 0.3f);
                case ZombiesKind.Rock: return new Vector3(0.3f, 0.2f, 0.2f);
                case ZombiesKind.Lightning: return new Vector3(0.3f, 0.3f, 0.3f);
                case ZombiesKind.Lamp: return new Vector3(1.2f, 0.15f, 0.2f);
                case ZombiesKind.Decal: return new Vector3(1f, 1f, 0.02f);
                default: return Vector3.one;
            }
        }

        public static bool HasPrice(ZombiesKind k) => k == ZombiesKind.Door || k == ZombiesKind.Debris || k == ZombiesKind.WallBuy;

        public static int DefaultPrice(ZombiesKind k) => k == ZombiesKind.Door ? 750 : k == ZombiesKind.Debris ? 1000 : k == ZombiesKind.WallBuy ? 1000 : 0;

        public static bool HasZone(ZombiesKind k) => k == ZombiesKind.ZombieSpawn || k == ZombiesKind.Window || k == ZombiesKind.Zone;

        public static bool CanHaveCustomLook(ZombiesKind k) => k == ZombiesKind.Door || k == ZombiesKind.Debris || k == ZombiesKind.PowerLight || k == ZombiesKind.Lamp;

        /// <summary>Kinds that sit on a wall (placed on the surface the view points at, facing out of it).</summary>
        public static bool OnWall(ZombiesKind k) => k == ZombiesKind.WallBuy || k == ZombiesKind.Decal;

        /// <summary>Kinds whose arrow shows which way they face.</summary>
        public static bool HasFacing(ZombiesKind k) =>
            k == ZombiesKind.Window || k == ZombiesKind.ZombieSpawn || k == ZombiesKind.PlayerStart || k == ZombiesKind.BoxSpot
            || k == ZombiesKind.PerkMachine || k == ZombiesKind.PackAPunch || k == ZombiesKind.WallBuy || k == ZombiesKind.PowerSwitch
            || k == ZombiesKind.Door || k == ZombiesKind.Debris || k == ZombiesKind.ExfilActivator || k == ZombiesKind.Decal;

        /// <summary>A short label for the Scene view.</summary>
        public static string Label(ZombiesMarker m)
        {
            string what = m.What ?? "";
            string zone = m.Zone > 0 ? $" (zone {m.Zone})" : "";
            switch (m.Kind)
            {
                case ZombiesKind.PerkMachine: return PerkName(what);
                case ZombiesKind.WallBuy: return $"{KitInstall.GunName(what)} ({m.Price})";
                case ZombiesKind.Door:
                case ZombiesKind.Debris:
                    return $"{KindName(m.Kind)} ({(m.OpensWithPower ? "power" : m.Price.ToString())}){(m.CustomLook != null ? " custom" : "")}";
                case ZombiesKind.Window: return "Window" + zone;
                case ZombiesKind.ZombieSpawn: return "Zombie spawn" + zone;
                case ZombiesKind.Zone: return $"Zone {m.Zone}";
                case ZombiesKind.BoxSpot: return m.BoxStartsHere ? "Box (starts here)" : "Box";
                case ZombiesKind.Rock: return what == "lore" ? "115 rock (lore)" : "115 rock";
                default: return KindName(m.Kind);
            }
        }

        /// <summary>The choices for a kind's "What", or null when it has none.</summary>
        public static List<(string id, string label)> Choices(ZombiesKind k)
        {
            var list = new List<(string, string)>();
            switch (k)
            {
                case ZombiesKind.PerkMachine:
                    foreach (string p in PerkIds)
                        list.Add((p, PerkName(p)));
                    return list;
                case ZombiesKind.WallBuy:
                    foreach (KitInstall.Gun g in KitInstall.Guns())
                        list.Add((g.Id, g.Name + (g.Role == "wall" ? "" : "  (" + g.Role + ")")));
                    return list;
                case ZombiesKind.Door:
                    foreach (string s in KitModels.Offered("zm_door_", DoorLooks))
                        list.Add((s, Nice(s)));
                    return list;
                case ZombiesKind.Debris:
                    foreach (string s in KitModels.Offered("zm_debris_", DebrisLooks))
                        list.Add((s, Nice(s)));
                    return list;
                case ZombiesKind.Rock:
                    list.Add(("easter_egg", "Easter egg"));
                    list.Add(("lore", "Lore"));
                    return list;
                case ZombiesKind.Lightning:
                    list.Add(("crackle", "Crackle"));
                    list.Add(("storm", "Storm"));
                    return list;
                case ZombiesKind.Decal:
                    foreach (string s in KitInstall.DecalNames())
                        list.Add((s, Nice(s)));
                    return list;
                default:
                    return null;
            }
        }
    }
}
