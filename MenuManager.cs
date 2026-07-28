using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Rage;
using RAGENativeUI;
using RAGENativeUI.Elements;

namespace CustomELSSirens
{
    public static class MenuManager
    {
        public static MenuPool MenuPool;
        public static UIMenu MainMenu;
        public static UIMenu SettingsMenu;

        public static List<string> AvailableWavs = new List<string>();

        public static UIMenuListItem modeItem;
        private static UIMenuListItem tone1Item, tone2Item, tone3Item, tone4Item, hornItem, manualItem;
        private static UIMenuNumericScrollerItem<float> tone1Vol, tone2Vol, tone3Vol, tone4Vol, hornVol, manualVol;
        private static UIMenuNumericScrollerItem<float> masterVolumeItem;

        private static UIMenuCheckboxItem lightRestrictionItem;
        private static UIMenuCheckboxItem lightStageTrackingItem;
        private static UIMenuNumericScrollerItem<int> customStageAmountItem;

        private static UIMenuCheckboxItem controllerSupportItem;
        private static UIMenuCheckboxItem useElsKeybindsItem;
        private static UIMenuCheckboxItem hornInterruptItem;
        private static UIMenuCheckboxItem aiCutoffItem;
        private static UIMenuNumericScrollerItem<int> aiScanIntervalItem;
        private static UIMenuNumericScrollerItem<int> maxAiUnitsItem;
        private static UIMenuNumericScrollerItem<float> falloffItem;
        private static UIMenuNumericScrollerItem<float> maxDistanceItem;
        private static UIMenuNumericScrollerItem<float> reverbIntensityItem;
        private static UIMenuItem reloadWavsItem;
        private static UIMenuItem reloadConfigsItem;

        public static void ToggleCustomSirenMenu()
        {
            if (MainMenu == null) return;
            UpdateMenuSelections();
            MainMenu.Visible = !MainMenu.Visible;
            if (!MainMenu.Visible && SettingsMenu != null) SettingsMenu.Visible = false;
            Game.Console.Print("[CustomSirens] Configuration menu toggled via console command.");
        }

        public static void Process()
        {
            MenuPool?.ProcessMenus();

            if (Game.IsKeyDownRightNow(PluginConfig.MenuKey))
            {
                if (!MainMenu.Visible && (SettingsMenu == null || !SettingsMenu.Visible))
                {
                    UpdateMenuSelections();
                    MainMenu.Visible = true;
                }
            }
        }

