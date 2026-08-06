namespace Client.MirSounds
{
    // SoundManager 的 Client.Core seam（占位）：真实音频由 AudioSource/AudioMixer 提供。
    public static class SoundManager
    {
        public static void PlaySound(int sound) { }
        public static void PlaySound(int sound, bool loop) { }
        public static void PlaySound(int sound, bool loop, int delay) { }
        public static void StopSound(int sound) { }
    }
}
