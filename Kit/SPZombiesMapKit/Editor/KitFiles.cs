using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// Picking the map's sound and picture files from anywhere on the PC: copied into the project
    /// (Assets/SPZombiesMapData/&lt;map&gt;), and sounds that SPZombies can't play (.mp3, .ogg, .aiff, 24-bit or float .wav)
    /// made into a plain 16-bit PCM .wav. Unity's own audio importer decodes the file (stored uncompressed, at its own
    /// sample rate, so nothing is re-compressed on the way); the kit writes the samples out as a standard .wav.
    /// </summary>
    internal static class KitFiles
    {
        private const string LastDirPref = "SPZombiesMapKit.LastFileDir";
        public static readonly string[] SoundFilter = { "Sounds", "wav,mp3,ogg,aif,aiff", "All files", "*" };
        public static readonly string[] PictureFilter = { "Pictures", "png,jpg,jpeg", "All files", "*" };

        private static string LastDir
        {
            get => EditorPrefs.GetString(LastDirPref, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            set => EditorPrefs.SetString(LastDirPref, value ?? "");
        }

        /// <summary>The project folder the map's files are kept in (Assets/SPZombiesMapData/&lt;map id&gt;).</summary>
        public static string DataFolder(ZombiesMapSettings s)
        {
            string id = KitExport.Clean(s != null ? s.MapId : "");
            return $"{KitExport.DataFolder}/{(id.Length > 0 ? id : "map")}";
        }

        /// <summary>Asks for a file (a dialog); null when cancelled.</summary>
        public static string Ask(string title, string[] filter)
        {
            string file = EditorUtility.OpenFilePanelWithFilters(title, LastDir, filter);
            if (string.IsNullOrEmpty(file))
                return null;
            LastDir = Path.GetDirectoryName(file);
            return file;
        }

        // ---------- Sounds ----------

        /// <summary>
        /// A sound file from anywhere, ready for SPZombies: copied into the map's data folder as &lt;name&gt;.wav (16-bit PCM),
        /// converted first when it isn't one already. Returns the clip, or null with why not.
        /// </summary>
        public static AudioClip AddSound(ZombiesMapSettings s, string file, string name, out string message)
        {
            message = null;
            string folder = DataFolder(s);
            Directory.CreateDirectory(Path.Combine(KitExport.ProjectRoot, folder));
            string wavAsset = $"{folder}/{name}.wav";
            string wavFull = Path.Combine(KitExport.ProjectRoot, wavAsset);
            string ext = Path.GetExtension(file).ToLowerInvariant();
            // Already a .wav SPZombies plays: copied as it is
            if (ext == ".wav" && KitExport.WavProblem(file) == null)
            {
                if (!SamePath(file, wavFull))
                    File.Copy(file, wavFull, true);
                AssetDatabase.ImportAsset(wavAsset);
                message = $"{Path.GetFileName(file)} copied into {folder}.";
                return AssetDatabase.LoadAssetAtPath<AudioClip>(wavAsset);
            }
            // Anything else: decoded by Unity, written out as 16-bit PCM
            string source = $"{folder}/_converting_{name}{ext}";
            string sourceFull = Path.Combine(KitExport.ProjectRoot, source);
            try
            {
                File.Copy(file, sourceFull, true);
                AssetDatabase.ImportAsset(source);
                string error = Convert(source, wavFull, out string done);
                if (error != null)
                {
                    message = $"{Path.GetFileName(file)}: {error}";
                    return null;
                }
                AssetDatabase.ImportAsset(wavAsset);
                message = $"{Path.GetFileName(file)} made into a 16-bit .wav ({done}) in {folder}.";
                return AssetDatabase.LoadAssetAtPath<AudioClip>(wavAsset);
            }
            finally
            {
                if (AssetDatabase.LoadMainAssetAtPath(source) != null)
                    AssetDatabase.DeleteAsset(source);
                else if (File.Exists(sourceFull))
                    File.Delete(sourceFull);
            }
        }

        /// <summary>A clip already in the project that SPZombies can't play, made into a 16-bit .wav beside it. Null with why not.</summary>
        public static AudioClip ConvertClip(AudioClip clip, out string message)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path))
            {
                message = $"{clip.name} isn't a file in the project.";
                return null;
            }
            string dir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
            string baseName = Path.GetFileNameWithoutExtension(path);
            string wavAsset = $"{dir}/{baseName}_16bit.wav";
            // Its own import settings are put back afterwards
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            AudioImporterSampleSettings before = importer != null ? importer.defaultSampleSettings : default;
            string error = Convert(path, Path.Combine(KitExport.ProjectRoot, wavAsset), out string done);
            if (importer != null)
            {
                importer.defaultSampleSettings = before;
                importer.SaveAndReimport();
            }
            if (error != null)
            {
                message = $"{clip.name}: {error}";
                return null;
            }
            AssetDatabase.ImportAsset(wavAsset);
            message = $"{clip.name} made into {baseName}_16bit.wav ({done}).";
            return AssetDatabase.LoadAssetAtPath<AudioClip>(wavAsset);
        }

        /// <summary>Decodes an audio asset with Unity's importer and writes it as a 16-bit PCM .wav. Null on success, else why not.</summary>
        private static string Convert(string asset, string toFull, out string done)
        {
            done = "";
            if (!(AssetImporter.GetAtPath(asset) is AudioImporter importer))
                return "Unity can't read it as a sound (use .wav, .mp3, .ogg or .aiff).";
            // Stored uncompressed at its own rate and loaded whole, so GetData hands back the decoded samples
            AudioImporterSampleSettings ss = importer.defaultSampleSettings;
            ss.loadType = AudioClipLoadType.DecompressOnLoad;
            ss.compressionFormat = AudioCompressionFormat.PCM;
            ss.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = ss;
            importer.forceToMono = false;
            importer.loadInBackground = false;
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(asset);
            if (clip == null || clip.samples <= 0 || clip.channels <= 0)
                return "Unity couldn't decode it.";
            clip.LoadAudioData();
            var samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0))
                return "Unity couldn't hand over its samples.";
            WriteWav(toFull, samples, clip.channels, clip.frequency);
            string problem = KitExport.WavProblem(toFull);
            if (problem != null)
                return "the converted file " + problem;
            done = $"{clip.length:0.0} s, {(clip.channels == 1 ? "mono" : clip.channels == 2 ? "stereo" : clip.channels + " channels")}, {clip.frequency} Hz";
            return null;
        }

        /// <summary>A standard 44-byte-header 16-bit PCM .wav (the layout SPZombies reads).</summary>
        public static void WriteWav(string path, float[] samples, int channels, int rate)
        {
            int dataBytes = samples.Length * 2;
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataBytes);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(rate);
                w.Write(rate * channels * 2);
                w.Write((short)(channels * 2));
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataBytes);
                foreach (float f in samples)
                    w.Write((short)Mathf.Clamp(Mathf.RoundToInt(f * 32767f), -32768, 32767));
            }
        }

        // ---------- Pictures ----------

        /// <summary>A picture file from anywhere, copied into the map's data folder. Returns the texture, or null with why not.</summary>
        public static Texture2D AddPicture(ZombiesMapSettings s, string file, string name, out string message)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
            {
                message = $"{Path.GetFileName(file)}: use a .png or .jpg picture.";
                return null;
            }
            string folder = DataFolder(s);
            Directory.CreateDirectory(Path.Combine(KitExport.ProjectRoot, folder));
            string asset = $"{folder}/{name}{(ext == ".jpeg" ? ".jpg" : ext)}";
            string full = Path.Combine(KitExport.ProjectRoot, asset);
            if (!SamePath(file, full))
                File.Copy(file, full, true);
            AssetDatabase.ImportAsset(asset);
            message = $"{Path.GetFileName(file)} copied into {folder}.";
            return AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
        }

        /// <summary>A picture's size in pixels as saved (not as Unity shrank it on import).</summary>
        public static Vector2Int PictureSize(Texture2D tex)
        {
            string path = tex != null ? AssetDatabase.GetAssetPath(tex) : null;
            if (string.IsNullOrEmpty(path) || !(AssetImporter.GetAtPath(path) is TextureImporter ti))
                return Vector2Int.zero;
            ti.GetSourceTextureWidthAndHeight(out int w, out int h);
            return new Vector2Int(w, h);
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }
}
