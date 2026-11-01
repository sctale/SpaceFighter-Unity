using UnityEngine;

namespace AuroraRift
{
    public enum GameState { Menu, Playing, Paused, Dead }

    public class RunStats
    {
        public int Kills, Graze, Hits, BossKills;
        public int MaxCombo, Novas, Phases, Breaks;
        public float Time;
        // 本星区内的擦弹/受击，用于击破结算的「无伤」与擦弹奖励
        public int SectorGraze, SectorHits;
    }

    /// <summary>
    /// 全局根：状态机、分数、连锁倍率、教学提示、暂停与重开。整个场景由代码搭建，
    /// 不依赖 .unity 里手工拖拽的引用，保证命令行可以直接构建。
    /// </summary>
    public class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance;

        public PlayerShip Player { get; private set; }
        public GameState State { get; private set; } = GameState.Menu;
        public float Score { get; private set; }
        public int Best { get; private set; }
        public Difficulty Difficulty { get; private set; }
        public RunStats Stats { get; private set; } = new RunStats();

        // ---- 连锁（game.js:74-76,977-979）----
        public int Combo { get; private set; }
        float _comboTimer;
        public float ComboRatio => Mathf.Clamp01(_comboTimer / GameConfig.ComboWindow);
        public int Multiplier => 1 + Mathf.Min(GameConfig.ComboMaxMult, Combo / GameConfig.ComboStep);

        /// <summary>命令行 -zone N：直接从指定星区开局（用于验证天空与难度曲线）。</summary>
        public int StartZone { get; private set; } = 1;

        public string[] LastClearLines { get; private set; }
        public float LastClearAt { get; private set; } = -99f;

        public bool LowFx { get; private set; }
        public bool HighContrast { get; private set; }
        public bool Muted { get; private set; }

        /// <summary>公告（原版 ui.announce）：标题 + 副标题 + 时长 + 是否警示色 + 是否贴底。</summary>
        public string AnnounceTitle { get; private set; }
        public string AnnounceSub { get; private set; }
        public bool AnnounceAlert { get; private set; }
        public bool AnnounceBottom { get; private set; }
        public float AnnounceAt { get; private set; } = -99f;
        public float AnnounceDur { get; private set; } = 2f;
        public string Toast { get; private set; }
        public float ToastAt { get; private set; } = -99f;
        public string Hint { get; private set; }
        public float HintAt { get; private set; } = -99f;

        int _hintIndex;
        bool _touchLayout;

        // 教学时间轴（game.js:281-292），Boss 战期间暂停计时
        static readonly (float t, string desktop, string touch)[] Hints =
        {
            (2.4f,  "拖动 驾驶战机 · 自动开火", "拖动 驾驶战机 · 自动开火"),
            (8.4f,  "贴近弹幕边缘 擦弹 · 积攒 NOVA", "贴近弹幕边缘 擦弹 · 积攒 NOVA"),
            (15.6f, "NOVA 就绪后按 X 释放星爆", "NOVA 就绪后点击 星爆"),
            (22.5f, "SPACE 冲刺穿越弹幕 · 短暂无敌", "危险时点 冲刺 · 短暂无敌穿越弹幕"),
            (31f,   "拾取 棱镜 / 蜂群 解锁武器线 · 各自升到 LV.5", "拾取 棱镜 / 蜂群 解锁武器线 · 各自升到 LV.5"),
            (40f,   "武器线打空会 失谐 · 排气到 满 才重新就绪，按 1/2/3 换线攒 共鸣",
                    "武器线打空会 失谐 · 排气到 满 才重新就绪，点武器胶囊换线攒 共鸣"),
            (46f,   "青色 换线弹箱 · 立刻切到蓄能最满的武器线并回充，过关必掉",
                    "青色 换线弹箱 · 立刻切到蓄能最满的武器线并回充，过关必掉"),
            (50f,   "按住 SHIFT 精准移动 · 弹道收束、判定点显形", "按住 精准 键慢速移动 · 弹道收束、判定点显形"),
            (56f,   "SPACE 穿过弹幕触发 相位 · 额外星爆充能", "冲刺 穿过弹幕触发 相位 · 额外星爆充能"),
            (63f,   "青色弧段 装甲 · 金色箭头 急速 · 玫红虚线 爆裂", "青色弧段 装甲 · 金色箭头 急速 · 玫红虚线 爆裂"),
        };

