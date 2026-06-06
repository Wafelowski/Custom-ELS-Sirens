using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Rage;
using Rage.Native;
using RAGENativeUI;
using RAGENativeUI.Elements;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Xml;

[assembly: Rage.Attributes.Plugin("Custom ELS Sirens", Author = "Maggie Waggie", Description = "Custom per Vehicle ELS compatible Sirens, instant global RAM caching, multi-vehicle tracking, and acoustic cabin dampening.")]

namespace CustomELSSirens
{
    public static class EntryPoint
    {
        public static string baseFolder = @"Plugins\CustomSirens\";
        public static string wavFolder = baseFolder + @"WAVs\";
        public static string profilesFolder = baseFolder + @"Profiles\";
        public static string configFile = baseFolder + "Config.ini";

        private static MenuPool menuPool;
        private static UIMenu sirenMenu;
        private static UIMenuListItem modeItem, tone1Item, tone2Item, tone3Item, tone4Item, hornItem, manualItem;
        private static UIMenuNumericScrollerItem<float> tone1Vol, tone2Vol, tone3Vol, tone4Vol, hornVol, manualVol;
        private static UIMenuNumericScrollerItem<float> masterVolumeItem;
        private static UIMenuCheckboxItem aiCutoffItem;
        private static UIMenuNumericScrollerItem<int> aiScanIntervalItem;
        private static UIMenuNumericScrollerItem<int> maxAiUnitsItem;
        private static UIMenuItem reloadWavsItem;
        private static UIMenuItem reloadConfigsItem;
        private static UIMenuNumericScrollerItem<float> falloffItem;
        private static List<string> availableWavs = new List<string>();

        public static Dictionary<string, CachedSound> cachedSirens =
            new Dictionary<string, CachedSound>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> _profileCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<Vehicle> _vehicleScratch = new List<Vehicle>();

        private static readonly SirenPlayer activeSiren = new SirenPlayer();
        private static readonly SirenPlayer activeHorn = new SirenPlayer();
        private static readonly SirenPlayer activeManual = new SirenPlayer();
        private static readonly Dictionary<Vehicle, AiSirenState> activeAiSirens =
            new Dictionary<Vehicle, AiSirenState>();

        private static Vehicle currentVehicle = null;
        private static string currentVehicleModel = string.Empty;
        private static int maxStage = 3;
        private static int requiredSirenStage = 3;
        private static bool vehicleProfileExists = false;

        private static int activeToneIndex = 0;
        private static bool isAutoScanActive = false;
        private static uint nextScanChangeTime = 0;

        private static bool was1, was2, was3, was4, wasHorn, wasManul, wasScan, wasTonX, wasPnic;

        private static uint nextAiScanTime = 0;
        private static readonly Random rnd = new Random();

        [Rage.Attributes.ConsoleCommand("ToggleSirenMenu",
            Description = "Opens or closes the Custom ELS Sirens configuration menu.")]
        public static void ToggleCustomSirenMenu()
        {
            if (sirenMenu == null) return;
            UpdateMenuSelections();
            sirenMenu.Visible = !sirenMenu.Visible;
            Game.Console.Print("[CustomSirens] Configuration menu toggled via console command.");
        }

        public static void Main()
        {
            PluginConfig.Load();
            SetupMenu();
            Game.DisplayNotification(
                $"~b~Custom Sirens~w~ initialized. Press ~y~{PluginConfig.MenuKey}~w~ or use console for the menu.");
            GameFiber.StartNew(MainLoop);
        }

        private static void MainLoop()
        {
            try
            {
                while (true)
                {
                    GameFiber.Yield();

                    menuPool?.ProcessMenus();

                    if (Game.IsKeyDownRightNow(PluginConfig.MenuKey) && !sirenMenu.Visible)
                    {
                        UpdateMenuSelections();
                        sirenMenu.Visible = true;
                    }

                    Ped player = Game.LocalPlayer.Character;
                    if (player == null || !player.IsAlive)
                    {
                        KillAllSounds(true);
                        KillAllAiSounds(true);
                        continue;
                    }

                    bool inVehicle = player.IsInAnyVehicle(false);
                    Vehicle veh = inVehicle ? player.CurrentVehicle : currentVehicle;

                    if (inVehicle && currentVehicle != veh)
                    {
                        currentVehicle = veh;
                        currentVehicleModel = GetVehicleModelName(veh);
                        activeToneIndex = 0;
                        isAutoScanActive = false;
                        activeSiren.Stop();
                        activeHorn.Stop();
                        activeManual.Stop();
                        ParseVCF(currentVehicleModel);
                        _profileCache.Clear();
                        CacheVehicleSirens();
                    }

                    if (currentVehicle != null && currentVehicle.IsValid() && currentVehicle.IsAlive)
                    {
                        if (currentVehicle.HasSiren)
                        {
                            NativeFunction.Natives.SET_VEHICLE_HAS_MUTED_SIRENS<bool>(currentVehicle, true);
                            Game.DisableControlAction(0, GameControl.VehicleHorn, true);
                        }

                        bool isLightsOn = IsVehicleLightsOn(currentVehicle);

                        if (inVehicle)
                        {
                            HandleInputs(currentVehicle, isLightsOn);
                        }
                        else
                        {
                            if (activeHorn.IsPlaying) activeHorn.Stop();
                            if (activeManual.IsPlaying) activeManual.Stop();

                            if (PluginConfig.AutomaticAiSirenCutoff && currentVehicle.IsSirenOn)
                            {
                                currentVehicle.IsSirenOn = false;
                                activeToneIndex = 0;
                                isAutoScanActive = false;
                                activeSiren.Stop();
                            }
                        }

                        if (!isLightsOn && activeToneIndex != 0)
                        {
                            activeToneIndex = 0;
                            isAutoScanActive = false;
                            activeSiren.Stop();
                        }
                    }
                    else
                    {
                        KillAllSounds();
                    }

                    bool isPaused = Game.IsPaused || Game.IsLoading || NativeFunction.Natives.IS_PAUSE_MENU_ACTIVE<bool>();

                    if (isPaused)
                    {
                        activeSiren.SetVolume(0f);
                        activeHorn.SetVolume(0f);
                        activeManual.SetVolume(0f);

                        foreach (var ai in activeAiSirens.Values)
                            ai.Player.SetVolume(0f);
                    }
                    else
                    {
                        bool isMenuOpen = sirenMenu != null && sirenMenu.Visible;
                        bool shouldMuteAll = isMenuOpen;

                        Vector3 camPos = NativeFunction.Natives.GET_GAMEPLAY_CAM_COORD<Vector3>();
                        Vector3 camRot = NativeFunction.Natives.GET_GAMEPLAY_CAM_ROT<Vector3>(2);

                        if (currentVehicle != null && currentVehicle.IsValid() && currentVehicle.IsAlive)
                        {
                            bool sirenForceMute = shouldMuteAll || activeHorn.IsPlaying || activeManual.IsPlaying;

                            activeSiren.Update3D(currentVehicle, camPos, camRot, sirenForceMute);
                            activeHorn.Update3D(currentVehicle, camPos, camRot, shouldMuteAll);
                            activeManual.Update3D(currentVehicle, camPos, camRot, shouldMuteAll);
                        }

                        UpdateActiveAiSirens(camPos, camRot, shouldMuteAll);
                    }

                    if (Game.GameTime >= nextAiScanTime)
                    {
                        nextAiScanTime = Game.GameTime + (uint)PluginConfig.AiScanInterval;
                        ScanForAiVehicles();
                    }
                }
            }
            catch (ThreadAbortException)
            {
            }
            finally
            {
                Shutdown();
            }
        }

