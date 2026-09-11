using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CustomELSSirens
{
    internal static class AudioMath
    {
        internal static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return min;
            return Math.Max(min, Math.Min(max, value));
        }
    }

    // A lightweight handle on the game thread. AudioEngine's worker owns decoding.
    public sealed class CachedSound
    {
        public const int SampleRate = 44100;
        internal const int MaxSamples = SampleRate * 120;
        internal readonly string FilePath;
        internal volatile bool Failed;
        internal bool PreloadQueued;
        internal LinkedListNode<CachedSound> CacheNode;
        public float[] AudioData { get; private set; }
        public WaveFormat WaveFormat { get; private set; }

        public CachedSound(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("An audio path is required.", nameof(filePath));
            FilePath = filePath;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
        }

        internal CachedSound(float[] samples, WaveFormat format)
        {
            AudioData = samples ?? throw new ArgumentNullException(nameof(samples));
            WaveFormat = format ?? throw new ArgumentNullException(nameof(format));
        }

        internal void ReleaseData() => AudioData = null;

        internal void Load(Func<bool> cancelled)
        {
            if (AudioData != null) return;
            using (var reader = new AudioFileReader(FilePath))
            {
                // Keep the original decoder/Windows codec support, but construct
                // it only on the worker. Resampling is done once before mixing.
                ISampleProvider source = reader;
                if (source.WaveFormat.Channels == 2)
                    source = new StereoToMonoSampleProvider(source) { LeftVolume = 0.5f, RightVolume = 0.5f };
                if (source.WaveFormat.Channels != 1)
                    throw new InvalidDataException("Use a mono or stereo PCM/IEEE-float WAV file.");
                if (source.WaveFormat.SampleRate != SampleRate)
                    source = new WdlResamplingSampleProvider(source, SampleRate);

                if (reader.TotalTime.TotalSeconds > 120.01)
                    throw new InvalidDataException("Siren WAV files must be at most 120 seconds long.");
                int estimatedSamples = (int)Math.Min(MaxSamples, Math.Ceiling(reader.TotalTime.TotalSeconds * SampleRate) + 4096);
                var data = new List<float>(Math.Max(0, estimatedSamples));
                var buffer = new float[8192];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (cancelled()) throw new OperationCanceledException();
                    if (data.Count > MaxSamples - read)
                        throw new InvalidDataException("Siren WAV files must be at most 120 seconds long.");
                    for (int i = 0; i < read; i++)
                    {
                        float sample = buffer[i];
                        data.Add(float.IsNaN(sample) || float.IsInfinity(sample) ? 0f : sample);
                    }
                }
                if (data.Count == 0) throw new InvalidDataException("The WAV file contains no audio samples.");
                AudioData = data.ToArray();
            }
        }
    }

    public sealed class CachedSampleProvider : ISampleProvider
    {
        // Retain the array so eviction/reloading cannot change an active voice.
        private readonly float[] data;
        private readonly bool loop;
        private readonly int channels;
        private readonly int xfadeLength;
        private int position;
        public WaveFormat WaveFormat { get; }

        public CachedSampleProvider(CachedSound sound, bool loop)
        {
            if (sound == null) throw new ArgumentNullException(nameof(sound));
            data = sound.AudioData ?? throw new InvalidOperationException("Audio must be decoded on the worker before playback.");
            WaveFormat = sound.WaveFormat;
            channels = WaveFormat.Channels;
            if (data.Length % channels != 0) throw new InvalidDataException("Incomplete audio frame.");
            this.loop = loop;
            xfadeLength = loop && data.Length > 1024 * channels ? 512 * channels : 0;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException();
            if (data.Length == 0 || count == 0) return 0;
            int written = 0;
            int crossfadeStart = data.Length - xfadeLength;
            while (written < count)
            {
                if (position >= data.Length)
                {
                    if (!loop) break;
                    position = xfadeLength;
                }
                if (xfadeLength > 0 && position >= crossfadeStart)
                {
                    int crossfadePosition = position - crossfadeStart;
                    // Both channels of a stereo frame get exactly the same gain.
                    float t = (float)(crossfadePosition / channels + 1) / (xfadeLength / channels + 1);
                    buffer[offset + written++] = data[position++] * (1f - t) + data[crossfadePosition] * t;
                }
                else
                {
                    int available = Math.Min(crossfadeStart - position, count - written);
                    Array.Copy(data, position, buffer, offset + written, available);
                    position += available;
                    written += available;
                }
            }
            return written;
        }
    }

    public sealed class CityReverbProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly float[] left1, left2, right1, right2;
        private int l1, l2, r1, r2;
        public float BaseReverb { get; set; } = 0.30f;
        public float DistanceReverb { get; set; }
        public float Intensity { get; set; } = 1f;
        public WaveFormat WaveFormat => source.WaveFormat;

        public CityReverbProvider(ISampleProvider source)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            if (source.WaveFormat.Channels != 2) throw new ArgumentException("Reverb requires stereo audio.", nameof(source));
            int rate = source.WaveFormat.SampleRate;
            left1 = new float[Math.Max(1, (int)(rate * 0.137f))];
            left2 = new float[Math.Max(1, (int)(rate * 0.197f))];
            right1 = new float[Math.Max(1, (int)(rate * 0.153f))];
            right2 = new float[Math.Max(1, (int)(rate * 0.223f))];
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            float wet = AudioMath.Clamp((BaseReverb + DistanceReverb) * Intensity, 0f, 0.8f);
            for (int i = 0; i + 1 < read; i += 2)
            {
                float inputL = buffer[offset + i];
                float inputR = buffer[offset + i + 1];
                float delayL = left1[l1] + left2[l2];
                float delayR = right1[r1] + right2[r2];
                buffer[offset + i] = inputL + (delayL * 0.65f + delayR * 0.28f) * wet;
                buffer[offset + i + 1] = inputR + (delayR * 0.65f + delayL * 0.28f) * wet;
                left1[l1] = inputL + left1[l1] * 0.44f;
                left2[l2] = inputL + left2[l2] * 0.34f;
                right1[r1] = inputR + right1[r1] * 0.44f;
                right2[r2] = inputR + right2[r2] * 0.34f;
                if (++l1 == left1.Length) l1 = 0;
                if (++l2 == left2.Length) l2 = 0;
                if (++r1 == right1.Length) r1 = 0;
                if (++r2 == right2.Length) r2 = 0;
            }
            return read;
        }
    }
}
