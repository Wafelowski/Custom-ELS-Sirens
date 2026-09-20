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
        internal bool HornInterruptsSiren;
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
        // DEBUG observes native extras independently of the assignable 1-12 range.
        private const int MaxProbeId = 255;
        private readonly HashSet<int> known = new HashSet<int>();
        private int nextId;
        internal bool IsScanning => nextId <= MaxProbeId;
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

    internal enum DebugTone { Normal, Heading, Info, Active, Inactive, Warning, Error, Detail }

    internal readonly struct DebugLine
    {
        internal readonly string Text;
        internal readonly DebugTone Tone;
        internal readonly bool Bold;
        internal DebugLine(string text, DebugTone tone, bool bold)
        { Text = text; Tone = tone; Bold = bold; }
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

        private static DebugTone VoiceTone(string status)
        {
            switch (status)
            {
                case "PLAYING": return DebugTone.Active;
                case "OFF": return DebugTone.Inactive;
                case "LOADING":
                case "MUTED":
                case "FADING OUT":
                case "STOPPED FOR HORN": return DebugTone.Warning;
                default: return DebugTone.Normal;
            }
        }

        internal static DebugLine[] BuildLines(VehicleDebugState state)
        {
            if (state == null) return new DebugLine[0];
            var lines = new List<DebugLine>();
            void Add(string text, DebugTone tone = DebugTone.Normal, bool bold = false)
                => lines.Add(new DebugLine(Clean(text), tone, bold));

            Add("CUSTOM ELS SIRENS | DEBUG", DebugTone.Heading, true);
            Add("Vehicle: " + state.Model, DebugTone.Normal, true);
            Add("AUDIO", DebugTone.Heading, true);
            DebugTone outputTone = state.Output == "DEVICE UNAVAILABLE" ? DebugTone.Error :
                state.Output == "RUNNING" ? (state.MasterVolume > 0f ? DebugTone.Active : DebugTone.Warning) :
                state.Output == "STOPPED" ? DebugTone.Inactive : DebugTone.Warning;
            Add("Audio: " + state.Output + " | Master: " + Math.Round(state.MasterVolume * 100).ToString(CultureInfo.InvariantCulture) + "%", outputTone, true);
            foreach (DebugVoiceState voice in state.Voices)
            {
                Add(voice.Name + ": " + voice.Status, VoiceTone(voice.Status), voice.Status == "PLAYING");
                string file = string.IsNullOrEmpty(voice.Sound) ? "None" : Path.GetFileName(voice.Sound);
                Add("  WAV: " + file, DebugTone.Detail);
            }
            Add("CONTROLS", DebugTone.Heading, true);
            Add("Horn cycle: " + HornModes.Labels[(int)state.HornMode], state.HornMode == HornCycleMode.Off ? DebugTone.Inactive : DebugTone.Info);
            Add("Horn interrupts siren: " + OnOff(state.HornInterruptsSiren), state.HornInterruptsSiren ? DebugTone.Info : DebugTone.Inactive);
            Add("Car horn: " + (state.NativeHornSuppressed ? "SUPPRESSED" : state.NativeHornPressed ? "PRESSED" : "OFF"),
                state.NativeHornSuppressed ? DebugTone.Warning : state.NativeHornPressed ? DebugTone.Active : DebugTone.Inactive,
                state.NativeHornPressed && !state.NativeHornSuppressed);
            Add("Rumbler: " + OnOff(state.RumblerOn) + (state.RumblerEnabled ? "" : " (disabled in profile)"),
                state.RumblerOn ? DebugTone.Active : DebugTone.Inactive, state.RumblerOn);
            Add("FIAMMS toggle: " + OnOff(state.FiammsOn), state.FiammsOn ? DebugTone.Active : DebugTone.Inactive, state.FiammsOn);
            Add("VEHICLE & EXTRAS", DebugTone.Heading, true);
            Add("Vehicle siren flag: " + OnOff(state.SirenFlag) + " | Light gate: " + (state.LightGate ? "READY" : "BLOCKED"),
                state.LightGate ? DebugTone.Info : DebugTone.Warning);
            Add("Tracked light stage: " + (state.StageTracking ? state.Stage + "/" + state.StageCount : "DISABLED"),
                state.StageTracking ? DebugTone.Info : DebugTone.Inactive);
            for (int i = 0; i < ExtraControls.Names.Length; i++)
            {
                int id = state.MappedExtras[i];
                string status = id < 0 ? "UNASSIGNED" : "extra " + id + ": " +
                    (!state.MappedExists[i] ? "MISSING" : OnOff(state.MappedEnabled[i]));
                DebugTone tone = id < 0 ? DebugTone.Inactive : !state.MappedExists[i] ? DebugTone.Error :
                    state.MappedEnabled[i] ? DebugTone.Active : DebugTone.Inactive;
                Add(ExtraControls.Labels[i] + ": " + status, tone, tone == DebugTone.Active || tone == DebugTone.Error);
            }
            string heading = "All enabled extras" + (state.ScanningExtras ? " (scanning)" : "") + ": ";
            DebugTone extrasTone = state.ScanningExtras ? DebugTone.Warning : DebugTone.Info;
            if (state.EnabledExtras.Length == 0) Add(heading + "None", state.ScanningExtras ? DebugTone.Warning : DebugTone.Inactive);
            else
            {
                string line = heading;
                for (int i = 0; i < state.EnabledExtras.Length; i++)
                {
                    int first = state.EnabledExtras[i], last = first;
                    while (i + 1 < state.EnabledExtras.Length && state.EnabledExtras[i + 1] == last + 1) last = state.EnabledExtras[++i];
                    string group = first == last ? first.ToString(CultureInfo.InvariantCulture) : first + "-" + last;
                    if (line.Length + group.Length + 2 > MaxLine) { Add(line.TrimEnd(' ', ','), extrasTone); line = "  "; }
                    line += group + (i + 1 < state.EnabledExtras.Length ? ", " : "");
                }
                Add(line, extrasTone);
            }
            return lines.ToArray();
        }

        internal static string Build(VehicleDebugState state)
        {
            DebugLine[] lines = BuildLines(state);
            var text = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++) text[i] = lines[i].Text;
            return string.Join(Environment.NewLine, text);
        }
    }
}
