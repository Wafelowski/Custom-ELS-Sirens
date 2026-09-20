using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CustomELSSirens
{
    internal enum HornCycleMode { Off, CarHorn, SirenHorn, Silent }

    internal struct HornBehavior
    {
        internal bool Cycle;
        internal bool PlaySirenHorn;
        internal bool SuppressCarHorn;
        internal bool InterruptSiren;
    }

    internal static class HornModes
    {
        internal static readonly string[] Labels = { "Off", "With horn (car horn)", "With horn (siren horn)", "Without horn" };

        internal static HornCycleMode Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOf(',') >= 0) return HornCycleMode.Off;
            return Enum.TryParse(value, true, out HornCycleMode mode) && Enum.IsDefined(typeof(HornCycleMode), mode)
                ? mode : HornCycleMode.Off;
        }

        internal static HornBehavior Resolve(HornCycleMode mode, bool hasSirenHorn, bool interrupt)
        {
            switch (mode)
            {
                case HornCycleMode.CarHorn:
                    return new HornBehavior { Cycle = true, InterruptSiren = interrupt };
                case HornCycleMode.SirenHorn:
                    return new HornBehavior { Cycle = true, PlaySirenHorn = hasSirenHorn, SuppressCarHorn = true, InterruptSiren = interrupt && hasSirenHorn };
                case HornCycleMode.Silent:
                    return new HornBehavior { Cycle = true, SuppressCarHorn = true };
                default:
                    // Off disables cycling, preserving the normal horn route.
                    return new HornBehavior { PlaySirenHorn = hasSirenHorn, SuppressCarHorn = hasSirenHorn, InterruptSiren = interrupt };
            }
        }
    }

    internal static class ToneSlots
    {
        internal const int Count = 6;
        internal static readonly string[] Keys = { "Tone1", "Tone2", "Tone3", "Tone4", "Tone5", "Tone6" };
        internal static int Next(int current, Func<int, bool> available)
        {
            for (int i = 0; i < Count; i++)
            {
                current = current >= Count || current < 0 ? 1 : current + 1;
                if (available(current)) return current;
            }
            return 0;
        }
    }

    // The selected tone remains separate from whether its voice is running.
    // Releasing a horn restarts the latest selection, never an obsolete request.
    internal sealed class HornInterruption
    {
        private bool wasPressed;
        internal bool IsActive { get; private set; }
        internal void Reset() { IsActive = false; wasPressed = false; }
        internal void Update(bool pressed, bool enabled, Action stop, Action restart, Action cycle = null)
        {
            bool risingEdge = pressed && !wasPressed;
            wasPressed = pressed;
            bool interrupt = pressed && enabled;
            if (interrupt != IsActive)
            {
                IsActive = interrupt;
                if (interrupt) stop();
                else restart();
            }
            // Set interruption before changing selection, so a horn press cannot
            // briefly start the next tone before it is supposed to restart.
            if (risingEdge) cycle?.Invoke();
        }
    }

    internal static class KeyBindings
    {
        internal static bool IsDown(Keys binding, Func<Keys, bool> down)
        {
            Keys key = binding & Keys.KeyCode;
            if (key == Keys.None) return false;
            bool control = down(Keys.LControlKey) || down(Keys.RControlKey);
            bool shift = down(Keys.LShiftKey) || down(Keys.RShiftKey);
            bool alt = down(Keys.LMenu) || down(Keys.RMenu);
            return control == ((binding & Keys.Control) != 0) &&
                shift == ((binding & Keys.Shift) != 0) &&
                alt == ((binding & Keys.Alt) != 0) && down(key);
        }
    }

    internal interface IVehicleExtras
    {
        bool Exists(int extra);
        bool IsEnabled(int extra);
        void SetEnabled(int extra, bool enabled);
    }

    internal static class ExtraControls
    {
        internal static readonly string[] Names = { "RedBeacon", "MatrixText1", "MatrixText2", "MatrixText3" };
        internal static readonly string[] Labels = { "Red beacon", "Matrix text 1", "Matrix text 2", "Matrix text 3" };
        internal const int Disabled = -1;
        internal const int MaxId = 255;
        internal static int ValidateId(int id) => id >= 0 && id <= MaxId ? id : Disabled;

        internal static void ValidateMappings(int[] ids)
        {
            // An extra cannot be both the independent beacon and a text mode.
            // Keep the first assignment and disable later duplicates.
            var used = new HashSet<int>();
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = ValidateId(ids[i]);
                if (ids[i] >= 0 && !used.Add(ids[i])) ids[i] = Disabled;
            }
        }

        internal static bool Toggle(IVehicleExtras vehicle, int[] mappings, int action)
        {
            if (action < 0 || action >= mappings.Length) return false;
            int id = ValidateId(mappings[action]);
            if (id < 0 || !vehicle.Exists(id)) return false;
            bool enable = !vehicle.IsEnabled(id);
            if (enable && action > 0)
            {
                for (int i = 1; i < mappings.Length; i++)
                {
                    int other = ValidateId(mappings[i]);
                    if (i != action && other >= 0 && other != id && vehicle.Exists(other) && vehicle.IsEnabled(other))
                        vehicle.SetEnabled(other, false);
                }
            }
            vehicle.SetEnabled(id, enable);
            return true;
        }
    }
}
