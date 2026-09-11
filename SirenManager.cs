using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Windows.Forms;

namespace CustomELSSirens
{
    public static class SirenManager
    {
        public static Dictionary<string, CachedSound> cachedSirens = new Dictionary<string, CachedSound>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Vehicle> _vehicleScratch = new List<Vehicle>();

        private static readonly SirenPlayer activeSiren = new SirenPlayer();
        private static readonly SirenPlayer activeHorn = new SirenPlayer();
        private static readonly SirenPlayer activeManual = new SirenPlayer();
        private static readonly Dictionary<Vehicle, AiSirenState> activeAiSirens = new Dictionary<Vehicle, AiSirenState>();

        private static Vehicle currentVehicle = null;
        public static string CurrentVehicleModel = string.Empty;

        private static int requiredSirenStage = 2;
        private static bool vehicleProfileExists = false;

        private static int activeToneIndex = 0;
        private static bool isAutoScanActive = false;
        private static uint nextScanChangeTime = 0;

        private static readonly bool[] wasTone = new bool[ToneSlots.Count];
        private static bool wasHorn, wasManul, wasScan, wasTonX;
        private static readonly HornInterruption hornInterruption = new HornInterruption();
        private static readonly Dictionary<Vehicle, bool> rumblerStates = new Dictionary<Vehicle, bool>();
        private static bool wasRumbler;
        private static readonly bool[] wasExtra = new bool[4];
        private static bool featureInputSuppressed = true;
        private static readonly Func<Keys, bool> readKey = Game.IsKeyDownRightNow;
        private static uint nextAiScanTime = 0;
        private static readonly Random rnd = new Random();

        private static int currentTrackedStage = 0;
        private static bool wasLstKey = false;

        private static string manualVolumeKey = "ManualVol";

