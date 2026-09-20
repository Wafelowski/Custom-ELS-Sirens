using Rage;
using System;
using System.Threading;

[assembly: Rage.Attributes.Plugin("Custom ELS Sirens", Author = "Maggie Waggie", Description = "Per-vehicle ELS sirens with asynchronous WAV caching, shared audio output and positional sound.")]

namespace CustomELSSirens
{
    public static class EntryPoint
    {
        [Rage.Attributes.ConsoleCommand("ToggleSirenMenu", Description = "Opens or closes the Custom ELS Sirens configuration menu.")]
        public static void ToggleCustomSirenMenu() => MenuManager.ToggleCustomSirenMenu();

        public static void Main()
        {
            try
            {
                PluginConfig.Load();
                AudioEngine.Start();
                MenuManager.SetupMenu();
                DebugOverlay.Start();
                Game.DisplayNotification($"~b~Custom Sirens~w~ initialized. Press ~y~{PluginConfig.MenuKey}~w~ or use console for the menu.");
                while (true)
                {
                    GameFiber.Yield();
                    AudioEngine.Heartbeat();
                    while (AudioEngine.TryGetMessage(out string message))
                        Game.Console.Print("[CustomSirens] " + message);
                    MenuManager.Process();
                    SirenManager.ProcessLoop();
                    DebugOverlay.Update();
                }
            }
            catch (ThreadAbortException) { }
            catch (Exception ex)
            {
                Game.Console.Print("[CustomSirens] Plugin stopped: " + ex);
            }
            finally
            {
                DebugOverlay.Shutdown();
                SirenManager.Shutdown();
            }
        }
    }
}
