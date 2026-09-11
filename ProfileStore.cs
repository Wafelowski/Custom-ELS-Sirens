using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Rage;

namespace CustomELSSirens
{
    // Game-fiber-only snapshots: no INI reads or File.Exists calls in steady-state
    // per-frame light, volume, or profile lookups. Reload/save invalidates these.
    internal static class ProfileStore
    {
        internal sealed class Profile
        {
            internal bool HasOwnProfile;
            internal bool LightRestriction;
            internal bool StageTracking;
            internal int StageCount;
            internal bool RumblerEnabled;
            internal Keys RumblerKey;
            internal Keys FiammsKey;
            internal readonly int[] Extras = { -1, -1, -1, -1 };
            internal readonly Keys[] ExtraKeys = new Keys[4];
            internal readonly Dictionary<string, string> SoundFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, string> RumblerSoundFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, string> RumblerSounds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, float> RumblerVolumes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, string> Sounds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, float> Volumes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            internal string GetSound(string key, bool rumbler)
            {
                if (rumbler && RumblerEnabled && RumblerSounds.TryGetValue(key, out string alternate) && alternate != "None")
                    return alternate;
                return Sounds.TryGetValue(key, out string normal) ? normal : "None";
            }

            internal float GetVolume(string key, bool rumbler)
            {
                var source = rumbler && RumblerEnabled ? RumblerVolumes : Volumes;
                return source.TryGetValue(key, out float value) ? value : PluginConfig.DefaultSirenVolume(key);
            }
        }

        internal static readonly string[] SoundKeys = { "Tone1", "Tone2", "Tone3", "Tone4", "Tone5", "Tone6", "Horn", "Manual", "FIAMMS" };
        private static readonly Dictionary<string, Profile> profiles = new Dictionary<string, Profile>(StringComparer.OrdinalIgnoreCase);

        internal static void Clear() => profiles.Clear();

        internal static Profile Get(string modelName)
        {
            string model = string.IsNullOrWhiteSpace(modelName) ? "Global" : modelName;
            if (profiles.TryGetValue(model, out var cached)) return cached;
            var profile = new Profile();
            string path = Path.Combine(PluginConfig.ProfilesFolder, model + ".ini");
            profile.HasOwnProfile = File.Exists(path);
            var global = new InitializationFile(Path.Combine(PluginConfig.ProfilesFolder, "Global.ini"));
            var ini = profile.HasOwnProfile ? new InitializationFile(path) : global;
            profile.LightRestriction = ini.ReadBoolean("Settings", "SirenLightRestriction",
                global.ReadBoolean("Settings", "SirenLightRestriction", PluginConfig.SirenLightRestriction));
            profile.StageTracking = ini.ReadBoolean("Settings", "EnableLightStageTracking",
                global.ReadBoolean("Settings", "EnableLightStageTracking", PluginConfig.EnableLightStageTracking));
            profile.StageCount = Math.Max(1, Math.Min(4, ini.ReadInt32("Settings", "CustomLightStageAmount",
                global.ReadInt32("Settings", "CustomLightStageAmount", PluginConfig.CustomLightStageAmount))));
            profile.RumblerEnabled = ini.ReadBoolean("Rumbler", "Enabled", global.ReadBoolean("Rumbler", "Enabled", false));
            profile.RumblerKey = ini.ReadEnum("Keybinds", "Toggle_Rumbler", global.ReadEnum("Keybinds", "Toggle_Rumbler", PluginConfig.Toggle_Rumbler));
            profile.FiammsKey = ini.ReadEnum("Keybinds", "Toggle_FIAMMS", global.ReadEnum("Keybinds", "Toggle_FIAMMS", PluginConfig.Toggle_FIAMMS));
            Keys[] defaults = { PluginConfig.Toggle_RedBeacon, PluginConfig.Toggle_MatrixText1, PluginConfig.Toggle_MatrixText2, PluginConfig.Toggle_MatrixText3 };
            for (int i = 0; i < ExtraControls.Names.Length; i++)
            {
                string key = "Toggle_" + ExtraControls.Names[i];
                profile.ExtraKeys[i] = ini.ReadEnum("Keybinds", key, global.ReadEnum("Keybinds", key, defaults[i]));
                // Extra meshes differ between models. Never inherit their IDs
                // from a global profile or silently map an unconfigured vehicle.
                profile.Extras[i] = profile.HasOwnProfile && !model.Equals("Global", StringComparison.OrdinalIgnoreCase)
                    ? ini.ReadInt32("VehicleExtras", ExtraControls.Names[i], -1) : -1;
            }
            int[] suppliedExtras = (int[])profile.Extras.Clone();
            ExtraControls.ValidateMappings(profile.Extras);
            for (int i = 0; i < profile.Extras.Length; i++)
                if (suppliedExtras[i] != profile.Extras[i])
                    Game.Console.Print("[CustomSirens] Disabled invalid/duplicate extra mapping: " + model + " / " + ExtraControls.Names[i]);

            foreach (string key in SoundKeys)
            {
                // Preserve explicit per-vehicle tone selections, including None.
                string file = ini.ReadString("Sirens", key, "None").Trim();
                profile.SoundFiles[key] = file;
                profile.Sounds[key] = ResolveSound(file);
                string alternate = ini.ReadString("RumblerSirens", key, "None").Trim();
                profile.RumblerSoundFiles[key] = alternate;
                profile.RumblerSounds[key] = ResolveSound(alternate);
                string volumeKey = key + "Vol";
                float fallback = PluginConfig.DefaultSirenVolume(volumeKey);
                float globalVolume = PluginConfig.ReadFloat(global, "SirenVolumes", volumeKey, fallback);
                profile.Volumes[volumeKey] = AudioMath.Clamp(PluginConfig.ReadFloat(ini, "SirenVolumes", volumeKey, globalVolume), 0f, 1f);
                profile.RumblerVolumes[volumeKey] = AudioMath.Clamp(PluginConfig.ReadFloat(ini, "RumblerVolumes", volumeKey, profile.Volumes[volumeKey]), 0f, 1f);
            }
            profiles[model] = profile;
            return profile;
        }

        private static string ResolveSound(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || file.Equals("None", StringComparison.OrdinalIgnoreCase)) return "None";
            try
            {
                string root = Path.GetFullPath(PluginConfig.WavFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string path = Path.GetFullPath(Path.Combine(root, file));
                return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : "None";
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is IOException)
            {
                Game.Console.Print("[CustomSirens] Invalid WAV path '" + file + "': " + ex.Message);
                return "None";
            }
        }

        internal static void SetVolume(string model, string key, float value, bool rumbler = false)
        {
            value = AudioMath.Clamp(value, 0f, 1f);
            if (string.Equals(model, "Global", StringComparison.OrdinalIgnoreCase))
            {
                var global = Get("Global");
                (rumbler ? global.RumblerVolumes : global.Volumes)[key] = value;
                foreach (var pair in profiles)
                    if (!pair.Value.HasOwnProfile) (rumbler ? pair.Value.RumblerVolumes : pair.Value.Volumes)[key] = value;
            }
            else if (!string.IsNullOrWhiteSpace(model))
            {
                var profile = Get(model);
                (rumbler ? profile.RumblerVolumes : profile.Volumes)[key] = value;
            }
        }
    }
}