        /// <summary>原版 ui.js:469 announce(title, sub, {alert, duration, bottom})。</summary>
        public void Announce(string title, string sub = null, float dur = 2f, bool alert = false, bool bottom = false)
        {
            AnnounceTitle = title;
            AnnounceSub = sub;
            AnnounceDur = dur;
            AnnounceAlert = alert;
            AnnounceBottom = bottom;
            AnnounceAt = Time.unscaledTime;
        }

        public void ShowBanner(string text) => Announce(text, null, 1.6f);
        public void ShowToast(string text) { Toast = text; ToastAt = Time.unscaledTime; }
        void ShowHint(string text) { Hint = text; HintAt = Time.unscaledTime; }

        public void ShowClear(string[] lines)
        {
            LastClearLines = lines;
            LastClearAt = Time.unscaledTime;
        }

        /// <summary>评级完全按网页版 ui.js:555-557：分数或星区任一达标即可。</summary>
        public int Grade
        {
            get
            {
                int sector = Director.Instance != null ? Director.Instance.Zone : 1;
                int s = Mathf.RoundToInt(Score);
                if (s >= GameConfig.GradeS || sector >= GameConfig.GradeSectorS) return 4;
                if (s >= GameConfig.GradeA || sector >= GameConfig.GradeSectorA) return 3;
                if (s >= GameConfig.GradeB || sector >= GameConfig.GradeSectorB) return 2;
                return 1;
            }
        }

        public string GradeText => Grade switch { 4 => "S", 3 => "A", 2 => "B", _ => "C" };

