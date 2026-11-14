using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// The map settings' Inspector (also the kit's Map tab): every setting with a plain explanation on hover, the start
    /// gun and perk prices as lists, the weather sliders always on show (used only while frozen), and file pickers for
    /// the sounds and pictures that copy them into the project (sounds made into 16-bit .wav when they need it).
    /// </summary>
    [CustomEditor(typeof(ZombiesMapSettings))]
    internal class ZombiesMapSettingsEditor : UnityEditor.Editor
    {
        private static bool _pricesOpen;
        private string _fileNote;
        private MessageType _fileNoteType;

        private SerializedProperty P(string name) => serializedObject.FindProperty(name);

        private static GUIContent C(string label, string tip) => new GUIContent(label, tip);

        private static void Note(string text) => EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);

        public override void OnInspectorGUI()
        {
            var s = (ZombiesMapSettings)target;
            serializedObject.Update();
            MapSection(s);
            EditorGUILayout.Space();
            MatchSection(s);
            EditorGUILayout.Space();
            TimeSection();
            EditorGUILayout.Space();
            SoundSection(s);
            EditorGUILayout.Space();
            PictureSection(s);
            EditorGUILayout.Space();
            TipSection(s);
            serializedObject.ApplyModifiedProperties();
            if (!string.IsNullOrEmpty(_fileNote))
                EditorGUILayout.HelpBox(_fileNote, _fileNoteType);
        }

        // ---------- Map ----------

        private void MapSection(ZombiesMapSettings s)
        {
            EditorGUILayout.LabelField("Map", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(P(nameof(ZombiesMapSettings.MapId)), C("Map id",
                    "The map's id: the same as the SDK Map Definition's Map Id (e.g. zm_mymap). Starting it with zm_ is recommended."));
                KitSdk.MapDef def = KitSdk.For(null);
                if (def != null && def.MapId != s.MapId && GUILayout.Button("Use " + def.MapId, GUILayout.Width(150)))
                    P(nameof(ZombiesMapSettings.MapId)).stringValue = def.MapId;
            }
            EditorGUILayout.PropertyField(P(nameof(ZombiesMapSettings.MapName)), C("Map name",
                "Your map's name, letters and digits (e.g. Bunker). It names the map's folder (ZM_Bunker) and its read-me."));
            EditorGUILayout.LabelField(C("Install folder", "Where the map's zombies files go in a player's SPT folder. Made from the map name."),
                new GUIContent(KitExport.InstallPath(s)), EditorStyles.miniLabel);

            const string spotTip = "Solo players pick a map on the game's map screen. Your map's icon sits on the spot of the map you choose here " +
                                   "(that map is hidden while zombies mode is on, so they never overlap). \"Its own place\" uses the icon spot from " +
                                   "your map's own server settings (the SDK's Edit Server Data). Co-op lobbies use the lobby's map list instead.";
            SerializedProperty spot = P(nameof(ZombiesMapSettings.MapSelectSpot));
            int index = System.Array.IndexOf(KitInfo.MapSelectSpots, spot.stringValue);
            var labels = KitInfo.MapSelectLabels.ToList();
            if (index < 0)
            {
                labels.Add(spot.stringValue);
                index = labels.Count - 1;
            }
            int picked = EditorGUILayout.Popup(C("Map screen spot", spotTip), index, labels.Select(l => new GUIContent(l)).ToArray());
            if (picked < KitInfo.MapSelectSpots.Length)
                spot.stringValue = KitInfo.MapSelectSpots[picked];
            Note("Where your map's icon appears on the map screen for solo players (hover for more).");
        }

        // ---------- Match ----------

        private void MatchSection(ZombiesMapSettings s)
        {
            EditorGUILayout.LabelField("Match", EditorStyles.boldLabel);

            SerializedProperty always = P(nameof(ZombiesMapSettings.AlwaysPowered));
            bool needsPower = EditorGUILayout.Toggle(C("Map needs power",
                "On: players must find the power switch and turn it on before the perk machines, Pack-a-Punch, power lights " +
                "and power doors work. Off: the power is on from the start and no power switch is needed."), !always.boolValue);
            always.boolValue = !needsPower;
            Note(needsPower
                ? "Perk machines, Pack-a-Punch, power lights and power doors stay off until a player turns the power switch on. Place a power switch."
                : "Everything works from the start. No power switch needed.");

            SerializedProperty max = P(nameof(ZombiesMapSettings.MaxPerks));
            int shown = max.intValue > 0 ? max.intValue : 4;
            int now = EditorGUILayout.IntSlider(C("Perks a player can hold",
                "How many perks one player can have at once. 4 is the normal limit."), shown, 1, KitInfo.PerkIds.Length);
            max.intValue = now == 4 ? 0 : now;

            StartGun();
            PerkPrices(s);
        }

        private void StartGun()
        {
            SerializedProperty gun = P(nameof(ZombiesMapSettings.StartGun));
            if (string.IsNullOrEmpty(gun.stringValue))
                gun.stringValue = KitInfo.DefaultStartGun;
            const string tip = "The gun every player starts with, and gets back when they respawn. It starts with four mags in reserve " +
                               "(the M1911: 8 loaded + 32). The M1911 is the normal one. Needs SPZombies 0.1.2 or newer.";
            List<KitInstall.Gun> guns = KitInstall.Guns().Where(g => g.Id != KitInfo.GrenadesWallId).ToList();
            if (guns.Count == 0)
            {
                EditorGUILayout.PropertyField(gun, C("Starting gun (id)", tip + " Set your SPT folder in Setup to pick from a list."));
                return;
            }
            // Pistols first (the M1911 at the top), then the rest by name
            guns = guns.OrderBy(g => g.Id == KitInfo.DefaultStartGun ? 0 : g.Class == "pistol" ? 1 : 2).ThenBy(g => g.Name).ToList();
            int index = guns.FindIndex(g => g.Id == gun.stringValue);
            var labels = guns.Select(g => g.Id == KitInfo.DefaultStartGun ? g.Name + " (normal)" : g.Name).ToList();
            if (index < 0)
            {
                labels.Add($"(not in the gun list: {gun.stringValue})");
                index = labels.Count - 1;
            }
            int picked = EditorGUILayout.Popup(C("Starting gun", tip), index, labels.Select(l => new GUIContent(l)).ToArray());
            if (picked < guns.Count)
                gun.stringValue = guns[picked].Id;
        }

        private void PerkPrices(ZombiesMapSettings s)
        {
            SerializedProperty custom = P(nameof(ZombiesMapSettings.CustomPerkPrices));
            SerializedProperty list = P(nameof(ZombiesMapSettings.PerkPrices));
            // Every perk has a row (missing ones at their normal price)
            foreach (string id in KitInfo.PerkIds)
            {
                bool found = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).FindPropertyRelative("Id").stringValue == id)
                        found = true;
                if (found)
                    continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                SerializedProperty row = list.GetArrayElementAtIndex(list.arraySize - 1);
                row.FindPropertyRelative("Id").stringValue = id;
                row.FindPropertyRelative("Price").intValue = KitInfo.PerkDefaultPrice(id);
            }

            EditorGUILayout.PropertyField(custom, C("Custom perk prices",
                "Set your own price for each perk on this map. Off: SPZombies' normal prices. Needs SPZombies 0.1.2 or newer."));
            if (!custom.boolValue)
                return;
            EditorGUILayout.HelpBox("Changing a price can make the loading tips wrong. If you used Add starter tips, check the tips below: " +
                                    "a tip with an old price is flagged there. Remove it and press Add starter tips again.", MessageType.Warning);
            _pricesOpen = EditorGUILayout.Foldout(_pricesOpen, "Perk prices (points)", true);
            if (!_pricesOpen)
                return;
            HashSet<string> placed = new HashSet<string>(KitExport.InOrder<ZombiesMarker>().Where(m => m.Kind == ZombiesKind.PerkMachine).Select(m => m.What));
            EditorGUI.indentLevel++;
            foreach (string id in KitInfo.PerkIds)
                for (int i = 0; i < list.arraySize; i++)
                {
                    SerializedProperty row = list.GetArrayElementAtIndex(i);
                    if (row.FindPropertyRelative("Id").stringValue != id)
                        continue;
                    SerializedProperty price = row.FindPropertyRelative("Price");
                    int normal = KitInfo.PerkDefaultPrice(id);
                    string label = KitInfo.PerkName(id) + (id == "revive" ? " (co-op)" : "") + (placed.Contains(id) ? "" : "  (not on the map)");
                    string tip = id == "revive"
                        ? $"Quick Revive's price in co-op (normal {normal}). Playing solo it always costs 500, then 1500, then 2500."
                        : $"{KitInfo.PerkGameName(id)}'s price on this map (normal {normal}).";
                    price.intValue = Mathf.Max(0, EditorGUILayout.IntField(C(label, tip), price.intValue));
                    break;
                }
            EditorGUI.indentLevel--;
            if (GUILayout.Button("Back to the normal prices"))
                for (int i = 0; i < list.arraySize; i++)
                {
                    SerializedProperty row = list.GetArrayElementAtIndex(i);
                    row.FindPropertyRelative("Price").intValue = KitInfo.PerkDefaultPrice(row.FindPropertyRelative("Id").stringValue);
                }
        }

        // ---------- Time and weather ----------

        private void TimeSection()
        {
            EditorGUILayout.LabelField("Time and weather", EditorStyles.boldLabel);
            SerializedProperty holdTime = P(nameof(ZombiesMapSettings.HoldTime));
            EditorGUILayout.PropertyField(holdTime, C("Freeze the time of day",
                "On: the map's clock is frozen at the time below for the whole match. Off: the raid's own clock runs, so day and night pass."));
            using (new EditorGUI.DisabledScope(!holdTime.boolValue))
            {
                SerializedProperty hour = P(nameof(ZombiesMapSettings.Hour));
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.Slider(hour, 0f, 24f, C("Time of day", "The hour the clock is frozen at (0-24)."));
                    int minutes = Mathf.RoundToInt(hour.floatValue * 60f) % (24 * 60);
                    GUILayout.Label($"{minutes / 60:00}:{minutes % 60:00}", GUILayout.Width(40));
                }
            }
            Note(holdTime.boolValue ? "The map's time stays frozen at this hour." : "The raid's own clock runs (not frozen).");
            EditorGUILayout.PropertyField(P(nameof(ZombiesMapSettings.Moonlight)), C("Moonlit nights",
                "A bright moonlit night: players see fine in the dark, with no hard shadows."));

            SerializedProperty holdWeather = P(nameof(ZombiesMapSettings.HoldWeather));
            EditorGUILayout.PropertyField(holdWeather, C("Freeze the weather",
                "On: the clouds, rain, fog and wind below are fixed for the whole match. Off: the raid's own weather runs and the sliders are not used. " +
                "Needs the Tushonka SDK's full weather system in your map (its Time of Day / Weather window, set to full); with the simple time of day the sliders do nothing."));
            using (new EditorGUI.DisabledScope(!holdWeather.boolValue))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(P(nameof(ZombiesMapSettings.Clouds)), C("Clouds", "-1 clear sky .. 1 fully overcast."));
                EditorGUILayout.PropertyField(P(nameof(ZombiesMapSettings.Rain)), C("Rain", "0 dry .. 1 pouring."));
                EditorGUILayout.PropertyField(P(nameof(ZombiesMapSettings.Fog)), C("Fog", "0.001 clear .. 0.255 very thick."));
                EditorGUILayout.PropertyField(P(nameof(ZombiesMapSettings.Wind)), C("Wind", "0 still .. 1 strong."));
                EditorGUI.indentLevel--;
            }
            Note(holdWeather.boolValue ? "The weather stays frozen at these values." : "The raid's own weather runs (sliders not used until you freeze it).");
        }

        // ---------- Sounds ----------

        private void SoundSection(ZombiesMapSettings s)
        {
            EditorGUILayout.LabelField("Sounds", EditorStyles.boldLabel);
            Note("Pick a .wav, .mp3, .ogg or .aiff from anywhere: the kit copies it into " + KitFiles.DataFolder(s) +
                 " and makes it a 16-bit .wav (what SPZombies plays) when it isn't one.");
            Sound(s, nameof(ZombiesMapSettings.LoadingMusic), "Loading screen music", "loading_music",
                "Music while the map loads, until the match fades in.");
            Sound(s, nameof(ZombiesMapSettings.Ambience), "Map ambience", "ambience",
                "A background sound bed (wind, distant noises), looped for the whole match.");
            Sound(s, nameof(ZombiesMapSettings.EasterEggSong), "Easter egg song", "easter_egg_song",
                "Plays once every easter egg 115 rock on the map has been found.");
        }

        private void Sound(ZombiesMapSettings s, string field, string label, string fileName, string tip)
        {
            SerializedProperty prop = P(field);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(prop, C(label, tip));
                if (GUILayout.Button(C("Choose file...", "Pick a sound file from your PC."), GUILayout.Width(100)))
                {
                    string file = KitFiles.Ask("Pick the " + label.ToLowerInvariant(), KitFiles.SoundFilter);
                    if (file != null)
                    {
                        AudioClip clip = KitFiles.AddSound(s, file, fileName, out string message);
                        Report(message, clip != null);
                        if (clip != null)
                            prop.objectReferenceValue = clip;
                        serializedObject.ApplyModifiedProperties();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            var current = prop.objectReferenceValue as AudioClip;
            if (current == null)
                return;
            string path = AssetDatabase.GetAssetPath(current);
            string problem = path.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase) ? KitExport.WavProblem(path) : "isn't a .wav file.";
            if (problem == null)
            {
                EditorGUILayout.LabelField(" ", $"{current.length:0.0} s, ready", EditorStyles.miniLabel);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox($"{current.name} {problem} SPZombies won't play it.", MessageType.Warning);
                if (GUILayout.Button("Convert to\n16-bit .wav", GUILayout.Width(100), GUILayout.Height(38)))
                {
                    AudioClip made = KitFiles.ConvertClip(current, out string message);
                    Report(message, made != null);
                    if (made != null)
                        prop.objectReferenceValue = made;
                    serializedObject.ApplyModifiedProperties();
                    GUIUtility.ExitGUI();
                }
            }
        }

        // ---------- Pictures ----------

        private void PictureSection(ZombiesMapSettings s)
        {
            EditorGUILayout.LabelField("Pictures", EditorStyles.boldLabel);
            Picture(s, nameof(ZombiesMapSettings.Banner), "Map screen picture", "banner",
                "Your map's picture on the map screen and the lobby's map card. A .png, wide (16:9), e.g. 1000 x 563 or bigger.");
            Picture(s, nameof(ZombiesMapSettings.LoadingPicture), "Loading screen picture", "loading_picture",
                "The picture shown while the map loads, fitted to the screen. A .jpg or .png, 16:9; 1920 x 1080 looks sharp.");
        }

        private void Picture(ZombiesMapSettings s, string field, string label, string fileName, string tip)
        {
            SerializedProperty prop = P(field);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(prop, C(label, tip));
                if (GUILayout.Button(C("Choose file...", "Pick a .png or .jpg from your PC."), GUILayout.Width(100)))
                {
                    string file = KitFiles.Ask("Pick the " + label.ToLowerInvariant(), KitFiles.PictureFilter);
                    if (file != null)
                    {
                        Texture2D tex = KitFiles.AddPicture(s, file, fileName, out string message);
                        Report(message, tex != null);
                        if (tex != null)
                            prop.objectReferenceValue = tex;
                        serializedObject.ApplyModifiedProperties();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (prop.objectReferenceValue is Texture2D t)
            {
                Vector2Int size = KitFiles.PictureSize(t);
                if (size.x > 0)
                    EditorGUILayout.LabelField(" ", $"{size.x} x {size.y}", EditorStyles.miniLabel);
            }
        }

        // ---------- Tips ----------

        private void TipSection(ZombiesMapSettings s)
        {
            EditorGUILayout.LabelField("Loading tips", EditorStyles.boldLabel);
            SerializedProperty tips = P(nameof(ZombiesMapSettings.Tips));
            int count = KitTips.Lines(tips.stringValue).Count;
            EditorGUILayout.PropertyField(tips, C($"Loading tips (one per line){(count > 0 ? $": {count}" : "")}",
                "Tips about your map, shown on the loading screen mixed in with SPZombies' own. One tip per line. " +
                "SPZombies already explains the power-ups, what each perk does, the box and Pack-a-Punch, so keep these about your map."));
            using (new EditorGUILayout.HorizontalScope())
            {
                List<string> missing = KitTips.Missing(s);
                using (new EditorGUI.DisabledScope(missing.Count == 0))
                    if (GUILayout.Button(C(missing.Count > 0 ? $"Add starter tips ({missing.Count})" : "Add starter tips (all added)",
                            "Adds ready-made tips about what's on your map: its perks and their prices, wall buys, the power, box spots, 115 rocks " +
                            "and the starting gun. Edit or delete any of them afterwards. Place your things first.")))
                    {
                        string text = tips.stringValue.TrimEnd();
                        tips.stringValue = (text.Length > 0 ? text + "\n" : "") + string.Join("\n", missing);
                        Report($"{missing.Count} starter tips added. Edit them as you like.", true);
                    }
            }
            List<string> wrong = KitTips.WrongPrices(s);
            if (wrong.Count > 0)
                EditorGUILayout.HelpBox("These tips have a price that doesn't match the map:\n" + string.Join("\n", wrong), MessageType.Warning);
        }

        private void Report(string message, bool ok)
        {
            _fileNote = message;
            _fileNoteType = ok ? MessageType.Info : MessageType.Warning;
            if (!string.IsNullOrEmpty(message))
                Debug.Log("[SPZombies Kit] " + message);
        }
    }
}
