using Rage;
using System.IO;
using System.Windows.Forms;

namespace CustomELSSirens
{
    public static class PluginConfig
    {
        public static string BaseFolder = @"Plugins\CustomSirens\";
        public static string WavFolder = BaseFolder + @"WAVs\";
        public static string ProfilesFolder = BaseFolder + @"Profiles\";
        public static string ConfigFile = BaseFolder + "Config.ini";

        public static Keys MenuKey = Keys.F10;
        public static Keys Sound_Manul = (Keys)82;
        public static Keys Snd_SrnTon1 = (Keys)49;
        public static Keys Snd_SrnTon2 = (Keys)50;
        public static Keys Snd_SrnTon3 = (Keys)51;
        public static Keys Snd_SrnTon4 = (Keys)52;
        public static Keys Snd_SrnScan = (Keys)53;
        public static Keys Snd_SrnTonX = (Keys)54;
        public static Keys Snd_SrnPnic = (Keys)55;

        // Controller Support Configuration
        public static bool EnableControllerSupport = true;
        public static ControllerButtons Controller_Manul = ControllerButtons.B;
        public static ControllerButtons Controller_SrnToggle = ControllerButtons.DPadDown;
        public static ControllerButtons Controller_SrnTonX = ControllerButtons.DPadRight;

        public static float MasterVolume = 0.5f;
        public static bool AutomaticAiSirenCutoff = true;
        public static int AiScanInterval = 500;
        public static int MaxAiUnits = 10;

        public static float Tone1Vol = 1.0f;
        public static float Tone2Vol = 1.0f;
        public static float Tone3Vol = 1.0f;
        public static float Tone4Vol = 1.0f;
        public static float HornVol = 1.0f;
        public static float ManualVol = 1.0f;

        public static float MaxDistance = 250f;
        public static float MinDistance = 5f;
        public static float FalloffExponent = 1.5f;
        public static float ReverbIntensity = 1.0f;

        public static void Load()
        {
            if (!Directory.Exists(BaseFolder)) Directory.CreateDirectory(BaseFolder);

            InitializationFile ini = new InitializationFile(ConfigFile);
            if (!ini.Exists())
            {
                ini.Create();
                ini.Write("Settings", "MenuKey", MenuKey.ToString());
                ini.Write("Settings", "EnableControllerSupport", EnableControllerSupport.ToString());
                ini.Write("Settings", "Controller_Manul", Controller_Manul.ToString());
                ini.Write("Settings", "Controller_SrnToggle", Controller_SrnToggle.ToString());
                ini.Write("Settings", "Controller_SrnTonX", Controller_SrnTonX.ToString());
                ini.Write("Settings", "MasterVolume", MasterVolume.ToString());
                ini.Write("Settings", "AutomaticAiSirenCutoff", AutomaticAiSirenCutoff.ToString());
                ini.Write("Settings", "AiScanInterval", AiScanInterval.ToString());
                ini.Write("Settings", "MaxAiUnits", MaxAiUnits.ToString());
                ini.Write("Settings", "MaxDistance", MaxDistance.ToString());
                ini.Write("Settings", "MinDistance", MinDistance.ToString());
                ini.Write("Settings", "FalloffExponent", FalloffExponent.ToString());
                ini.Write("Settings", "ReverbIntensity", ReverbIntensity.ToString());

                ini.Write("SirenVolumes", "Tone1Vol", Tone1Vol.ToString());
                ini.Write("SirenVolumes", "Tone2Vol", Tone2Vol.ToString());
                ini.Write("SirenVolumes", "Tone3Vol", Tone3Vol.ToString());
                ini.Write("SirenVolumes", "Tone4Vol", Tone4Vol.ToString());
                ini.Write("SirenVolumes", "HornVol", HornVol.ToString());
                ini.Write("SirenVolumes", "ManualVol", ManualVol.ToString());
            }
            else
            {
                MenuKey = ini.ReadEnum("Settings", "MenuKey", MenuKey);
                EnableControllerSupport = ini.ReadBoolean("Settings", "EnableControllerSupport", EnableControllerSupport);
                Controller_Manul = ini.ReadEnum("Settings", "Controller_Manul", Controller_Manul);
                Controller_SrnToggle = ini.ReadEnum("Settings", "Controller_SrnToggle", Controller_SrnToggle);
                Controller_SrnTonX = ini.ReadEnum("Settings", "Controller_SrnTonX", Controller_SrnTonX);

                MasterVolume = ini.ReadSingle("Settings", "MasterVolume", MasterVolume);
                AutomaticAiSirenCutoff = ini.ReadBoolean("Settings", "AutomaticAiSirenCutoff", AutomaticAiSirenCutoff);
                AiScanInterval = ini.ReadInt32("Settings", "AiScanInterval", AiScanInterval);
                MaxAiUnits = ini.ReadInt32("Settings", "MaxAiUnits", MaxAiUnits);
                MaxDistance = ini.ReadSingle("Settings", "MaxDistance", MaxDistance);
                MinDistance = ini.ReadSingle("Settings", "MinDistance", MinDistance);
                FalloffExponent = ini.ReadSingle("Settings", "FalloffExponent", FalloffExponent);
                ReverbIntensity = ini.ReadSingle("Settings", "ReverbIntensity", ReverbIntensity);

                Tone1Vol = ini.ReadSingle("SirenVolumes", "Tone1Vol", Tone1Vol);
                Tone2Vol = ini.ReadSingle("SirenVolumes", "Tone2Vol", Tone2Vol);
                Tone3Vol = ini.ReadSingle("SirenVolumes", "Tone3Vol", Tone3Vol);
                Tone4Vol = ini.ReadSingle("SirenVolumes", "Tone4Vol", Tone4Vol);
                HornVol = ini.ReadSingle("SirenVolumes", "HornVol", HornVol);
                ManualVol = ini.ReadSingle("SirenVolumes", "ManualVol", ManualVol);
            }

            LoadELSKeybinds();
        }