        private static void Shutdown()
        {
            activeSiren.Stop(true);
            activeHorn.Stop(true);
            activeManual.Stop(true);

            foreach (var p in activeAiSirens.Values)
                p.Player.Stop(true);
            activeAiSirens.Clear();

            cachedSirens.Clear();
            _profileCache.Clear();
            _vehicleScratch.Clear();

            Game.Console.Print("[CustomSirens] Unloaded – all audio resources released.");
        }

        private static void KillAllSounds(bool dropInstantly = false)
        {
            if (activeSiren.IsPlaying) activeSiren.Stop(dropInstantly);
            if (activeHorn.IsPlaying) activeHorn.Stop(dropInstantly);
            if (activeManual.IsPlaying) activeManual.Stop(dropInstantly);
            activeToneIndex = 0;
            isAutoScanActive = false;
            currentVehicle = null;
        }

        private static void KillAllAiSounds(bool dropInstantly = false)
        {
            foreach (var p in activeAiSirens.Values) p.Player.Stop(dropInstantly);
            activeAiSirens.Clear();
        }

        private static void ParseVCF(string modelName)
        {
            requiredSirenStage = 3;
            maxStage = 1;

            if (!Directory.Exists("ELS")) return;
            string[] files = Directory.GetFiles("ELS", $"{modelName}.xml", SearchOption.AllDirectories);
            if (files.Length == 0) return;

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(files[0]);

                XmlNode sirenNode = doc.SelectSingleNode("//MISC/DfltSirenLtsActivateAtLstg");
                if (sirenNode != null) int.TryParse(sirenNode.InnerText, out requiredSirenStage);

                string[] sections = { "PRML", "WRNL", "SECL" };
                foreach (var sec in sections)
                {
                    for (int i = 3; i >= 1; i--)
                    {
                        XmlNode node = doc.SelectSingleNode($"//{sec}/PresetPatterns/Lstg{i}");
                        if (node?.Attributes?["Enabled"] != null &&
                            node.Attributes["Enabled"].Value.Equals("true", StringComparison.OrdinalIgnoreCase))
                        {
                            if (i > maxStage) maxStage = i;
                        }
                    }
                }
            }
            catch { }
        }

        private static string GetVehicleModelName(Vehicle veh)
        {
            string name = veh.Model.Name;
            if (string.IsNullOrEmpty(name) || name.StartsWith("0x"))
                name = NativeFunction.Natives.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL<string>(veh.Model.Hash);
            return name.ToLower();
        }

        private static void KillAllSounds()
        {
            if (activeSiren.IsPlaying) activeSiren.Stop();
            if (activeHorn.IsPlaying) activeHorn.Stop();
            if (activeManual.IsPlaying) activeManual.Stop();
            activeToneIndex = 0;
            isAutoScanActive = false;
            currentVehicle = null;
        }

        private static void KillAllAiSounds()
        {
            foreach (var p in activeAiSirens.Values) p.Player.Stop();
            activeAiSirens.Clear();
        }