        public static void SetupMenu()
        {
            if (!Directory.Exists(PluginConfig.WavFolder)) Directory.CreateDirectory(PluginConfig.WavFolder);
            if (!Directory.Exists(PluginConfig.ProfilesFolder)) Directory.CreateDirectory(PluginConfig.ProfilesFolder);

            LoadWavFiles();

            MenuPool = new MenuPool();
            MainMenu = new UIMenu("~r~Custom ~p~ELS ~b~Sirens", "Main Menu");
            MenuPool.Add(MainMenu);

            List<dynamic> wavsDynamic = AvailableWavs.Cast<dynamic>().ToList();

            modeItem = new UIMenuListItem("~h~~y~Profile Mode", new List<dynamic> { "Global Default", "Vehicle Specific" }, 0);

            lightRestrictionItem = new UIMenuCheckboxItem("~c~Siren Light Restriction", PluginConfig.SirenLightRestriction, "If enabled, emergency lights must be active to trigger custom sirens. Disable this if your addon vehicle's lights aren't being detected.");

            lightStageTrackingItem = new UIMenuCheckboxItem("Toggle Custom Light stage ammount and Enable Light stage tracking", PluginConfig.EnableLightStageTracking, "Manually tracks the active ELS light stage by keypresses instead of relying on game status.");
            customStageAmountItem = new UIMenuNumericScrollerItem<int>("Custom Light Stage Amount", "Select the maximum amount of light stages to track (1-4). Sirens will only play on the highest stage.", 1, 4, 1);
            customStageAmountItem.Value = PluginConfig.CustomLightStageAmount;
            customStageAmountItem.Enabled = PluginConfig.EnableLightStageTracking;

            masterVolumeItem = new UIMenuNumericScrollerItem<float>("~h~~y~Master Volume", "Volume Percentage for the global Volume", 0f, 100f, 5f);
            masterVolumeItem.Value = PluginConfig.MasterVolume * 100f;

            tone1Item = new UIMenuListItem("~h~~b~Tone 1", wavsDynamic, 0);
            tone1Vol = new UIMenuNumericScrollerItem<float>("~o~Tone 1 Volume", "Volume Percentage for the specific Siren", 0f, 100f, 5f);
            tone1Vol.Value = PluginConfig.Tone1Vol * 100f;

            tone2Item = new UIMenuListItem("~h~~b~Tone 2", wavsDynamic, 0);
            tone2Vol = new UIMenuNumericScrollerItem<float>("~o~Tone 2 Volume", "Volume Percentage for the specific Siren", 0f, 100f, 5f);
            tone2Vol.Value = PluginConfig.Tone2Vol * 100f;

            tone3Item = new UIMenuListItem("~h~~b~Tone 3", wavsDynamic, 0);
            tone3Vol = new UIMenuNumericScrollerItem<float>("~o~Tone 3 Volume", "Volume Percentage for the specific Siren", 0f, 100f, 5f);
            tone3Vol.Value = PluginConfig.Tone3Vol * 100f;

            tone4Item = new UIMenuListItem("~h~~b~Tone 4", wavsDynamic, 0);
            tone4Vol = new UIMenuNumericScrollerItem<float>("~o~Tone 4 Volume", "Volume Percentage for the specific Siren", 0f, 100f, 5f);
            tone4Vol.Value = PluginConfig.Tone4Vol * 100f;

            hornItem = new UIMenuListItem("~h~~b~Airhorn", wavsDynamic, 0);
            hornVol = new UIMenuNumericScrollerItem<float>("~o~Horn Volume", "Volume Percentage for the specific Siren", 0f, 100f, 5f);
            hornVol.Value = PluginConfig.HornVol * 100f;

            manualItem = new UIMenuListItem("~h~~b~Manual Siren", wavsDynamic, 0);
            manualVol = new UIMenuNumericScrollerItem<float>("~o~Manual Volume", "Volume Percentage for the specific Siren", 0f, 100f, 5f);
            manualVol.Value = PluginConfig.ManualVol * 100f;

            UIMenuItem saveItem = new UIMenuItem("~h~~g~Save Profile", "~g~Saves the selected tones and volumes.");

            MainMenu.AddItem(modeItem);
            MainMenu.AddItem(lightRestrictionItem);
            MainMenu.AddItem(lightStageTrackingItem);
            MainMenu.AddItem(customStageAmountItem);
            MainMenu.AddItem(masterVolumeItem);
            MainMenu.AddItem(tone1Item); MainMenu.AddItem(tone1Vol);
            MainMenu.AddItem(tone2Item); MainMenu.AddItem(tone2Vol);
            MainMenu.AddItem(tone3Item); MainMenu.AddItem(tone3Vol);
            MainMenu.AddItem(tone4Item); MainMenu.AddItem(tone4Vol);
            MainMenu.AddItem(hornItem); MainMenu.AddItem(hornVol);
            MainMenu.AddItem(manualItem); MainMenu.AddItem(manualVol);
            MainMenu.AddItem(saveItem);

            SettingsMenu = MenuPool.AddSubMenu(MainMenu, "~h~~y~Misc Settings");

            controllerSupportItem = new UIMenuCheckboxItem("~c~Controller Support", PluginConfig.EnableControllerSupport, "Enables Controller inputs for toggling sirens (DPad Down, DPad Right, B).");
            useElsKeybindsItem = new UIMenuCheckboxItem("~c~Use ELS Keybinds", PluginConfig.UseElsKeybinds, "If enabled, directly imports and syncs your controls from the root directory ELS.ini on startup.");
            hornInterruptItem = new UIMenuCheckboxItem("~c~Horn Interrupts Siren", PluginConfig.HornInterruptsSiren, "If enabled, blasting the airhorn will temporarily mute the primary siren.");

            reverbIntensityItem = new UIMenuNumericScrollerItem<float>("~c~Reverb Intensity", "Adjusts the strength of the city reverb effect. (Default: 100%)", 0f, 200f, 5f);
            reverbIntensityItem.Value = PluginConfig.ReverbIntensity * 100f;

            maxDistanceItem = new UIMenuNumericScrollerItem<float>("~c~Max Hearing Distance", "How far sirens can be heard.", 50f, 1000f, 10f);
            maxDistanceItem.Value = PluginConfig.MaxDistance;

            aiCutoffItem = new UIMenuCheckboxItem("~c~Automatic Siren Cutoff", PluginConfig.AutomaticAiSirenCutoff, "Cuts off the siren when the driver gets out of the vehicle.");
            aiScanIntervalItem = new UIMenuNumericScrollerItem<int>("~c~AI Scan Frequency", "Time in ms the Plugin should scan for AI vehicles (Lower = more demanding).", 100, 5000, 50);
            aiScanIntervalItem.Value = PluginConfig.AiScanInterval;

            maxAiUnitsItem = new UIMenuNumericScrollerItem<int>("~c~Max Affected AI Units", "How many AI units can be affected simultaneously.", 1, 50, 1);
            maxAiUnitsItem.Value = PluginConfig.MaxAiUnits;

            falloffItem = new UIMenuNumericScrollerItem<float>("~c~Siren Falloff Curve", "Adjusts how quickly the sound fades over distance. (Default: 3.0)", 0.5f, 10.0f, 0.5f);
            falloffItem.Value = PluginConfig.FalloffExponent;

            reloadConfigsItem = new UIMenuItem("~q~Reload Configurations", "~q~Reloads settings and keybinds from Config.ini and ELS.ini.");
            reloadWavsItem = new UIMenuItem("~q~Reload WAV Files", "~q~Rescans the WAVs directory to update available audio files.");
            UIMenuItem patchELSItem = new UIMenuItem("~r~Kill ELS Sounds (Patch VCFs)", "~r~Mutes the Sirens in your ELS VCFs (Creates a backup of your current VCFs before patching).~n~Game Restart required!");

            SettingsMenu.AddItem(controllerSupportItem);
            SettingsMenu.AddItem(useElsKeybindsItem);
            SettingsMenu.AddItem(hornInterruptItem);
            SettingsMenu.AddItem(reverbIntensityItem);
            SettingsMenu.AddItem(maxDistanceItem);
            SettingsMenu.AddItem(aiCutoffItem);
            SettingsMenu.AddItem(aiScanIntervalItem);
            SettingsMenu.AddItem(maxAiUnitsItem);
            SettingsMenu.AddItem(falloffItem);
            SettingsMenu.AddItem(reloadConfigsItem);
            SettingsMenu.AddItem(reloadWavsItem);
            SettingsMenu.AddItem(patchELSItem);

            MainMenu.OnListChange += (s, item, idx) => { if (item == modeItem) UpdateMenuSelections(); };

            MainMenu.OnCheckboxChange += (s, item, checkedState) =>
            {
                if (item == lightStageTrackingItem)
                {
                    customStageAmountItem.Enabled = checkedState;
                }
            };

            tone1Vol.IndexChanged += (s, o, n) => { UpdateVolSlider("Tone1Vol", tone1Vol.Value / 100f); };
            tone2Vol.IndexChanged += (s, o, n) => { UpdateVolSlider("Tone2Vol", tone2Vol.Value / 100f); };
            tone3Vol.IndexChanged += (s, o, n) => { UpdateVolSlider("Tone3Vol", tone3Vol.Value / 100f); };
            tone4Vol.IndexChanged += (s, o, n) => { UpdateVolSlider("Tone4Vol", tone4Vol.Value / 100f); };
            hornVol.IndexChanged += (s, o, n) => { UpdateVolSlider("HornVol", hornVol.Value / 100f); };
            manualVol.IndexChanged += (s, o, n) => { UpdateVolSlider("ManualVol", manualVol.Value / 100f); };

            masterVolumeItem.IndexChanged += (s, o, n) => { PluginConfig.MasterVolume = masterVolumeItem.Value / 100f; PluginConfig.SaveConfig(); };

            MainMenu.OnItemSelect += (s, item, idx) =>
            {
                if (item == saveItem)
                {
                    SaveToneFilesToProfile();
                    SirenManager.ClearProfileCache();
                    SirenManager.CacheVehicleSirens();
                }
            };

            SettingsMenu.OnCheckboxChange += (s, item, checkedState) =>
            {
                if (item == aiCutoffItem)
                {
                    PluginConfig.AutomaticAiSirenCutoff = checkedState;
                    PluginConfig.SaveConfig();
                }
                if (item == controllerSupportItem)
                {
                    PluginConfig.EnableControllerSupport = checkedState;
                    PluginConfig.SaveConfig();
                }
                if (item == useElsKeybindsItem)
                {
                    PluginConfig.UseElsKeybinds = checkedState;
                    PluginConfig.SaveConfig();
                }
                if (item == hornInterruptItem)
                {
                    PluginConfig.HornInterruptsSiren = checkedState;
                    PluginConfig.SaveConfig();
                }
            };

            reverbIntensityItem.IndexChanged += (s, o, n) => { PluginConfig.ReverbIntensity = reverbIntensityItem.Value / 100f; PluginConfig.SaveConfig(); };
            maxDistanceItem.IndexChanged += (s, o, n) => { PluginConfig.MaxDistance = maxDistanceItem.Value; PluginConfig.SaveConfig(); };
            aiScanIntervalItem.IndexChanged += (s, o, n) => { PluginConfig.AiScanInterval = aiScanIntervalItem.Value; PluginConfig.SaveConfig(); };
            maxAiUnitsItem.IndexChanged += (s, o, n) => { PluginConfig.MaxAiUnits = maxAiUnitsItem.Value; PluginConfig.SaveConfig(); };
            falloffItem.IndexChanged += (s, o, n) => { PluginConfig.FalloffExponent = falloffItem.Value; PluginConfig.SaveConfig(); };

            SettingsMenu.OnItemSelect += (s, item, idx) =>
            {
                if (item == patchELSItem) PatchELSVCFs();
                if (item == reloadConfigsItem)
                {
                    PluginConfig.Load();

                    controllerSupportItem.Checked = PluginConfig.EnableControllerSupport;
                    useElsKeybindsItem.Checked = PluginConfig.UseElsKeybinds;
                    hornInterruptItem.Checked = PluginConfig.HornInterruptsSiren;
                    masterVolumeItem.Value = PluginConfig.MasterVolume * 100f;
                    aiCutoffItem.Checked = PluginConfig.AutomaticAiSirenCutoff;
                    aiScanIntervalItem.Value = PluginConfig.AiScanInterval;
                    maxAiUnitsItem.Value = PluginConfig.MaxAiUnits;
                    falloffItem.Value = PluginConfig.FalloffExponent;
                    maxDistanceItem.Value = PluginConfig.MaxDistance;
                    reverbIntensityItem.Value = PluginConfig.ReverbIntensity * 100f;

                    UpdateMenuSelections();

                    Game.DisplayNotification("~g~Configurations & Keybinds Loaded!");
                }
                if (item == reloadWavsItem)
                {
                    LoadWavFiles();

                    List<dynamic> updatedWavs = AvailableWavs.Cast<dynamic>().ToList();
                    tone1Item.Items = updatedWavs;
                    tone2Item.Items = updatedWavs;
                    tone3Item.Items = updatedWavs;
                    tone4Item.Items = updatedWavs;
                    hornItem.Items = updatedWavs;
                    manualItem.Items = updatedWavs;

                    UpdateMenuSelections();
                    Game.DisplayNotification("~g~WAV Files Reloaded Successfully!");
                }
            };
        }

