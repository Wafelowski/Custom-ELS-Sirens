using Rage;
using System;
using System.Threading;

[assembly: Rage.Attributes.Plugin("Custom ELS Sirens", Author = "Maggie Waggie", Description = "Custom per Vehicle ELS compatible Sirens, instant global RAM caching, multi-vehicle tracking, and acoustic cabin dampening.")]

namespace CustomELSSirens
{
    public static class EntryPoint
    {
        private static volatile int _fiberHeartbeat;

        [Rage.Attributes.ConsoleCommand("ToggleSirenMenu", Description = "Opens or closes the Custom ELS Sirens configuration menu.")]
        public static void ToggleCustomSirenMenu()
        {
            MenuManager.ToggleCustomSirenMenu();
        }

        public static void Main()
        {
            UpdateManager.CheckForUpdates();
            PluginConfig.Load();
            MenuManager.SetupMenu();
            Game.DisplayNotification($"~b~Custom Sirens~w~ initialized. Press ~y~{PluginConfig.MenuKey}~w~ or use console for the menu.");

            new System.Threading.Timer(_ =>
            {
                if (Environment.TickCount - _fiberHeartbeat > 250)
                {
                    SirenManager.DropVolumes();
                }
            }, null, 500, 100);

            GameFiber.StartNew(MainLoop);
        }

        private static void MainLoop()
        {
            try
            {
                while (true)
                {
                    GameFiber.Yield();
                    _fiberHeartbeat = Environment.TickCount;

                    MenuManager.Process();
                    SirenManager.ProcessLoop();
                }
            }
            catch (ThreadAbortException)
            {
            }
            finally
            {
                SirenManager.Shutdown();
            }
        }
    }
}