        private static void HandleInputs(Vehicle veh, bool isLightsOn)
        {
            CheckToneKey(PluginConfig.Snd_SrnTon1, ref was1, 1, isLightsOn);
            CheckToneKey(PluginConfig.Snd_SrnTon2, ref was2, 2, isLightsOn);
            CheckToneKey(PluginConfig.Snd_SrnTon3, ref was3, 3, isLightsOn);
            CheckToneKey(PluginConfig.Snd_SrnTon4, ref was4, 4, isLightsOn);

            bool isManulPressed = Game.IsKeyDownRightNow(PluginConfig.Sound_Manul);
            if (isManulPressed && !wasManul)
            {
                string manualPath = GetProfileSiren(currentVehicleModel, "Manual");
                bool hasCustomManual = !string.IsNullOrEmpty(manualPath) && manualPath != "None" && File.Exists(manualPath);

                int toneToPlay = 1;
                if (!hasCustomManual)
                {
                    if (activeToneIndex == 0) toneToPlay = 1;
                    else if (activeToneIndex == 1) toneToPlay = 2;
                    else if (activeToneIndex == 2) toneToPlay = 3;
                    else if (activeToneIndex == 3) toneToPlay = 4;
                    else if (activeToneIndex == 4) toneToPlay = 1;

                    bool found = false;
                    for (int i = 0; i < 4; i++)
                    {
                        string p = GetProfileSiren(currentVehicleModel, $"Tone{toneToPlay}");
                        if (!string.IsNullOrEmpty(p) && p != "None" && File.Exists(p))
                        {
                            found = true;
                            break;
                        }
                        toneToPlay++;
                        if (toneToPlay > 4) toneToPlay = 1;
                    }

                    if (found)
                    {
                        manualPath = GetProfileSiren(currentVehicleModel, $"Tone{toneToPlay}");
                    }
                }

                if (!string.IsNullOrEmpty(manualPath) && manualPath != "None" && File.Exists(manualPath))
                {
                    CachedSound sound = GetCachedSound(manualPath);
                    if (sound != null)
                        activeManual.Play(sound, true, hasCustomManual ? PluginConfig.ManualVol : GetPerSirenVolume($"Tone{toneToPlay}Vol"));
                }
            }
            else if (!isManulPressed && wasManul)
            {
                activeManual.Stop();
            }
            wasManul = isManulPressed;

            bool isScanPressed = Game.IsKeyDownRightNow(PluginConfig.Snd_SrnScan);
            if (isScanPressed && !wasScan)
            {
                if (isLightsOn)
                {
                    if (isAutoScanActive)
                    {
                        isAutoScanActive = false;
                        activeToneIndex = 0;
                        activeSiren.Stop();
                    }
                    else
                    {
                        isAutoScanActive = true;

                        if (activeToneIndex == 0)
                        {
                            activeToneIndex = 1;
                            string path = GetProfileSiren(currentVehicleModel, "Tone1");
                            if (string.IsNullOrEmpty(path) || path == "None" || !File.Exists(path))
                            {
                                AdvanceToNextValidTone(ref activeToneIndex);
                            }
                            PlayCurrentTone();
                        }

                        nextScanChangeTime = Game.GameTime + 6000;
                    }
                }
                else
                {
                    Game.Console.Print("[CustomSirens] Vehicle emergency lights must be active to trigger custom sirens.");
                }
            }
            wasScan = isScanPressed;

            if (isAutoScanActive && activeToneIndex != 0)
            {
                if (Game.GameTime > nextScanChangeTime)
                {
                    AdvanceToNextValidTone(ref activeToneIndex);
                    PlayCurrentTone();
                    nextScanChangeTime = Game.GameTime + 6000;
                }
            }

            bool isTonX = Game.IsKeyDownRightNow(PluginConfig.Snd_SrnTonX);
            wasTonX = isTonX;
            bool isPnic = Game.IsKeyDownRightNow(PluginConfig.Snd_SrnPnic);
            wasPnic = isPnic;

            bool isHornPressed = NativeFunction.Natives.IS_DISABLED_CONTROL_PRESSED<bool>(0, (int)GameControl.VehicleHorn);
            if (isHornPressed && !wasHorn)
            {
                string path = GetProfileSiren(currentVehicleModel, "Horn");
                if (!string.IsNullOrEmpty(path) && path != "None" && File.Exists(path))
                {
                    CachedSound sound = GetCachedSound(path);
                    if (sound != null)
                        activeHorn.Play(sound, true, GetPerSirenVolume("HornVol"));
                }
            }
            else if (!isHornPressed && wasHorn)
            {
                activeHorn.Stop();
            }
            wasHorn = isHornPressed;
        }

        private static void AdvanceToNextValidTone(ref int toneIndex)
        {
            int startIdx = toneIndex == 0 ? 1 : toneIndex;
            int nextIdx = startIdx;
            for (int i = 0; i < 4; i++)
            {
                nextIdx++;
                if (nextIdx > 4) nextIdx = 1;
                string path = GetProfileSiren(currentVehicleModel, $"Tone{nextIdx}");
                if (!string.IsNullOrEmpty(path) && path != "None" && File.Exists(path))
                {
                    toneIndex = nextIdx;
                    return;
                }
            }
        }

        private static void PlayCurrentTone()
        {
            string path = GetProfileSiren(currentVehicleModel, $"Tone{activeToneIndex}");
            CachedSound sound = GetCachedSound(path);
            if (sound != null)
                activeSiren.Play(sound, true, GetPerSirenVolume($"Tone{activeToneIndex}Vol"));
        }

        private static void CheckToneKey(Keys key, ref bool wasKey, int toneIndex, bool isLightsOn)
        {
            bool isKey = Game.IsKeyDownRightNow(key);
            if (isKey && !wasKey)
            {
                if (!isLightsOn)
                {
                    Game.Console.Print(
                        "[CustomSirens] Vehicle emergency lights must be active to trigger custom sirens.");
                }
                else
                {
                    string path = GetProfileSiren(currentVehicleModel, $"Tone{toneIndex}");

                    if (string.IsNullOrEmpty(path) || path == "None" || !File.Exists(path))
                    {
                        wasKey = isKey;
                        return;
                    }

                    if (activeToneIndex == toneIndex)
                    {
                        if (isAutoScanActive)
                        {
                            isAutoScanActive = false;
                        }
                        else
                        {
                            activeToneIndex = 0;
                            activeSiren.Stop();
                        }
                    }
                    else
                    {
                        isAutoScanActive = false;
                        activeToneIndex = toneIndex;
                        CachedSound sound = GetCachedSound(path);
                        if (sound != null)
                            activeSiren.Play(sound, true, GetPerSirenVolume($"Tone{toneIndex}Vol"));
                    }
                }
            }
            wasKey = isKey;
        }

        private static float GetPerSirenVolume(string configKey)
        {
            switch (configKey)
            {
                case "Tone1Vol": return PluginConfig.Tone1Vol;
                case "Tone2Vol": return PluginConfig.Tone2Vol;
                case "Tone3Vol": return PluginConfig.Tone3Vol;
                case "Tone4Vol": return PluginConfig.Tone4Vol;
                case "HornVol": return PluginConfig.HornVol;
                case "ManualVol": return PluginConfig.ManualVol;
                default: return 1f;
            }
        }