        private static void UpdateVolSlider(string key, float value)
        {
            if (modeItem.Index == 0)
            {
                switch (key)
                {
                    case "Tone1Vol": PluginConfig.Tone1Vol = value; break;
                    case "Tone2Vol": PluginConfig.Tone2Vol = value; break;
                    case "Tone3Vol": PluginConfig.Tone3Vol = value; break;
                    case "Tone4Vol": PluginConfig.Tone4Vol = value; break;
                    case "HornVol": PluginConfig.HornVol = value; break;
                    case "ManualVol": PluginConfig.ManualVol = value; break;
                }
                SirenManager.UpdateCachedVolume("Global", key, value);
            }
            else
            {
                SirenManager.UpdateCachedVolume(SirenManager.CurrentVehicleModel, key, value);
            }
        }

        private static void PatchELSVCFs()
        {
            string backupFolder = Path.Combine("ELS", "Original VCF Backups");
            if (!Directory.Exists("ELS")) return;
            if (!Directory.Exists(backupFolder)) Directory.CreateDirectory(backupFolder);

            string[] files = Directory.GetFiles("ELS", "*.xml", SearchOption.AllDirectories);
            int count = 0;

            foreach (string file in files)
            {
                try
                {
                    if (file.IndexOf("Original VCF Backups", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    string fileName = Path.GetFileName(file);
                    string backupPath = Path.Combine(backupFolder, fileName);

                    if (!File.Exists(backupPath)) File.Copy(file, backupPath);

                    XmlDocument doc = new XmlDocument();
                    doc.Load(file);
                    bool modified = false;

                    XmlNode soundsNode = doc.SelectSingleNode("//SOUNDS");
                    if (soundsNode != null)
                    {
                        foreach (XmlNode node in soundsNode.ChildNodes)
                        {
                            if (node.Attributes?["AllowUse"] != null &&
                                node.Attributes["AllowUse"].Value.Equals("true", StringComparison.OrdinalIgnoreCase))
                            {
                                node.Attributes["AllowUse"].Value = "false";
                                modified = true;
                            }
                        }
                    }

                    if (modified)
                    {
                        doc.Save(file);
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    Game.Console.Print($"[CustomSirens] Failed to patch/backup {file}: {ex.Message}");
                }
            }

            if (count > 0)
                Game.DisplayNotification($"~g~Disabled ELS Sounds in {count} VCFs! Backups created in 'Original VCF Backups'.");
            else
                Game.DisplayNotification("~y~No VCFs required patching (or they were already patched).");
        }

        private static void LoadWavFiles()
        {
            AvailableWavs.Clear();
            AvailableWavs.Add("None");
            if (Directory.Exists(PluginConfig.WavFolder))
                AvailableWavs.AddRange(Directory.GetFiles(PluginConfig.WavFolder, "*.wav").Select(Path.GetFileName));
        }

        private static void UpdateMenuSelections()
        {
            string targetIni = modeItem.Index == 0 ? "Global.ini" : $"{SirenManager.CurrentVehicleModel}.ini";
            InitializationFile ini = new InitializationFile($@"{PluginConfig.ProfilesFolder}{targetIni}");

            void SetIndex(UIMenuListItem listItem, string toneName)
            {
                string saved = ini.ReadString("Sirens", toneName, "None");
                int idx = AvailableWavs.IndexOf(saved);
                listItem.Index = idx >= 0 ? idx : 0;
            }

            SetIndex(tone1Item, "Tone1"); SetIndex(tone2Item, "Tone2");
            SetIndex(tone3Item, "Tone3"); SetIndex(tone4Item, "Tone4");
            SetIndex(hornItem, "Horn"); SetIndex(manualItem, "Manual");

            tone1Vol.Value = ini.ReadSingle("SirenVolumes", "Tone1Vol", PluginConfig.Tone1Vol) * 100f;
            tone2Vol.Value = ini.ReadSingle("SirenVolumes", "Tone2Vol", PluginConfig.Tone2Vol) * 100f;
            tone3Vol.Value = ini.ReadSingle("SirenVolumes", "Tone3Vol", PluginConfig.Tone3Vol) * 100f;
            tone4Vol.Value = ini.ReadSingle("SirenVolumes", "Tone4Vol", PluginConfig.Tone4Vol) * 100f;
            hornVol.Value = ini.ReadSingle("SirenVolumes", "HornVol", PluginConfig.HornVol) * 100f;
            manualVol.Value = ini.ReadSingle("SirenVolumes", "ManualVol", PluginConfig.ManualVol) * 100f;

            lightRestrictionItem.Checked = ini.ReadBoolean("Settings", "SirenLightRestriction", PluginConfig.SirenLightRestriction);

            bool tracking = ini.ReadBoolean("Settings", "EnableLightStageTracking", PluginConfig.EnableLightStageTracking);
            lightStageTrackingItem.Checked = tracking;

            customStageAmountItem.Value = ini.ReadInt32("Settings", "CustomLightStageAmount", PluginConfig.CustomLightStageAmount);
            customStageAmountItem.Enabled = tracking;
        }

        private static void SaveToneFilesToProfile()
        {
            string targetIni = modeItem.Index == 0 ? "Global.ini" : $"{SirenManager.CurrentVehicleModel}.ini";
            InitializationFile ini = new InitializationFile($@"{PluginConfig.ProfilesFolder}{targetIni}");
            if (!ini.Exists()) ini.Create();

            ini.Write("Sirens", "Tone1", AvailableWavs[tone1Item.Index]);
            ini.Write("Sirens", "Tone2", AvailableWavs[tone2Item.Index]);
            ini.Write("Sirens", "Tone3", AvailableWavs[tone3Item.Index]);
            ini.Write("Sirens", "Tone4", AvailableWavs[tone4Item.Index]);
            ini.Write("Sirens", "Horn", AvailableWavs[hornItem.Index]);
            ini.Write("Sirens", "Manual", AvailableWavs[manualItem.Index]);

            ini.Write("SirenVolumes", "Tone1Vol", (tone1Vol.Value / 100f).ToString());
            ini.Write("SirenVolumes", "Tone2Vol", (tone2Vol.Value / 100f).ToString());
            ini.Write("SirenVolumes", "Tone3Vol", (tone3Vol.Value / 100f).ToString());
            ini.Write("SirenVolumes", "Tone4Vol", (tone4Vol.Value / 100f).ToString());
            ini.Write("SirenVolumes", "HornVol", (hornVol.Value / 100f).ToString());
            ini.Write("SirenVolumes", "ManualVol", (manualVol.Value / 100f).ToString());

            ini.Write("Settings", "SirenLightRestriction", lightRestrictionItem.Checked.ToString());
            ini.Write("Settings", "EnableLightStageTracking", lightStageTrackingItem.Checked.ToString());
            ini.Write("Settings", "CustomLightStageAmount", customStageAmountItem.Value.ToString());

            if (modeItem.Index == 0)
            {
                PluginConfig.Tone1Vol = tone1Vol.Value / 100f;
                PluginConfig.Tone2Vol = tone2Vol.Value / 100f;
                PluginConfig.Tone3Vol = tone3Vol.Value / 100f;
                PluginConfig.Tone4Vol = tone4Vol.Value / 100f;
                PluginConfig.HornVol = hornVol.Value / 100f;
                PluginConfig.ManualVol = manualVol.Value / 100f;

                PluginConfig.SirenLightRestriction = lightRestrictionItem.Checked;
                PluginConfig.EnableLightStageTracking = lightStageTrackingItem.Checked;
                PluginConfig.CustomLightStageAmount = customStageAmountItem.Value;
                PluginConfig.SaveConfig();
            }

            Game.DisplayNotification($"~g~Saved selections & volumes to {targetIni}!");
        }
    }
}