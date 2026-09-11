using Rage;
using System;
using System.Globalization;
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
        // Keep the tone keys introduced in 1.9 compatible with existing configs.
        public static Keys Snd_SrnTon5 = Keys.D8;
        public static Keys Snd_SrnTon6 = Keys.D9;
        public static Keys Toggle_Rumbler = Keys.F11;
        public static Keys Toggle_FIAMMS = Keys.F9;
        public static Keys Toggle_RedBeacon = Keys.None;
        public static Keys Toggle_MatrixText1 = Keys.None;
        public static Keys Toggle_MatrixText2 = Keys.None;
        public static Keys Toggle_MatrixText3 = Keys.None;
        public static Keys Snd_SrnTonX = (Keys)54;
        public static Keys Toggle_Lsts = Keys.J;

        public static bool EnableControllerSupport = true;
        public static ControllerButtons Controller_Manul = ControllerButtons.B;
        public static ControllerButtons Controller_SrnToggle = ControllerButtons.DPadDown;
        public static ControllerButtons Controller_SrnTonX = ControllerButtons.DPadRight;

        public static bool UseElsKeybinds = true;
        public static bool EnableLightStageTracking = false;
        public static int CustomLightStageAmount = 3;
        public static bool SirenLightRestriction = true;
        public static bool HornInterruptsSiren = true;
        public static bool HornCyclesSiren = false;
        public static float MasterVolume = 0.5f;
        public static bool AutomaticAiSirenCutoff = true;
        public static int AiScanInterval = 500;
        public static int MaxAiUnits = 10;

        public static float Tone1Vol = 1.0f;
        public static float Tone2Vol = 1.0f;
        public static float Tone3Vol = 1.0f;
        public static float Tone4Vol = 1.0f;
        public static float Tone5Vol = 1.0f;
        public static float Tone6Vol = 1.0f;
        public static float HornVol = 1.0f;
        public static float ManualVol = 1.0f;
        public static float FIAMMSVol = 1.0f;

        public static float MaxDistance = 250f;
        public static float MinDistance = 5f;
        public static float FalloffExponent = 1.5f;
        public static float ReverbIntensity = 1.0f;

        public static void Load()
        {
            Directory.CreateDirectory(BaseFolder);
            Directory.CreateDirectory(WavFolder);
            Directory.CreateDirectory(ProfilesFolder);

            InitializationFile ini = new InitializationFile(ConfigFile);

            UseElsKeybinds = ini.ReadBoolean("Settings", "UseElsKeybinds", UseElsKeybinds);

            if (!ini.Exists())
            {
                SaveConfig();
            }
            else
            {
                EnableLightStageTracking = ini.ReadBoolean("Settings", "EnableLightStageTracking", EnableLightStageTracking);
                CustomLightStageAmount = ini.ReadInt32("Settings", "CustomLightStageAmount", CustomLightStageAmount);
                MasterVolume = ReadFloat(ini, "Settings", "MasterVolume", MasterVolume);
                AutomaticAiSirenCutoff = ini.ReadBoolean("Settings", "AutomaticAiSirenCutoff", AutomaticAiSirenCutoff);
                AiScanInterval = ini.ReadInt32("Settings", "AiScanInterval", AiScanInterval);
                MaxAiUnits = ini.ReadInt32("Settings", "MaxAiUnits", MaxAiUnits);
                MaxDistance = ReadFloat(ini, "Settings", "MaxDistance", MaxDistance);
                MinDistance = ReadFloat(ini, "Settings", "MinDistance", MinDistance);
                FalloffExponent = ReadFloat(ini, "Settings", "FalloffExponent", FalloffExponent);
                ReverbIntensity = ReadFloat(ini, "Settings", "ReverbIntensity", ReverbIntensity);
                EnableControllerSupport = ini.ReadBoolean("Settings", "EnableControllerSupport", EnableControllerSupport);
                SirenLightRestriction = ini.ReadBoolean("Settings", "SirenLightRestriction", SirenLightRestriction);
                HornInterruptsSiren = ini.ReadBoolean("Settings", "HornInterruptsSiren", HornInterruptsSiren);
                HornCyclesSiren = ini.ReadBoolean("Settings", "HornCyclesSiren", HornCyclesSiren);

                MenuKey = ini.ReadEnum("Keybinds", "MenuKey", ini.ReadEnum("Settings", "MenuKey", MenuKey));

                {
                    Sound_Manul = ini.ReadEnum("Keybinds", "Sound_Manul", Sound_Manul);
                    Snd_SrnTon1 = ini.ReadEnum("Keybinds", "Snd_SrnTon1", Snd_SrnTon1);
                    Snd_SrnTon2 = ini.ReadEnum("Keybinds", "Snd_SrnTon2", Snd_SrnTon2);
                    Snd_SrnTon3 = ini.ReadEnum("Keybinds", "Snd_SrnTon3", Snd_SrnTon3);
                    Snd_SrnTon4 = ini.ReadEnum("Keybinds", "Snd_SrnTon4", Snd_SrnTon4);
                    Snd_SrnTon5 = ini.ReadEnum("Keybinds", "Snd_SrnTon5", Snd_SrnTon5);
                    Snd_SrnTon6 = ini.ReadEnum("Keybinds", "Snd_SrnTon6", Snd_SrnTon6);
                    Toggle_Rumbler = ini.ReadEnum("Keybinds", "Toggle_Rumbler", Toggle_Rumbler);
                    Toggle_FIAMMS = ini.ReadEnum("Keybinds", "Toggle_FIAMMS", Toggle_FIAMMS);
                    Toggle_RedBeacon = ini.ReadEnum("Keybinds", "Toggle_RedBeacon", Toggle_RedBeacon);
                    Toggle_MatrixText1 = ini.ReadEnum("Keybinds", "Toggle_MatrixText1", Toggle_MatrixText1);
                    Toggle_MatrixText2 = ini.ReadEnum("Keybinds", "Toggle_MatrixText2", Toggle_MatrixText2);
                    Toggle_MatrixText3 = ini.ReadEnum("Keybinds", "Toggle_MatrixText3", Toggle_MatrixText3);
                    Snd_SrnTonX = ini.ReadEnum("Keybinds", "Snd_SrnTonX", Snd_SrnTonX);
                    Toggle_Lsts = ini.ReadEnum("Keybinds", "Toggle_Lsts", Toggle_Lsts);
                }

                Controller_Manul = ini.ReadEnum("Keybinds", "Controller_Manul", ini.ReadEnum("Settings", "Controller_Manul", Controller_Manul));
                Controller_SrnToggle = ini.ReadEnum("Keybinds", "Controller_SrnToggle", ini.ReadEnum("Settings", "Controller_SrnToggle", Controller_SrnToggle));
                Controller_SrnTonX = ini.ReadEnum("Keybinds", "Controller_SrnTonX", ini.ReadEnum("Settings", "Controller_SrnTonX", Controller_SrnTonX));

                Tone1Vol = ReadFloat(ini, "SirenVolumes", "Tone1Vol", Tone1Vol);
                Tone2Vol = ReadFloat(ini, "SirenVolumes", "Tone2Vol", Tone2Vol);
                Tone3Vol = ReadFloat(ini, "SirenVolumes", "Tone3Vol", Tone3Vol);
                Tone4Vol = ReadFloat(ini, "SirenVolumes", "Tone4Vol", Tone4Vol);
                Tone5Vol = ReadFloat(ini, "SirenVolumes", "Tone5Vol", Tone5Vol);
                Tone6Vol = ReadFloat(ini, "SirenVolumes", "Tone6Vol", Tone6Vol);
                HornVol = ReadFloat(ini, "SirenVolumes", "HornVol", HornVol);
                ManualVol = ReadFloat(ini, "SirenVolumes", "ManualVol", ManualVol);
                FIAMMSVol = ReadFloat(ini, "SirenVolumes", "FIAMMSVol", FIAMMSVol);
            }
            Validate();
            if (UseElsKeybinds) LoadELSKeybinds();
        }

        public static float ReadFloat(InitializationFile ini, string section, string key, float fallback)
        {
            string text = ini.ReadString(section, key, string.Empty);
            float value;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                !float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) &&
                !float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return fallback;
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }

        public static Keys GetToneKey(int tone)
        {
            switch (tone)
            {
                case 1: return Snd_SrnTon1;
                case 2: return Snd_SrnTon2;
                case 3: return Snd_SrnTon3;
                case 4: return Snd_SrnTon4;
                case 5: return Snd_SrnTon5;
                case 6: return Snd_SrnTon6;
                default: return Keys.None;
            }
        }

        public static float DefaultSirenVolume(string key)
        {
            switch (key)
            {
                case "Tone1Vol": return Tone1Vol;
                case "Tone2Vol": return Tone2Vol;
                case "Tone3Vol": return Tone3Vol;
                case "Tone4Vol": return Tone4Vol;
                case "Tone5Vol": return Tone5Vol;
                case "Tone6Vol": return Tone6Vol;
                case "HornVol": return HornVol;
                case "ManualVol": return ManualVol;
                case "FIAMMSVol": return FIAMMSVol;
                default: return 1f;
            }
        }

        public static void Validate()
        {
            MasterVolume = AudioMath.Clamp(MasterVolume, 0f, 1f);
            Tone1Vol = AudioMath.Clamp(Tone1Vol, 0f, 1f);
            Tone2Vol = AudioMath.Clamp(Tone2Vol, 0f, 1f);
            Tone3Vol = AudioMath.Clamp(Tone3Vol, 0f, 1f);
            Tone4Vol = AudioMath.Clamp(Tone4Vol, 0f, 1f);
            Tone5Vol = AudioMath.Clamp(Tone5Vol, 0f, 1f);
            Tone6Vol = AudioMath.Clamp(Tone6Vol, 0f, 1f);
            HornVol = AudioMath.Clamp(HornVol, 0f, 1f);
            ManualVol = AudioMath.Clamp(ManualVol, 0f, 1f);
            FIAMMSVol = AudioMath.Clamp(FIAMMSVol, 0f, 1f);
            CustomLightStageAmount = Math.Max(1, Math.Min(4, CustomLightStageAmount));
            AiScanInterval = Math.Max(100, Math.Min(5000, AiScanInterval));
            MaxAiUnits = Math.Max(1, Math.Min(50, MaxAiUnits));
            MaxDistance = AudioMath.Clamp(MaxDistance, 50f, 1000f);
            MinDistance = AudioMath.Clamp(MinDistance, 0f, MaxDistance - 1f);
            FalloffExponent = AudioMath.Clamp(FalloffExponent, 0.5f, 10f);
            ReverbIntensity = AudioMath.Clamp(ReverbIntensity, 0f, 2f);
        }

        private static void LoadELSKeybinds()
        {
            string elsPath = @"ELS.ini";
            if (File.Exists(elsPath))
            {
                InitializationFile elsIni = new InitializationFile(elsPath);
                Toggle_Lsts = (Keys)elsIni.ReadInt32("CONTROLS", "Toggle_Lsts", 74);
                Sound_Manul = (Keys)elsIni.ReadInt32("CONTROLS", "Sound_Manul", (int)Sound_Manul);
                Snd_SrnTon1 = (Keys)elsIni.ReadInt32("CONTROLS", "Snd_SrnTon1", (int)Snd_SrnTon1);
                Snd_SrnTon2 = (Keys)elsIni.ReadInt32("CONTROLS", "Snd_SrnTon2", (int)Snd_SrnTon2);
                Snd_SrnTon3 = (Keys)elsIni.ReadInt32("CONTROLS", "Snd_SrnTon3", (int)Snd_SrnTon3);
                Snd_SrnTon4 = (Keys)elsIni.ReadInt32("CONTROLS", "Snd_SrnTon4", (int)Snd_SrnTon4);
                Snd_SrnTonX = (Keys)elsIni.ReadInt32("CONTROLS", "Snd_SrnTonX", (int)Snd_SrnTonX);
            }
            else
            {
                Game.Console.Print("[CustomSirens] Warning: 'ELS.ini' not found in root directory! Custom defaults active.");
            }
        }

        public static void SaveConfig()
        {
            Validate();
            Directory.CreateDirectory(BaseFolder);
            InitializationFile ini = new InitializationFile(ConfigFile);
            if (!ini.Exists()) ini.Create();
            ini.Write("SirenVolumes", "Tone1Vol", Tone1Vol.ToString(CultureInfo.InvariantCulture));
            ini.Write("SirenVolumes", "Tone2Vol", Tone2Vol.ToString(CultureInfo.InvariantCulture));
            ini.Write("SirenVolumes", "Tone3Vol", Tone3Vol.ToString(CultureInfo.InvariantCulture));
            ini.Write("SirenVolumes", "Tone4Vol", Tone4Vol.ToString(CultureInfo.InvariantCulture));
            ini.Write("SirenVolumes", "Tone5Vol", Tone5Vol.ToString(CultureInfo.InvariantCulture));
            ini.Write("SirenVolumes", "Tone6Vol", Tone6Vol.ToString(CultureInfo.InvariantCulture));
            ini.Write("SirenVolumes", "HornVol", HornVol.ToString(CultureInfo.InvariantCulture));
            ini.Write("SirenVolumes", "ManualVol", ManualVol.ToString(CultureInfo.InvariantCulture));
            ini.Write("SirenVolumes", "FIAMMSVol", FIAMMSVol.ToString(CultureInfo.InvariantCulture));

            ini.Write("Settings", "UseElsKeybinds", UseElsKeybinds.ToString());
            ini.Write("Settings", "EnableLightStageTracking", EnableLightStageTracking.ToString());
            ini.Write("Settings", "CustomLightStageAmount", CustomLightStageAmount.ToString());
            ini.Write("Settings", "EnableControllerSupport", EnableControllerSupport.ToString());
            ini.Write("Settings", "SirenLightRestriction", SirenLightRestriction.ToString());
            ini.Write("Settings", "HornInterruptsSiren", HornInterruptsSiren.ToString());
            ini.Write("Settings", "HornCyclesSiren", HornCyclesSiren.ToString());
            ini.Write("Settings", "MasterVolume", MasterVolume.ToString(CultureInfo.InvariantCulture));
            ini.Write("Settings", "AutomaticAiSirenCutoff", AutomaticAiSirenCutoff.ToString());
            ini.Write("Settings", "AiScanInterval", AiScanInterval.ToString());
            ini.Write("Settings", "MaxAiUnits", MaxAiUnits.ToString());
            ini.Write("Settings", "FalloffExponent", FalloffExponent.ToString(CultureInfo.InvariantCulture));
            ini.Write("Settings", "MaxDistance", MaxDistance.ToString(CultureInfo.InvariantCulture));
            ini.Write("Settings", "MinDistance", MinDistance.ToString(CultureInfo.InvariantCulture));
            ini.Write("Settings", "ReverbIntensity", ReverbIntensity.ToString(CultureInfo.InvariantCulture));

            ini.Write("Keybinds", "MenuKey", MenuKey.ToString());
            ini.Write("Keybinds", "Sound_Manul", Sound_Manul.ToString());
            ini.Write("Keybinds", "Snd_SrnTon1", Snd_SrnTon1.ToString());
            ini.Write("Keybinds", "Snd_SrnTon2", Snd_SrnTon2.ToString());
            ini.Write("Keybinds", "Snd_SrnTon3", Snd_SrnTon3.ToString());
            ini.Write("Keybinds", "Snd_SrnTon4", Snd_SrnTon4.ToString());
            ini.Write("Keybinds", "Snd_SrnTon5", Snd_SrnTon5.ToString());
            ini.Write("Keybinds", "Snd_SrnTon6", Snd_SrnTon6.ToString());
            ini.Write("Keybinds", "Toggle_Rumbler", Toggle_Rumbler.ToString());
            ini.Write("Keybinds", "Toggle_FIAMMS", Toggle_FIAMMS.ToString());
            ini.Write("Keybinds", "Toggle_RedBeacon", Toggle_RedBeacon.ToString());
            ini.Write("Keybinds", "Toggle_MatrixText1", Toggle_MatrixText1.ToString());
            ini.Write("Keybinds", "Toggle_MatrixText2", Toggle_MatrixText2.ToString());
            ini.Write("Keybinds", "Toggle_MatrixText3", Toggle_MatrixText3.ToString());
            ini.Write("Keybinds", "Snd_SrnTonX", Snd_SrnTonX.ToString());
            ini.Write("Keybinds", "Toggle_Lsts", Toggle_Lsts.ToString());

            ini.Write("Keybinds", "Controller_Manul", Controller_Manul.ToString());
            ini.Write("Keybinds", "Controller_SrnToggle", Controller_SrnToggle.ToString());
            ini.Write("Keybinds", "Controller_SrnTonX", Controller_SrnTonX.ToString());
        }
    }
}
