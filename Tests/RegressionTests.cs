using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CustomELSSirens;
using NAudio.Wave;

internal static class RegressionTests
{
    private static int passed;
    private static string temporary;
    private static int Main()
    {
        temporary = Path.Combine(Path.GetTempPath(), "CustomSirensTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            Run("Empty loop returns without hanging", EmptyLoop);
            Run("Short loops honor offsets and fill every sample", ShortLoop);
            Run("One-shot completion and cached-array lifetime", OneShot);
            Run("Stereo crossfade uses identical channel weights", StereoCrossfade);
            Run("PCM/float WAVs normalize to 44.1 kHz mono", Formats);
            Run("Empty/corrupt WAVs fail cleanly", InvalidFiles);
            Run("Cancelled voices cannot emit audio", CancelledVoice);
            Run("Blocked device init does not block play/stop; output is reused", DeviceLifecycle);
            Run("Muted fades complete; invalid vehicles stop immediately", MutedFade);
            Run("Configuration validation, locale and MinDistance round-trip", Configuration);
            Run("Profiles cache reads, reload files, and update global volumes", Profiles);
            Run("All six slots participate in tone/manual/AI selection", SixTones);
            Run("Horn stops and restarts the latest selection", HornRestart);
            Run("Rumbler banks, fallback and volumes remain independent", RumblerProfiles);
            Run("Mapped extras toggle safely with exclusive matrix texts", ExtraMappings);
            Run("Unbound keys and modifier chords", ModifierKeys);
            Run("New keybinds and tone volumes survive reload", NewConfigRoundTrip);
            Run("Horn cycles once per press and restarts the next tone", HornCycling);
            Run("FIAMMS profile slots, rumbler fallback and key overrides", FiammsProfiles);
            Run("FIAMMS and main siren voices play and stop independently", IndependentLayers);
            Run("Pause freezes all mixer cursors and newly loaded voices", PausedMixer);
            Run("Four horn routes keep sound/cycling/interrupt behavior distinct", HornModeRouting);
            Run("Horn cycle modes are isolated to each vehicle profile", HornModeProfiles);
            Run("DEBUG discovers and polls extras within a fixed query budget", DebugExtrasDiscovery);
            Run("DEBUG formats current vehicle state and bounds long filenames", DebugSnapshotText);
            Run("DEBUG distinguishes pending, playing, fading and stopped voices", DebugPlaybackStatus);
            Console.WriteLine("PASS: " + passed + " regression checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        finally { AudioEngine.Shutdown(); Directory.Delete(temporary, true); }
    }
    private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static CachedSound Samples(float[] data, int channels = 1) => new CachedSound(data, WaveFormat.CreateIeeeFloatWaveFormat(CachedSound.SampleRate, channels));
    private static CachedSound Constant() => Samples(Enumerable.Repeat(0.25f, 4096).ToArray());
    private static void EmptyLoop()
    {
        var source = new CachedSampleProvider(Samples(new float[0]), true);
        var read = Task.Run(() => source.Read(new float[1024], 0, 1024));
        Check(read.Wait(1000), "Empty looping source hung.");
        Check(read.Result == 0, "Empty source should return EOF.");
    }
    private static void ShortLoop()
    {
        var source = new CachedSampleProvider(Samples(new[] { 0.1f, 0.2f, 0.3f }), true);
        var buffer = Enumerable.Repeat(-9f, 104).ToArray();
        Check(source.Read(buffer, 2, 100) == 100, "Loop did not fill buffer.");
        for (int i = 0; i < 100; i++) Check(Math.Abs(buffer[i + 2] - (i % 3 + 1) * 0.1f) < 0.00001f, "Short loop skipped/repeated incorrectly.");
        Check(buffer[0] == -9 && buffer[1] == -9 && buffer[102] == -9 && buffer[103] == -9, "Read wrote outside requested range.");
    }
    private static void OneShot()
    {
        var sound = Samples(new[] { 0.1f, 0.2f, 0.3f });
        var source = new CachedSampleProvider(sound, false);
        sound.ReleaseData();
        var buffer = new float[16];
        Check(source.Read(buffer, 2, 10) == 3 && buffer[4] == 0.3f, "Eviction invalidated an active voice.");
        Check(source.Read(buffer, 0, buffer.Length) == 0, "One-shot did not reach EOF.");
    }
    private static void StereoCrossfade()
    {
        var data = new float[6000];
        for (int i = 0; i < data.Length; i += 2) data[i] = data[i + 1] = (float)Math.Sin(i * 0.007);
        var source = new CachedSampleProvider(Samples(data, 2), true);
        var buffer = new float[15000];
        Check(source.Read(buffer, 0, buffer.Length) == buffer.Length, "Crossfade failed to loop.");
        for (int i = 0; i < buffer.Length; i += 2) Check(buffer[i] == buffer[i + 1], "Crossfade changed the stereo balance.");
    }
    private static string WriteWav(string name, int rate, int channels, int bits, bool floating, int frames)
    {
        string path = Path.Combine(temporary, name);
        int bytes = frames * channels * bits / 8;
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)(floating ? 3 : 1)); writer.Write((short)channels);
            writer.Write(rate); writer.Write(rate * channels * bits / 8);
            writer.Write((short)(channels * bits / 8)); writer.Write((short)bits);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
            for (int frame = 0; frame < frames; frame++)
            {
                float sample = (float)Math.Sin(frame * 2 * Math.PI * 400 / rate) * 0.25f;
                for (int channel = 0; channel < channels; channel++)
                {
                    if (floating) writer.Write(sample);
                    else if (bits == 8) writer.Write((byte)(sample * 127 + 128));
                    else if (bits == 16) writer.Write((short)(sample * 32767));
                    else if (bits == 24)
                    {
                        int value = (int)(sample * 8388607);
                        writer.Write((byte)value); writer.Write((byte)(value >> 8)); writer.Write((byte)(value >> 16));
                    }
                    else writer.Write((int)(sample * 2147483647));
                }
            }
        }
        return path;
    }
    private static void Formats()
    {
        int[][] cases = { new[] { 44100, 1, 16, 0 }, new[] { 48000, 2, 24, 0 }, new[] { 22050, 1, 8, 0 }, new[] { 96000, 2, 32, 1 }, new[] { 48000, 1, 32, 0 } };
        for (int i = 0; i < cases.Length; i++)
        {
            int[] c = cases[i];
            var sound = new CachedSound(WriteWav("format" + i + ".wav", c[0], c[1], c[2], c[3] == 1, c[0] / 10));
            Check(sound.AudioData == null, "Constructing a handle decoded the file on the caller.");
            sound.Load(() => false);
            Check(sound.WaveFormat.SampleRate == 44100 && sound.WaveFormat.Channels == 1, "Mixed sample formats were not normalized.");
            Check(Math.Abs(sound.AudioData.Length - 4410) < 128, "Resampling changed duration unexpectedly.");
            Check(sound.AudioData.All(value => !float.IsNaN(value) && !float.IsInfinity(value)), "Non-finite sample decoded.");
        }
    }
    private static void InvalidFiles()
    {
        string empty = WriteWav("empty.wav", 44100, 1, 16, false, 0);
        string corrupt = Path.Combine(temporary, "corrupt.wav");
        File.WriteAllText(corrupt, "not a WAV");
        foreach (string path in new[] { empty, corrupt })
        {
            bool failed = false;
            try { new CachedSound(path).Load(() => false); }
            catch (Exception ex) when (ex is InvalidDataException || ex is FormatException || ex is ArgumentException) { failed = true; }
            Check(failed, "Invalid WAV was accepted: " + path);
        }
    }
    private static void CancelledVoice()
    {
        var owner = new SirenPlayer { TargetVolume = 1f };
        var request = new PlaybackRequest(owner, Constant(), true) { Cancelled = true };
        var voice = new AudioEngine.PlaybackVoice(request);
        Check(voice.Read(new float[1024], 0, 1024) == 0 && request.Completed, "A cancelled request became audible.");
    }
    private static void DeviceLifecycle()
    {
        var fake = new FakeOutput(true);
        int creations = 0;
        AudioEngine.Start(() => { Interlocked.Increment(ref creations); return fake; });
        try
        {
            Check(fake.InitEntered.Wait(1000), "Worker did not start device initialization.");
            var owner = new SirenPlayer();
            var sound = Constant();
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++) { owner.Play(sound, true, 1f); owner.Stop(true); }
            Check(timer.ElapsedMilliseconds < 500, "Play/stop waited on the blocked audio device.");
            Check(!owner.IsPlaying, "Cancelled pending start still reports playing.");
            owner.Play(sound, true, 1f);
            owner.TargetVolume = 1f;
            fake.AllowInit.Set();
            AudioEngine.SetMuted(false);
            Check(SpinWait.SpinUntil(() => { AudioEngine.Heartbeat(); return fake.Pull().Any(x => x != 0f); }, 2000), "Latest uncancelled request did not play.");
            Check(creations == 1 && fake.StopCount == 0, "Tone changes reopened/stopped the output device.");
            AudioEngine.SetPaused(true);
            Check(fake.Pull().All(x => x == 0f) && owner.IsPlaying, "Pause did not silence the live output while retaining the request.");
            AudioEngine.SetPaused(false);
            AudioEngine.Heartbeat();
            Check(fake.Pull().Any(x => x != 0f) && creations == 1 && fake.StopCount == 0, "Resume recreated/stopped the device or lost playback.");
            AudioEngine.SetMuted(true);
            Check(fake.Pull().All(x => x == 0f), "Global mute leaked samples.");
            owner.Stop(true);
        }
        finally { fake.AllowInit.Set(); AudioEngine.Shutdown(); }
        Check(fake.DisposeCount == 1, "Output was not disposed exactly once.");
    }
    private static void MutedFade()
    {
        var fake = new FakeOutput(false);
        AudioEngine.Start(() => fake);
        try
        {
            var owner = new SirenPlayer();
            var vehicle = new Rage.Vehicle();
            Rage.Game.GameTime = 1000;
            owner.Play(Constant(), true, 1f);
            owner.Stop(false);
            Rage.Game.GameTime = 1101;
            owner.Update3D(vehicle, default(Rage.Vector3), default(Rage.Vector3), true);
            Check(!owner.IsPlaying && !owner.IsFadingOut, "Force-muting prevented fade completion.");
            Rage.Game.GameTime = 2000;
            owner.Play(Constant(), true, 1f);
            owner.Stop(false);
            Rage.Game.GameTime = 7025;
            owner.DelayForPause(5000);
            owner.Update3D(vehicle, default(Rage.Vector3), default(Rage.Vector3));
            Check(owner.IsPlaying && owner.IsFadingOut, "A game-clock jump during pause consumed the fade.");
            Rage.Game.GameTime = 7101;
            owner.Update3D(vehicle, default(Rage.Vector3), default(Rage.Vector3));
            Check(!owner.IsPlaying, "Fade did not complete after resuming.");
            owner.Play(Constant(), true, 1f);
            vehicle.Valid = false;
            owner.Update3D(vehicle, default(Rage.Vector3), default(Rage.Vector3));
            Check(!owner.IsPlaying, "Deleted vehicle left an orphan voice.");
        }
        finally { AudioEngine.Shutdown(); }
    }
    private static void Configuration()
    {
        PluginConfig.BaseFolder = Path.Combine(temporary, "config");
        PluginConfig.ConfigFile = Path.Combine(PluginConfig.BaseFolder, "Config.ini");
        PluginConfig.WavFolder = Path.Combine(PluginConfig.BaseFolder, "WAVs");
        PluginConfig.ProfilesFolder = Path.Combine(PluginConfig.BaseFolder, "Profiles");
        PluginConfig.UseElsKeybinds = false;
        PluginConfig.Load();
        PluginConfig.MaxDistance = 50;
        PluginConfig.MinDistance = 500;
        PluginConfig.AiScanInterval = -1;
        PluginConfig.MaxAiUnits = 1000;
        PluginConfig.ReverbIntensity = float.NaN;
        PluginConfig.Validate();
        Check(PluginConfig.MinDistance < PluginConfig.MaxDistance && PluginConfig.AiScanInterval == 100 && PluginConfig.MaxAiUnits == 50 && PluginConfig.ReverbIntensity == 0, "Configuration was not clamped.");
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            PluginConfig.MinDistance = 7.5f;
            PluginConfig.SaveConfig();
            Check(File.ReadAllText(PluginConfig.ConfigFile).Contains("MinDistance=7.5"), "Float serialization depends on OS locale.");
            PluginConfig.MinDistance = 1;
            PluginConfig.Load();
            Check(PluginConfig.MinDistance == 7.5f, "MinDistance was lost on save/reload.");
            var ini = new Rage.InitializationFile(PluginConfig.ConfigFile);
            ini.Write("Settings", "MasterVolume", "0,35");
            Check(Math.Abs(PluginConfig.ReadFloat(ini, "Settings", "MasterVolume", 1f) - 0.35f) < 0.0001f, "Legacy decimal-comma config was not read.");
        }
        finally { CultureInfo.CurrentCulture = before; }
    }
    private static void Profiles()
    {
        ProfileStore.Clear();
        string wav = WriteWav("profile.wav", 44100, 1, 16, false, 1000);
        File.Copy(wav, Path.Combine(PluginConfig.WavFolder, "profile.wav"));
        string path = Path.Combine(PluginConfig.ProfilesFolder, "Global.ini");
        File.WriteAllText(path, "[Sirens]\nTone1=profile.wav\n[SirenVolumes]\nTone1Vol=0.4\n");
        var first = ProfileStore.Get("POLICE");
        File.WriteAllText(path, "[Sirens]\nTone1=None\n[SirenVolumes]\nTone1Vol=0.7\n");
        Check(ProfileStore.Get("POLICE").Volumes["Tone1Vol"] == 0.4f, "Steady-state lookup reread the INI.");
        ProfileStore.SetVolume("Global", "Tone1Vol", 0.2f);
        Check(ProfileStore.Get("POLICE").Volumes["Tone1Vol"] == 0.2f, "Global volume did not reach inherited profiles.");
        ProfileStore.Clear();
        Check(ProfileStore.Get("POLICE").Sounds["Tone1"] == "None", "Reload kept a stale tone selection.");
        Check(ProfileStore.Get("POLICE").Volumes["Tone1Vol"] == 0.7f, "Reload kept stale volume.");
    }

    private static void SixTones()
    {
        for (int tone = 1; tone <= 6; tone++)
            Check(ToneSlots.Next(tone, index => true) == tone % 6 + 1, "Six-tone cycling order is incorrect.");
        Check(ToneSlots.Next(0, tone => tone == 5 || tone == 6) == 5, "Tone5-only profiles cannot start.");
        Check(ToneSlots.Next(5, tone => tone == 6) == 6, "Tone6 is skipped by manual/cycle selection.");
        Check(ToneSlots.Next(6, tone => tone == 6) == 6, "Single configured Tone6 cannot loop through the slots.");
        Check(ToneSlots.Next(3, tone => false) == 0, "An empty tone bank did not return OFF.");
        Check(ProfileStore.SoundKeys.Contains("Tone5") && ProfileStore.SoundKeys.Contains("Tone6"), "Profile/menu slots are missing.");
    }

    private static void HornRestart()
    {
        var fake = new FakeOutput(false);
        AudioEngine.Start(() => fake);
        try
        {
            var owner = new SirenPlayer();
            var rule = new HornInterruption();
            var sound = Constant();
            int selected = 1, resumed = 0, stops = 0, starts = 0;
            Action stop = () => { stops++; owner.Stop(true); };
            Action restart = () => { if (selected != 0) { starts++; resumed = selected; owner.Play(sound, true, 1f); } };
            owner.Play(sound, true, 1f);
            rule.Update(true, true, stop, restart);
            Check(!owner.IsPlaying && rule.IsActive && stops == 1, "Horn only muted/paused the siren.");
            for (int i = 0; i < 5; i++) rule.Update(true, true, stop, restart);
            Check(stops == 1 && starts == 0, "Holding the horn repeatedly restarted the siren.");
            selected = 6; // User changes the selected tone while the horn is held.
            rule.Update(false, true, stop, restart);
            Check(owner.IsPlaying && !rule.IsActive && starts == 1 && resumed == 6, "Release did not restart the latest tone selection.");
            rule.Update(false, true, stop, restart);
            Check(starts == 1, "Release restarted more than once.");
            rule.Update(true, true, stop, restart);
            selected = 0; // Siren switched OFF during interruption.
            rule.Update(false, true, stop, restart);
            Check(!owner.IsPlaying && starts == 1, "Horn release resurrected a switched-off siren.");
            rule.Update(true, false, stop, restart);
            Check(!rule.IsActive && stops == 2, "Disabled horn interruption still stopped the siren.");
            rule.Update(true, true, stop, restart);
            rule.Reset(); // Vehicle switch/unload discards the old interruption.
            rule.Update(false, true, stop, restart);
            Check(starts == 1, "Reset retained a pending restart.");
        }
        finally { AudioEngine.Shutdown(); }
    }

    private static void RumblerProfiles()
    {
        string normalFile = "bank-normal.wav", onFile = "bank-rumbler.wav";
        File.Copy(WriteWav("normal-fixture.wav", 44100, 1, 16, false, 1000), Path.Combine(PluginConfig.WavFolder, normalFile));
        File.Copy(WriteWav("rumbler-fixture.wav", 44100, 1, 16, false, 1000), Path.Combine(PluginConfig.WavFolder, onFile));
        string path = Path.Combine(PluginConfig.ProfilesFolder, "RUMBLERTEST.ini");
        var ini = new Rage.InitializationFile(path);
        ini.Create();
        ini.Write("Rumbler", "Enabled", "true");
        for (int tone = 1; tone <= 6; tone++)
        {
            ini.Write("Sirens", "Tone" + tone, normalFile);
            ini.Write("RumblerSirens", "Tone" + tone, onFile);
        }
        ini.Write("SirenVolumes", "Tone6Vol", "0.3");
        ini.Write("RumblerVolumes", "Tone6Vol", "0.7");
        ini.Write("Sirens", "Horn", normalFile);
        ini.Write("RumblerSirens", "Horn", "None");
        ini.Write("Keybinds", "Toggle_Rumbler", "Control, F11");
        ProfileStore.Clear();
        var profile = ProfileStore.Get("RUMBLERTEST");
        for (int tone = 1; tone <= 6; tone++)
        {
            Check(Path.GetFileName(profile.GetSound("Tone" + tone, false)) == normalFile, "Normal WAV bank incorrect.");
            Check(Path.GetFileName(profile.GetSound("Tone" + tone, true)) == onFile, "Rumbler WAV bank missing a tone.");
        }
        Check(profile.GetVolume("Tone6Vol", false) == 0.3f && profile.GetVolume("Tone6Vol", true) == 0.7f, "Rumbler volumes overwrite normal volumes.");
        Check(Path.GetFileName(profile.GetSound("Horn", true)) == normalFile, "Unassigned rumbler horn did not fall back to normal.");
        Check(profile.RumblerKey == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.F11), "Per-vehicle rumbler chord did not load.");
        ProfileStore.SetVolume("RUMBLERTEST", "Tone6Vol", 0.2f, true);
        Check(profile.GetVolume("Tone6Vol", false) == 0.3f && profile.GetVolume("Tone6Vol", true) == 0.2f, "Editing one bank changed both banks.");
        ini.Write("Rumbler", "Enabled", "false");
        ProfileStore.Clear();
        Check(Path.GetFileName(ProfileStore.Get("RUMBLERTEST").GetSound("Tone6", true)) == normalFile, "Disabled rumbler still chose alternate WAVs.");
    }

    private static void ExtraMappings()
    {
        var vehicle = new FakeExtras();
        int[] mappings = { 0, 3, 4, 5 };
        Check(ExtraControls.Toggle(vehicle, mappings, 0) && vehicle.IsEnabled(0), "Extra 0/beacon did not enable.");
        Check(ExtraControls.Toggle(vehicle, mappings, 1) && vehicle.IsEnabled(3), "Matrix text 1 did not enable.");
        ExtraControls.Toggle(vehicle, mappings, 2);
        Check(vehicle.IsEnabled(0) && !vehicle.IsEnabled(3) && vehicle.IsEnabled(4), "Matrix text 2 did not replace text 1 independently of the beacon.");
        ExtraControls.Toggle(vehicle, mappings, 3);
        Check(vehicle.IsEnabled(0) && !vehicle.IsEnabled(4) && vehicle.IsEnabled(5), "Matrix text 3 did not replace text 2.");
        ExtraControls.Toggle(vehicle, mappings, 3);
        Check(vehicle.IsEnabled(0) && !vehicle.IsEnabled(3) && !vehicle.IsEnabled(4) && !vehicle.IsEnabled(5), "Toggling current text did not turn it off.");
        int writes = vehicle.Writes;
        Check(!ExtraControls.Toggle(vehicle, new[] { -1, -1, -1, -1 }, 0) && vehicle.Writes == writes, "Unmapped control touched vehicle extras.");
        Check(!ExtraControls.Toggle(vehicle, new[] { 200, -1, -1, -1 }, 0) && vehicle.Writes == writes, "Missing extra was written.");
        int[] invalid = { 3, 3, -2, 999 };
        ExtraControls.ValidateMappings(invalid);
        Check(invalid.SequenceEqual(new[] { 3, -1, -1, -1 }), "Duplicate/out-of-range mappings were not disabled.");
        string globalPath = Path.Combine(PluginConfig.ProfilesFolder, "Global.ini");
        var global = new Rage.InitializationFile(globalPath);
        global.Write("VehicleExtras", "RedBeacon", "9");
        string path = Path.Combine(PluginConfig.ProfilesFolder, "EXTRATEST.ini");
        File.WriteAllText(path, "[VehicleExtras]\nRedBeacon=0\nMatrixText1=3\nMatrixText2=4\nMatrixText3=5\n[Keybinds]\nToggle_MatrixText1=Control, NumPad1\n");
        ProfileStore.Clear();
        var profile = ProfileStore.Get("EXTRATEST");
        Check(profile.Extras.SequenceEqual(mappings), "Per-vehicle extra mappings did not load.");
        Check(profile.ExtraKeys[1] == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.NumPad1), "Per-vehicle extra key did not load.");
        Check(ProfileStore.Get("UNCONFIGURED").Extras.All(id => id == -1), "Global mesh IDs leaked into an unconfigured vehicle.");
    }

    private static void ModifierKeys()
    {
        var down = new System.Collections.Generic.HashSet<System.Windows.Forms.Keys>();
        var key = System.Windows.Forms.Keys.B;
        Check(!KeyBindings.IsDown(System.Windows.Forms.Keys.None, any => true), "An unbound control fired.");
        down.Add(key);
        Check(KeyBindings.IsDown(key, down.Contains), "Plain key did not fire.");
        Check(!KeyBindings.IsDown(key | System.Windows.Forms.Keys.Control, down.Contains), "Chord fired without its modifier.");
        down.Add(System.Windows.Forms.Keys.LControlKey);
        Check(!KeyBindings.IsDown(key, down.Contains), "Ctrl+B also fired the plain B action.");
        Check(KeyBindings.IsDown(key | System.Windows.Forms.Keys.Control, down.Contains), "Ctrl+B did not fire.");
        down.Remove(key);
        Check(!KeyBindings.IsDown(key | System.Windows.Forms.Keys.Control, down.Contains), "Releasing B left the chord active.");
    }

    private static void NewConfigRoundTrip()
    {
        PluginConfig.UseElsKeybinds = false;
        PluginConfig.Snd_SrnTon5 = System.Windows.Forms.Keys.D8;
        PluginConfig.Snd_SrnTon6 = System.Windows.Forms.Keys.D9;
        PluginConfig.Toggle_Rumbler = System.Windows.Forms.Keys.F11;
        PluginConfig.Toggle_FIAMMS = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.F9;
        PluginConfig.Debug = true;
        PluginConfig.FIAMMSVol = 0.65f;
        PluginConfig.Toggle_RedBeacon = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.B;
        PluginConfig.Tone5Vol = 0.45f;
        PluginConfig.Tone6Vol = 0.55f;
        PluginConfig.SaveConfig();
        PluginConfig.Snd_SrnTon5 = PluginConfig.Snd_SrnTon6 = PluginConfig.Toggle_Rumbler = PluginConfig.Toggle_RedBeacon = System.Windows.Forms.Keys.None;
        PluginConfig.Tone5Vol = PluginConfig.Tone6Vol = 0;
        PluginConfig.Toggle_FIAMMS = System.Windows.Forms.Keys.None;
        PluginConfig.Debug = false;
        PluginConfig.FIAMMSVol = 0;
        PluginConfig.Load();
        Check(PluginConfig.GetToneKey(5) == System.Windows.Forms.Keys.D8 && PluginConfig.GetToneKey(6) == System.Windows.Forms.Keys.D9, "New tone keybinds were lost.");
        Check(PluginConfig.Toggle_Rumbler == System.Windows.Forms.Keys.F11 && PluginConfig.Toggle_RedBeacon == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.B), "Feature keybinds were lost.");
        Check(PluginConfig.Tone5Vol == 0.45f && PluginConfig.Tone6Vol == 0.55f, "New tone volumes were lost.");
        Check(PluginConfig.Toggle_FIAMMS == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.F9) && PluginConfig.FIAMMSVol == 0.65f, "FIAMMS binding/volume were lost.");
        Check(PluginConfig.Debug, "Global DEBUG option was lost.");
    }

    private static void HornCycling()
    {
        var rule = new HornInterruption();
        var events = new System.Collections.Generic.List<string>();
        int selected = 5, playing = 5, cycles = 0;
        Action stop = () => { playing = 0; events.Add("stop"); };
        Action restart = () => { playing = selected; events.Add("restart:" + selected); };
        Action cycle = () =>
        {
            cycles++;
            selected = ToneSlots.Next(selected, tone => tone == 2 || tone == 5 || tone == 6);
            if (!rule.IsActive) playing = selected;
            events.Add("cycle:" + selected);
        };
        rule.Update(true, true, stop, restart, cycle);
        Check(playing == 0 && selected == 6, "Horn cycling started the next tone before release.");
        for (int i = 0; i < 10; i++) rule.Update(true, true, stop, restart, cycle);
        Check(cycles == 1, "Held horn cycled repeatedly.");
        rule.Update(false, true, stop, restart, cycle);
        Check(playing == 6 && events.SequenceEqual(new[] { "stop", "cycle:6", "restart:6" }), "Stop/cycle/restart order was incorrect.");
        rule.Update(true, true, stop, restart, cycle);
        rule.Update(false, true, stop, restart, cycle);
        Check(playing == 2 && cycles == 2, "Horn cycling did not wrap past empty slots.");
        rule.Update(true, false, stop, restart, cycle);
        Check(playing == 5 && !rule.IsActive && cycles == 3, "Non-interrupting horn failed to cycle immediately.");
        rule.Update(false, false, stop, restart, cycle);
        // A disabled/suppressed cycling callback still tracks the held key.
        rule.Update(true, false, stop, restart);
        rule.Update(true, false, stop, restart, cycle);
        Check(cycles == 3, "Enabling cycling while the horn was held triggered a new press.");
        rule.Update(false, false, stop, restart, cycle);
        rule.Update(true, false, stop, restart, cycle);
        Check(cycles == 4 && playing == 6, "Fresh horn press did not re-arm cycling.");
    }

    private static void FiammsProfiles()
    {
        File.Copy(WriteWav("fiamms-fixture.wav", 44100, 1, 16, false, 1000), Path.Combine(PluginConfig.WavFolder, "fiamms.wav"));
        File.Copy(WriteWav("fiamms-on-fixture.wav", 44100, 1, 16, false, 1000), Path.Combine(PluginConfig.WavFolder, "fiamms-on.wav"));
        var ini = new Rage.InitializationFile(Path.Combine(PluginConfig.ProfilesFolder, "FIAMMSTEST.ini"));
        ini.Create();
        ini.Write("Sirens", "Tone1", "profile.wav");
        ini.Write("Sirens", "FIAMMS", "fiamms.wav");
        ini.Write("RumblerSirens", "FIAMMS", "fiamms-on.wav");
        ini.Write("SirenVolumes", "FIAMMSVol", "0.4");
        ini.Write("RumblerVolumes", "FIAMMSVol", "0.8");
        ini.Write("Rumbler", "Enabled", "true");
        ini.Write("Keybinds", "Toggle_FIAMMS", "Control, F8");
        ProfileStore.Clear();
        var profile = ProfileStore.Get("FIAMMSTEST");
        Check(ProfileStore.SoundKeys.Contains("FIAMMS"), "FIAMMS is absent from menu/save/preload slots.");
        Check(Path.GetFileName(profile.GetSound("FIAMMS", false)) == "fiamms.wav" && Path.GetFileName(profile.GetSound("FIAMMS", true)) == "fiamms-on.wav", "FIAMMS banks did not resolve independently.");
        Check(profile.GetVolume("FIAMMSVol", false) == 0.4f && profile.GetVolume("FIAMMSVol", true) == 0.8f, "FIAMMS bank volumes were not loaded.");
        Check(profile.FiammsKey == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.F8), "Vehicle FIAMMS key did not override the config key.");
        Check(ProfileStore.Get("UNCONFIGURED").FiammsKey == PluginConfig.Toggle_FIAMMS, "FIAMMS config key was not inherited.");
        ini.Write("RumblerSirens", "FIAMMS", "None");
        ProfileStore.Clear();
        Check(Path.GetFileName(ProfileStore.Get("FIAMMSTEST").GetSound("FIAMMS", true)) == "fiamms.wav", "Unassigned alternate FIAMMS failed to fall back to normal.");
        ini.Write("Sirens", "FIAMMS", "None");
        ProfileStore.Clear();
        Check(ProfileStore.Get("FIAMMSTEST").GetSound("FIAMMS", false) == "None", "Unassigned FIAMMS unexpectedly fell back to a main tone.");
    }

    private static void IndependentLayers()
    {
        var fake = new FakeOutput(false);
        AudioEngine.Start(() => fake);
        try
        {
            var primary = new SirenPlayer();
            var fiamms = new SirenPlayer();
            primary.Play(Constant(), true, 1f);
            fiamms.Play(Constant(), true, 1f);
            primary.TargetVolume = fiamms.TargetVolume = 1f;
            AudioEngine.SetMuted(false);
            Check(SpinWait.SpinUntil(() =>
            {
                AudioEngine.Heartbeat();
                var samples = fake.Pull();
                return samples.Length != 0 && samples.Max() > 0.3f;
            }, 2000), "The two siren voices did not mix together.");
            primary.Stop(true); // Horn interruption of the main siren.
            AudioEngine.Heartbeat();
            Check(!primary.IsPlaying && fiamms.IsPlaying && fake.Pull().Any(value => value != 0f), "Stopping the primary also stopped the FIAMMS layer.");
            primary.Play(Constant(), true, 1f);
            primary.TargetVolume = 1f;
            fiamms.Stop(true);
            Check(primary.IsPlaying && !fiamms.IsPlaying, "FIAMMS toggle-off interrupted the primary request.");
            Check(SpinWait.SpinUntil(() => { AudioEngine.Heartbeat(); return fake.Pull().Any(value => value != 0f); }, 2000), "Main siren failed to continue after FIAMMS stopped.");
            primary.Stop(true);
        }
        finally { AudioEngine.Shutdown(); }
    }

    private static NAudio.Wave.SampleProviders.MixingSampleProvider CreatePauseMixer(System.Collections.Generic.List<PlaybackRequest> requests)
    {
        var mixer = new NAudio.Wave.SampleProviders.MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(CachedSound.SampleRate, 2)) { ReadFully = true };
        // Distinct main/horn/manual/FIAMMS/AI voices exercise the shared gate.
        for (int voice = 0; voice < 5; voice++)
        {
            var owner = new SirenPlayer { TargetVolume = 0.4f + voice * 0.05f, DistanceReverb = 0.15f, ReverbIntensity = 0.6f };
            var data = Enumerable.Range(0, 2048).Select(sample => (float)Math.Sin(sample * (voice + 1) * 0.02) * 0.025f).ToArray();
            var request = new PlaybackRequest(owner, Samples(data), false);
            requests.Add(request);
            mixer.AddMixerInput(new AudioEngine.PlaybackVoice(request));
        }
        return mixer;
    }

    private static void PausedMixer()
    {
        bool paused = false, muted = false;
        var requests = new System.Collections.Generic.List<PlaybackRequest>();
        var referenceRequests = new System.Collections.Generic.List<PlaybackRequest>();
        var mixer = CreatePauseMixer(requests);
        var referenceMixer = CreatePauseMixer(referenceRequests);
        var output = new AudioEngine.OutputProvider(mixer, () => paused, () => muted);
        var reference = new AudioEngine.OutputProvider(referenceMixer, () => false, () => false);
        var actual = new float[512];
        var expected = new float[512];
        output.Read(actual, 0, actual.Length);
        reference.Read(expected, 0, expected.Length);
        Check(actual.SequenceEqual(expected) && actual.Any(value => value != 0f), "Pause test fixture did not produce matching audible voices.");
        paused = true;
        var sentinel = Enumerable.Repeat(-9f, 40).ToArray();
        Check(output.Read(sentinel, 2, 32) == 32 && sentinel.Skip(2).Take(32).All(value => value == 0f), "Pause did not return a full silent buffer.");
        Check(sentinel.Take(2).Concat(sentinel.Skip(34)).All(value => value == -9f), "Pause wrote outside the requested range.");
        for (int i = 0; i < 20; i++) output.Read(actual, 0, actual.Length);
        Check(actual.All(value => value == 0f) && requests.All(request => !request.Completed), "Paused reads advanced or completed a one-shot.");
        // Simulate the worker finishing a queued WAV load while already paused.
        var added = new PlaybackRequest(new SirenPlayer { TargetVolume = 0.2f }, Constant(), false);
        var referenceAdded = new PlaybackRequest(new SirenPlayer { TargetVolume = 0.2f }, Constant(), false);
        mixer.AddMixerInput(new AudioEngine.PlaybackVoice(added));
        referenceMixer.AddMixerInput(new AudioEngine.PlaybackVoice(referenceAdded));
        for (int i = 0; i < 20; i++) output.Read(actual, 0, actual.Length);
        Check(!added.Completed, "A voice loaded during pause ran to completion silently.");
        requests[0].Cancelled = referenceRequests[0].Cancelled = true;
        paused = false;
        output.Read(actual, 0, actual.Length);
        reference.Read(expected, 0, expected.Length);
        Check(actual.SequenceEqual(expected), "Resume skipped/restarted samples or advanced reverb/gain state during pause.");
        // Menu mute still advances audio; game pause takes priority over it.
        muted = true;
        output.Read(actual, 0, actual.Length);
        reference.Read(expected, 0, expected.Length);
        Check(actual.All(value => value == 0f), "Menu mute leaked samples.");
        muted = false;
        output.Read(actual, 0, actual.Length);
        reference.Read(expected, 0, expected.Length);
        Check(actual.SequenceEqual(expected), "Menu mute changed the continuing-playback contract.");
    }

    private static void HornModeRouting()
    {
        foreach (bool interrupt in new[] { false, true })
        {
            foreach (bool available in new[] { false, true })
            {
                var car = HornModes.Resolve(HornCycleMode.CarHorn, available, interrupt);
                Check(car.Cycle && !car.PlaySirenHorn && !car.SuppressCarHorn && car.InterruptSiren == interrupt, "Car-horn mode played/suppressed the wrong horn.");
                var siren = HornModes.Resolve(HornCycleMode.SirenHorn, available, interrupt);
                Check(siren.Cycle && siren.PlaySirenHorn == available && siren.SuppressCarHorn && siren.InterruptSiren == (interrupt && available), "Siren-horn mode leaked the car horn or handled an unassigned WAV incorrectly.");
                var silent = HornModes.Resolve(HornCycleMode.Silent, available, interrupt);
                Check(silent.Cycle && !silent.PlaySirenHorn && silent.SuppressCarHorn && !silent.InterruptSiren, "Silent mode still sounded a horn or stopped the main siren.");
                var off = HornModes.Resolve(HornCycleMode.Off, available, interrupt);
                Check(!off.Cycle && off.PlaySirenHorn == available && off.SuppressCarHorn == available && off.InterruptSiren == interrupt, "Off changed normal horn behavior or kept cycling enabled.");
            }
        }
        var rule = new HornInterruption();
        var behavior = HornModes.Resolve(HornCycleMode.Silent, true, true);
        int tone = 6, stops = 0, restarts = 0, cycles = 0;
        Action cycle = () => { cycles++; tone = ToneSlots.Next(tone, id => id == 2 || id == 6); };
        for (int i = 0; i < 5; i++) rule.Update(true, behavior.InterruptSiren, () => stops++, () => restarts++, cycle);
        Check(tone == 2 && cycles == 1 && stops == 0, "Silent horn press did not cycle immediately and only once.");
        rule.Update(false, behavior.InterruptSiren, () => stops++, () => restarts++, cycle);
        Check(restarts == 0, "Releasing a silent horn unnecessarily restarted the tone.");
        Check(HornModes.Parse("carhorn") == HornCycleMode.CarHorn && HornModes.Parse("Silent") == HornCycleMode.Silent, "Horn mode names are case sensitive.");
        Check(HornModes.Parse("bogus") == HornCycleMode.Off && HornModes.Parse("99") == HornCycleMode.Off && HornModes.Parse("CarHorn, SirenHorn") == HornCycleMode.Off, "Invalid or combined horn modes were accepted.");
    }

    private static void HornModeProfiles()
    {
        var global = new Rage.InitializationFile(Path.Combine(PluginConfig.ProfilesFolder, "Global.ini"));
        global.Write("Settings", "HornCycleMode", "SirenHorn");
        var a = new Rage.InitializationFile(Path.Combine(PluginConfig.ProfilesFolder, "HORN_A.ini"));
        var b = new Rage.InitializationFile(Path.Combine(PluginConfig.ProfilesFolder, "HORN_B.ini"));
        a.Create(); b.Create();
        a.Write("Settings", "HornCycleMode", "CarHorn");
        b.Write("Settings", "HornCycleMode", "Silent");
        b.Write("Settings", "Debug", "false");
        PluginConfig.Debug = true;
        ProfileStore.Clear();
        Check(ProfileStore.Get("HORN_A").HornCycle == HornCycleMode.CarHorn && ProfileStore.Get("HORN_B").HornCycle == HornCycleMode.Silent, "Vehicle horn settings leaked between models.");
        Check(ProfileStore.Get("Global").HornCycle == HornCycleMode.Off && ProfileStore.Get("HORN_UNCONFIGURED").HornCycle == HornCycleMode.Off, "Global horn mode applied to an unconfigured model.");
        Check(PluginConfig.Debug, "A vehicle profile overrode the global DEBUG flag.");
        a.Write("Settings", "HornCycleMode", "Off");
        Check(ProfileStore.Get("HORN_A").HornCycle == HornCycleMode.CarHorn, "Horn routing reread INI files during cached lookups.");
        ProfileStore.Clear();
        Check(ProfileStore.Get("HORN_A").HornCycle == HornCycleMode.Off, "Profile reload did not apply the updated horn mode.");
    }

    private static void DebugExtrasDiscovery()
    {
        var vehicle = new DebugExtrasFixture();
        var tracker = new DebugExtraTracker();
        int[] mapped = { 220, -1, -1, -1 };
        int[] enabled = tracker.Refresh(vehicle, mapped);
        Check(enabled.SequenceEqual(new[] { 0, 220 }), "DEBUG missed a native active extra or a high mapped ID.");
        Check(vehicle.ExistenceQueries <= 20 && tracker.IsScanning, "DEBUG scanned all IDs in the first frame.");
        vehicle.SetEnabled(5, true);
        for (int i = 0; i < 15; i++)
        {
            int before = vehicle.ExistenceQueries;
            enabled = tracker.Refresh(vehicle, mapped);
            Check(vehicle.ExistenceQueries - before <= 20, "Extra discovery exceeded its refresh budget.");
        }
        Check(!tracker.IsScanning && enabled.SequenceEqual(new[] { 0, 5, 220 }), "Progressive extra discovery failed to finish or missed a state change.");
        vehicle.SetEnabled(220, false);
        enabled = tracker.Refresh(vehicle, mapped);
        Check(enabled.SequenceEqual(new[] { 0, 5 }) && tracker.Exists(220), "DEBUG confused a disabled extra with a missing extra.");
        var replacement = new DebugExtrasFixture();
        replacement.Values.Clear(); replacement.Values[2] = true;
        enabled = new DebugExtraTracker().Refresh(replacement, new[] { -1, -1, -1, -1 });
        Check(enabled.SequenceEqual(new[] { 2 }), "Changing vehicle retained the previous vehicle's extras.");
    }

    private static void DebugSnapshotText()
    {
        var state = new VehicleDebugState
        {
            Model = "POLICE", Output = "PAUSED", MasterVolume = 0.5f,
            HornMode = HornCycleMode.Silent, NativeHornSuppressed = true,
            RumblerEnabled = true, RumblerOn = true, FiammsOn = true,
            SirenFlag = true, LightGate = true, StageTracking = true, Stage = 3, StageCount = 3,
            Voices = new[]
            {
                new DebugVoiceState { Name = "Tone6", Status = "PLAYING", Sound = "tone6.wav" },
                new DebugVoiceState { Name = "Siren horn", Status = "OFF", Sound = "horn.wav" },
                new DebugVoiceState { Name = "Manual", Status = "OFF", Sound = "None" },
                new DebugVoiceState { Name = "FIAMMS", Status = "LOADING", Sound = new string('x', 200) + ".wav" }
            },
            MappedExtras = new[] { 0, 3, -1, 250 },
            MappedExists = new[] { true, true, false, false },
            MappedEnabled = new[] { true, false, false, false },
            EnabledExtras = new[] { 0, 5, 6, 7, 200 }, ScanningExtras = false
        };
        string text = DebugText.Build(state);
        Check(text.Contains("Vehicle: POLICE") && text.Contains("Tone6: PLAYING") && text.Contains("Audio: PAUSED"), "DEBUG omitted the current vehicle, tone or pause state.");
        Check(text.Contains("Rumbler: ON") && text.Contains("FIAMMS toggle: ON") && text.Contains("FIAMMS: LOADING"), "DEBUG omitted feature or loading states.");
        Check(text.Contains("Red beacon: extra 0: ON") && text.Contains("Matrix text 1: extra 3: OFF") && text.Contains("Matrix text 2: UNASSIGNED") && text.Contains("Matrix text 3: extra 250: MISSING"), "DEBUG extra labels did not reflect mapped native states.");
        Check(text.Contains("All enabled extras: 0, 5-7, 200"), "DEBUG omitted enabled extras outside the four mappings.");
        Check(text.Split(new[] { Environment.NewLine }, StringSplitOptions.None).All(line => line.Length <= 64), "A long WAV filename overflowed the DEBUG text column.");
        Check(DebugText.Build(null) == string.Empty, "A missing current vehicle produced visible debug text.");
    }

    private static void DebugPlaybackStatus()
    {
        var fake = new FakeOutput(true);
        AudioEngine.Start(() => fake);
        try
        {
            Check(fake.InitEntered.Wait(1000), "Audio worker did not reach the blocked device.");
            var voice = new SirenPlayer();
            voice.Play(Constant(), true, 1f);
            voice.TargetVolume = 1f;
            Check(voice.DebugStatus == "LOADING", "A pending request was reported as playing.");
            fake.AllowInit.Set();
            AudioEngine.SetMuted(false);
            Check(SpinWait.SpinUntil(() => voice.DebugStatus == "PLAYING", 2000), "A started voice still reported loading.");
            AudioEngine.SetPaused(true);
            Check(AudioEngine.DebugStatus == "PAUSED", "DEBUG did not report the global pause gate.");
            voice.Stop(false);
            Check(voice.DebugStatus == "FADING OUT", "DEBUG did not report fade state.");
            voice.Stop(true);
            Check(voice.DebugStatus == "OFF", "A cancelled voice still appeared enabled.");
        }
        finally { fake.AllowInit.Set(); AudioEngine.Shutdown(); }
    }

    private sealed class DebugExtrasFixture : IVehicleExtras
    {
        internal readonly System.Collections.Generic.Dictionary<int, bool> Values = new System.Collections.Generic.Dictionary<int, bool> { { 0, true }, { 5, false }, { 220, true } };
        internal int ExistenceQueries;
        public bool Exists(int id) { ExistenceQueries++; return Values.ContainsKey(id); }
        public bool IsEnabled(int id) => Values[id];
        public void SetEnabled(int id, bool enabled) { Values[id] = enabled; }
    }

    private sealed class FakeExtras : IVehicleExtras
    {
        private readonly System.Collections.Generic.Dictionary<int, bool> values = new System.Collections.Generic.Dictionary<int, bool> { { 0, false }, { 3, false }, { 4, false }, { 5, false } };
        internal int Writes;
        public bool Exists(int id) => values.ContainsKey(id);
        public bool IsEnabled(int id) => values[id];
        public void SetEnabled(int id, bool enabled) { Check(Exists(id), "Attempted to write a missing extra."); values[id] = enabled; Writes++; }
    }

    private sealed class FakeOutput : IWavePlayer
    {
        private IWaveProvider provider;
        private int state;
        public readonly ManualResetEventSlim InitEntered = new ManualResetEventSlim();
        public readonly ManualResetEventSlim AllowInit;
        public int StopCount;
        public int DisposeCount;
        public FakeOutput(bool block) { AllowInit = new ManualResetEventSlim(!block); }
        public void Init(IWaveProvider value)
        {
            InitEntered.Set();
            if (!AllowInit.Wait(3000)) throw new TimeoutException("Test did not release device initialization.");
            Volatile.Write(ref provider, value);
        }
        public float[] Pull()
        {
            var current = Volatile.Read(ref provider);
            if (current == null || PlaybackState != PlaybackState.Playing) return new float[0];
            var bytes = new byte[4096];
            int read = current.Read(bytes, 0, bytes.Length);
            var samples = new float[read / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, samples, 0, read);
            return samples;
        }
        public void Play() => Volatile.Write(ref state, (int)PlaybackState.Playing);
        public void Pause() => Volatile.Write(ref state, (int)PlaybackState.Paused);
        public void Stop() { Interlocked.Increment(ref StopCount); Volatile.Write(ref state, (int)PlaybackState.Stopped); }
        public void Dispose() => Interlocked.Increment(ref DisposeCount);
        public float Volume { get; set; } = 1f;
        public PlaybackState PlaybackState => (PlaybackState)Volatile.Read(ref state);
        public WaveFormat OutputWaveFormat => Volatile.Read(ref provider)?.WaveFormat;
        public event EventHandler<StoppedEventArgs> PlaybackStopped { add { } remove { } }
    }
}
