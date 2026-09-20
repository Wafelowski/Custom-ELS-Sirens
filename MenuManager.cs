using System;
using System.Globalization;
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

        private static bool synchronizing;
        private static bool wasMenuKey;
        public static bool IsAnyMenuOpen => (MainMenu != null && MainMenu.Visible) || (SettingsMenu != null && SettingsMenu.Visible);

        public static List<string> AvailableWavs = new List<string>();

        public static UIMenuListItem modeItem;
        private static readonly Dictionary<string, UIMenuListItem> soundItems = new Dictionary<string, UIMenuListItem>();
        private static readonly Dictionary<string, UIMenuNumericScrollerItem<float>> volumeItems = new Dictionary<string, UIMenuNumericScrollerItem<float>>();
        private static readonly Dictionary<string, string>[] draftFiles = { new Dictionary<string, string>(), new Dictionary<string, string>() };
        private static readonly Dictionary<string, float>[] draftVolumes = { new Dictionary<string, float>(), new Dictionary<string, float>() };
        private static int editingBank;
        private static string editingModel = "Global";
        private static UIMenuListItem soundSetItem;
        private static UIMenuCheckboxItem rumblerEnabledItem, rumblerActiveItem;
        private static UIMenuCheckboxItem fiammsActiveItem;
        private static readonly UIMenuListItem[] extraItems = new UIMenuListItem[4];
        private static UIMenuNumericScrollerItem<float> masterVolumeItem;

        private static UIMenuCheckboxItem lightRestrictionItem;
        private static UIMenuCheckboxItem lightStageTrackingItem;
        private static UIMenuNumericScrollerItem<int> customStageAmountItem;

        private static UIMenuCheckboxItem controllerSupportItem;
        private static UIMenuCheckboxItem useElsKeybindsItem;
        private static UIMenuCheckboxItem hornInterruptItem;
        private static UIMenuListItem hornCycleItem;
        private static UIMenuCheckboxItem debugItem;
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
            if (IsAnyMenuOpen)
            {
                MainMenu.Visible = false;
                if (SettingsMenu != null) SettingsMenu.Visible = false;
            }
            else
            {
                SirenManager.ClearProfileCache();
                UpdateMenuSelections();
                MainMenu.Visible = true;
            }
        }

        public static void Process()
        {
            if (SirenManager.IsGamePaused)
            {
                wasMenuKey = Game.IsKeyDownRightNow(PluginConfig.MenuKey);
                return;
            }
            MenuPool?.ProcessMenus();
            bool key = Game.IsKeyDownRightNow(PluginConfig.MenuKey);
            if (key && !wasMenuKey) ToggleCustomSirenMenu();
            wasMenuKey = key;
            if (IsAnyMenuOpen)
            {
                if (modeItem.Index != 0 && editingModel != SirenManager.CurrentVehicleModel) UpdateMenuSelections();
                RefreshVehicleControls();
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

            lightStageTrackingItem = new UIMenuCheckboxItem("Track Custom Light Stages", PluginConfig.EnableLightStageTracking, "Manually tracks the active ELS light stage by keypresses instead of relying on game status.");
            customStageAmountItem = new UIMenuNumericScrollerItem<int>("Custom Light Stage Amount", "Select the maximum amount of light stages to track (1-4). Sirens will only play on the highest stage.", 1, 4, 1);
            customStageAmountItem.Value = PluginConfig.CustomLightStageAmount;
            customStageAmountItem.Enabled = PluginConfig.EnableLightStageTracking;
            hornCycleItem = new UIMenuListItem("Horn Cycles Siren", HornModes.Labels.Cast<dynamic>().ToList(), 0,
                "Vehicle-specific. Car horn uses the native horn; siren horn uses the saved Airhorn WAV. Without horn cycles immediately and suppresses both horns. Off keeps normal horn behavior. Save Profile to apply.");
            hornCycleItem.Enabled = false;
            hornInterruptItem = new UIMenuCheckboxItem("Horn Interrupts Siren", true,
                "Vehicle-specific. The horn stops the main siren; releasing it restarts the selected tone from the beginning. Disable to mix both sounds. Without horn mode always cycles immediately. Save Profile to apply.");
            hornInterruptItem.Enabled = false;

            masterVolumeItem = new UIMenuNumericScrollerItem<float>("~h~~y~Master Volume", "Volume Percentage for the global Volume", 0f, 100f, 5f);
            masterVolumeItem.Value = PluginConfig.MasterVolume * 100f;

            soundSetItem = new UIMenuListItem("WAV set to edit", new List<dynamic> { "Normal / rumbler OFF", "Rumbler ON" }, 0);
            rumblerEnabledItem = new UIMenuCheckboxItem("Enable rumbler for this profile", false, "Configure both WAV sets and save this profile to allow the rumbler toggle key.");
            rumblerActiveItem = new UIMenuCheckboxItem("Rumbler active in current vehicle", false, "Switch the current vehicle between its saved normal and rumbler WAV sets. Save profile changes first.");
            fiammsActiveItem = new UIMenuCheckboxItem("FIAMMS active in current vehicle", false, "Toggle the saved FIAMMS WAV alongside the main siren. Assign its WAV and save the profile first. Honors the vehicle's siren light restriction.");
            UIMenuItem saveItem = new UIMenuItem("~h~~g~Save Profile", "Saves both WAV sets, volumes, rumbler support, horn options, light settings and extra mappings.");

            MainMenu.AddItem(modeItem);
            MainMenu.AddItem(lightRestrictionItem);
            MainMenu.AddItem(lightStageTrackingItem);
            MainMenu.AddItem(customStageAmountItem);
            MainMenu.AddItem(hornCycleItem);
            MainMenu.AddItem(hornInterruptItem);
            MainMenu.AddItem(masterVolumeItem);
            MainMenu.AddItem(rumblerEnabledItem);
            MainMenu.AddItem(rumblerActiveItem);
            MainMenu.AddItem(fiammsActiveItem);
            MainMenu.AddItem(soundSetItem);
            foreach (string key in ProfileStore.SoundKeys)
            {
                string label = key.StartsWith("Tone", StringComparison.Ordinal) ? "Tone " + key.Substring(4) : key == "Horn" ? "Airhorn" : key == "Manual" ? "Manual siren" : "FIAMMS";
                var soundItem = new UIMenuListItem("~h~~b~" + label, wavsDynamic, 0);
                var volumeItem = new UIMenuNumericScrollerItem<float>("~o~" + label + " volume", "Volume for this sound in the selected WAV set. In the rumbler set, None uses the normal WAV.", 0f, 100f, 5f);
                volumeItem.Value = PluginConfig.DefaultSirenVolume(key + "Vol") * 100f;
                soundItems[key] = soundItem;
                volumeItems[key] = volumeItem;
                MainMenu.AddItem(soundItem);
                MainMenu.AddItem(volumeItem);
                string capturedKey = key;
                volumeItem.IndexChanged += (sender, oldIndex, newIndex) => UpdateVolSlider(capturedKey + "Vol", volumeItem.Value / 100f);
            }
            var extraChoices = new List<dynamic> { "Disabled" };
            extraChoices.AddRange(Enumerable.Range(ExtraControls.MinId, ExtraControls.MaxId - ExtraControls.MinId + 1).Cast<dynamic>());
            for (int i = 0; i < extraItems.Length; i++)
            {
                extraItems[i] = new UIMenuListItem(ExtraControls.Labels[i] + " extra ID", extraChoices, 0,
                    "Select Disabled or extra 1-12 for this vehicle. Assign the global toggle key in Config.ini. Matrix text modes are mutually exclusive.");
                extraItems[i].Enabled = false;
                MainMenu.AddItem(extraItems[i]);
            }
            MainMenu.AddItem(saveItem);

            SettingsMenu = MenuPool.AddSubMenu(MainMenu, "~h~~y~Misc Settings");

            controllerSupportItem = new UIMenuCheckboxItem("~c~Controller Support", PluginConfig.EnableControllerSupport, "Enables Controller inputs for toggling sirens (DPad Down, DPad Right, B).");
            useElsKeybindsItem = new UIMenuCheckboxItem("~c~Use ELS Keybinds", PluginConfig.UseElsKeybinds, "If enabled, directly imports and syncs your controls from the root directory ELS.ini on startup.");
            debugItem = new UIMenuCheckboxItem("~c~DEBUG", PluginConfig.Debug, "Global live vehicle status in the top-right corner: sirens, WAVs, horn routing, rumbler, FIAMMS, lights and extras. Hidden on foot.");

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
            SettingsMenu.AddItem(debugItem);
            SettingsMenu.AddItem(reverbIntensityItem);
            SettingsMenu.AddItem(maxDistanceItem);
            SettingsMenu.AddItem(aiCutoffItem);
            SettingsMenu.AddItem(aiScanIntervalItem);
            SettingsMenu.AddItem(maxAiUnitsItem);
            SettingsMenu.AddItem(falloffItem);
            SettingsMenu.AddItem(reloadConfigsItem);
            SettingsMenu.AddItem(reloadWavsItem);
            SettingsMenu.AddItem(patchELSItem);

            MainMenu.OnListChange += (sender, item, index) =>
            {
                if (synchronizing) return;
                if (item == modeItem) UpdateMenuSelections();
                else if (item == soundSetItem)
                {
                    CaptureDisplayedBank();
                    editingBank = index;
                    DisplayBank();
                }
            };

            MainMenu.OnCheckboxChange += (s, item, checkedState) =>
            {
                if (synchronizing) return;
                if (item == rumblerActiveItem)
                {
                    SirenManager.SetCurrentRumbler(checkedState);
                    RefreshVehicleControls();
                }
                if (item == fiammsActiveItem)
                {
                    SirenManager.SetCurrentFiamms(checkedState);
                    RefreshVehicleControls();
                }
                if (item == lightStageTrackingItem)
                {
                    customStageAmountItem.Enabled = checkedState;
                }
            };

            masterVolumeItem.IndexChanged += (s, o, n) => { if (synchronizing) return; PluginConfig.MasterVolume = masterVolumeItem.Value / 100f; PluginConfig.SaveConfig(); };

            MainMenu.OnItemSelect += (s, item, idx) =>
            {
                if (item == saveItem)
                {
                    if (SaveToneFilesToProfile())
                    {
                        SirenManager.ReloadProfiles();
                        UpdateMenuSelections();
                    }
                }
            };

            SettingsMenu.OnCheckboxChange += (s, item, checkedState) =>
            {
                if (synchronizing) return;
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
                if (item == debugItem)
                {
                    PluginConfig.Debug = checkedState;
                    if (!checkedState) DebugOverlay.Hide();
                    PluginConfig.SaveConfig();
                }
            };

            reverbIntensityItem.IndexChanged += (s, o, n) => { if (synchronizing) return; PluginConfig.ReverbIntensity = reverbIntensityItem.Value / 100f; PluginConfig.SaveConfig(); };
            maxDistanceItem.IndexChanged += (s, o, n) => { if (synchronizing) return; PluginConfig.MaxDistance = maxDistanceItem.Value; PluginConfig.SaveConfig(); };
            aiScanIntervalItem.IndexChanged += (s, o, n) => { if (synchronizing) return; PluginConfig.AiScanInterval = aiScanIntervalItem.Value; PluginConfig.SaveConfig(); };
            maxAiUnitsItem.IndexChanged += (s, o, n) => { if (synchronizing) return; PluginConfig.MaxAiUnits = maxAiUnitsItem.Value; PluginConfig.SaveConfig(); };
            falloffItem.IndexChanged += (s, o, n) => { if (synchronizing) return; PluginConfig.FalloffExponent = falloffItem.Value; PluginConfig.SaveConfig(); };

            SettingsMenu.OnItemSelect += (s, item, idx) =>
            {
                if (item == patchELSItem) PatchELSVCFs();
                if (item == reloadConfigsItem)
                {
                    PluginConfig.Load();

                    SirenManager.ReloadProfiles();
                    RefreshSettings();
                    UpdateMenuSelections();

                    Game.DisplayNotification("~g~Configurations & Keybinds Loaded!");
                }
                if (item == reloadWavsItem)
                {
                    LoadWavFiles();

                    SirenManager.ReloadAudio();
                    UpdateMenuSelections();
                    Game.DisplayNotification("~g~WAV list refreshed; audio will load in the background.");
                }
            };
        }

        private static void UpdateVolSlider(string key, float value)
        {
            if (synchronizing) return;
            draftVolumes[editingBank][key] = value;
            if (editingModel == "Global" && editingBank == 0) SetGlobalVolume(key, value);
            SirenManager.UpdateCachedVolume(editingModel, key, value, editingBank == 1);
        }

        private static void SetGlobalVolume(string key, float value)
        {
            switch (key)
            {
                case "Tone1Vol": PluginConfig.Tone1Vol = value; break;
                case "Tone2Vol": PluginConfig.Tone2Vol = value; break;
                case "Tone3Vol": PluginConfig.Tone3Vol = value; break;
                case "Tone4Vol": PluginConfig.Tone4Vol = value; break;
                case "Tone5Vol": PluginConfig.Tone5Vol = value; break;
                case "Tone6Vol": PluginConfig.Tone6Vol = value; break;
                case "HornVol": PluginConfig.HornVol = value; break;
                case "ManualVol": PluginConfig.ManualVol = value; break;
                case "FIAMMSVol": PluginConfig.FIAMMSVol = value; break;
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

                    string elsRoot = Path.GetFullPath("ELS").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    string relativePath = Path.GetFullPath(file).Substring(elsRoot.Length);
                    string backupPath = Path.Combine(backupFolder, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath));

                    if (!File.Exists(backupPath)) File.Copy(file, backupPath);

                    XmlDocument doc = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
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
                AvailableWavs.AddRange(Directory.GetFiles(PluginConfig.WavFolder, "*.wav").Select(Path.GetFileName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        }

        private static void RefreshSettings()
        {
            synchronizing = true;
            try
            {
                controllerSupportItem.Checked = PluginConfig.EnableControllerSupport;
                useElsKeybindsItem.Checked = PluginConfig.UseElsKeybinds;
                debugItem.Checked = PluginConfig.Debug;
                masterVolumeItem.Value = PluginConfig.MasterVolume * 100f;
                aiCutoffItem.Checked = PluginConfig.AutomaticAiSirenCutoff;
                aiScanIntervalItem.Value = PluginConfig.AiScanInterval;
                maxAiUnitsItem.Value = PluginConfig.MaxAiUnits;
                falloffItem.Value = PluginConfig.FalloffExponent;
                maxDistanceItem.Value = PluginConfig.MaxDistance;
                reverbIntensityItem.Value = PluginConfig.ReverbIntensity * 100f;
            }
            finally { synchronizing = false; }
        }

        private static void RefreshVehicleControls()
        {
            if (rumblerActiveItem == null) return;
            bool previous = synchronizing;
            synchronizing = true;
            try
            {
                rumblerActiveItem.Enabled = SirenManager.CurrentRumblerAvailable;
                rumblerActiveItem.Checked = SirenManager.CurrentRumblerActive;
                fiammsActiveItem.Enabled = SirenManager.CanControlCurrentVehicle &&
                    (SirenManager.CurrentFiammsActive || SirenManager.CurrentFiammsAvailable);
                fiammsActiveItem.Checked = SirenManager.CurrentFiammsActive;
            }
            finally { synchronizing = previous; }
        }

        private static void UpdateMenuSelections()
        {
            synchronizing = true;
            try
            {
                if (modeItem.Index != 0 && string.IsNullOrEmpty(SirenManager.CurrentVehicleModel)) modeItem.Index = 0;
                editingModel = modeItem.Index == 0 ? "Global" : SirenManager.CurrentVehicleModel;
                var profile = ProfileStore.Get(editingModel);
                for (int bank = 0; bank < 2; bank++)
                {
                    draftFiles[bank].Clear();
                    draftVolumes[bank].Clear();
                    foreach (string key in ProfileStore.SoundKeys)
                    {
                        string file = (bank == 0 ? profile.SoundFiles : profile.RumblerSoundFiles)[key];
                        if (string.IsNullOrWhiteSpace(file) || file.Equals("None", StringComparison.OrdinalIgnoreCase)) file = "None";
                        draftFiles[bank][key] = file;
                        draftVolumes[bank][key + "Vol"] = (bank == 0 ? profile.Volumes : profile.RumblerVolumes)[key + "Vol"];
                        // Keep missing or nested saved filenames visible so saving
                        // another setting does not erase their assignments.
                        if (!AvailableWavs.Any(wav => wav.Equals(file, StringComparison.OrdinalIgnoreCase))) AvailableWavs.Add(file);
                    }
                }
                foreach (var item in soundItems.Values) item.Items = AvailableWavs.Cast<dynamic>().ToList();
                editingBank = soundSetItem.Index;
                lightRestrictionItem.Checked = profile.LightRestriction;
                lightStageTrackingItem.Checked = profile.StageTracking;
                customStageAmountItem.Value = profile.StageCount;
                customStageAmountItem.Enabled = profile.StageTracking;
                hornCycleItem.Index = (int)profile.HornCycle;
                hornCycleItem.Enabled = modeItem.Index != 0;
                hornInterruptItem.Checked = profile.HornInterruptsSiren;
                hornInterruptItem.Enabled = modeItem.Index != 0;
                rumblerEnabledItem.Checked = profile.RumblerEnabled;
                for (int i = 0; i < extraItems.Length; i++)
                {
                    extraItems[i].Index = Math.Max(0, profile.Extras[i]);
                    extraItems[i].Enabled = modeItem.Index != 0;
                }
            }
            finally { synchronizing = false; }
            DisplayBank();
            RefreshVehicleControls();
        }

        private static void CaptureDisplayedBank()
        {
            foreach (string key in ProfileStore.SoundKeys)
            {
                draftFiles[editingBank][key] = AvailableWavs[soundItems[key].Index];
            }
        }

        private static void DisplayBank()
        {
            synchronizing = true;
            try
            {
                foreach (string key in ProfileStore.SoundKeys)
                {
                    string file = draftFiles[editingBank][key];
                    int index = AvailableWavs.FindIndex(wav => wav.Equals(file, StringComparison.OrdinalIgnoreCase));
                    soundItems[key].Index = Math.Max(0, index);
                    volumeItems[key].Value = draftVolumes[editingBank][key + "Vol"] * 100f;
                }
            }
            finally { synchronizing = false; }
        }

        private static bool SaveToneFilesToProfile()
        {
            if (modeItem.Index != 0 && (string.IsNullOrEmpty(SirenManager.CurrentVehicleModel) || editingModel != SirenManager.CurrentVehicleModel))
            {
                Game.DisplayNotification("~y~Enter the vehicle whose profile you want to save.");
                return false;
            }
            CaptureDisplayedBank();
            string targetIni = editingModel + ".ini";
            string targetPath = Path.Combine(PluginConfig.ProfilesFolder, targetIni);
            InitializationFile ini = new InitializationFile(targetPath);
            if (!ini.Exists()) ini.Create();
            for (int bank = 0; bank < 2; bank++)
            {
                foreach (string key in ProfileStore.SoundKeys)
                {
                    ini.Write(bank == 0 ? "Sirens" : "RumblerSirens", key, draftFiles[bank][key]);
                    ini.Write(bank == 0 ? "SirenVolumes" : "RumblerVolumes", key + "Vol", draftVolumes[bank][key + "Vol"].ToString(CultureInfo.InvariantCulture));
                }
            }
            ini.Write("Rumbler", "Enabled", rumblerEnabledItem.Checked.ToString());
            ini.Write("Settings", "SirenLightRestriction", lightRestrictionItem.Checked.ToString());
            ini.Write("Settings", "EnableLightStageTracking", lightStageTrackingItem.Checked.ToString());
            ini.Write("Settings", "CustomLightStageAmount", customStageAmountItem.Value.ToString());
            if (modeItem.Index != 0)
            {
                ini.Write("Settings", "HornCycleMode", ((HornCycleMode)hornCycleItem.Index).ToString());
                ini.Write("Settings", "HornInterruptsSiren", hornInterruptItem.Checked.ToString());
                int[] extras = extraItems.Select(item => ExtraControls.ValidateId(item.Index)).ToArray();
                ExtraControls.ValidateMappings(extras);
                for (int i = 0; i < extras.Length; i++)
                {
                    if (extras[i] != ExtraControls.ValidateId(extraItems[i].Index))
                        Game.Console.Print("[CustomSirens] Duplicate/invalid mapping disabled: " + ExtraControls.Labels[i]);
                    ini.Write("VehicleExtras", ExtraControls.Names[i], extras[i].ToString(CultureInfo.InvariantCulture));
                }
            }
            if (modeItem.Index == 0)
            {
                foreach (string key in ProfileStore.SoundKeys) SetGlobalVolume(key + "Vol", draftVolumes[0][key + "Vol"]);
                PluginConfig.SirenLightRestriction = lightRestrictionItem.Checked;
                PluginConfig.EnableLightStageTracking = lightStageTrackingItem.Checked;
                PluginConfig.CustomLightStageAmount = customStageAmountItem.Value;
                PluginConfig.SaveConfig();
            }
            Game.DisplayNotification("~g~Saved both WAV sets and vehicle options to " + targetIni + "!");
            return true;
        }
    }
}
