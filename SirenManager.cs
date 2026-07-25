using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace CustomELSSirens
{
    public static class SirenManager
    {
        public static Dictionary<string, CachedSound> cachedSirens = new Dictionary<string, CachedSound>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _profileCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Vehicle> _vehicleScratch = new List<Vehicle>();

        private static readonly SirenPlayer activeSiren = new SirenPlayer();
        private static readonly SirenPlayer activeHorn = new SirenPlayer();
        private static readonly SirenPlayer activeManual = new SirenPlayer();
        private static readonly Dictionary<Vehicle, AiSirenState> activeAiSirens = new Dictionary<Vehicle, AiSirenState>();

        private static Vehicle currentVehicle = null;
        public static string CurrentVehicleModel = string.Empty;

        private static int maxStage = 3;
        private static int requiredSirenStage = 3;
        private static bool vehicleProfileExists = false;

        private static int activeToneIndex = 0;
        private static bool isAutoScanActive = false;
        private static uint nextScanChangeTime = 0;

        private static bool was1, was2, was3, was4, wasHorn, wasManul, wasScan, wasTonX, wasPnic;
        private static bool _wasSirenInterrupted = false;
        private static uint nextAiScanTime = 0;
        private static readonly Random rnd = new Random();

        private static readonly HashSet<string> _elsModelsCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _elsModelsCached = false;

        public static void ProcessLoop()
        {
            Ped player = Game.LocalPlayer.Character;
            if (player == null || !player.IsAlive)
            {
                KillAllSounds(true);
                KillAllAiSounds(true);
                return;
            }

            bool inVehicle = player.IsInAnyVehicle(false);
            Vehicle veh = inVehicle ? player.CurrentVehicle : currentVehicle;

            if (inVehicle && currentVehicle != veh)
            {
                currentVehicle = veh;
                CurrentVehicleModel = GetVehicleModelName(veh);
                activeToneIndex = 0;
                isAutoScanActive = false;

                activeSiren.Stop(true);
                activeHorn.Stop(true);
                activeManual.Stop(true);

                ResetInputs();
                ParseVCF(CurrentVehicleModel);
                _profileCache.Clear();
                CacheVehicleSirens();
            }

            if (currentVehicle != null && currentVehicle.IsValid() && currentVehicle.IsAlive)
            {
                string hornProfile = GetProfileSiren(CurrentVehicleModel, "Horn");
                bool hasCustomHorn = !string.IsNullOrEmpty(hornProfile) && hornProfile != "None";

                bool isEmergency = currentVehicle.HasSiren ||
                                   currentVehicle.Class == VehicleClass.Emergency ||
                                   IsElsVehicle(CurrentVehicleModel) ||
                                   vehicleProfileExists ||
                                   IsEmergencyModelName(CurrentVehicleModel);

                if (isEmergency)
                {
                    if (currentVehicle.HasSiren && hasCustomHorn)
                    {
                        Game.DisableControlAction(0, GameControl.VehicleHorn, true);
                    }

                    bool isLightsOn = IsVehicleLightsOn(currentVehicle);

                    if (inVehicle)
                    {
                        HandleInputs(currentVehicle, isLightsOn);
                    }
                    else
                    {
                        if (activeHorn.IsPlaying) activeHorn.Stop(false);
                        if (activeManual.IsPlaying) activeManual.Stop(false);

                        wasHorn = false;
                        wasManul = false;

                        bool hasNoDriver = currentVehicle.Driver == null || !currentVehicle.Driver.IsValid();
                        bool isDriverDead = !hasNoDriver && !currentVehicle.Driver.IsAlive;

                        if (isDriverDead || (hasNoDriver && PluginConfig.AutomaticAiSirenCutoff))
                        {
                            if (activeToneIndex != 0)
                            {
                                activeToneIndex = 0;
                                isAutoScanActive = false;
                                activeSiren.Stop(false, true);
                            }
                        }
                    }

                    if (!isLightsOn && activeToneIndex != 0)
                    {
                        activeToneIndex = 0;
                        isAutoScanActive = false;
                        activeSiren.Stop(false);
                    }
                }
                else
                {
                    StopAllLocalSounds(true);
                }
            }
            else
            {
                KillAllSounds();
            }

            bool isPaused = Game.IsPaused || Game.IsLoading || NativeFunction.Natives.IS_PAUSE_MENU_ACTIVE<bool>() || Game.TimeScale == 0f;

            if (isPaused)
            {
                DropVolumes();
            }
            else
            {
                bool isMenuOpen = (MenuManager.MainMenu != null && MenuManager.MainMenu.Visible) ||
                                  (MenuManager.SettingsMenu != null && MenuManager.SettingsMenu.Visible);
                bool shouldMuteAll = isMenuOpen;

                Vector3 camPos = NativeFunction.Natives.GET_GAMEPLAY_CAM_COORD<Vector3>();
                Vector3 camRot = NativeFunction.Natives.GET_GAMEPLAY_CAM_ROT<Vector3>(2);

                if (currentVehicle != null && currentVehicle.IsValid() && currentVehicle.IsAlive)
                {
                    bool isHornActive = wasHorn || (activeHorn.IsPlaying && !activeHorn.IsFadingOut);
                    bool isManualActive = wasManul || (activeManual.IsPlaying && !activeManual.IsFadingOut);

                    bool sirenForceMute = shouldMuteAll || isManualActive || (PluginConfig.HornInterruptsSiren && isHornActive);

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

        private static bool IsEmergencyModelName(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return false;
            string lower = modelName.ToLower();
            return lower.Contains("police") ||
                   lower.Contains("sheriff") ||
                   lower.Contains("fbi") ||
                   lower.Contains("fhp") ||
                   lower.Contains("dsp") ||
                   lower.Contains("lspd") ||
                   lower.Contains("bcso") ||
                   lower.Contains("sast") ||
                   lower.Contains("sapr") ||
                   lower.Contains("swat") ||
                   lower.Contains("unmarked") ||
                   lower.Contains("fire") ||
                   lower.Contains("amb") ||
                   lower.Contains("ems") ||
                   lower.Contains("ranger") ||
                   lower.Contains("medic") ||
                   lower.Contains("rescue");
        }

        private static void ResetInputs()
        {
            was1 = was2 = was3 = was4 = wasHorn = wasManul = wasScan = wasTonX = wasPnic = false;
            _wasSirenInterrupted = false;
        }

        public static void DropVolumes()
        {
            activeSiren.SetVolume(0f);
            activeHorn.SetVolume(0f);
            activeManual.SetVolume(0f);

            foreach (var ai in activeAiSirens.Values)
            {
                ai.Player.SetVolume(0f);
            }
        }

        public static void Shutdown()
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
            ResetInputs();
        }

        private static void StopAllLocalSounds(bool dropInstantly = false)
        {
            if (activeSiren.IsPlaying) activeSiren.Stop(dropInstantly);
            if (activeHorn.IsPlaying) activeHorn.Stop(dropInstantly);
            if (activeManual.IsPlaying) activeManual.Stop(dropInstantly);
            activeToneIndex = 0;
            isAutoScanActive = false;
            ResetInputs();
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
            if (veh == null || !veh.IsValid()) return "UNKNOWN";

            string name = veh.Model.Name;

            if (string.IsNullOrEmpty(name) || name.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                name = $"0x{veh.Model.Hash:X8}";
            }

            return name.ToUpper();
        }

        private static void CacheElsModels()
        {
            if (_elsModelsCached) return;
            _elsModelsCache.Clear();

            if (Directory.Exists("ELS"))
            {
                string[] files = Directory.GetFiles("ELS", "*.xml", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    if (file.IndexOf("Original VCF Backups", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    _elsModelsCache.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            _elsModelsCached = true;
        }

        private static bool IsElsVehicle(string modelName)
        {
            CacheElsModels();
            return _elsModelsCache.Contains(modelName);
        }

        private static bool GetVehicleLightRestriction(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return PluginConfig.SirenLightRestriction;

            string vehIni = $@"{PluginConfig.ProfilesFolder}{modelName}.ini";
            if (File.Exists(vehIni))
            {
                InitializationFile vIni = new InitializationFile(vehIni);
                return vIni.ReadBoolean("Settings", "SirenLightRestriction", PluginConfig.SirenLightRestriction);
            }

            string globalIniPath = $@"{PluginConfig.ProfilesFolder}Global.ini";
            if (File.Exists(globalIniPath))
            {
                InitializationFile gIni = new InitializationFile(globalIniPath);
                return gIni.ReadBoolean("Settings", "SirenLightRestriction", PluginConfig.SirenLightRestriction);
            }

            return PluginConfig.SirenLightRestriction;
        }

        private static void HandleInputs(Vehicle veh, bool isLightsOn)
        {
            CheckToneKey(PluginConfig.Snd_SrnTon1, ref was1, 1, isLightsOn);
            CheckToneKey(PluginConfig.Snd_SrnTon2, ref was2, 2, isLightsOn);
            CheckToneKey(PluginConfig.Snd_SrnTon3, ref was3, 3, isLightsOn);
            CheckToneKey(PluginConfig.Snd_SrnTon4, ref was4, 4, isLightsOn);

            bool isManulPressed = Game.IsKeyDownRightNow(PluginConfig.Sound_Manul) ||
                                  (PluginConfig.EnableControllerSupport && Game.IsControllerButtonDownRightNow(PluginConfig.Controller_Manul));

            if (isManulPressed && !wasManul)
            {
                string manualPath = GetProfileSiren(CurrentVehicleModel, "Manual");
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
                        string p = GetProfileSiren(CurrentVehicleModel, $"Tone{toneToPlay}");
                        if (!string.IsNullOrEmpty(p) && p != "None" && File.Exists(p))
                        {
                            found = true;
                            break;
                        }
                        toneToPlay++;
                        if (toneToPlay > 4) toneToPlay = 1;
                    }

                    if (found) manualPath = GetProfileSiren(CurrentVehicleModel, $"Tone{toneToPlay}");
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
                activeManual.Stop(false);
            }
            wasManul = isManulPressed;

            bool isScanPressed = Game.IsKeyDownRightNow(PluginConfig.Snd_SrnScan) ||
                                 (PluginConfig.EnableControllerSupport && Game.IsControllerButtonDownRightNow(PluginConfig.Controller_SrnToggle));

            if (isScanPressed && !wasScan)
            {
                if (isLightsOn)
                {
                    if (activeToneIndex != 0)
                    {
                        isAutoScanActive = false;
                        activeToneIndex = 0;
                        activeSiren.Stop(false);
                    }
                    else
                    {
                        isAutoScanActive = true;
                        activeToneIndex = 1;

                        string path = GetProfileSiren(CurrentVehicleModel, "Tone1");
                        if (string.IsNullOrEmpty(path) || path == "None" || !File.Exists(path))
                            AdvanceToNextValidTone(ref activeToneIndex);

                        PlayCurrentTone();
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

            bool isTonX = Game.IsKeyDownRightNow(PluginConfig.Snd_SrnTonX) ||
                          (PluginConfig.EnableControllerSupport && Game.IsControllerButtonDownRightNow(PluginConfig.Controller_SrnTonX));

            if (isTonX && !wasTonX)
            {
                if (isLightsOn && activeToneIndex != 0)
                {
                    isAutoScanActive = false;
                    AdvanceToNextValidTone(ref activeToneIndex);
                    PlayCurrentTone();
                }
            }
            wasTonX = isTonX;

            bool isPnic = Game.IsKeyDownRightNow(PluginConfig.Snd_SrnPnic);
            wasPnic = isPnic;

            bool isHornPressed = NativeFunction.Natives.IS_CONTROL_PRESSED<bool>(0, (int)GameControl.VehicleHorn) ||
                                 NativeFunction.Natives.IS_DISABLED_CONTROL_PRESSED<bool>(0, (int)GameControl.VehicleHorn);

            if (isHornPressed && !wasHorn)
            {
                string path = GetProfileSiren(CurrentVehicleModel, "Horn");
                if (!string.IsNullOrEmpty(path) && path != "None" && File.Exists(path))
                {
                    CachedSound sound = GetCachedSound(path);
                    if (sound != null)
                        activeHorn.Play(sound, true, GetPerSirenVolume("HornVol"));
                }
            }
            else if (!isHornPressed && wasHorn)
            {
                activeHorn.Stop(false);
            }
            wasHorn = isHornPressed;

            bool isHornActive = wasHorn || (activeHorn.IsPlaying && !activeHorn.IsFadingOut);
            bool isManualActive = wasManul || (activeManual.IsPlaying && !activeManual.IsFadingOut);
            bool isInterruptedNow = isManualActive || (PluginConfig.HornInterruptsSiren && isHornActive);

            if (!isInterruptedNow && _wasSirenInterrupted && activeToneIndex != 0)
            {
                PlayCurrentTone();
            }

            _wasSirenInterrupted = isInterruptedNow;
        }

        private static void AdvanceToNextValidTone(ref int toneIndex)
        {
            int startIdx = toneIndex == 0 ? 1 : toneIndex;
            int nextIdx = startIdx;
            for (int i = 0; i < 4; i++)
            {
                nextIdx++;
                if (nextIdx > 4) nextIdx = 1;
                string path = GetProfileSiren(CurrentVehicleModel, $"Tone{nextIdx}");
                if (!string.IsNullOrEmpty(path) && path != "None" && File.Exists(path))
                {
                    toneIndex = nextIdx;
                    return;
                }
            }
        }

        private static void PlayCurrentTone()
        {
            string path = GetProfileSiren(CurrentVehicleModel, $"Tone{activeToneIndex}");
            CachedSound sound = GetCachedSound(path);
            if (sound != null)
                activeSiren.Play(sound, true, GetPerSirenVolume($"Tone{activeToneIndex}Vol"));
        }

        private static void CheckToneKey(System.Windows.Forms.Keys key, ref bool wasKey, int toneIndex, bool isLightsOn)
        {
            bool isKey = Game.IsKeyDownRightNow(key);
            if (isKey && !wasKey)
            {
                if (!isLightsOn)
                {
                    Game.Console.Print("[CustomSirens] Vehicle emergency lights must be active to trigger custom sirens.");
                }
                else
                {
                    string path = GetProfileSiren(CurrentVehicleModel, $"Tone{toneIndex}");

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
                            activeSiren.Stop(false);
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
            string vehIni = $@"{PluginConfig.ProfilesFolder}{modelName}.ini";

            if (File.Exists(vehIni))
            {
                InitializationFile vIni = new InitializationFile(vehIni);
                string file = vIni.ReadString("Sirens", key, "None");
                result = (file != "None" && File.Exists(PluginConfig.WavFolder + file)) ? PluginConfig.WavFolder + file : "None";
            }
            else
            {
                InitializationFile gIni = new InitializationFile($@"{PluginConfig.ProfilesFolder}Global.ini");
                string gFile = gIni.ReadString("Sirens", key, "None");
                result = (gFile != "None" && File.Exists(PluginConfig.WavFolder + gFile)) ? PluginConfig.WavFolder + gFile : "None";
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

        public static void ClearProfileCache() => _profileCache.Clear();

        public static void CacheVehicleSirens()
        {
            vehicleProfileExists = File.Exists($@"{PluginConfig.ProfilesFolder}{CurrentVehicleModel}.ini");

            foreach (var key in new[] { "Tone1", "Tone2", "Tone3", "Tone4", "Horn", "Manual" })
                GetCachedSound(GetProfileSiren(CurrentVehicleModel, key));
        }

        private static bool IsVehicleLightsOn(Vehicle veh)
        {
            if (veh == null || !veh.IsValid()) return false;

            string modelName = GetVehicleModelName(veh);
            bool restrictionActive = GetVehicleLightRestriction(modelName);

            if (!restrictionActive && veh == currentVehicle) return true;

            if (veh.IsSirenOn) return true;

            if (NativeFunction.Natives.DECOR_EXIST_ON<bool>(veh, "ELS_lightstage"))
            {
                int stage = NativeFunction.Natives.DECOR_GET_INT<int>(veh, "ELS_lightstage");
                if (stage >= 2) return true;
            }

            return false;
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
                if (activeAiSirens.TryGetValue(v, out var p)) p.Player.Stop(true);
                activeAiSirens.Remove(v);
            }

            Vehicle[] allVehicles = World.GetAllVehicles();
            foreach (Vehicle v in allVehicles)
            {
                if (activeAiSirens.Count >= PluginConfig.MaxAiUnits) break;
                if (!v.IsValid() || !v.IsAlive || v == currentVehicle) continue;
                if (activeAiSirens.ContainsKey(v)) continue;

                string modelName = GetVehicleModelName(v);

                bool isEmergency = v.HasSiren || v.Class == VehicleClass.Emergency || IsElsVehicle(modelName);
                if (!isEmergency) continue;

                if (Vector3.Distance(playerPos, v.Position) > PluginConfig.MaxDistance) continue;

                bool hasNoDriver = v.Driver == null || !v.Driver.IsValid();
                bool isDriverDead = !hasNoDriver && !v.Driver.IsAlive;

                if (isDriverDead || (hasNoDriver && PluginConfig.AutomaticAiSirenCutoff))
                {
                    continue;
                }

                bool isCode3 = v.IsSirenOn || IsVehicleLightsOn(v);

                if (isCode3)
                {
                    string path = GetProfileSiren(modelName, "Tone1");
                    CachedSound sound = GetCachedSound(path);
                    if (sound != null)
                    {
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
                    aiPlayer.Stop(true);
                    _vehicleScratch.Add(aiVeh);
                    continue;
                }

                bool hasNoDriver = aiVeh.Driver == null || !aiVeh.Driver.IsValid();
                bool isDriverDead = !hasNoDriver && !aiVeh.Driver.IsAlive;
                bool shouldStop = isDriverDead || (hasNoDriver && PluginConfig.AutomaticAiSirenCutoff);

                if (shouldStop)
                {
                    aiPlayer.Stop();
                }
                else if (!aiPlayer.IsFadingOut)
                {
                    bool isCode3 = aiVeh.IsSirenOn || IsVehicleLightsOn(aiVeh);

                    if (!isCode3)
                    {
                        aiPlayer.Stop(false);
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
                    }
                }

                aiPlayer.Update3D(aiVeh, camPos, camRot, forceMuteAll);

                if (!aiPlayer.IsPlaying)
                {
                    _vehicleScratch.Add(aiVeh);
                }
            }

            foreach (var v in _vehicleScratch) activeAiSirens.Remove(v);
        }
    }
}