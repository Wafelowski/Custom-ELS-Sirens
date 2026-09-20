using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CustomELSSirens
{
    internal sealed class DebugVoiceState
    {
        internal string Name;
        internal string Status;
        internal string Sound;
    }

    internal sealed class VehicleDebugState
    {
        internal string Model;
        internal string Output;
        internal float MasterVolume;
        internal HornCycleMode HornMode;
        internal bool NativeHornPressed;
        internal bool NativeHornSuppressed;
        internal bool SirenFlag;
        internal bool LightGate;
        internal bool StageTracking;
        internal int Stage, StageCount;
        internal bool RumblerEnabled, RumblerOn, FiammsOn;
        internal DebugVoiceState[] Voices;
        internal int[] MappedExtras;
        internal bool[] MappedExists, MappedEnabled;
        internal int[] EnabledExtras;
        internal bool ScanningExtras;
    }

    // Probing all possible IDs every frame adds needless native calls. Discover
    // 16 per refresh, prioritize mapped IDs, then poll only existing extras.
    internal sealed class DebugExtraTracker
    {
        private readonly HashSet<int> known = new HashSet<int>();
        private int nextId;
        internal bool IsScanning => nextId <= ExtraControls.MaxId;
        internal bool Exists(int id) => known.Contains(id);

        internal int[] Refresh(IVehicleExtras vehicle, int[] mappings)
        {
            foreach (int id in mappings)
                if (ExtraControls.ValidateId(id) >= 0 && !known.Contains(id) && vehicle.Exists(id)) known.Add(id);
            for (int count = 0; count < 16 && IsScanning; count++, nextId++)
                if (!known.Contains(nextId) && vehicle.Exists(nextId)) known.Add(nextId);
            var enabled = new List<int>();
            foreach (int id in known)
                if (vehicle.IsEnabled(id)) enabled.Add(id);
            enabled.Sort();
            return enabled.ToArray();
        }
    }

    internal static class DebugText
    {
        private const int MaxLine = 64;

        private static string Clean(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "None";
            text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            return text.Length <= MaxLine ? text : text.Substring(0, MaxLine - 3) + "...";
        }

        private static string OnOff(bool value) => value ? "ON" : "OFF";

        internal static string Build(VehicleDebugState state)
        {
            if (state == null) return string.Empty;
            var lines = new List<string>
            {
                "CUSTOM ELS SIRENS | DEBUG",
                Clean("Vehicle: " + state.Model),
                "Audio: " + state.Output + " | Master: " + Math.Round(state.MasterVolume * 100).ToString(CultureInfo.InvariantCulture) + "%",
                "Horn cycle: " + HornModes.Labels[(int)state.HornMode],
                "Car horn: " + (state.NativeHornSuppressed ? "SUPPRESSED" : state.NativeHornPressed ? "PRESSED" : "OFF"),
                "Vehicle siren flag: " + OnOff(state.SirenFlag) + " | Light gate: " + (state.LightGate ? "READY" : "BLOCKED"),
                "Tracked light stage: " + (state.StageTracking ? state.Stage + "/" + state.StageCount : "DISABLED"),
                "Rumbler: " + OnOff(state.RumblerOn) + (state.RumblerEnabled ? "" : " (disabled in profile)"),
                "FIAMMS toggle: " + OnOff(state.FiammsOn)
            };
            foreach (DebugVoiceState voice in state.Voices)
            {
                lines.Add(Clean(voice.Name + ": " + voice.Status));
                string file = string.IsNullOrEmpty(voice.Sound) ? "None" : Path.GetFileName(voice.Sound);
                lines.Add(Clean("  WAV: " + file));
            }
            for (int i = 0; i < ExtraControls.Names.Length; i++)
            {
                int id = state.MappedExtras[i];
                string status = id < 0 ? "UNASSIGNED" : "extra " + id + ": " +
                    (!state.MappedExists[i] ? "MISSING" : OnOff(state.MappedEnabled[i]));
                lines.Add(ExtraControls.Labels[i] + ": " + status);
            }
            string heading = "All enabled extras" + (state.ScanningExtras ? " (scanning)" : "") + ": ";
            if (state.EnabledExtras.Length == 0) lines.Add(heading + "None");
            else
            {
                string line = heading;
                for (int i = 0; i < state.EnabledExtras.Length; i++)
                {
                    int first = state.EnabledExtras[i], last = first;
                    while (i + 1 < state.EnabledExtras.Length && state.EnabledExtras[i + 1] == last + 1) last = state.EnabledExtras[++i];
                    string group = first == last ? first.ToString(CultureInfo.InvariantCulture) : first + "-" + last;
                    if (line.Length + group.Length + 2 > MaxLine) { lines.Add(line.TrimEnd(' ', ',')); line = "  "; }
                    line += group + (i + 1 < state.EnabledExtras.Length ? ", " : "");
                }
                lines.Add(line);
            }
            return string.Join(Environment.NewLine, lines);
        }
    }
}
