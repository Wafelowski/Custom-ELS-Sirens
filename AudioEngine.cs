using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CustomELSSirens
{
    internal sealed class PlaybackRequest
    {
        internal readonly SirenPlayer Owner;
        internal readonly CachedSound Sound;
        internal readonly bool Loop;
        internal volatile bool Cancelled;
        internal volatile bool Completed;
        internal volatile bool Started;

        internal PlaybackRequest(SirenPlayer owner, CachedSound sound, bool loop)
        {
            Owner = owner;
            Sound = sound;
            Loop = loop;
        }
    }

    internal static class AudioEngine
    {
        private static Engine instance;
        private static readonly ConcurrentQueue<string> messages = new ConcurrentQueue<string>();

        internal static void Start(Func<IWavePlayer> outputFactory = null)
        {
            if (instance != null) return;
            instance = new Engine(outputFactory ?? (() => new WaveOutEvent { DesiredLatency = 90, NumberOfBuffers = 3 }));
        }

        internal static void Play(PlaybackRequest request)
        {
            var engine = instance;
            if (engine == null) { request.Completed = true; return; }
            engine.Enqueue(request);
        }

        internal static void Preload(CachedSound sound)
        {
            // Called only on the game fiber; one preload per handle.
            if (sound == null || sound.PreloadQueued || sound.Failed) return;
            sound.PreloadQueued = true;
            instance?.Preload(sound);
        }

        internal static void SetMuted(bool muted)
        {
            var engine = instance;
            if (engine != null) engine.Muted = muted;
        }

        internal static void SetPaused(bool paused)
        {
            var engine = instance;
            if (engine != null) engine.Paused = paused;
        }

        internal static void Heartbeat()
        {
            var engine = instance;
            if (engine != null) engine.Heartbeat = Environment.TickCount;
        }

        internal static bool TryGetMessage(out string message) => messages.TryDequeue(out message);
        internal static string DebugStatus => instance == null ? "STOPPED" : instance.DebugStatus;
        internal static void ClearCache() => instance?.RequestCacheClear();

        internal static void Shutdown()
        {
            var engine = instance;
            instance = null;
            engine?.Stop();
        }

        private sealed class Engine
        {
            private const long MaxCacheBytes = 256L * 1024 * 1024;
            private readonly Func<IWavePlayer> outputFactory;
            private readonly ConcurrentQueue<PlaybackRequest> requests = new ConcurrentQueue<PlaybackRequest>();
            private readonly ConcurrentQueue<CachedSound> preloads = new ConcurrentQueue<CachedSound>();
            private readonly AutoResetEvent signal = new AutoResetEvent(false);
            private readonly LinkedList<CachedSound> lru = new LinkedList<CachedSound>();
            private readonly List<PlaybackVoice> voices = new List<PlaybackVoice>();
            private readonly MixingSampleProvider mixer;
            private readonly Thread worker;
            private volatile bool stopping;
            private int clearCache;
            private long cacheBytes;
            private IWavePlayer output;
            private int nextDeviceAttempt;
            internal volatile bool Muted = true;
            internal volatile bool Paused;
            private volatile bool outputAvailable;
            internal volatile int Heartbeat = Environment.TickCount;
            internal string DebugStatus => Paused ? "PAUSED" : IsSuspended() ? "SUSPENDED" :
                !outputAvailable ? "DEVICE UNAVAILABLE" : Muted ? "MENU MUTED" : "RUNNING";

            internal Engine(Func<IWavePlayer> outputFactory)
            {
                this.outputFactory = outputFactory;
                mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(CachedSound.SampleRate, 2)) { ReadFully = true };
                nextDeviceAttempt = Environment.TickCount;
                worker = new Thread(Run) { IsBackground = true, Name = "CustomSirens audio" };
                worker.Start();
            }

            internal void Enqueue(PlaybackRequest request)
            {
                if (stopping) { request.Completed = true; return; }
                requests.Enqueue(request);
                Wake();
            }

            internal void Preload(CachedSound sound) { preloads.Enqueue(sound); Wake(); }
            internal void RequestCacheClear() { Interlocked.Exchange(ref clearCache, 1); Wake(); }
            private void Wake()
            {
                try { signal.Set(); }
                catch (ObjectDisposedException) { /* Worker already shut down. */ }
            }

            internal void Stop()
            {
                Muted = true;
                stopping = true;
                Wake();
                // Never wait on the device for a tone change; a bounded join is
                // used only once, on plugin unload.
                worker.Join(1000);
            }

            private void Run()
            {
                try
                {
                    while (!stopping)
                    {
                        if (Interlocked.Exchange(ref clearCache, 0) != 0) ReleaseCache();
                        for (int i = voices.Count - 1; i >= 0; i--)
                        {
                            if (!voices[i].HasEnded) continue;
                            mixer.RemoveMixerInput(voices[i]);
                            voices.RemoveAt(i);
                        }
                        EnsureOutput();
                        if (requests.TryDequeue(out var request))
                        {
                            if (request.Cancelled) continue;
                            if (!Load(request.Sound) || stopping || request.Cancelled)
                            {
                                request.Completed = true;
                                continue;
                            }
                            try
                            {
                                var voice = new PlaybackVoice(request);
                                mixer.AddMixerInput(voice);
                                request.Started = true;
                                voices.Add(voice);
                            }
                            catch (Exception ex)
                            {
                                request.Completed = true;
                                messages.Enqueue("Cannot start voice: " + ex.Message);
                            }
                        }
                        else if (preloads.TryDequeue(out var sound)) Load(sound);
                        else signal.WaitOne(100);
                    }
                }
                catch (Exception ex) { messages.Enqueue("Audio worker stopped: " + ex.Message); }
                finally
                {
                    stopping = true;
                    Muted = true;
                    foreach (var voice in voices) voice.Finish();
                    voices.Clear();
                    while (requests.TryDequeue(out var request)) request.Completed = true;
                    while (preloads.TryDequeue(out _)) { }
                    DisposeOutput();
                    mixer.RemoveAllMixerInputs();
                    ReleaseCache();
                    signal.Dispose();
                }
            }

            private void EnsureOutput()
            {
                if (output != null && output.PlaybackState == PlaybackState.Playing) return;
                outputAvailable = false;
                if (unchecked(Environment.TickCount - nextDeviceAttempt) < 0) return;
                DisposeOutput();
                try
                {
                    output = outputFactory();
                    output.Init(new SampleToWaveProvider(new OutputProvider(mixer, IsSuspended, () => Muted)));
                    output.Play();
                    outputAvailable = true;
                }
                catch (Exception ex)
                {
                    DisposeOutput();
                    messages.Enqueue("Audio device unavailable; retrying in 5 seconds: " + ex.Message);
                }
                nextDeviceAttempt = unchecked(Environment.TickCount + 5000);
            }

            private bool IsSuspended() => stopping || Paused ||
                unchecked((uint)(Environment.TickCount - Heartbeat)) > 1000u;

            private void DisposeOutput()
            {
                outputAvailable = false;
                var previous = output;
                output = null;
                if (previous == null) return;
                try { previous.Stop(); }
                catch (Exception ex) { messages.Enqueue("Audio stop failed: " + ex.Message); }
                finally
                {
                    try { previous.Dispose(); }
                    catch (Exception ex) { messages.Enqueue("Audio disposal failed: " + ex.Message); }
                }
            }

            private bool Load(CachedSound sound)
            {
                if (sound.Failed) return false;
                try
                {
                    sound.Load(() => stopping);
                    if (sound.CacheNode != null) lru.Remove(sound.CacheNode);
                    else cacheBytes += sound.AudioData.LongLength * sizeof(float);
                    sound.CacheNode = lru.AddLast(sound);
                    while (cacheBytes > MaxCacheBytes && lru.Count > 1) Evict(lru.First.Value);
                    return true;
                }
                catch (OperationCanceledException) { return false; }
                catch (Exception ex)
                {
                    sound.Failed = true;
                    messages.Enqueue("Cannot load '" + sound.FilePath + "': " + ex.Message + " Use Reload WAV Files after fixing it.");
                    return false;
                }
            }

            private void Evict(CachedSound sound)
            {
                cacheBytes -= sound.AudioData.LongLength * sizeof(float);
                lru.Remove(sound.CacheNode);
                sound.CacheNode = null;
                sound.ReleaseData();
            }

            private void ReleaseCache()
            {
                while (lru.Count > 0) Evict(lru.First.Value);
            }

        }

        internal sealed class OutputProvider : ISampleProvider
        {
            private readonly ISampleProvider source;
            private readonly Func<bool> shouldPause;
            private readonly Func<bool> shouldMute;
            public WaveFormat WaveFormat => source.WaveFormat;
            internal OutputProvider(ISampleProvider source, Func<bool> shouldPause, Func<bool> shouldMute)
            {
                this.source = source;
                this.shouldPause = shouldPause;
                this.shouldMute = shouldMute;
            }
            public int Read(float[] buffer, int offset, int count)
            {
                // Return silence before touching the mixer. This freezes every
                // WAV cursor, reverb buffer and gain ramp, including new voices
                // loaded during pause, while retaining the same output device.
                if (shouldPause())
                {
                    Array.Clear(buffer, offset, count);
                    return count;
                }
                int read = source.Read(buffer, offset, count);
                // Recheck to silence an in-flight read if pause changed. A menu
                // mute deliberately retains its existing continuing-playback behavior.
                if (shouldPause() || shouldMute()) Array.Clear(buffer, offset, read);
                else
                    for (int i = offset; i < offset + read; i++)
                        buffer[i] = float.IsNaN(buffer[i]) || float.IsInfinity(buffer[i]) ? 0f : AudioMath.Clamp(buffer[i], -1f, 1f);
                return read;
            }
        }

        internal sealed class PlaybackVoice : ISampleProvider
        {
            private readonly PlaybackRequest request;
            private readonly PanningSampleProvider panner;
            private readonly CityReverbProvider reverb;
            private float gain;
            public WaveFormat WaveFormat => reverb.WaveFormat;
            internal bool HasEnded => request.Cancelled || request.Completed;
            internal void Finish() => request.Completed = true;

            internal PlaybackVoice(PlaybackRequest request)
            {
                this.request = request;
                panner = new PanningSampleProvider(new CachedSampleProvider(request.Sound, request.Loop));
                reverb = new CityReverbProvider(panner);
            }

            public int Read(float[] buffer, int offset, int count)
            {
                if (request.Cancelled) { request.Completed = true; return 0; }
                var owner = request.Owner;
                panner.Pan = owner.Pan;
                reverb.DistanceReverb = owner.DistanceReverb;
                reverb.Intensity = owner.ReverbIntensity;
                int read = reverb.Read(buffer, offset, count);
                float target = owner.TargetVolume;
                const float step = 1f / (CachedSound.SampleRate * 0.005f);
                for (int i = 0; i + 1 < read; i += 2)
                {
                    gain += Math.Max(-step, Math.Min(step, target - gain));
                    buffer[offset + i] *= gain;
                    buffer[offset + i + 1] *= gain;
                }
                if (read < count) request.Completed = true;
                return read;
            }
        }
    }
}