        private static void LoadELSKeybinds()
        {
            string elsPath = @"ELS.ini";
            if (File.Exists(elsPath))
            {
                InitializationFile elsIni = new InitializationFile(elsPath);
                Sound_Manul = (Keys)elsIni.ReadInt32("CONTROLS", "Sound_Manul", (int)Sound_Manul);
                Snd_SrnTon1 = (Keys)elsIni.ReadInt32("CONTROLS", "Snd_SrnTon1", (int)Snd_SrnTon1);
                Snd_SrnTon2 = (Keys)elsIni.ReadInt32("CONTROLS", "Snd_SrnTon2", (int)Snd_SrnTon2);
                Snd_SrnTon3 = (Keys)elsIni.ReadInt32("CONTROLS", "Snd_SrnTon3", (int)Snd_SrnTon3);
                Snd_SrnTon4 = (Keys)elsIni.ReadInt32("CONTROLS", "Snd_SrnTon4", (int)Snd_SrnTon4);
                Snd_SrnScan = (Keys)elsIni.ReadInt32("CONTROLS", "Snd_SrnScan", (int)Snd_SrnScan);
                Snd_SrnTonX = (Keys)elsIni.ReadInt32("CONTROLS", "Snd_SrnTonX", (int)Snd_SrnTonX);
                Snd_SrnPnic = (Keys)elsIni.ReadInt32("CONTROLS", "Snd_SrnPnic", (int)Snd_SrnPnic);
            }
            else
            {
                Game.Console.Print("[CustomSirens] Warning: 'ELS.ini' not found in root directory! Default keybinds active.");
            }
        }

        public static void SaveConfig()
        {
            InitializationFile ini = new InitializationFile(ConfigFile);
            ini.Write("SirenVolumes", "Tone1Vol", Tone1Vol.ToString());
            ini.Write("SirenVolumes", "Tone2Vol", Tone2Vol.ToString());
            ini.Write("SirenVolumes", "Tone3Vol", Tone3Vol.ToString());
            ini.Write("SirenVolumes", "Tone4Vol", Tone4Vol.ToString());
            ini.Write("SirenVolumes", "HornVol", HornVol.ToString());
            ini.Write("SirenVolumes", "ManualVol", ManualVol.ToString());

            ini.Write("Settings", "EnableControllerSupport", EnableControllerSupport.ToString());
            ini.Write("Settings", "Controller_Manul", Controller_Manul.ToString());
            ini.Write("Settings", "Controller_SrnToggle", Controller_SrnToggle.ToString());
            ini.Write("Settings", "Controller_SrnTonX", Controller_SrnTonX.ToString());

            ini.Write("Settings", "MasterVolume", MasterVolume.ToString());
            ini.Write("Settings", "AutomaticAiSirenCutoff", AutomaticAiSirenCutoff.ToString());
            ini.Write("Settings", "AiScanInterval", AiScanInterval.ToString());
            ini.Write("Settings", "MaxAiUnits", MaxAiUnits.ToString());
            ini.Write("Settings", "FalloffExponent", FalloffExponent.ToString());
            ini.Write("Settings", "MaxDistance", MaxDistance.ToString());
            ini.Write("Settings", "ReverbIntensity", ReverbIntensity.ToString());
        }
    }
}