        /// <summary>距上一档还差多少分（S 档返回 0）。</summary>
        public float NextGradeGap
        {
            get
            {
                int s = Mathf.RoundToInt(Score);
                float next = Grade switch { 1 => GameConfig.GradeB, 2 => GameConfig.GradeA, 3 => GameConfig.GradeS, _ => s };
                return Mathf.Max(0f, next - s);
            }
        }

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            Best = PlayerPrefs.GetInt("aurora.best", 0);
            Difficulty = Difficulties.ById(PlayerPrefs.GetString("aurora.diff", "standard"));
            LowFx = PlayerPrefs.GetInt("aurora.lowfx", 0) == 1;
            HighContrast = PlayerPrefs.GetInt("aurora.contrast", 0) == 1;
            Muted = PlayerPrefs.GetInt("aurora.muted", 0) == 1;
            _touchLayout = SystemInfo.deviceType == DeviceType.Handheld || Input.touchSupported;

            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-zone" && int.TryParse(args[i + 1], out int z))
                    StartZone = Mathf.Clamp(z, 1, 10);
        }

        public void AttachPlayer(PlayerShip p) => Player = p;

        /// <summary>开始菜单里点难度。</summary>
        public void SetDifficulty(Difficulty d)
        {
            Difficulty = d;
            PlayerPrefs.SetString("aurora.diff", d.Id);
            PlayerPrefs.Save();
        }

        public void StartRun()
        {
            if (State == GameState.Playing) return;
            State = GameState.Playing;
            Time.timeScale = 1f;
            _hintIndex = 0;
            var dir = Director.Instance;
            if (dir != null)
                Announce("SECTOR " + dir.Zone.ToString("00"),
                    dir.ZoneName + " // 巨构 " + dir.BossName + " 接近", 2.2f, false, true);
            else
                ShowToast("敌机信号接近");
        }

        public void AddScore(float v) => Score += v;

        /// <summary>击杀计分：基础分 × 连锁倍率 × 难度倍率。</summary>
        public int AddKilledScore(float baseScore)
        {
            int points = Mathf.RoundToInt(baseScore * Multiplier * Difficulty.ScoreMul);
            Score += points;
            return points;
        }

        public void RegisterGraze()
        {
            Stats.Graze++; Stats.SectorGraze++;
            Score += 4 * Multiplier;
        }

        public void RegisterPhase()
        {
            Stats.Phases++;
            Score += GameConfig.PhaseScorePerBullet * Multiplier;
        }

        public void RegisterNova() { Stats.Novas++; }
        public void RegisterBreak() { Stats.Breaks++; }

        public void RegisterHullHit()
        {
            Stats.Hits++; Stats.SectorHits++;
            Combo = 0; _comboTimer = 0f;
        }

        public void RegisterKill()
        {
            Combo++;
            _comboTimer = GameConfig.ComboWindow;
            if (Combo > Stats.MaxCombo) Stats.MaxCombo = Combo;
        }

        public void ClearEnemyBullets(bool scoreThem)
        {
            var pool = BulletPool.Instance;
            if (pool == null) return;
            int n = 0;
            for (int i = pool.Active.Count - 1; i >= 0; i--)
            {
                var b = pool.Active[i];
                if (b == null || b.Team != Team.Enemy) continue;
                pool.Release(b);
                n++;
            }
            if (scoreThem && n > 0) Score += n * GameConfig.ClearScorePerBullet;
        }

        public void OnPlayerDead()
        {
            State = GameState.Dead;
            Time.timeScale = 1f;      // 结算页要能点，不用 timeScale=0
            if (Score > Best)
            {
                Best = Mathf.RoundToInt(Score);
                PlayerPrefs.SetInt("aurora.best", Best);
                PlayerPrefs.Save();
            }
        }

        /// <summary>返回基地（开始菜单），不回退到上一局。</summary>
        public void BackToMenu()
        {
            Time.timeScale = 1f;
            GameBootstrap.Rebuild(false);
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            // 不走 LoadScene：世界全是代码自举的，重载场景会让 DontDestroyOnLoad
            // 的引导对象活着但不再 Awake，结果重开=黑屏。直接拆树重建。
            GameBootstrap.Rebuild();
        }

        void Update()
        {
            Controls.Layout();
            Controls.Tick();

            if (State == GameState.Playing)
            {
                Stats.Time += Time.unscaledDeltaTime;
                TickCombo(Time.unscaledDeltaTime);
                TickHints();
            }

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) TogglePause();
            if (State == GameState.Menu || State == GameState.Paused) TickMenuKeys();

            if (State == GameState.Dead && (Input.GetKeyDown(KeyCode.R))) Restart();
        }

        void TickCombo(float dt)
        {
            if (_comboTimer > 0f)
            {
                _comboTimer -= dt;
                if (_comboTimer <= 0f) Combo = 0;
            }
        }

        void TickHints()
        {
            if (Director.Instance != null && Director.Instance.Boss != null) return;   // Boss 战暂停教学
            while (_hintIndex < Hints.Length && Stats.Time >= Hints[_hintIndex].t)
            {
                var h = Hints[_hintIndex];
                ShowHint(_touchLayout ? h.touch : h.desktop);
                _hintIndex++;
            }
        }

        void TickMenuKeys()
        {
            if (Input.GetKeyDown(KeyCode.F1)) SetDifficulty(Difficulties.Cruise);
            if (Input.GetKeyDown(KeyCode.F2)) SetDifficulty(Difficulties.Standard);
            if (Input.GetKeyDown(KeyCode.F3)) SetDifficulty(Difficulties.Abyss);
            if (Input.GetKeyDown(KeyCode.F6)) ToggleLowFx();
            if (Input.GetKeyDown(KeyCode.F7)) ToggleHighContrast();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (State == GameState.Menu) StartRun();
                else if (State == GameState.Paused) TogglePause();
            }
        }

        public void ToggleLowFx()
        {
            LowFx = !LowFx;
            PlayerPrefs.SetInt("aurora.lowfx", LowFx ? 1 : 0);
            PlayerPrefs.Save();
            ShowToast(LowFx ? "轻柔特效：开" : "轻柔特效：关");
        }

        public void ToggleHighContrast()
        {
            HighContrast = !HighContrast;
            PlayerPrefs.SetInt("aurora.contrast", HighContrast ? 1 : 0);
            PlayerPrefs.Save();
            ShowToast(HighContrast ? "高对比：开（敌弹白圈）" : "高对比：关");
        }

        public void ToggleMute()
        {
            Muted = !Muted;
            PlayerPrefs.SetInt("aurora.muted", Muted ? 1 : 0);
            PlayerPrefs.Save();
            AudioDirector.Instance?.SetMuted(Muted);
        }

        public void TogglePause()
        {
            if (State == GameState.Playing) { State = GameState.Paused; }
            else if (State == GameState.Paused) { State = GameState.Playing; }
        }

        /// <summary>
        /// 原版 game.js:162：页面隐藏且正在打就自动暂停；core.js:124 失焦时还会清掉
        /// 按键与指针状态。手机上没有这两条的话，切出去再回来要么在后台挨打，
        /// 要么拖拽粘在一根已经不存在的手指上。
        /// </summary>
        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            if (State == GameState.Playing) TogglePause();
            Player?.ClearInputState();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) return;
            if (State == GameState.Playing) TogglePause();
            Player?.ClearInputState();
        }
    }
}