        private static readonly HashSet<string> _elsModelsCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _elsModelsCached = false;
        private static readonly Dictionary<string, string> _elsFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void ProcessLoop()
        {
            bool isPaused = Game.IsPaused || Game.IsLoading || NativeFunction.Natives.IS_PAUSE_MENU_ACTIVE<bool>() || Game.TimeScale == 0f;
            bool isMenuOpen = MenuManager.IsAnyMenuOpen;
            AudioEngine.SetMuted(isPaused || isMenuOpen);
            if (isPaused) { featureInputSuppressed = true; return; }

            Ped player = Game.LocalPlayer.Character;
            if (player == null || !player.IsValid() || !player.IsAlive)
            {
                KillAllSounds();
                KillAllAiSounds();
                return;
            }

            bool inVehicle = player.IsInAnyVehicle(false);
            Vehicle veh = inVehicle ? player.CurrentVehicle : currentVehicle;
            if (inVehicle && (veh == null || !veh.IsValid()))
            {
                KillAllSounds();
                return;
            }

            if (inVehicle && currentVehicle != veh)
            {
                if (activeAiSirens.TryGetValue(veh, out var previousAi))
                {
                    previousAi.Player.Stop(true);
                    activeAiSirens.Remove(veh);
                }
                currentVehicle = veh;
                CurrentVehicleModel = GetVehicleModelName(veh);
                activeToneIndex = 0;
                isAutoScanActive = false;

                activeSiren.Stop(true);
                activeHorn.Stop(true);
                activeManual.Stop(true);

                ResetInputs();
                ParseVCF(CurrentVehicleModel);
                CacheVehicleSirens();
            }

            if (activeToneIndex != 0 && !activeSiren.IsPlaying && !hornInterruption.IsActive)
            {
                activeToneIndex = 0;
                isAutoScanActive = false;
            }

            if (currentVehicle != null && currentVehicle.IsValid() && currentVehicle.IsAlive)
            {
                if (inVehicle && !isMenuOpen) HandleVehicleFeatures();
                else featureInputSuppressed = true;

                string hornProfile = GetLocalSiren("Horn");
                bool hasCustomHorn = IsSoundAvailable(hornProfile);

                bool isEmergency = currentVehicle.HasSiren ||
                                   currentVehicle.Class == VehicleClass.Emergency ||
                                   IsElsVehicle(CurrentVehicleModel) ||
                                   vehicleProfileExists ||
                                   IsEmergencyModelName(CurrentVehicleModel);

                if (isEmergency)
                {
                    if (inVehicle && currentVehicle.HasSiren && hasCustomHorn)
                    {
                        Game.DisableControlAction(0, GameControl.VehicleHorn, true);
                    }

                    bool isLightsOn = IsVehicleLightsOn(currentVehicle);

                    if (inVehicle && !isMenuOpen)
                    {
                        HandleInputs(currentVehicle, isLightsOn);
                        isLightsOn = IsVehicleLightsOn(currentVehicle);
                    }
                    else
                    {
                        if (activeHorn.IsPlaying) activeHorn.Stop(true);
                        if (activeManual.IsPlaying) activeManual.Stop(false);

                        wasHorn = false;
                        wasManul = false;

                        Ped driver = currentVehicle.Driver;
                        bool hasNoDriver = driver == null || !driver.IsValid();
                        bool isDriverDead = !hasNoDriver && !driver.IsAlive;

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

                    if (!inVehicle || isMenuOpen)
                        hornInterruption.Update(false, PluginConfig.HornInterruptsSiren, () => activeSiren.Stop(true), RestartAfterHorn);

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

            {
                bool shouldMuteAll = isMenuOpen;

                Vector3 camPos = NativeFunction.Natives.GET_GAMEPLAY_CAM_COORD<Vector3>();
                Vector3 camRot = NativeFunction.Natives.GET_GAMEPLAY_CAM_ROT<Vector3>(2);

                if (currentVehicle != null && currentVehicle.IsValid() && currentVehicle.IsAlive)
                {
                    bool isManualActive = activeManual.IsPlaying && !activeManual.IsFadingOut;

                    bool sirenForceMute = shouldMuteAll || isManualActive;

                    if (activeToneIndex != 0) activeSiren.SetSirenVolume(GetLocalVolume($"Tone{activeToneIndex}Vol"));
                    activeHorn.SetSirenVolume(GetLocalVolume("HornVol"));
                    activeManual.SetSirenVolume(GetLocalVolume(manualVolumeKey));
                    activeSiren.Update3D(currentVehicle, camPos, camRot, sirenForceMute);
                    activeHorn.Update3D(currentVehicle, camPos, camRot, shouldMuteAll);
                    activeManual.Update3D(currentVehicle, camPos, camRot, shouldMuteAll);
                }

                UpdateActiveAiSirens(camPos, camRot, shouldMuteAll);
            }

            if (TimeReached(Game.GameTime, nextAiScanTime))
            {
                nextAiScanTime = Game.GameTime + (uint)PluginConfig.AiScanInterval;
                ScanForAiVehicles();
            }
        }

        private static bool IsEmergencyModelName(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return false;
            string lower = modelName.ToLowerInvariant();
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
            Array.Clear(wasTone, 0, wasTone.Length);
            wasHorn = wasManul = wasScan = wasTonX = false;
            hornInterruption.Reset();
            featureInputSuppressed = true;
            wasRumbler = false;
            Array.Clear(wasExtra, 0, wasExtra.Length);

            currentTrackedStage = 0;
            wasLstKey = false;
        }

        internal static bool TimeReached(uint now, uint deadline) => unchecked((int)(now - deadline)) >= 0;

        public static void DropVolumes() => AudioEngine.SetMuted(true);

        public static void Shutdown()
        {
            activeSiren.Stop(true);
            activeHorn.Stop(true);
            activeManual.Stop(true);

            foreach (var p in activeAiSirens.Values)
                p.Player.Stop(true);
            activeAiSirens.Clear();

            cachedSirens.Clear();
            rumblerStates.Clear();
            ProfileStore.Clear();
            _vehicleScratch.Clear();

            AudioEngine.Shutdown();
            Game.Console.Print("[CustomSirens] Unloaded.");
        }

        private static void KillAllSounds()
        {
            if (activeSiren.IsPlaying) activeSiren.Stop(true);
            if (activeHorn.IsPlaying) activeHorn.Stop(true);
            if (activeManual.IsPlaying) activeManual.Stop(true);
            activeToneIndex = 0;
            isAutoScanActive = false;
            currentVehicle = null;
            CurrentVehicleModel = string.Empty;
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

        private static void KillAllAiSounds()
        {
            foreach (var p in activeAiSirens.Values) p.Player.Stop(true);
            activeAiSirens.Clear();
        }

        private static void ParseVCF(string modelName)
        {
            requiredSirenStage = 2;
            CacheElsModels();
            if (!_elsFiles.TryGetValue(modelName, out var path)) return;
            try
            {
                var doc = new XmlDocument { XmlResolver = null };
                doc.Load(path);
                var node = doc.SelectSingleNode("//MISC/DfltSirenLtsActivateAtLstg");
                if (node != null && int.TryParse(node.InnerText, out int stage) && stage >= 1 && stage <= 3)
                    requiredSirenStage = stage;
            }
            catch (Exception ex) { Game.Console.Print("[CustomSirens] Cannot read VCF: " + ex.Message); }
        }

        private static string GetVehicleModelName(Vehicle veh)
        {
            if (veh == null || !veh.IsValid()) return "UNKNOWN";

            string name = veh.Model.Name;

            if (string.IsNullOrEmpty(name) || name.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                name = $"0x{veh.Model.Hash:X8}";
            }

            return name.ToUpperInvariant();
        }

        private static void CacheElsModels()
        {
            if (_elsModelsCached) return;
            _elsModelsCache.Clear();
            _elsFiles.Clear();

            if (Directory.Exists("ELS"))
            {
                string[] files = Directory.GetFiles("ELS", "*.xml", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    if (file.IndexOf("Original VCF Backups", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    string model = Path.GetFileNameWithoutExtension(file);
                    _elsModelsCache.Add(model);
                    if (!_elsFiles.ContainsKey(model)) _elsFiles[model] = file;
                }
            }
            _elsModelsCached = true;
        }

        private static bool IsElsVehicle(string modelName)
        {
            CacheElsModels();
            return _elsModelsCache.Contains(modelName);
        }

        private static bool GetVehicleLightRestriction(string modelName) => ProfileStore.Get(modelName).LightRestriction;
        private static bool GetVehicleLightStageTracking(string modelName) => ProfileStore.Get(modelName).StageTracking;
        private static int GetVehicleCustomStageAmount(string modelName) => ProfileStore.Get(modelName).StageCount;

        public static float GetVehicleVolume(string modelName, string key, float defaultVal)
        {
            return ProfileStore.Get(modelName).Volumes.TryGetValue(key, out float volume) ? volume : defaultVal;
        }

        public static void UpdateCachedVolume(string modelName, string key, float value, bool rumbler = false) => ProfileStore.SetVolume(modelName, key, value, rumbler);

        private static void HandleInputs(Vehicle veh, bool isLightsOn)
        {
            bool isHornPressed = NativeFunction.Natives.IS_CONTROL_PRESSED<bool>(0, (int)GameControl.VehicleHorn) ||
                                 NativeFunction.Natives.IS_DISABLED_CONTROL_PRESSED<bool>(0, (int)GameControl.VehicleHorn);
            if (isHornPressed && !wasHorn) PlayHorn();
            else if (!isHornPressed && wasHorn) activeHorn.Stop(true);
            wasHorn = isHornPressed;
            hornInterruption.Update(isHornPressed, PluginConfig.HornInterruptsSiren, () => activeSiren.Stop(true), RestartAfterHorn);


            bool enableTracking = GetVehicleLightStageTracking(CurrentVehicleModel);
            if (enableTracking)
            {
                int maxStages = GetVehicleCustomStageAmount(CurrentVehicleModel);

                bool isLstKey = IsKeyDown(PluginConfig.Toggle_Lsts) ||
                                 (PluginConfig.EnableControllerSupport && Game.IsControllerButtonDownRightNow(ControllerButtons.DPadLeft));

                if (isLstKey && !wasLstKey)
                {
                    currentTrackedStage++;
                    if (currentTrackedStage > maxStages)
                    {
                        currentTrackedStage = 0;
                    }
                    Game.Console.Print($"[CustomSirens] Local Light Stage changed to: {currentTrackedStage} / {maxStages}");
                }
                wasLstKey = isLstKey;
                isLightsOn = IsVehicleLightsOn(veh);
            }

            for (int i = 0; i < ToneSlots.Count; i++) CheckToneKey(PluginConfig.GetToneKey(i + 1), ref wasTone[i], i + 1, isLightsOn);

            bool isManulPressed = IsKeyDown(PluginConfig.Sound_Manul) ||
                                  (PluginConfig.EnableControllerSupport && Game.IsControllerButtonDownRightNow(PluginConfig.Controller_Manul));

            if (isManulPressed && !wasManul)
            {
                PlayManual();
            }
            else if (!isManulPressed && wasManul)
            {
                activeManual.Stop(false);
            }
            wasManul = isManulPressed;

            bool isScanPressed = IsKeyDown(PluginConfig.Snd_SrnScan) ||
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

                        string path = GetLocalSiren("Tone1");
                        if (!IsSoundAvailable(path))
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

            if (isAutoScanActive && activeToneIndex != 0 && !hornInterruption.IsActive)
            {
                if (TimeReached(Game.GameTime, nextScanChangeTime))
                {
                    int previousTone = activeToneIndex;
                    AdvanceToNextValidTone(ref activeToneIndex);
                    if (activeToneIndex != previousTone) PlayCurrentTone();
                    nextScanChangeTime = Game.GameTime + 6000;
                }
            }

            bool isTonX = IsKeyDown(PluginConfig.Snd_SrnTonX) ||
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

        }

        private static void AdvanceToNextValidTone(ref int toneIndex)
        {
            toneIndex = ToneSlots.Next(toneIndex, index => IsSoundAvailable(GetLocalSiren(ToneSlots.Keys[index - 1])));
        }

        private static void RestartAfterHorn()
        {
            PlayCurrentTone();
            if (isAutoScanActive) nextScanChangeTime = Game.GameTime + 6000;
        }

        private static void PlayCurrentTone()
        {
            if (activeToneIndex == 0)
            {
                isAutoScanActive = false;
                activeSiren.Stop(true);
                return;
            }
            if (hornInterruption.IsActive) { activeSiren.Stop(true); return; }
            var sound = GetCachedSound(GetLocalSiren(ToneSlots.Keys[activeToneIndex - 1]));
            if (sound != null) activeSiren.Play(sound, true, GetLocalVolume($"Tone{activeToneIndex}Vol"));
            else
            {
                activeSiren.Stop(true);
                activeToneIndex = 0;
                isAutoScanActive = false;
            }
        }

        private static void PlayHorn()
        {
            activeHorn.Play(GetCachedSound(GetLocalSiren("Horn")), true, GetLocalVolume("HornVol"));
        }

        private static void PlayManual()
        {
            string path = GetLocalSiren("Manual");
            manualVolumeKey = "ManualVol";
            if (!IsSoundAvailable(path))
            {
                int next = ToneSlots.Next(activeToneIndex, index => IsSoundAvailable(GetLocalSiren(ToneSlots.Keys[index - 1])));
                if (next == 0) { activeManual.Stop(true); return; }
                path = GetLocalSiren(ToneSlots.Keys[next - 1]);
                manualVolumeKey = $"Tone{next}Vol";
            }
            activeManual.Play(GetCachedSound(path), true, GetLocalVolume(manualVolumeKey));
        }

        private static void CheckToneKey(System.Windows.Forms.Keys key, ref bool wasKey, int toneIndex, bool isLightsOn)
        {
            bool isKey = IsKeyDown(key);
            if (isKey && !wasKey)
            {
                if (!isLightsOn)
                {
                    Game.Console.Print("[CustomSirens] Vehicle emergency lights must be active to trigger custom sirens.");
                }
                else
                {
                    string path = GetLocalSiren($"Tone{toneIndex}");

                    if (!IsSoundAvailable(path))
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
                        PlayCurrentTone();
                    }
                }
            }
            wasKey = isKey;
        }

        private static bool IsKeyDown(Keys key) => KeyBindings.IsDown(key, readKey);

        private static float GetPerSirenVolume(string modelName, string key, bool rumbler = false)
            => ProfileStore.Get(modelName).GetVolume(key, rumbler);
        private static float GetLocalVolume(string key)
            => GetPerSirenVolume(CurrentVehicleModel, key, CurrentRumblerActive);
        private static string GetProfileSiren(string modelName, string key, bool rumbler = false)
            => string.IsNullOrEmpty(modelName) ? "None" : ProfileStore.Get(modelName).GetSound(key, rumbler);
        private static string GetLocalSiren(string key)
            => GetProfileSiren(CurrentVehicleModel, key, CurrentRumblerActive);

        internal static bool CanControlCurrentVehicle
        {
            get
            {
                Ped player = Game.LocalPlayer.Character;
                return player != null && player.IsValid() && player.IsAlive && currentVehicle != null && currentVehicle.IsValid() && currentVehicle.IsAlive &&
                    player.IsInAnyVehicle(false) && player.CurrentVehicle == currentVehicle;
            }
        }
        internal static bool CurrentRumblerActive => GetRumblerState(currentVehicle, CurrentVehicleModel);
        internal static bool CurrentRumblerAvailable => CanControlCurrentVehicle && ProfileStore.Get(CurrentVehicleModel).RumblerEnabled;
        private static bool GetRumblerState(Vehicle vehicle, string model)
            => vehicle != null && !string.IsNullOrEmpty(model) && ProfileStore.Get(model).RumblerEnabled &&
                rumblerStates.TryGetValue(vehicle, out bool enabled) && enabled;

        internal static bool SetCurrentRumbler(bool enabled)
        {
            if (!CanControlCurrentVehicle || (enabled && !CurrentRumblerAvailable)) return false;
            if (enabled == CurrentRumblerActive) return true;
            rumblerStates[currentVehicle] = enabled;
            if (activeToneIndex != 0) PlayCurrentTone();
            if (activeHorn.IsPlaying && !activeHorn.IsFadingOut) PlayHorn();
            if (activeManual.IsPlaying && !activeManual.IsFadingOut) PlayManual();
            Game.DisplayNotification(enabled ? "~b~Rumbler ON" : "~w~Rumbler OFF");
            return true;
        }

        private static void HandleVehicleFeatures()
        {
            var profile = ProfileStore.Get(CurrentVehicleModel);
            bool rumblerDown = IsKeyDown(profile.RumblerKey);
            if (!featureInputSuppressed && rumblerDown && !wasRumbler && profile.RumblerEnabled)
                SetCurrentRumbler(!CurrentRumblerActive);
            wasRumbler = rumblerDown;
            // Resolve all edges before acting: a duplicate key has one stable
            // action instead of repeatedly switching between text modes.
            int requestedExtra = -1;
            for (int i = 0; i < wasExtra.Length; i++)
            {
                bool down = IsKeyDown(profile.ExtraKeys[i]);
                if (!featureInputSuppressed && down && !wasExtra[i] && profile.Extras[i] >= 0 && requestedExtra < 0)
                    requestedExtra = i;
                wasExtra[i] = down;
            }
            featureInputSuppressed = false;
            if (requestedExtra >= 0)
            {
                if (!ExtraControls.Toggle(new NativeVehicleExtras(currentVehicle), profile.Extras, requestedExtra))
                    Game.DisplayNotification("~y~" + ExtraControls.Labels[requestedExtra] + ": the configured extra does not exist on this vehicle.");
            }
        }

        private static bool IsSoundAvailable(string path)
        {
            return !string.IsNullOrEmpty(path) && !path.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                (!cachedSirens.TryGetValue(path, out var sound) || !sound.Failed);
        }

        public static CachedSound GetCachedSound(string path)
        {
            if (!IsSoundAvailable(path)) return null;
            if (!cachedSirens.TryGetValue(path, out var sound))
            {
                sound = new CachedSound(path);
                cachedSirens[path] = sound;
            }
            return sound;
        }

        public static void ClearProfileCache()
        {
            ProfileStore.Clear();
            if (!string.IsNullOrEmpty(CurrentVehicleModel))
                vehicleProfileExists = ProfileStore.Get(CurrentVehicleModel).HasOwnProfile;
        }

        public static void ReloadAudio()
        {
            StopAllLocalSounds(true);
            KillAllAiSounds();
            cachedSirens.Clear();
            AudioEngine.ClearCache();
            ClearProfileCache();
            CacheVehicleSirens();
            nextAiScanTime = Game.GameTime;
        }

        public static void ReloadProfiles()
        {
            // Apply changed tone selections without leaving old WAVs playing.
            StopAllLocalSounds(true);
            KillAllAiSounds();
            ClearProfileCache();
            CacheVehicleSirens();
            _elsModelsCached = false;
            if (!string.IsNullOrEmpty(CurrentVehicleModel)) ParseVCF(CurrentVehicleModel);
            nextAiScanTime = Game.GameTime;
        }

        public static void CacheVehicleSirens()
        {
            if (string.IsNullOrEmpty(CurrentVehicleModel)) return;
            var profile = ProfileStore.Get(CurrentVehicleModel);
            vehicleProfileExists = profile.HasOwnProfile;
            if (!profile.RumblerEnabled && currentVehicle != null) rumblerStates.Remove(currentVehicle);
            foreach (string key in ProfileStore.SoundKeys)
            {
                AudioEngine.Preload(GetCachedSound(GetProfileSiren(CurrentVehicleModel, key, false)));
                if (profile.RumblerEnabled)
                    AudioEngine.Preload(GetCachedSound(GetProfileSiren(CurrentVehicleModel, key, true)));
            }
        }

        private static bool IsVehicleLightsOn(Vehicle veh)
        {
            if (veh == null || !veh.IsValid()) return false;

            string modelName = GetVehicleModelName(veh);

            if (veh == currentVehicle)
            {
                if (!GetVehicleLightRestriction(modelName)) return true;
                bool enableTracking = GetVehicleLightStageTracking(modelName);
                if (enableTracking)
                {
                    int maxStages = GetVehicleCustomStageAmount(modelName);
                    return currentTrackedStage >= maxStages;
                }
            }

            bool restrictionActive = GetVehicleLightRestriction(modelName);
            if (!restrictionActive && veh == currentVehicle) return true;

            if (veh.IsSirenOn) return true;

            if (NativeFunction.Natives.DECOR_EXIST_ON<bool>(veh, "ELS_lightstage"))
            {
                int stage = NativeFunction.Natives.DECOR_GET_INT<int>(veh, "ELS_lightstage");
                if (stage >= (veh == currentVehicle ? requiredSirenStage : 2)) return true;
            }

            return false;
        }

        private static void ScanForAiVehicles()
        {
            if (Game.LocalPlayer.Character == null) return;
            Vector3 playerPos = Game.LocalPlayer.Character.Position;
            _vehicleScratch.Clear();
            foreach (var pair in rumblerStates)
                if (!pair.Key.IsValid() || !pair.Key.IsAlive) _vehicleScratch.Add(pair.Key);
            foreach (Vehicle vehicle in _vehicleScratch) rumblerStates.Remove(vehicle);

            _vehicleScratch.Clear();
            foreach (var kvp in activeAiSirens)
            {
                if (!kvp.Key.IsValid() || !kvp.Key.IsAlive || kvp.Key == currentVehicle ||
                    Vector3.Distance(playerPos, kvp.Key.Position) > PluginConfig.MaxDistance ||
                    activeAiSirens.Count - _vehicleScratch.Count > PluginConfig.MaxAiUnits)
                    _vehicleScratch.Add(kvp.Key);
            }
            foreach (var v in _vehicleScratch)
            {
                if (activeAiSirens.TryGetValue(v, out var p)) p.Player.Stop(true);
                activeAiSirens.Remove(v);
            }

            if (activeAiSirens.Count >= PluginConfig.MaxAiUnits) return;
            Vehicle[] allVehicles = World.GetAllVehicles();
            foreach (Vehicle v in allVehicles)
            {
                if (activeAiSirens.Count >= PluginConfig.MaxAiUnits) break;
                if (v == null || !v.IsValid() || !v.IsAlive || v == currentVehicle) continue;
                if (activeAiSirens.ContainsKey(v)) continue;

                if (Vector3.Distance(playerPos, v.Position) > PluginConfig.MaxDistance) continue;

                string modelName = GetVehicleModelName(v);

                bool isEmergency = v.HasSiren || v.Class == VehicleClass.Emergency || IsElsVehicle(modelName);
                if (!isEmergency) continue;

                Ped driver = v.Driver;
                bool hasNoDriver = driver == null || !driver.IsValid();
                bool isDriverDead = !hasNoDriver && !driver.IsAlive;

                if (isDriverDead || (hasNoDriver && PluginConfig.AutomaticAiSirenCutoff))
                {
                    continue;
                }

                bool isCode3 = v.IsSirenOn || IsVehicleLightsOn(v);

                if (isCode3)
                {
                    bool rumbler = GetRumblerState(v, modelName);
                    int firstTone = ToneSlots.Next(0, tone => IsSoundAvailable(GetProfileSiren(modelName, ToneSlots.Keys[tone - 1], rumbler)));
                    if (firstTone == 0) continue;
                    string path = GetProfileSiren(modelName, $"Tone{firstTone}", rumbler);
                    CachedSound sound = GetCachedSound(path);
                    if (sound != null)
                    {
                        SirenPlayer aiPlayer = new SirenPlayer();
                        aiPlayer.Play(sound, true, GetPerSirenVolume(modelName, $"Tone{firstTone}Vol", rumbler));

                        AiSirenState state = new AiSirenState
                        {
                            Player = aiPlayer,
                            ModelName = modelName,
                            RumblerOn = rumbler,
                            CurrentToneIndex = firstTone,
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

                Ped driver = aiVeh.Driver;
                bool hasNoDriver = driver == null || !driver.IsValid();
                bool isDriverDead = !hasNoDriver && !driver.IsAlive;
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
                        string modelName = state.ModelName;
                        bool rumbler = GetRumblerState(aiVeh, modelName);
                        if (state.RumblerOn != rumbler) { aiPlayer.Stop(true); state.RumblerOn = rumbler; }

                        if (!aiPlayer.IsPlaying)
                        {
                            string path = GetProfileSiren(modelName, $"Tone{state.CurrentToneIndex}", rumbler);
                            if (!IsSoundAvailable(path))
                            {
                                state.CurrentToneIndex = ToneSlots.Next(0, tone => IsSoundAvailable(GetProfileSiren(modelName, ToneSlots.Keys[tone - 1], rumbler)));
                                path = state.CurrentToneIndex == 0 ? "None" : GetProfileSiren(modelName, ToneSlots.Keys[state.CurrentToneIndex - 1], rumbler);
                            }

                            CachedSound sound = GetCachedSound(path);
                            if (sound != null)
                            {
                                aiPlayer.Play(sound, true, GetPerSirenVolume(modelName, $"Tone{state.CurrentToneIndex}Vol", rumbler));
                                state.NextToneChangeTime = Game.GameTime + (uint)rnd.Next(4000, 8000);
                            }
                        }
                        else
                        {
                            if (TimeReached(Game.GameTime, state.NextToneChangeTime) && !forceMuteAll)
                            {
                                int nextTone = ToneSlots.Next(state.CurrentToneIndex, tone => IsSoundAvailable(GetProfileSiren(modelName, ToneSlots.Keys[tone - 1], rumbler)));
                                if (nextTone != 0 && nextTone != state.CurrentToneIndex)
                                {
                                    CachedSound sound = GetCachedSound(GetProfileSiren(modelName, ToneSlots.Keys[nextTone - 1], rumbler));
                                    if (sound != null)
                                    {
                                        aiPlayer.Play(sound, true, GetPerSirenVolume(modelName, $"Tone{nextTone}Vol", rumbler));
                                        state.CurrentToneIndex = nextTone;
                                    }
                                }
                                state.NextToneChangeTime = Game.GameTime + (uint)rnd.Next(4000, 8000);
                            }
                        }
                    }
                }

                aiPlayer.SetSirenVolume(GetPerSirenVolume(state.ModelName, $"Tone{state.CurrentToneIndex}Vol", state.RumblerOn));
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
