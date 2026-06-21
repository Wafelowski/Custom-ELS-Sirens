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
        private static uint nextAiScanTime = 0;
        private static readonly Random rnd = new Random();

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
                activeSiren.Stop();
                activeHorn.Stop();
                activeManual.Stop();
                ParseVCF(CurrentVehicleModel);
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
                bool isMenuOpen = (MenuManager.MainMenu != null && MenuManager.MainMenu.Visible) ||
                                  (MenuManager.SettingsMenu != null && MenuManager.SettingsMenu.Visible);
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

        public static void DropVolumes()
        {
            activeSiren.SetVolume(0f);
            activeHorn.SetVolume(0f);
            activeManual.SetVolume(0f);
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

        private static void HandleInputs(Vehicle veh, bool isLightsOn)
        {
            CheckToneKey(PluginConfig.Snd_SrnTon1, ref was1, 1, isLightsOn);
            CheckToneKey(PluginConfig.Snd_SrnTon2, ref was2, 2, isLightsOn);
            CheckToneKey(PluginConfig.Snd_SrnTon3, ref was3, 3, isLightsOn);
            CheckToneKey(PluginConfig.Snd_SrnTon4, ref was4, 4, isLightsOn);

            bool isManulPressed = Game.IsKeyDownRightNow(PluginConfig.Sound_Manul);
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

            bool isTonX = Game.IsKeyDownRightNow(PluginConfig.Snd_SrnTonX);
            wasTonX = isTonX;
            bool isPnic = Game.IsKeyDownRightNow(PluginConfig.Snd_SrnPnic);
            wasPnic = isPnic;

            bool isHornPressed = NativeFunction.Natives.IS_DISABLED_CONTROL_PRESSED<bool>(0, (int)GameControl.VehicleHorn);
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
                bool isDriverDead = !hasNoDriver && !aiVeh.Driver.IsAlive;

                if (isDriverDead || (hasNoDriver && PluginConfig.AutomaticAiSirenCutoff))
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
    }
}