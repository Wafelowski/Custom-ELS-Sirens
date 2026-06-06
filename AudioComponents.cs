using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Rage;

namespace CustomELSSirens
{
    public class AiSirenState
    {
        public SirenPlayer Player { get; set; } = new SirenPlayer();
        public int CurrentToneIndex { get; set; } = 1;
        public uint NextToneChangeTime { get; set; } = 0;
    }

    public class SirenPlayer : IDisposable
    {
        private WaveOutEvent waveOut;
        private CachedSampleProvider cachedProvider;
        private PanningSampleProvider panProvider;
        private CityReverbProvider reverbProvider;
        private VolumeSampleProvider volProvider;
        private float perSirenVolume = 1f;

        public bool IsPlaying { get; private set; }

        public void Play(CachedSound cached, bool loop, float volumeMultiplier)
        {
            Stop();
            perSirenVolume = volumeMultiplier;
            if (cached == null) return;

            try
            {
                cachedProvider = new CachedSampleProvider(cached, loop);
                ISampleProvider sp = cachedProvider;

                if (sp.WaveFormat.Channels == 2)
                    sp = new StereoToMonoSampleProvider(sp) { LeftVolume = 0.5f, RightVolume = 0.5f };

                panProvider = new PanningSampleProvider(sp) { Pan = 0f };
                reverbProvider = new CityReverbProvider(panProvider) { BaseReverb = 0.30f, DistanceReverb = 0f, Intensity = PluginConfig.ReverbIntensity };
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
                if (cachedProvider != null) cachedProvider.IsMuted = true;

                var oldWave = waveOut;

                GameFiber.StartNew(() =>
                {
                    GameFiber.Sleep(2000);
                    try
                    {
                        oldWave?.Stop();
                        oldWave?.Dispose();
                    }
                    catch { }
                });
            }

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
                float reverbScale = MathHelper.Clamp(distance / (PluginConfig.MaxDistance * 0.4f), 0f, 1f);
                reverbProvider.DistanceReverb = reverbScale * 0.55f;
                reverbProvider.Intensity = PluginConfig.ReverbIntensity;
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

        private readonly int xfadeLen;

        public CachedSampleProvider(CachedSound cachedSound, bool loop)
        {
            this.cachedSound = cachedSound;
            this.loop = loop;

            int channels = cachedSound.WaveFormat.Channels;
            int minLengthForXfade = 512 * channels * 2;
            xfadeLen = (loop && cachedSound.AudioData.Length > minLengthForXfade)
                ? 512 * channels
                : 0;
        }

        public WaveFormat WaveFormat => cachedSound.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            if (IsMuted)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            int totalLen = cachedSound.AudioData.Length;
            int xfadeStart = totalLen - xfadeLen;
            int written = 0;

            while (written < count)
            {
                if (position >= totalLen)
                {
                    if (!loop) break;
                    position = xfadeLen;
                }

                bool inXfade = xfadeLen > 0 && position >= xfadeStart;

                if (inXfade)
                {
                    int xPos = (int)(position - xfadeStart);
                    float t = (float)(xPos + 1) / (xfadeLen + 1);
                    buffer[offset + written] =
                        cachedSound.AudioData[position] * (1f - t) +
                        cachedSound.AudioData[xPos] * t;
                    position++;
                    written++;
                }
                else
                {
                    int ceiling = xfadeLen > 0 ? xfadeStart : totalLen;
                    int canRead = (int)Math.Min(ceiling - position, count - written);
                    if (canRead <= 0) { position = xfadeLen; continue; }
                    Array.Copy(cachedSound.AudioData, position, buffer, offset + written, canRead);
                    position += canRead;
                    written += canRead;
                }
            }

            return written;
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
        public float Intensity { get; set; } = 1f;

        public WaveFormat WaveFormat => source.WaveFormat;

        public CityReverbProvider(ISampleProvider source)
        {
            this.source = source;
            int sr = source.WaveFormat.SampleRate;

            delayLeft1 = new float[(int)(sr * 0.137f)];
            delayLeft2 = new float[(int)(sr * 0.197f)];
            delayRight1 = new float[(int)(sr * 0.153f)];
            delayRight2 = new float[(int)(sr * 0.223f)];
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            float wetLevel = (BaseReverb + DistanceReverb) * Intensity;
            if (wetLevel > 0.8f) wetLevel = 0.8f;

            for (int i = 0; i < read; i += 2)
            {
                if (i + 1 >= read) break;

                float inL = buffer[offset + i];
                float inR = buffer[offset + i + 1];

                float dL = delayLeft1[posL1] + delayLeft2[posL2];
                float dR = delayRight1[posR1] + delayRight2[posR2];

                float outL = inL + (dL * 0.65f + dR * 0.28f) * wetLevel;
                float outR = inR + (dR * 0.65f + dL * 0.28f) * wetLevel;

                buffer[offset + i] = outL;
                buffer[offset + i + 1] = outR;

                delayLeft1[posL1] = inL + delayLeft1[posL1] * 0.44f;
                delayLeft2[posL2] = inL + delayLeft2[posL2] * 0.34f;
                delayRight1[posR1] = inR + delayRight1[posR1] * 0.44f;
                delayRight2[posR2] = inR + delayRight2[posR2] * 0.34f;

                posL1 = (posL1 + 1) % delayLeft1.Length;
                posL2 = (posL2 + 1) % delayLeft2.Length;
                posR1 = (posR1 + 1) % delayRight1.Length;
                posR2 = (posR2 + 1) % delayRight2.Length;
            }
            return read;
        }
    }
}