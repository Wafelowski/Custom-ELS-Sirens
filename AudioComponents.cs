using System;
using Rage;

namespace CustomELSSirens
{
    public sealed class AiSirenState
    {
        public SirenPlayer Player { get; set; }
        public string ModelName { get; set; }
        public bool RumblerOn { get; set; }
        public int CurrentToneIndex { get; set; } = 1;
        public uint NextToneChangeTime { get; set; }
    }

    // Only the game fiber touches entities/natives. Audio threads read scalars
    // and a cancellable request; they never access a Rage entity.
    public sealed class SirenPlayer : IDisposable
    {
        private volatile PlaybackRequest request;
        private float perSirenVolume = 1f;
        private uint fadeStart;
        private bool isExitFade;
        internal volatile float TargetVolume;
        internal volatile float Pan;
        internal volatile float DistanceReverb;
        internal volatile float ReverbIntensity = 1f;

        public bool IsPlaying
        {
            get
            {
                var current = request;
                return current != null && !current.Cancelled && !current.Completed;
            }
        }
        public bool IsFadingOut { get; private set; }

        public void Play(CachedSound cached, bool loop, float volumeMultiplier)
        {
            Stop(true);
            if (cached == null || cached.Failed) return;
            perSirenVolume = AudioMath.Clamp(volumeMultiplier, 0f, 1f);
            var next = new PlaybackRequest(this, cached, loop);
            request = next;
            AudioEngine.Play(next);
        }

        public void Stop(bool dropInstantly = false, bool exitFade = false)
        {
            if (dropInstantly)
            {
                var current = request;
                if (current != null) current.Cancelled = true;
                request = null;
                TargetVolume = 0f;
                IsFadingOut = false;
                return;
            }
            if (IsPlaying && !IsFadingOut)
            {
                IsFadingOut = true;
                fadeStart = Game.GameTime;
                isExitFade = exitFade;
            }
        }

        public void Dispose() => Stop(true);
        public void SetVolume(float volume) => TargetVolume = AudioMath.Clamp(volume, 0f, 1f);
        public void SetSirenVolume(float volume) => perSirenVolume = AudioMath.Clamp(volume, 0f, 1f);

        internal void DelayForPause(uint elapsed)
        {
            if (IsFadingOut) fadeStart = unchecked(fadeStart + elapsed);
        }

        public void Update3D(Vehicle veh, Vector3 camPos, Vector3 camRot, bool forceMute = false)
        {
            if (!IsPlaying) { IsFadingOut = false; return; }
            if (veh == null || !veh.IsValid() || !veh.IsAlive) { Stop(true); return; }

            // Advance fades even when a menu or manual tone mutes this voice.
            float fade = 1f;
            if (IsFadingOut)
            {
                uint elapsed = unchecked(Game.GameTime - fadeStart);
                uint hold = isExitFade ? 300u : 0u;
                uint duration = isExitFade ? 150u : 100u;
                if (elapsed >= hold + duration) { Stop(true); return; }
                if (elapsed >= hold)
                {
                    float remaining = 1f - (float)(elapsed - hold) / duration;
                    fade = remaining * remaining;
                }
            }

            Vector3 position = veh.Position;
            float distance = Vector3.Distance(camPos, position);
            float range = Math.Max(1f, PluginConfig.MaxDistance - PluginConfig.MinDistance);
            float normalized = AudioMath.Clamp((distance - PluginConfig.MinDistance) / range, 0f, 1f);
            float attenuation = (float)Math.Pow(1f - normalized, PluginConfig.FalloffExponent);
            DistanceReverb = AudioMath.Clamp(distance / (PluginConfig.MaxDistance * 0.4f), 0f, 1f) * 0.55f;
            ReverbIntensity = PluginConfig.ReverbIntensity;
            TargetVolume = forceMute ? 0f : AudioMath.Clamp(
                attenuation * PluginConfig.MasterVolume * perSirenVolume * fade, 0f, 1f);

            float dx = position.X - camPos.X;
            float dy = position.Y - camPos.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            double yaw = camRot.Z * Math.PI / 180.0;
            Pan = length > 0.01f
                ? AudioMath.Clamp((float)(Math.Cos(yaw) * dx + Math.Sin(yaw) * dy) / length, -1f, 1f)
                : 0f;
        }
    }
}
