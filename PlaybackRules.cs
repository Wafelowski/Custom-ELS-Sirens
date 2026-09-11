using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CustomELSSirens
{
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
        internal bool IsActive { get; private set; }
        internal void Reset() => IsActive = false;
        internal void Update(bool pressed, bool enabled, Action stop, Action restart)
        {
            bool interrupt = pressed && enabled;
            if (interrupt == IsActive) return;
            IsActive = interrupt;
            if (interrupt) stop();
            else restart();
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