        private static string GetProfileSiren(string modelName, string key)
        {
            if (string.IsNullOrEmpty(modelName)) return "None";

            string cacheKey = modelName + "|" + key;
            if (_profileCache.TryGetValue(cacheKey, out string cached)) return cached;

            string result;
            string vehIni = $@"{profilesFolder}{modelName}.ini";

            if (File.Exists(vehIni))
            {
                InitializationFile vIni = new InitializationFile(vehIni);
                string file = vIni.ReadString("Sirens", key, "None");
                result = (file != "None" && File.Exists(wavFolder + file)) ? wavFolder + file : "None";
            }
            else
            {
                InitializationFile gIni = new InitializationFile($@"{profilesFolder}Global.ini");
                string gFile = gIni.ReadString("Sirens", key, "None");
                result = (gFile != "None" && File.Exists(wavFolder + gFile)) ? wavFolder + gFile : "None";
            }

            _profileCache[cacheKey] = result;
            return result;
        }

        public static CachedSound GetCachedSound(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "None" || !File.Exists(path)) return null;

            if (!cachedSirens.TryGetValue(path, out CachedSound sound))
            {
                try
                {
                    sound = new CachedSound(path);
                    cachedSirens[path] = sound;
                }
                catch (Exception ex)
                {
                    Game.Console.Print($"[CustomSirens] RAM Caching Failed for {path}: {ex.Message}");
                    return null;
                }
            }
            return sound;
        }

        private static void CacheVehicleSirens()
        {
            vehicleProfileExists = File.Exists($@"{profilesFolder}{currentVehicleModel}.ini");

            foreach (var key in new[] { "Tone1", "Tone2", "Tone3", "Tone4", "Horn", "Manual" })
                GetCachedSound(GetProfileSiren(currentVehicleModel, key));
        }

        private static bool IsVehicleLightsOn(Vehicle veh)
        {
            if (veh == null || !veh.IsValid()) return false;

            if (NativeFunction.Natives.DECOR_EXIST_ON<bool>(veh, "ELS_lightstage"))
            {
                int stage = NativeFunction.Natives.DECOR_GET_INT<int>(veh, "ELS_lightstage");
                return stage >= requiredSirenStage;
            }
            return veh.IsSirenOn;
        }

        private static void ScanForAiVehicles()
        {
            if (Game.LocalPlayer.Character == null) return;
            Vector3 playerPos = Game.LocalPlayer.Character.Position;

            _vehicleScratch.Clear();
            foreach (var kvp in activeAiSirens)
            {
                if (!kvp.Key.IsValid() || !kvp.Key.IsAlive ||
                    Vector3.Distance(playerPos, kvp.Key.Position) > PluginConfig.MaxDistance)
                    _vehicleScratch.Add(kvp.Key);
            }
            foreach (var v in _vehicleScratch)
            {
                if (activeAiSirens.TryGetValue(v, out var p)) p.Player.Stop();
                activeAiSirens.Remove(v);
            }

            Vehicle[] allVehicles = World.GetAllVehicles();
            foreach (Vehicle v in allVehicles)
            {
                if (activeAiSirens.Count >= PluginConfig.MaxAiUnits) break;
                if (!v.IsValid() || !v.IsAlive || v == currentVehicle) continue;
                if (activeAiSirens.ContainsKey(v)) continue;

                string modelName = GetVehicleModelName(v);
                bool isEmergency = v.HasSiren || v.Class == VehicleClass.Emergency || File.Exists($@"ELS\{modelName}.xml");
                if (!isEmergency) continue;

                if (Vector3.Distance(playerPos, v.Position) > PluginConfig.MaxDistance) continue;

                bool hasNoDriver = v.Driver == null || !v.Driver.IsValid();

                if (hasNoDriver && PluginConfig.AutomaticAiSirenCutoff)
                {
                    if (v.IsSirenOn) v.IsSirenOn = false;
                    continue;
                }

                bool isCode3 = v.IsSirenOn || IsVehicleLightsOn(v);

                if (isCode3)
                {
                    string path = GetProfileSiren(modelName, "Tone1");
                    CachedSound sound = GetCachedSound(path);
                    if (sound != null)
                    {
                        NativeFunction.Natives.SET_VEHICLE_HAS_MUTED_SIRENS<bool>(v, true);

                        SirenPlayer aiPlayer = new SirenPlayer();
                        aiPlayer.Play(sound, true, PluginConfig.Tone1Vol);

                        AiSirenState state = new AiSirenState
                        {
                            Player = aiPlayer,
                            CurrentToneIndex = 1,
                            NextToneChangeTime = Game.GameTime + (uint)rnd.Next(4000, 8000)
                        };
                        activeAiSirens[v] = state;
                    }
                }
            }
        }

        private static void UpdateActiveAiSirens(Vector3 camPos, Vector3 camRot, bool forceMuteAll = false)
        {
            if (activeAiSirens.Count == 0) return;

            _vehicleScratch.Clear();
            foreach (var pair in activeAiSirens)
            {
                Vehicle aiVeh = pair.Key;
                AiSirenState state = pair.Value;
                SirenPlayer aiPlayer = state.Player;

                if (!aiVeh.IsValid() || !aiVeh.IsAlive || aiVeh == currentVehicle)
                {
                    aiPlayer.Stop();
                    _vehicleScratch.Add(aiVeh);
                    continue;
                }

                bool hasNoDriver = aiVeh.Driver == null || !aiVeh.Driver.IsValid();

                if (hasNoDriver && PluginConfig.AutomaticAiSirenCutoff)
                {
                    if (aiVeh.IsSirenOn) aiVeh.IsSirenOn = false;
                    aiPlayer.Stop();
                    _vehicleScratch.Add(aiVeh);
                    continue;
                }

                bool isCode3 = aiVeh.IsSirenOn || IsVehicleLightsOn(aiVeh);

                if (!isCode3)
                {
                    if (aiPlayer.IsPlaying) aiPlayer.Stop();
                }
                else
                {
                    string modelName = GetVehicleModelName(aiVeh);

                    if (!aiPlayer.IsPlaying)
                    {
                        string path = GetProfileSiren(modelName, $"Tone{state.CurrentToneIndex}");
                        if (string.IsNullOrEmpty(path) || path == "None" || !File.Exists(path))
                        {
                            state.CurrentToneIndex = 1;
                            path = GetProfileSiren(modelName, "Tone1");
                        }

                        CachedSound sound = GetCachedSound(path);
                        if (sound != null)
                        {
                            NativeFunction.Natives.SET_VEHICLE_HAS_MUTED_SIRENS<bool>(aiVeh, true);
                            aiPlayer.Play(sound, true, GetPerSirenVolume($"Tone{state.CurrentToneIndex}Vol"));
                            state.NextToneChangeTime = Game.GameTime + (uint)rnd.Next(4000, 8000);
                        }
                    }
                    else
                    {
                        if (Game.GameTime > state.NextToneChangeTime && !forceMuteAll)
                        {
                            int nextTone = state.CurrentToneIndex;
                            bool found = false;

                            for (int i = 0; i < 4; i++)
                            {
                                nextTone++;
                                if (nextTone > 4) nextTone = 1;
                                string path = GetProfileSiren(modelName, $"Tone{nextTone}");
                                if (!string.IsNullOrEmpty(path) && path != "None" && File.Exists(path))
                                {
                                    found = true;
                                    break;
                                }
                            }

                            if (found && nextTone != state.CurrentToneIndex)
                            {
                                string path = GetProfileSiren(modelName, $"Tone{nextTone}");
                                CachedSound sound = GetCachedSound(path);
                                if (sound != null)
                                {
                                    aiPlayer.Play(sound, true, GetPerSirenVolume($"Tone{nextTone}Vol"));
                                    state.CurrentToneIndex = nextTone;
                                }
                            }
                            state.NextToneChangeTime = Game.GameTime + (uint)rnd.Next(4000, 8000);
                        }
                    }

                    aiPlayer.Update3D(aiVeh, camPos, camRot, forceMuteAll);
                }
            }

            foreach (var v in _vehicleScratch) activeAiSirens.Remove(v);
        }

        private static void SetupMenu()
        {
            if (!Directory.Exists(wavFolder)) Directory.CreateDirectory(wavFolder);
            if (!Directory.Exists(profilesFolder)) Directory.CreateDirectory(profilesFolder);

            LoadWavFiles();

            menuPool = new MenuPool();
            sirenMenu = new UIMenu("~r~Custom ~p~ELS ~b~Sirens", "");
            menuPool.Add(sirenMenu);

            List<dynamic> wavsDynamic = availableWavs.Cast<dynamic>().ToList();

            modeItem = new UIMenuListItem("~h~~y~Profile Mode", new List<dynamic> { "Global Default", "Vehicle Specific" }, 0);
            masterVolumeItem = new UIMenuNumericScrollerItem<float>("~h~~y~Master Volume", "Volume Percentage for the global Volume", 0f, 100f, 5f);
            masterVolumeItem.Value = PluginConfig.MasterVolume * 100f;

            aiCutoffItem = new UIMenuCheckboxItem("~c~Automatic Siren Cutoff", PluginConfig.AutomaticAiSirenCutoff, "Cuts off the siren when the driver gets out of the vehicle.");
            aiScanIntervalItem = new UIMenuNumericScrollerItem<int>("~c~AI Scan Frequency", "Time in ms the Plugin should scan for AI vehicles (Lower = more demanding).", 100, 5000, 50);
            maxAiUnitsItem = new UIMenuNumericScrollerItem<int>("~c~Max Affected AI Units", "How many AI units can be affected simultaneously.", 1, 50, 1);
            falloffItem = new UIMenuNumericScrollerItem<float>("~c~Siren Falloff Curve", "Adjusts how quickly the sound fades over distance. (Default: 3.0)", 0.5f, 10.0f, 0.5f);
            aiScanIntervalItem.Value = PluginConfig.AiScanInterval;
            maxAiUnitsItem.Value = PluginConfig.MaxAiUnits;
            falloffItem.Value = PluginConfig.FalloffExponent;

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

            reloadConfigsItem = new UIMenuItem("~q~Reload Configurations", "~q~Reloads settings and keybinds from Config.ini and ELS.ini.");
            reloadWavsItem = new UIMenuItem("~q~Reload WAV Files", "~q~Rescans the WAVs directory to update available audio files.");
            UIMenuItem patchELSItem = new UIMenuItem("~r~Kill ELS Sounds (Patch VCFs)", "~r~Mutes the Sirens in your ELS VCFs (Creates a backup of your current VCFs before patching).~n~Game Restart required!");
            UIMenuItem saveItem = new UIMenuItem("~h~~g~Save Profile", "~g~Saves the selected tones and volumes.");

            sirenMenu.AddItem(modeItem);
            sirenMenu.AddItem(masterVolumeItem);
            sirenMenu.AddItem(aiCutoffItem);
            sirenMenu.AddItem(aiScanIntervalItem);
            sirenMenu.AddItem(maxAiUnitsItem);
            sirenMenu.AddItem(falloffItem);
            sirenMenu.AddItem(tone1Item); sirenMenu.AddItem(tone1Vol);
            sirenMenu.AddItem(tone2Item); sirenMenu.AddItem(tone2Vol);
            sirenMenu.AddItem(tone3Item); sirenMenu.AddItem(tone3Vol);
            sirenMenu.AddItem(tone4Item); sirenMenu.AddItem(tone4Vol);
            sirenMenu.AddItem(hornItem); sirenMenu.AddItem(hornVol);
            sirenMenu.AddItem(manualItem); sirenMenu.AddItem(manualVol);
            sirenMenu.AddItem(reloadConfigsItem);
            sirenMenu.AddItem(reloadWavsItem);
            sirenMenu.AddItem(patchELSItem);
            sirenMenu.AddItem(saveItem);

            sirenMenu.OnListChange += (s, item, idx) => { if (item == modeItem) UpdateMenuSelections(); };

            sirenMenu.OnCheckboxChange += (s, item, checkedState) =>
            {
                if (item == aiCutoffItem)
                {
                    PluginConfig.AutomaticAiSirenCutoff = checkedState;
                    PluginConfig.SaveConfig();
                }
            };

            aiScanIntervalItem.IndexChanged +=
                (s, o, n) => { PluginConfig.AiScanInterval = aiScanIntervalItem.Value; PluginConfig.SaveConfig(); };
            maxAiUnitsItem.IndexChanged +=
                (s, o, n) => { PluginConfig.MaxAiUnits = maxAiUnitsItem.Value; PluginConfig.SaveConfig(); };
            masterVolumeItem.IndexChanged +=
                (s, o, n) => { PluginConfig.MasterVolume = masterVolumeItem.Value / 100f; PluginConfig.SaveConfig(); };
            falloffItem.IndexChanged +=
                (s, o, n) => { PluginConfig.FalloffExponent = falloffItem.Value; PluginConfig.SaveConfig(); };

            tone1Vol.IndexChanged += (s, o, n) => { PluginConfig.Tone1Vol = tone1Vol.Value / 100f; PluginConfig.SaveConfig(); };
            tone2Vol.IndexChanged += (s, o, n) => { PluginConfig.Tone2Vol = tone2Vol.Value / 100f; PluginConfig.SaveConfig(); };
            tone3Vol.IndexChanged += (s, o, n) => { PluginConfig.Tone3Vol = tone3Vol.Value / 100f; PluginConfig.SaveConfig(); };
            tone4Vol.IndexChanged += (s, o, n) => { PluginConfig.Tone4Vol = tone4Vol.Value / 100f; PluginConfig.SaveConfig(); };
            hornVol.IndexChanged += (s, o, n) => { PluginConfig.HornVol = hornVol.Value / 100f; PluginConfig.SaveConfig(); };
            manualVol.IndexChanged += (s, o, n) => { PluginConfig.ManualVol = manualVol.Value / 100f; PluginConfig.SaveConfig(); };

            sirenMenu.OnItemSelect += (s, item, idx) =>
            {
                if (item == saveItem)
                {
                    SaveToneFilesToProfile();
                    _profileCache.Clear();
                    CacheVehicleSirens();
                }
                if (item == patchELSItem) PatchELSVCFs();
                if (item == reloadConfigsItem)
                {
                    PluginConfig.Load();

                    masterVolumeItem.Value = PluginConfig.MasterVolume * 100f;
                    aiCutoffItem.Checked = PluginConfig.AutomaticAiSirenCutoff;
                    aiScanIntervalItem.Value = PluginConfig.AiScanInterval;
                    maxAiUnitsItem.Value = PluginConfig.MaxAiUnits;
                    falloffItem.Value = PluginConfig.FalloffExponent;

                    tone1Vol.Value = PluginConfig.Tone1Vol * 100f;
                    tone2Vol.Value = PluginConfig.Tone2Vol * 100f;
                    tone3Vol.Value = PluginConfig.Tone3Vol * 100f;
                    tone4Vol.Value = PluginConfig.Tone4Vol * 100f;
                    hornVol.Value = PluginConfig.HornVol * 100f;
                    manualVol.Value = PluginConfig.ManualVol * 100f;

                    Game.DisplayNotification("~g~Configurations & Keybinds Reloaded Successfully!");
                }
                if (item == reloadWavsItem)
                {
                    LoadWavFiles();

                    List<dynamic> updatedWavs = availableWavs.Cast<dynamic>().ToList();

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

        private static void PatchELSVCFs()
        {
            string backupFolder = Path.Combine("ELS", "Original VCF Backups");
            if (!Directory.Exists("ELS")) return;

            if (!Directory.Exists(backupFolder)) Directory.CreateDirectory(backupFolder);

            string[] files = Directory.GetFiles("ELS", "*.xml", SearchOption.TopDirectoryOnly);
            int count = 0;

            foreach (string file in files)
            {
                try
                {
                    string fileName = Path.GetFileName(file);
                    string backupPath = Path.Combine(backupFolder, fileName);

                    if (!File.Exists(backupPath))
                    {
                        File.Copy(file, backupPath);
                    }

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
            availableWavs.Clear();
            availableWavs.Add("None");
            if (Directory.Exists(wavFolder))
                availableWavs.AddRange(Directory.GetFiles(wavFolder, "*.wav").Select(Path.GetFileName));
        }

        private static void UpdateMenuSelections()
        {
            string targetIni = modeItem.Index == 0 ? "Global.ini" : $"{currentVehicleModel}.ini";
            InitializationFile ini = new InitializationFile($@"{profilesFolder}{targetIni}");

            void SetIndex(UIMenuListItem listItem, string toneName)
            {
                string saved = ini.ReadString("Sirens", toneName, "None");
                int idx = availableWavs.IndexOf(saved);
                listItem.Index = idx >= 0 ? idx : 0;
            }

            SetIndex(tone1Item, "Tone1"); SetIndex(tone2Item, "Tone2");
            SetIndex(tone3Item, "Tone3"); SetIndex(tone4Item, "Tone4");
            SetIndex(hornItem, "Horn"); SetIndex(manualItem, "Manual");
        }

        private static void SaveToneFilesToProfile()
        {
            string targetIni = modeItem.Index == 0 ? "Global.ini" : $"{currentVehicleModel}.ini";
            InitializationFile ini = new InitializationFile($@"{profilesFolder}{targetIni}");
            if (!ini.Exists()) ini.Create();

            ini.Write("Sirens", "Tone1", availableWavs[tone1Item.Index]);
            ini.Write("Sirens", "Tone2", availableWavs[tone2Item.Index]);
            ini.Write("Sirens", "Tone3", availableWavs[tone3Item.Index]);
            ini.Write("Sirens", "Tone4", availableWavs[tone4Item.Index]);
            ini.Write("Sirens", "Horn", availableWavs[hornItem.Index]);
            ini.Write("Sirens", "Manual", availableWavs[manualItem.Index]);

            Game.DisplayNotification($"~g~Saved tone selections to {targetIni}!");
        }
    }

    public class AiSirenState
    {
        public SirenPlayer Player { get; set; } = new SirenPlayer();
        public int CurrentToneIndex { get; set; } = 1;
        public uint NextToneChangeTime { get; set; } = 0;
    }

    public class SirenPlayer : IDisposable
    {
        private WaveOutEvent waveOut;
        private CachedSampleProvider cachedProvider; // Kept as a reference so we can mute it
        private PanningSampleProvider panProvider;
        private CityReverbProvider reverbProvider;
        private VolumeSampleProvider volProvider;
        private float perSirenVolume = 1f;

        public bool IsPlaying { get; private set; }

        public void Play(CachedSound cached, bool loop, float volumeMultiplier)
        {
            Stop(); // This will naturally trigger the reverb tail for the PREVIOUS sound
            perSirenVolume = volumeMultiplier;
            if (cached == null) return;

            try
            {
                cachedProvider = new CachedSampleProvider(cached, loop);
                ISampleProvider sp = cachedProvider;

                if (sp.WaveFormat.Channels == 2)
                    sp = new StereoToMonoSampleProvider(sp) { LeftVolume = 0.5f, RightVolume = 0.5f };

                panProvider = new PanningSampleProvider(sp) { Pan = 0f };
                reverbProvider = new CityReverbProvider(panProvider) { BaseReverb = 0.12f, DistanceReverb = 0f };
                volProvider = new VolumeSampleProvider(reverbProvider) { Volume = 0f };

                waveOut = new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 2 };
                waveOut.Init(volProvider);
                waveOut.Play();
                IsPlaying = true;
            }
            catch { }
        }

        public void Stop(bool dropInstantly = false)
        {
            IsPlaying = false;
            if (waveOut == null) return;

            if (dropInstantly)
            {
                waveOut.Stop();
                waveOut.Dispose();
            }
            else
            {
                // Mute the raw audio source so the CityReverbProvider decays naturally over 2 seconds
                if (cachedProvider != null) cachedProvider.IsMuted = true;

                var oldWave = waveOut; // Capture for the closure thread

                GameFiber.StartNew(() =>
                {
                    GameFiber.Sleep(2000); // Allow reverb tail to wash out completely
                    try
                    {
                        oldWave?.Stop();
                        oldWave?.Dispose();
                    }
                    catch { }
                });
            }

            // Detach components so a new Play() call can seamlessly overlay on top of the fading tail
            waveOut = null;
            cachedProvider = null;
            panProvider = null;
            reverbProvider = null;
            volProvider = null;
        }

        public void Dispose() => Stop(true);

        public void SetVolume(float vol)
        {
            if (volProvider != null)
                volProvider.Volume = MathHelper.Clamp(vol, 0f, 1f);
        }

        public void Update3D(Vehicle veh, Vector3 camPos, Vector3 camRot, bool forceMute = false)
        {
            if (!IsPlaying || volProvider == null || panProvider == null || !veh.IsValid()) return;

            if (forceMute)
            {
                volProvider.Volume = 0f;
                return;
            }

            Vector3 sirenPos = veh.Position;
            float distance = Vector3.Distance(camPos, sirenPos);
            float targetVolume;

            if (distance <= PluginConfig.MinDistance)
                targetVolume = 1f;
            else if (distance >= PluginConfig.MaxDistance)
                targetVolume = 0f;
            else
            {
                float t = (distance - PluginConfig.MinDistance) / (PluginConfig.MaxDistance - PluginConfig.MinDistance);
                targetVolume = (float)Math.Pow(1.0 - t, PluginConfig.FalloffExponent);
            }

            if (reverbProvider != null)
            {
                float reverbScale = MathHelper.Clamp(distance / (PluginConfig.MaxDistance * 0.5f), 0f, 1f);
                reverbProvider.DistanceReverb = reverbScale * 0.45f;
            }

            float cabinDampening = 1f;
            Ped player = Game.LocalPlayer.Character;
            if (player != null && player.IsInAnyVehicle(false))
                cabinDampening = player.CurrentVehicle == veh ? 0.45f : 0.20f;

            volProvider.Volume = MathHelper.Clamp(
                targetVolume * PluginConfig.MasterVolume * perSirenVolume * cabinDampening, 0f, 1f);

            Vector2 dirToSound = new Vector2(sirenPos.X - camPos.X, sirenPos.Y - camPos.Y);
            if (dirToSound.Length() > 0.01f) dirToSound.Normalize();

            double yawRads = camRot.Z * (Math.PI / 180.0);
            Vector2 camRight = new Vector2((float)Math.Cos(yawRads), (float)Math.Sin(yawRads));
            camRight.Normalize();

            panProvider.Pan = MathHelper.Clamp(Vector2.Dot(camRight, dirToSound), -1f, 1f);
        }
    }

    public class CachedSound
    {
        public float[] AudioData { get; private set; }
        public WaveFormat WaveFormat { get; private set; }

        public CachedSound(string filePath)
        {
            using (var reader = new AudioFileReader(filePath))
            {
                WaveFormat = reader.WaveFormat;

                int estimatedSamples = (int)(reader.Length / sizeof(float));
                AudioData = new float[estimatedSamples];

                var readBuffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
                int totalRead = 0;
                int samplesRead;

                while ((samplesRead = reader.Read(readBuffer, 0, readBuffer.Length)) > 0)
                {
                    if (totalRead + samplesRead > AudioData.Length)
                    {
                        float[] newArray = new float[(int)((totalRead + samplesRead) * 1.25f)];
                        Array.Copy(AudioData, newArray, totalRead);
                        AudioData = newArray;
                    }

                    Array.Copy(readBuffer, 0, AudioData, totalRead, samplesRead);
                    totalRead += samplesRead;
                }

                if (totalRead < AudioData.Length)
                {
                    float[] trimmed = new float[totalRead];
                    Array.Copy(AudioData, trimmed, totalRead);
                    AudioData = trimmed;
                }
            }
        }
    }

    public class CachedSampleProvider : ISampleProvider
    {
        private readonly CachedSound cachedSound;
        private long position;
        private readonly bool loop;
        public bool IsMuted { get; set; } = false;

        public CachedSampleProvider(CachedSound cachedSound, bool loop)
        {
            this.cachedSound = cachedSound;
            this.loop = loop;
        }

        public WaveFormat WaveFormat => cachedSound.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            if (IsMuted)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            int availableSamples = cachedSound.AudioData.Length - (int)position;
            int samplesToCopy = Math.Min(availableSamples, count);
            Array.Copy(cachedSound.AudioData, position, buffer, offset, samplesToCopy);
            position += samplesToCopy;

            if (position >= cachedSound.AudioData.Length && loop)
            {
                position = 0;
                if (samplesToCopy < count)
                {
                    int additional = Math.Min(cachedSound.AudioData.Length, count - samplesToCopy);
                    Array.Copy(cachedSound.AudioData, 0, buffer, offset + samplesToCopy, additional);
                    position = additional;
                    samplesToCopy += additional;
                }
            }
            return samplesToCopy;
        }
    }

    public class CityReverbProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly float[] delayLeft1, delayLeft2;
        private readonly float[] delayRight1, delayRight2;
        private int posL1, posL2, posR1, posR2;

        public float BaseReverb { get; set; } = 0.12f;
        public float DistanceReverb { get; set; } = 0f;

        public WaveFormat WaveFormat => source.WaveFormat;

        public CityReverbProvider(ISampleProvider source)
        {
            this.source = source;
            int sr = source.WaveFormat.SampleRate;

            delayLeft1 = new float[(int)(sr * 0.113f)];
            delayLeft2 = new float[(int)(sr * 0.163f)];
            delayRight1 = new float[(int)(sr * 0.127f)];
            delayRight2 = new float[(int)(sr * 0.179f)];
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            float wetLevel = BaseReverb + DistanceReverb;
            if (wetLevel > 0.8f) wetLevel = 0.8f;

            for (int i = 0; i < read; i += 2)
            {
                if (i + 1 >= read) break;

                float inL = buffer[offset + i];
                float inR = buffer[offset + i + 1];

                float dL = delayLeft1[posL1] + delayLeft2[posL2];
                float dR = delayRight1[posR1] + delayRight2[posR2];

                float outL = inL + (dL * 0.5f + dR * 0.2f) * wetLevel;
                float outR = inR + (dR * 0.5f + dL * 0.2f) * wetLevel;

                buffer[offset + i] = outL;
                buffer[offset + i + 1] = outR;

                delayLeft1[posL1] = inL + delayLeft1[posL1] * 0.35f;
                delayLeft2[posL2] = inL + delayLeft2[posL2] * 0.25f;
                delayRight1[posR1] = inR + delayRight1[posR1] * 0.35f;
                delayRight2[posR2] = inR + delayRight2[posR2] * 0.25f;

                posL1 = (posL1 + 1) % delayLeft1.Length;
                posL2 = (posL2 + 1) % delayLeft2.Length;
                posR1 = (posR1 + 1) % delayRight1.Length;
                posR2 = (posR2 + 1) % delayRight2.Length;
            }
            return read;
        }
    }

    public static class PluginConfig
    {
        public static Keys MenuKey = Keys.F10;

        public static Keys Sound_Manul = (Keys)82;
        public static Keys Snd_SrnTon1 = (Keys)49;
        public static Keys Snd_SrnTon2 = (Keys)50;
        public static Keys Snd_SrnTon3 = (Keys)51;
        public static Keys Snd_SrnTon4 = (Keys)52;
        public static Keys Snd_SrnScan = (Keys)53;
        public static Keys Snd_SrnTonX = (Keys)54;
        public static Keys Snd_SrnPnic = (Keys)55;

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

        public static void Load()
        {
            if (!Directory.Exists(EntryPoint.baseFolder))
                Directory.CreateDirectory(EntryPoint.baseFolder);

            InitializationFile ini = new InitializationFile(EntryPoint.configFile);
            if (!ini.Exists())
            {
                ini.Create();
                ini.Write("Settings", "MenuKey", MenuKey.ToString());
                ini.Write("Settings", "MasterVolume", MasterVolume.ToString());
                ini.Write("Settings", "AutomaticAiSirenCutoff", AutomaticAiSirenCutoff.ToString());
                ini.Write("Settings", "AiScanInterval", AiScanInterval.ToString());
                ini.Write("Settings", "MaxAiUnits", MaxAiUnits.ToString());
                ini.Write("Settings", "MaxDistance", MaxDistance.ToString());
                ini.Write("Settings", "MinDistance", MinDistance.ToString());
                ini.Write("Settings", "FalloffExponent", FalloffExponent.ToString());

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
                MasterVolume = ini.ReadSingle("Settings", "MasterVolume", MasterVolume);
                AutomaticAiSirenCutoff = ini.ReadBoolean("Settings", "AutomaticAiSirenCutoff", AutomaticAiSirenCutoff);
                AiScanInterval = ini.ReadInt32("Settings", "AiScanInterval", AiScanInterval);
                MaxAiUnits = ini.ReadInt32("Settings", "MaxAiUnits", MaxAiUnits);
                MaxDistance = ini.ReadSingle("Settings", "MaxDistance", MaxDistance);
                MinDistance = ini.ReadSingle("Settings", "MinDistance", MinDistance);
                FalloffExponent = ini.ReadSingle("Settings", "FalloffExponent", FalloffExponent);

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
            InitializationFile ini = new InitializationFile(EntryPoint.configFile);
            ini.Write("SirenVolumes", "Tone1Vol", Tone1Vol.ToString());
            ini.Write("SirenVolumes", "Tone2Vol", Tone2Vol.ToString());
            ini.Write("SirenVolumes", "Tone3Vol", Tone3Vol.ToString());
            ini.Write("SirenVolumes", "Tone4Vol", Tone4Vol.ToString());
            ini.Write("SirenVolumes", "HornVol", HornVol.ToString());
            ini.Write("SirenVolumes", "ManualVol", ManualVol.ToString());
            ini.Write("Settings", "MasterVolume", MasterVolume.ToString());
            ini.Write("Settings", "AutomaticAiSirenCutoff", AutomaticAiSirenCutoff.ToString());
            ini.Write("Settings", "AiScanInterval", AiScanInterval.ToString());
            ini.Write("Settings", "MaxAiUnits", MaxAiUnits.ToString());
            ini.Write("Settings", "FalloffExponent", FalloffExponent.ToString());
        }
    }
}