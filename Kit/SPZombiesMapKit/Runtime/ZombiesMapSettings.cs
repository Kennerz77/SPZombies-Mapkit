using System;
using System.Collections.Generic;
using UnityEngine;

namespace SPZombies.MapKit
{
    /// <summary>
    /// The zombies map's own settings (one per map, editor only): its id, time of day, weather, power, perk limit and
    /// prices, the starting gun, where it shows on the map screen, and its sounds, pictures and loading tips. Written into
    /// the layout file on Export.
    /// </summary>
    [AddComponentMenu("SPZombies/Zombies Map Settings")]
    [DisallowMultipleComponent]
    public class ZombiesMapSettings : MonoBehaviour
    {
        [Tooltip("The map's id: the same as the SDK Map Definition's Map Id (e.g. zm_mymap). Starting it with zm_ is recommended.")]
        public string MapId = "";

        [Tooltip("The map's name, for its folder (BepInEx\\plugins\\SPZombies_Maps\\ZM_<name>) and its read-me. Letters and digits.")]
        public string MapName = "";

        [Tooltip("Freeze the time of day at Hour for the whole match (else the raid's own clock runs).")]
        public bool HoldTime = true;

        [Range(0f, 24f), Tooltip("The time of day the map is frozen at (0-24).")]
        public float Hour = 21f;

        [Tooltip("Freeze the weather below for the whole match (needs the Tushonka SDK's full weather system in the map).")]
        public bool HoldWeather;

        [Range(-1f, 1f), Tooltip("-1 clear .. 1 overcast")] public float Clouds = 0.6f;
        [Range(0f, 1f), Tooltip("0 .. 1")] public float Rain = 0.2f;
        [Range(0.001f, 0.255f), Tooltip("0.001 .. 0.255")] public float Fog = 0.02f;
        [Range(0f, 1f), Tooltip("0 .. 1")] public float Wind = 0.2f;

        [Tooltip("A bright moonlit night: players see fine at night, with no hard shadows.")]
        public bool Moonlight = true;

        [Tooltip("Perks a player may hold (0: four).")]
        public int MaxPerks;

        [Tooltip("The power is on from the start: no power switch needed, every machine works at once.")]
        public bool AlwaysPowered;

        [Tooltip("The gun players start and respawn with (an id from SPZombies' gun list). m1911 is the normal one.")]
        public string StartGun = "m1911";

        [Tooltip("Use the perk prices below instead of SPZombies' own.")]
        public bool CustomPerkPrices;

        [Tooltip("Each perk's price on this map (only used with Custom perk prices on).")]
        public List<PerkPrice> PerkPrices = new List<PerkPrice>();

        [Tooltip("Where the map shows on the map screen: the place of this map id (bigmap, woods...). Empty: its own place.")]
        public string MapSelectSpot = "woods";

        [Tooltip("Music while the map loads (16-bit PCM .wav).")]
        public AudioClip LoadingMusic;

        [Tooltip("The map's ambience bed, looped through the match (16-bit PCM .wav).")]
        public AudioClip Ambience;

        [Tooltip("The easter egg song, played when every easter egg 115 rock is found (16-bit PCM .wav).")]
        public AudioClip EasterEggSong;

        [Tooltip("The map's picture on the map screen and the lobby's map card (.png).")]
        public Texture2D Banner;

        [Tooltip("The picture shown while the map loads (.jpg or .png).")]
        public Texture2D LoadingPicture;

        [TextArea(3, 12), Tooltip("Loading screen tips for this map, one per line (shuffled in with the mode's own).")]
        public string Tips = "";

        /// <summary>One perk's price on this map.</summary>
        [Serializable]
        public class PerkPrice
        {
            public string Id;
            public int Price;
        }

        /// <summary>The map's price for a perk, or 0 when it uses SPZombies' own.</summary>
        public int PriceOf(string id)
        {
            if (!CustomPerkPrices || PerkPrices == null)
                return 0;
            foreach (PerkPrice p in PerkPrices)
                if (p != null && p.Id == id)
                    return Mathf.Max(0, p.Price);
            return 0;
        }
    }
}
