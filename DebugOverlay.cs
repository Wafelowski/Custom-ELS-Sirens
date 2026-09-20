using System;
using System.Drawing;
using System.Threading;
using Rage;

namespace CustomELSSirens
{
    internal static class DebugOverlay
    {
        // The render callback reads only this immutable snapshot. It never
        // touches entities, natives, profile dictionaries or audio devices.
        private sealed class Snapshot
        {
            internal readonly DebugLine[] Lines;
            internal readonly Size Resolution;
            internal readonly RectangleF Panel;
            internal readonly PointF Origin;
            internal readonly float FontSize;
            internal readonly float LineHeight;
            internal Snapshot(DebugLine[] lines, Size resolution, RectangleF panel, PointF origin, float fontSize, float lineHeight)
            { Lines = lines; Resolution = resolution; Panel = panel; Origin = origin; FontSize = fontSize; LineHeight = lineHeight; }

            internal bool Matches(DebugLine[] lines, Size resolution)
            {
                if (Resolution != resolution || Lines.Length != lines.Length) return false;
                for (int i = 0; i < lines.Length; i++)
                    if (Lines[i].Text != lines[i].Text || Lines[i].Tone != lines[i].Tone || Lines[i].Bold != lines[i].Bold) return false;
                return true;
            }
        }

        private static volatile Snapshot snapshot;
        private static Vehicle trackedVehicle;
        private static DebugExtraTracker extras;
        private static int nextRefresh;
        private static bool subscribed;
        private static volatile bool renderFaulted;
        private static string renderError;
        private const string Font = "Consolas";

        internal static void Start()
        {
            if (subscribed) return;
            Game.RawFrameRender += Draw;
            subscribed = true;
        }

        internal static void Hide()
        {
            snapshot = null;
            trackedVehicle = null;
            extras = null;
            nextRefresh = Environment.TickCount;
            renderFaulted = false;
        }

        internal static void Shutdown()
        {
            if (subscribed) Game.RawFrameRender -= Draw;
            subscribed = false;
            Hide();
        }

        internal static void Update()
        {
            try { UpdateCore(); }
            catch (Exception ex) { Fail(ex); }
        }

        private static void UpdateCore()
        {
            string error = Interlocked.Exchange(ref renderError, null);
            if (error != null) Game.Console.Print("[CustomSirens] DEBUG overlay drawing failed: " + error);
            if (!PluginConfig.Debug) { Hide(); return; }
            Vehicle vehicle = SirenManager.DebugVehicle;
            if (vehicle == null) { Hide(); return; }
            if (renderFaulted) return;
            int now = Environment.TickCount;
            if (vehicle != trackedVehicle)
            {
                snapshot = null;
                trackedVehicle = vehicle;
                extras = new DebugExtraTracker();
                nextRefresh = now;
            }
            if (unchecked(now - nextRefresh) < 0) return;
            nextRefresh = unchecked(now + 100);

            var state = SirenManager.CaptureDebugState();
            if (state == null) { Hide(); return; }
            var nativeExtras = new NativeVehicleExtras(vehicle);
            state.EnabledExtras = extras.Refresh(nativeExtras, state.MappedExtras);
            state.ScanningExtras = extras.IsScanning;
            state.MappedExists = new bool[ExtraControls.Names.Length];
            state.MappedEnabled = new bool[ExtraControls.Names.Length];
            for (int i = 0; i < state.MappedExtras.Length; i++)
            {
                int id = state.MappedExtras[i];
                state.MappedExists[i] = extras.Exists(id);
                state.MappedEnabled[i] = Array.IndexOf(state.EnabledExtras, id) >= 0;
            }

            DebugLine[] lines = DebugText.BuildLines(state);
            Size resolution = Game.Resolution;
            if (resolution.Width < 160 || resolution.Height < 120) { snapshot = null; return; }
            Snapshot previous = snapshot;
            if (previous != null && previous.Matches(lines, resolution)) return;
            float scale = Math.Max(0.5f, Math.Min(2f, resolution.Height / 1080f));
            float fontSize = 14f * scale;
            float padding = 12f * scale, margin = 24f * scale;
            SizeF size = Measure(lines, fontSize, out float lineHeight);
            float fit = Math.Min(1f, Math.Min((resolution.Width - 2 * (margin + padding)) / Math.Max(1f, size.Width),
                (resolution.Height - 2 * (margin + padding)) / Math.Max(1f, size.Height)));
            if (fit < 1f) { fontSize *= fit; size = Measure(lines, fontSize, out lineHeight); }
            float width = size.Width + 2 * padding, height = size.Height + 2 * padding;
            float left = Math.Max(margin, resolution.Width - margin - width);
            snapshot = new Snapshot(lines, resolution, new RectangleF(left, margin, width, height), new PointF(left + padding, margin + padding), fontSize, lineHeight);
        }

        private static float BoldOffset(float fontSize) => Math.Max(0.5f, fontSize / 18f);

        private static SizeF Measure(DebugLine[] lines, float fontSize, out float lineHeight)
        {
            float width = 0f;
            lineHeight = fontSize * 1.4f;
            foreach (DebugLine line in lines)
            {
                SizeF size = Rage.Graphics.MeasureText(line.Text, Font, fontSize);
                width = Math.Max(width, size.Width + (line.Bold ? BoldOffset(fontSize) : 0f));
                lineHeight = Math.Max(lineHeight, size.Height + 2f);
            }
            return new SizeF(width, lineHeight * lines.Length);
        }

        private static Color TextColor(DebugTone tone)
        {
            switch (tone)
            {
                case DebugTone.Heading: return Color.FromArgb(255, 94, 210, 255);
                case DebugTone.Info: return Color.FromArgb(255, 189, 168, 255);
                case DebugTone.Active: return Color.FromArgb(255, 113, 230, 158);
                case DebugTone.Inactive: return Color.FromArgb(255, 172, 184, 200);
                case DebugTone.Warning: return Color.FromArgb(255, 255, 201, 106);
                case DebugTone.Error: return Color.FromArgb(255, 255, 132, 132);
                case DebugTone.Detail: return Color.FromArgb(255, 154, 171, 194);
                default: return Color.FromArgb(255, 236, 243, 250);
            }
        }

        private static void Draw(object sender, GraphicsEventArgs e)
        {
            Snapshot current = snapshot;
            if (current == null || renderFaulted) return;
            try
            {
                e.Graphics.DrawRectangle(current.Panel, Color.FromArgb(225, 8, 12, 18));
                for (int i = 0; i < current.Lines.Length; i++)
                {
                    DebugLine line = current.Lines[i];
                    var position = new PointF(current.Origin.X, current.Origin.Y + i * current.LineHeight);
                    Color color = TextColor(line.Tone);
                    e.Graphics.DrawText(line.Text, Font, current.FontSize, position, color);
                    // The basic DrawText overload has no weight parameter. A
                    // narrow second pass adds emphasis without allocating fonts.
                    if (line.Bold)
                        e.Graphics.DrawText(line.Text, Font, current.FontSize,
                            new PointF(position.X + BoldOffset(current.FontSize), position.Y), color);
                }
            }
            catch (Exception ex) { Fail(ex); }
        }

        private static void Fail(Exception ex)
        {
            snapshot = null;
            renderFaulted = true;
            Interlocked.Exchange(ref renderError, ex.Message);
        }
    }
}
