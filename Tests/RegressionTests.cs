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
        Check(ToneSlots.Next(5, tone => tone == 6) == 6, "Tone6 is skipped by manual/scan selection.");
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
        PluginConfig.Toggle_RedBeacon = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.B;
        PluginConfig.Tone5Vol = 0.45f;
        PluginConfig.Tone6Vol = 0.55f;
        PluginConfig.SaveConfig();
        PluginConfig.Snd_SrnTon5 = PluginConfig.Snd_SrnTon6 = PluginConfig.Toggle_Rumbler = PluginConfig.Toggle_RedBeacon = System.Windows.Forms.Keys.None;
        PluginConfig.Tone5Vol = PluginConfig.Tone6Vol = 0;
        PluginConfig.Load();
        Check(PluginConfig.GetToneKey(5) == System.Windows.Forms.Keys.D8 && PluginConfig.GetToneKey(6) == System.Windows.Forms.Keys.D9, "New tone keybinds were lost.");
        Check(PluginConfig.Toggle_Rumbler == System.Windows.Forms.Keys.F11 && PluginConfig.Toggle_RedBeacon == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.B), "Feature keybinds were lost.");
        Check(PluginConfig.Tone5Vol == 0.45f && PluginConfig.Tone6Vol == 0.55f, "New tone volumes were lost.");
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
