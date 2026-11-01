using UnityEngine;

namespace AuroraRift
{
    public enum GameState { Playing, Paused, Dead }

    public class RunStats
    {
        public int Kills, Graze, BossKills, Hits;
        public float Time;
    }

    /// <summary>
    /// 全局根：状态机、分数、暂停、重开。整个场景由代码搭建，
    /// 不依赖 .unity 里手工拖拽的引用，保证 CLI 可以直接构建。
    /// </summary>
    public class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance;

        public PlayerShip Player { get; private set; }
        public GameState State { get; private set; } = GameState.Playing;
        public float Score { get; private set; }
        public int Best { get; private set; }
        public Difficulty Difficulty { get; private set; }
        public RunStats Stats { get; private set; } = new RunStats();

        /// <summary>命令行 -zone N：直接从指定星区开局（用于验证天空与难度曲线）。</summary>
        public int StartZone { get; private set; } = 1;

        /// <summary>Boss 击破后弹出 5 秒的结算明细。</summary>
        public string[] LastClearLines { get; private set; }
        public float LastClearAt { get; private set; } = -99f;

        public bool LowFx { get; private set; }
        public bool HighContrast { get; private set; }

        public string Banner { get; private set; }
        public float BannerAt { get; private set; } = -99f;

        public void ShowBanner(string text) { Banner = text; BannerAt = Time.unscaledTime; }

        public void ShowClear(string[] lines)
        {
            LastClearLines = lines;
            LastClearAt = Time.unscaledTime;
        }

        /// <summary>0=未达标 1=C 2=B 3=A 4=S；按分数 + 击破 + 擦弹综合。</summary>
        public int Grade
        {
            get
            {
                float s = Score + Stats.BossKills * 1500f + Stats.Graze * 6f - Stats.Hits * 400f;
                if (s >= 14000f) return 4;
                if (s >= 9000f) return 3;
                if (s >= 5000f) return 2;
                if (s >= 2000f) return 1;
                return 0;
            }
        }

        public string GradeText => Grade switch { 4 => "S", 3 => "A", 2 => "B", 1 => "C", _ => "D" };

        /// <summary>距上一档还差多少分（S 档返回 0）。</summary>
        public float NextGradeGap
        {
            get
            {
                float s = Score + Stats.BossKills * 1500f + Stats.Graze * 6f - Stats.Hits * 400f;
                float next = Grade switch { 0 => 2000f, 1 => 5000f, 2 => 9000f, 3 => 14000f, _ => s };
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

            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-zone" && int.TryParse(args[i + 1], out int z))
                    StartZone = Mathf.Clamp(z, 1, 10);
        }

        public void AttachPlayer(PlayerShip p) => Player = p;

        public void SetDifficulty(Difficulty d)
        {
            Difficulty = d;
            PlayerPrefs.SetString("aurora.diff", d.Id);
            PlayerPrefs.Save();
        }

        public void AddScore(float v) => Score += v * Difficulty.ScoreMul;

        public void OnPlayerDead()
        {
            State = GameState.Dead;
            if (Score > Best)
            {
                Best = Mathf.RoundToInt(Score);
                PlayerPrefs.SetInt("aurora.best", Best);
                PlayerPrefs.Save();
            }
            Time.timeScale = 0f;
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        void Update()
        {
            if (State == GameState.Playing) Stats.Time += Time.unscaledDeltaTime;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P))
                TogglePause();

            // 难度切换：F1 巡航 / F2 标准 / F3 深渊（立即生效并记忆）
            if (Input.GetKeyDown(KeyCode.F1)) SetDifficulty(Difficulties.Cruise);
            if (Input.GetKeyDown(KeyCode.F2)) SetDifficulty(Difficulties.Standard);
            if (Input.GetKeyDown(KeyCode.F3)) SetDifficulty(Difficulties.Abyss);
            if (Input.GetKeyDown(KeyCode.F5)) Director.Instance?.AdvanceZone();
            if (Input.GetKeyDown(KeyCode.F6)) ToggleLowFx();
            if (Input.GetKeyDown(KeyCode.F7)) ToggleHighContrast();

            if (State == GameState.Dead && (Input.GetKeyDown(KeyCode.R) || Input.GetMouseButtonDown(0)))
                Restart();
        }

        public void ToggleLowFx()
        {
            LowFx = !LowFx;
            PlayerPrefs.SetInt("aurora.lowfx", LowFx ? 1 : 0);
            PlayerPrefs.Save();
            ShowBanner(LowFx ? "轻柔特效：开" : "轻柔特效：关");
        }

        public void ToggleHighContrast()
        {
            HighContrast = !HighContrast;
            PlayerPrefs.SetInt("aurora.contrast", HighContrast ? 1 : 0);
            PlayerPrefs.Save();
            ShowBanner(HighContrast ? "高对比：开（敌弹白圈）" : "高对比：关");
        }

        public void TogglePause()
        {
            if (State == GameState.Dead) return;
            State = State == GameState.Playing ? GameState.Paused : GameState.Playing;
            Time.timeScale = State == GameState.Paused ? 0f : 1f;
        }
    }
}
