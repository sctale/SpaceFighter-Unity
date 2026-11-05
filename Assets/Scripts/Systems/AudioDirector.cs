using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 程序化音频：不依赖任何音频素材，全部在运行时合成。
    /// 分层音轨 = 低频 drone + 琶音 pulse 两层，音量/音高随星区与 Boss 阶段变化；
    /// 音效 = 射击 / 受击 / 爆炸 / 拾取 / 招式预告，各自短波形。
    /// </summary>
    public class AudioDirector : MonoBehaviour
    {
        public static AudioDirector Instance;

        const int Rate = 44100;

        AudioSource _drone, _pulse, _sfx;
        AudioClip _shot, _hit, _explode, _pickup, _warn;
        float _shotCooldown;

        void Awake()
        {
            Instance = this;

            _drone = MakeSource("Drone", 0.16f);
            _pulse = MakeSource("Pulse", 0.10f);
            _sfx = MakeSource("Sfx", 0.55f);

            _drone.clip = BuildDrone();
            _drone.loop = true;
            _drone.Play();

            _pulse.clip = BuildArp();
            _pulse.loop = true;
            _pulse.Play();

            _shot = BuildBlip(880f, 0.06f, 0.35f);
            _hit = BuildNoise(0.18f, 5f);
            _explode = BuildNoise(0.42f, 2.2f);
            _pickup = BuildChime();
            _warn = BuildSweep(280f, 620f, 0.28f);

            // 应用上次保存的静音：以前只在按开关时调用 SetMuted，重启后已保存的静音状态等于没生效
            SetMuted(GameRoot.Instance != null && GameRoot.Instance.Muted);
        }

        AudioSource MakeSource(string name, float vol)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.volume = vol;
            return s;
        }

        void Update()
        {
            var root = GameRoot.Instance;
            if (root == null) return;

            var dir = Director.Instance;
            int zone = dir != null ? dir.Zone : 1;
            bool boss = dir != null && dir.Boss != null;
            bool paused = root.State != GameState.Playing;

            // 星区推进 → 音乐变亮变快；Boss 战 → 更暗更低（对应原版"Boss 战切换更暗调式"）
            _drone.pitch = boss ? 0.72f : Mathf.Clamp(0.9f + zone * 0.02f, 0.9f, 1.12f);
            _pulse.pitch = boss ? 0.82f : Mathf.Clamp(1f + zone * 0.025f, 1f, 1.28f);
            _drone.volume = paused ? 0.04f : boss ? 0.22f : 0.16f;
            _pulse.volume = paused ? 0.02f : boss ? 0.14f : 0.10f;

            if (_shotCooldown > 0f) _shotCooldown -= Time.unscaledDeltaTime;
        }

        public void PlayShot()
        {
            if (_muted || _shotCooldown > 0f) return;
            _shotCooldown = 0.05f;
            _sfx.PlayOneShot(_shot, 0.28f);
        }

        public void PlayHit() => _sfx.PlayOneShot(_hit, 0.8f);
        public void PlayExplode() => _sfx.PlayOneShot(_explode, 0.5f);
        public void PlayPickup() => _sfx.PlayOneShot(_pickup, 0.7f);
        public void PlayWarn() => _sfx.PlayOneShot(_warn, 0.6f);

        /// <summary>冲刺：短促上扬音（复用警告音的高音版）。</summary>
        public void PlayDash()
        {
            if (_sfx == null) return;
            _sfx.PlayOneShot(_shot, 0.55f);
        }

        /// <summary>换线：清脆的一下 UI 音（复用拾取音）。</summary>
        public void PlaySwitch()
        {
            if (_sfx == null) return;
            _sfx.PlayOneShot(_pickup, 0.5f);
        }

        bool _muted;

        public void SetMuted(bool muted)
        {
            _muted = muted;
            // 「音乐与音效」是一个开关，音乐两层也必须一起静，否则关掉后 BGM 还在响
            if (_sfx != null) _sfx.mute = muted;
            if (_drone != null) _drone.mute = muted;
            if (_pulse != null) _pulse.mute = muted;
        }

        // ---- 波形合成 ----

        static AudioClip BuildDrone()
        {
            const float dur = 4f;
            int n = (int)(Rate * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float lfo = 1f + 0.12f * Mathf.Sin(2f * Mathf.PI * 0.25f * t);
                data[i] = (Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.6f
                         + Mathf.Sin(2f * Mathf.PI * 82.5f * t) * 0.3f) * 0.35f * lfo;
            }
            return FromData("drone", data);
        }

        static AudioClip BuildArp()
        {
            const float dur = 4f;
            int n = (int)(Rate * dur);
            var data = new float[n];
            float[] scale = { 220f, 277.18f, 329.63f, 440f, 329.63f, 277.18f, 246.94f, 220f };
            int step = n / 16;
            for (int i = 0; i < n; i++)
            {
                int s = (i / step) % scale.Length;
                float local = (i % step) / (float)Rate;
                float env = Mathf.Exp(-local * 9f);
                data[i] = Mathf.Sin(2f * Mathf.PI * scale[s] * (i / (float)Rate)) * env * 0.22f;
            }
            return FromData("arp", data);
        }

        static AudioClip BuildBlip(float freq, float dur, float gain)
        {
            int n = (int)(Rate * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * 60f);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * gain;
            }
            return FromData("blip", data);
        }

        static AudioClip BuildNoise(float dur, float decay)
        {
            int n = (int)(Rate * dur);
            var data = new float[n];
            var rnd = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * decay);
                float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
                data[i] = (noise * 0.7f + Mathf.Sin(2f * Mathf.PI * 90f * t) * 0.3f) * env * 0.6f;
            }
            return FromData("noise", data);
        }

        static AudioClip BuildChime()
        {
            const float dur = 0.5f;
            int n = (int)(Rate * dur);
            var data = new float[n];
            float[] notes = { 660f, 880f, 1320f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float v = 0f;
                for (int k = 0; k < notes.Length; k++)
                {
                    float delay = k * 0.07f;
                    if (t < delay) continue;
                    v += Mathf.Sin(2f * Mathf.PI * notes[k] * (t - delay)) * Mathf.Exp(-(t - delay) * 7f);
                }
                data[i] = v * 0.22f;
            }
            return FromData("chime", data);
        }

        static AudioClip BuildSweep(float f0, float f1, float dur)
        {
            int n = (int)(Rate * dur);
            var data = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(f0, f1, t / dur);
                phase += 2f * Mathf.PI * f / Rate;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur));
                data[i] = Mathf.Sin(phase) * env * 0.4f;
            }
            return FromData("sweep", data);
        }

        static AudioClip FromData(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
