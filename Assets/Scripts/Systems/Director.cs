using System.Collections.Generic;
using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 关卡导演：按星区撒编队、维护敌人表、做玩家子弹/机体接触判定、掉落与保底。
    /// 与网页版对齐的三件事：编队式刷怪（line / arc / pincer / nest）、
    /// 场上上限 42 架、以及「出场公平性」——未合法入屏的敌机不能被锁定或打死。
    /// </summary>
    public class Director : MonoBehaviour
    {
        public static Director Instance;

        readonly List<EnemyShip> _enemies = new List<EnemyShip>();
        Transform _root, _crateRoot;

        public int Zone { get; private set; } = 1;
        public int TotalZones => 10;
        public float ZoneProgress { get; private set; }
        public BossController Boss { get; private set; }
        public Difficulty Diff => GameRoot.Instance != null ? GameRoot.Instance.Difficulty : Difficulties.Standard;

        /// <summary>敌弹速度：原版 min(300, 150×dif) × 难度系数，换算成世界单位。</summary>
        public float EnemyBulletSpeed =>
            Mathf.Min(300f, 150f) * Diff.BulletSpeed * GameConfig.Px;

        /// <summary>原版「5 关一档弹幕强度」：tier = min(3, floor((s-1)/5))。</summary>
        public int DensityTier => Mathf.Min(3, (Zone - 1) / 5);

        float _supplyTimer, _clearTimer, _railProgress, _bossWarn, _clearHold;
        int _pityWeapon, _pityDrone;

        /// <summary>本星区开始时刻（Stats.Time 秒）。原版通关时间奖励按整个星区用时算。</summary>
        public float SectorStartTime { get; private set; }

        // ------------------------------------------------------------ 星区剧本
        // 原版 director.js 的模型：一个星区 = 一段固定剧本（buildScript），
        // 逐条到点触发 → 剧本放完进入 clearing（清场）→ 才出 Boss。
        // 之前 Unity 版是"随机编队 + 杀满 N 只进下一波、三波后出 Boss"，
        // 节奏和敌机构成都和原版对不上，这里改成剧本驱动。
        public enum RunState { Idle, Waves, Clearing, Boss, Intermission }
        public RunState State { get; private set; } = RunState.Idle;

        class Step { public float after; public System.Action run; }
        class PendingSpawn
        {
            public bool pickup; public EnemyKind kind; public CrateKind crate;
            public float xPx, yPx, t; public bool elite;
        }

        readonly List<Step> _script = new List<Step>();
        readonly List<PendingSpawn> _pending = new List<PendingSpawn>();
        int _scriptIndex;
        float _waveTimer;
        bool _midSupply;

        /// <summary>原版进度轨：剧本推进到 86%，清场 96%，Boss 亮终点。</summary>
        public float RailProgress => State switch
        {
            RunState.Waves => Mathf.Min(0.9f, (_script.Count > 0 ? (float)_scriptIndex / _script.Count : 0f) * 0.86f),
            RunState.Clearing => 0.96f,
            RunState.Boss => 1f,
            RunState.Intermission => 1f,
            _ => _railProgress,
        };

        static readonly string[] ZoneNames =
        {
            "巨行星", "环带", "极光", "裂隙", "岩带", "恒星", "双月", "彗星", "光纱", "核心弧环"
        };

        public string ZoneName => ZoneNames[(Zone - 1) % ZoneNames.Length];
        public string BossName => BossMoves.NameZh(BossMoves.BossForZone(Zone));
        /// <summary>
        /// 原版一个星区就是一段剧本（没有"三波"），所以这里报剧本进度而不是波次。
        /// </summary>
        public string WaveLabel
        {
            get
            {
                string tail = State switch
                {
                    RunState.Waves => $"编队 {_scriptIndex}/{_script.Count}",
                    RunState.Clearing => "清场",
                    RunState.Boss => "核心战",
                    _ => "待命",
                };
                return $"星区 {Zone}/10 · {ZoneName} · {tail}";
            }
        }
        public int AliveEnemies => _enemies.Count;

        void Awake()
        {
            Instance = this;
            _root = new GameObject("Enemies").transform;
            _root.SetParent(transform, false);
            _crateRoot = new GameObject("Crates").transform;
            _crateRoot.SetParent(transform, false);
            int start = GameRoot.Instance != null ? GameRoot.Instance.StartZone : 1;
            if (start > 1) Zone = start;
            _supplyTimer = GameConfig.SupplyInterval;
            StartSector(Zone);

            // 调试验收：-boss 直接进 Boss 战，方便截图检查三台机体
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-boss") >= 0)
                SpawnBoss();
        }

        /// <summary>原版 startSector()：换一个星区就重建剧本，计时器 1.2s 后开跑第一条。</summary>
        void StartSector(int sector)
        {
            Zone = Mathf.Clamp(sector, 1, 10);
            _script.Clear();
            _pending.Clear();
            BuildScript(Zone);
            _scriptIndex = 0;
            _waveTimer = 1.2f;
            _clearTimer = 0f;
            _midSupply = false;
            State = RunState.Waves;
            ZoneProgress = 0f;
            _railProgress = 0f;
            _clearHold = 0f;
            SectorStartTime = GameRoot.Instance != null ? GameRoot.Instance.Stats.Time : 0f;
            if (Zone == 1) { _pityWeapon = 0; _pityDrone = 0; }
        }

        void SpawnBoss()
        {
            var go = new GameObject("Boss");
            go.transform.SetParent(_root, false);
            Boss = go.AddComponent<BossController>();
            Boss.Setup(Zone, Diff);
            State = RunState.Boss;
        }

        // ------------------------------------------------------------ 剧本内容
        // 逐条对照 director.js:64 buildScript(s)。after = 上一条放完后隔多久轮到这条。

        void Add(float after, System.Action run) => _script.Add(new Step { after = after, run = run });

        void BuildScript(int s)
        {
            int hard = Mathf.Max(0, s - 1);

            Add(1.2f, () => V(EnemyKind.Drifter, 6 + Mathf.Min(3, hard), 54));
            Add(2.8f, () => { Line(EnemyKind.Drifter, 4 + Mathf.Min(3, hard), 68); QueueSupply(CrateKind.WeaponLevel); });
            Add(2.6f, () => WaspRush(3 + Mathf.Min(2, hard)));
            Add(2.8f, () => { Line(EnemyKind.Drifter, 4, 64); Queue(EnemyKind.Splitter, RefW * 0.5f, -90, 0.6f, false); });
            Add(2.6f, () => { if (s >= 2) Pincer(EnemyKind.Sidewinder, 2); else Line(EnemyKind.Drifter, 3, 120); });
            Add(3.0f, () =>
            {
                Line(EnemyKind.Lancer, 1 + (s >= 3 ? 1 : 0), 130);
                Queue(EnemyKind.Drifter, RefW * 0.2f, -70, 0.35f, false);
                Queue(EnemyKind.Drifter, RefW * 0.8f, -70, 0.5f, false);
            });
            Add(2.6f, () =>
            {
                Queue(EnemyKind.Orbiter, RefW * 0.32f, -80, 0f, false);
                if (s >= 4) Queue(EnemyKind.Orbiter, RefW * 0.68f, -90, 0.4f, false);
                if (s >= 3) Queue(EnemyKind.Minekeeper, RefW * 0.5f, -110, 0.8f, false);
            });
            Add(2.8f, () => Arc(EnemyKind.Splitter, 4));
            Add(2.6f, () =>
            {
                Queue(EnemyKind.Bulwark, RefW * 0.5f, -100, 0f, s >= 5);
                for (int i = 0; i < 2; i++)
                    Queue(EnemyKind.Rock, RefW * (0.22f + i * 0.56f), -60 - i * 40, 0.3f + i * 0.4f, false);
            });
            Add(2.8f, () => { WaspRush(3); Line(EnemyKind.Drifter, 3, 90); });

            if (s >= 2)
            {
                Add(2.8f, () => SniperNest(2));
                Add(3.0f, () =>
                {
                    var types = new[] { EnemyKind.Lancer, EnemyKind.Orbiter, EnemyKind.Splitter, EnemyKind.Sidewinder };
                    Queue(types[(s - 2) % 4], RefW * 0.5f, -100, 0f, true);
                    QueueSupply(s >= 4 ? (s % 2 == 1 ? CrateKind.Prism : CrateKind.Swarm) : CrateKind.Shield);
                });
            }
            if (s >= 3)
            {
                Add(3.0f, () =>
                {
                    Line(EnemyKind.Lancer, 2, 170, s >= 7);
                    V(EnemyKind.Wasp, 4, 52);
                });
                Add(2.8f, () => MineLine(s >= 6 ? 2 : 1));
            }
            if (s >= 4) Add(3.2f, CarrierGroup);
            if (s >= 5)
            {
                Add(3.2f, () =>
                {
                    Line(EnemyKind.Bulwark, 2, 200);
                    WaspRush(4);
                    if (s >= 7) Pincer(EnemyKind.Sidewinder, 2);
                });
            }

            Add(3.0f, () =>
            {
                Line(EnemyKind.Drifter, 5, 58);
                Queue(EnemyKind.Orbiter, RefW * 0.5f, -90, 0.9f, s >= 4);
                for (int i = 0; i < 2; i++)
                    Queue(EnemyKind.Splitter, RefW * (0.3f + i * 0.4f), -70, 1.3f + i * 0.3f, false);
                if (s >= 3) Queue(EnemyKind.Sniper, RefW * (s % 2 == 1 ? 0.18f : 0.82f), -70, 1.7f, false);
            });
            Add(2.6f, () => QueueSupply(CrateKind.Drone));
        }

        const float RefW = 540f;      // 原版画布宽（像素），剧本里的坐标都按它给

        // ------------------------------------------------------------ 排队与生成

        void Queue(EnemyKind kind, float xPx, float yPx, float delay, bool elite)
        {
            _pending.Add(new PendingSpawn
            {
                pickup = false, kind = kind, xPx = xPx, yPx = yPx,
                t = delay, elite = elite || EliteFor(kind),
            });
        }

        void QueueSupply(CrateKind crate) =>
            _pending.Add(new PendingSpawn { pickup = true, crate = crate, t = 0.2f });

        void TickPending(float dt)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                p.t -= dt;
                if (p.t > 0f) continue;
                _pending.RemoveAt(i);
                if (p.pickup) SpawnSupplyNow(p.crate);
                else SpawnAt(p.kind, X(p.xPx), Y(p.yPx), p.elite);
            }
        }

        /// <summary>原版把剧本补给箱直接投在玩家上方 330px、并夹在 16%~52% 高度之间。</summary>
        void SpawnSupplyNow(CrateKind crate)
        {
            var p = GameRoot.Instance?.Player;
            float px = 270f, py = 960f * 0.4f;
            if (p != null)
            {
                // 原版是画布像素（y 向下）：玩家左右 ±140px，上方 330px
                float playerX = p.transform.position.x / GameConfig.Px + RefW * 0.5f;
                float playerDownY = (GameView.HalfHeight - p.transform.position.y) / GameConfig.Px;
                px = playerX + Random.Range(-140f, 140f);
                py = playerDownY - 330f;
            }
            px = Mathf.Clamp(px, 54f, RefW - 54f);
            py = Mathf.Clamp(py, 960f * 0.16f, 960f * 0.52f);
            SpawnCrate(new Vector3(X(px), Y(py)), crate);
        }

        static float X(float px) => (px - RefW * 0.5f) * GameConfig.Px;
        static float Y(float px) => GameView.HalfHeight - px * GameConfig.Px;

        void Update()
        {
            var root = GameRoot.Instance;
            if (root == null || root.State != GameState.Playing) return;

            float dt = Time.deltaTime;

            if (State == RunState.Waves)
            {
                TickPending(dt);
                _waveTimer -= dt;
                if (_waveTimer <= 0f)
                {
                    if (_scriptIndex < _script.Count)
                    {
                        var step = _script[_scriptIndex];
                        _scriptIndex++;
                        step.run();
                        _waveTimer = _scriptIndex < _script.Count ? _script[_scriptIndex].after : 2.4f;
                        // 原版在剧本 55% 处插一箱中期补给（走完整掉落池）
                        if (!_midSupply && _scriptIndex == Mathf.CeilToInt(_script.Count * 0.55f))
                        {
                            _midSupply = true;
                            CollectSupply();
                        }
                    }
                    else
                    {
                        State = RunState.Clearing;
                        _clearTimer = 0f;
                    }
                }

                // 周期补给：首箱 20 秒，之后固定 19 秒一箱（director.js:298-303）
                _supplyTimer -= dt;
                if (_supplyTimer <= 0f)
                {
                    _supplyTimer = GameConfig.SupplyIntervalMin;
                    CollectSupply();
                }
            }
            else if (State == RunState.Intermission)
            {
                // 通关幕间 4.4 秒（game.js:1416）：这段时间是留给玩家收箱子的，不推进剧本
                _clearHold -= dt;
                if (_clearHold <= 0f) AdvanceSector();
            }
            else if (State == RunState.Clearing)
            {
                // 清场：全灭且至少 1 秒才出 Boss，最多等 9.5 秒（防止有飞机卡住不放行）
                TickPending(dt);
                _clearTimer += dt;
                // 原版 startBossWarning(3.0)：亮 3 秒 WARNING，同时把残敌与敌弹全部清掉，
                // Boss 战从干净盘面开始（game.js:1297-1313）。之前只亮公告 2.4 秒、不清场，
                // 残余编队和 Boss 弹幕叠在一起，密度比原版高一截。
                if (_bossWarn > 0f)
                {
                    _bossWarn -= dt;
                    if (_bossWarn <= 0f) SpawnBoss();
                }
                else if ((_enemies.Count == 0 && _clearTimer > 1.0f) || _clearTimer > 9.5f)
                {
                    _bossWarn = GameConfig.BossWarnSeconds;
                    for (int i = _enemies.Count - 1; i >= 0; i--) Despawn(_enemies[i]);
                    GameRoot.Instance?.ClearEnemyBullets(false);
                    GameRoot.Instance?.Announce("WARNING", "巨构信号接近 // MASSIVE SIGNAL", 2.7f, true);
                    AudioDirector.Instance?.PlayWarn();
                }
            }
            else
            {
                TickPending(dt);
            }

            CollidePlayerBullets();
            CollideBodies();
            _railProgress = RailProgress;
            ZoneProgress = RailProgress;
        }

        // ---------------- 编队 ----------------

        // ------------------------------------------------------------ 编队（原版 spawn helpers）

        void V(EnemyKind kind, int n, float spacing, bool elite = false)
        {
            float mid = (n - 1) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float x = RefW * 0.5f + (i - mid) * spacing;
                float y = -50f - Mathf.Abs(i - mid) * 34f;
                Queue(kind, x, y, i * 0.055f, elite);
            }
        }

        void Line(EnemyKind kind, int n, float spacing, bool elite = false)
        {
            float mid = (n - 1) * 0.5f;
            for (int i = 0; i < n; i++)
                Queue(kind, RefW * 0.5f + (i - mid) * spacing, -60f - i * 16f, i * 0.09f, elite);
        }

        void Arc(EnemyKind kind, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float t = n <= 1 ? 0.5f : i / (float)(n - 1);
                Queue(kind, RefW * (0.18f + t * 0.64f), -60f - Mathf.Sin(t * Mathf.PI) * 50f, i * 0.12f, false);
            }
        }

        void WaspRush(int n)
        {
            for (int i = 0; i < n; i++)
            {
                float side = i % 2 == 0 ? 1f : -1f;
                Queue(EnemyKind.Wasp, RefW * 0.5f + side * Random.Range(70f, 150f), -70f - i * 50f, i * 0.35f, false);
            }
        }

        void Pincer(EnemyKind kind, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float dir = i % 2 == 0 ? 1f : -1f;
                float x = dir > 0f ? -46f : RefW + 46f;
                Queue(kind, x, GameView.HalfHeight / GameConfig.Px * 2f * (0.16f + i * 0.07f), i * 0.4f, false);
            }
        }

        void SniperNest(int n)
        {
            for (int i = 0; i < n; i++)
                Queue(EnemyKind.Sniper, RefW * (0.26f + i * 0.48f), -70f - i * 30f, i * 0.45f, false);
        }

        void MineLine(int n)
        {
            for (int i = 0; i < n; i++)
                Queue(EnemyKind.Minekeeper, RefW * (n == 1 ? 0.5f : 0.3f + i * 0.4f), -90f - i * 30f, i * 0.5f, false);
        }

        void CarrierGroup()
        {
            Queue(EnemyKind.Carrier, RefW * 0.5f, -130f, 0f, false);
            Queue(EnemyKind.Drifter, RefW * 0.2f, -60f, 0.6f, false);
            Queue(EnemyKind.Drifter, RefW * 0.8f, -60f, 0.8f, false);
            if (Zone >= 6) Queue(EnemyKind.Sniper, RefW * 0.5f, -170f, 1.4f, false);
        }

        /// <summary>精英由星区门槛决定（原版 director.js：bulwark s≥5、lancer s≥7、orbiter s≥4）。</summary>
        /// <summary>
        /// Unity 独有增强：原版剧本之外不随机出精英（director.js 只在指定条目传 elite:true）。
        /// 想回到纯原版节奏，把 GameConfig.RandomEliteChance 改成 0 即可。
        /// </summary>
        bool EliteFor(EnemyKind kind)
        {
            if (kind == EnemyKind.Rock || kind == EnemyKind.Shardling) return false;
            int gate = kind switch
            {
                EnemyKind.Bulwark => 5,
                EnemyKind.Lancer => 7,
                EnemyKind.Orbiter => 4,
                EnemyKind.Carrier => 8,
                _ => 6,
            };
            return Zone >= gate && Random.value < GameConfig.RandomEliteChance;
        }

        float ClampX(float x) => Mathf.Clamp(x, -GameView.MoveHalfWidth + 0.4f, GameView.MoveHalfWidth - 0.4f);

        void SpawnAt(EnemyKind kind, float x, float y, bool elite)
        {
            if (_enemies.Count >= GameConfig.MaxEnemiesAlive) return;
            var go = new GameObject("Enemy_" + kind);
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(ClampX(x), y, 0f);
            var e = go.AddComponent<EnemyShip>();
            e.Setup(kind, Zone, Diff, elite);
            _enemies.Add(e);
        }

        public void SpawnShardling(Vector2 pos) => SpawnChild(EnemyKind.Shardling, pos, 1f, -1f);

        /// <summary>Boss 的「母舰召唤」：按当前星区放小兵，入场宽限用默认值（原版 {sector} 传参）。</summary>
        public void SpawnEscort(EnemyKind kind, Vector2 pos) => SpawnChild(kind, pos, 1f, -1f);

        /// <summary>
        /// 子系统出生（母舰放的小机、分裂体拆出的幼体）：可以指定血量比例与入场宽限。
        /// 原版 spawnEnemy(...) 之后直接改 child.hp / child.entryDelay（game.js:1034-1042）。
        /// </summary>
        public void SpawnChild(EnemyKind kind, Vector2 pos, float hpFrac = 1f, float entryDelay = -1f)
        {
            if (_enemies.Count >= GameConfig.MaxEnemiesAlive) return;
            var go = new GameObject("Enemy_" + kind);
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(ClampX(pos.x), pos.y, 0f);
            var e = go.AddComponent<EnemyShip>();
            e.Setup(kind, Zone, Diff, false, hpFrac, entryDelay);
            _enemies.Add(e);
        }

        // ---------------- 判定 ----------------

        void CollidePlayerBullets()
        {
            var pool = BulletPool.Instance;
            if (pool == null) return;
            var root = GameRoot.Instance;

            for (int bi = pool.Active.Count - 1; bi >= 0; bi--)
            {
                // 打中 Boss 可能触发换阶段/死亡，那两处都会清屏，pool.Active 会被整体改短；
                // 索引越界就退到当前末尾（和 PlayerShip.CheckGrazeAndHits 同一类坑）。
                if (bi >= pool.Active.Count) { bi = pool.Active.Count; continue; }
                var b = pool.Active[bi];
                if (b == null || b.Team != Team.Player) continue;

                for (int ei = _enemies.Count - 1; ei >= 0; ei--)
                {
                    var e = _enemies[ei];
                    if (e == null || !e.Targetable()) continue;
                    if (Vector2.Distance(b.transform.position, e.transform.position) <= b.Radius + e.Radius)
                    {
                        bool killed = e.TakeDamage(b.Damage);
                        if (!killed && root != null && root.Player != null) root.Player.AddNova(GameConfig.NovaOnHitEnemy);
                        // 原版是一颗弹结算完就继续下一颗，这里不能 return，
                        // 否则同一帧里后面的弹全部不参与判定，DPS 会被凭空砍掉。
                        if (!b.Piercing || --b.PierceLeft < 0) { pool.Release(b); break; }
                    }
                }

                if (Boss != null && b != null && b.gameObject.activeSelf && !Boss.Entering)
                {
                    // 原版是椭圆（game.js:839-840）：rx = size×1.02 + 弹半径，ry = size×0.82 + 弹半径。
                    // 之前用固定半径 1.55 的圆，纵向比原版胖约 34 %，Boss 掉血明显偏快。
                    Vector2 d = (Vector2)b.transform.position - (Vector2)Boss.transform.position;
                    float rx = Boss.HitRx + b.Radius, ry = Boss.HitRy + b.Radius;
                    if (!b.HitBoss && (d.x * d.x) / (rx * rx) + (d.y * d.y) / (ry * ry) < 1f)
                    {
                        // 一颗弹对 Boss 只结算一次：穿透弹会在体内停好几帧，否则等于连击
                        b.HitBoss = true;
                        float dmg = b.Damage * (b.BossBonus > 1f ? b.BossBonus : 1f);
                        Boss.TakeDamage(dmg);
                        if (root != null && root.Player != null) root.Player.AddNova(GameConfig.NovaOnHitBoss);
                        if (!b.Piercing || --b.PierceLeft < 0) pool.Release(b);
                    }
                }
            }
        }

        /// <summary>机体接触：撞到合法入屏的敌机会同归于尽（原版 contact 分支）。</summary>
        void CollideBodies()
        {
            var p = GameRoot.Instance?.Player;
            if (p == null || !p.Alive || p.Invuln > 0f || p.Dashing) return;
            // 原版的接触判定中心比机身低 2px（player.y + 2，画布 y 向下）
            Vector2 me = (Vector2)p.transform.position - Vector2.up * (2f * GameConfig.Px);

            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                if (e == null || e.Dead || !e.Targetable()) continue;
                float rr = e.Radius + 10f * GameConfig.Px;
                if (Vector2.Distance(me, e.transform.position) <= rr)
                {
                    e.TakeDamage(999f);
                    p.TakeHit();
                    return;
                }
            }

            if (Boss != null && !Boss.Dying)
            {
                // 原版：rx = size×0.85 + 8，ry = size×0.7 + 8，三台 size 分别 76/80/82
                float rx = Boss.ContactRx, ry = Boss.ContactRy;
                Vector2 d = (Vector2)Boss.transform.position - me;
                if ((d.x * d.x) / (rx * rx) + ((d.y - 2f * GameConfig.Px) * (d.y - 2f * GameConfig.Px)) / (ry * ry) < 1f)
                    p.TakeHit();
            }
        }

        // ---------------- 掉落与保底 ----------------

        public void OnEnemyKilled(EnemyShip e)
        {
            var root = GameRoot.Instance;
            if (root != null)
            {
                root.RegisterKill();
                int points = root.AddKilledScore(e.Score);
                // 精英/堡垒被击落时钉 25ms（game.js:1015）——大目标要有「被打穿」的顿挫
                if (e.Elite || e.Kind == EnemyKind.Bulwark) root.Hitstop(GameConfig.FreezeKill);
                if (root.Player != null)
                {
                    root.Player.AddNova(e.Elite ? GameConfig.NovaOnEliteKill : GameConfig.NovaOnKill);
                    if (root.Player.Overdrive > 0f)
                        root.Player.Overdrive = Mathf.Min(GameConfig.OverdriveMax, root.Player.Overdrive +
                            (e.Elite ? GameConfig.OverdriveEliteKillExtend : GameConfig.OverdriveKillExtend));
                }
                if (e.Elite || points > 0) root.ShowToast("+" + points);
            }
            RollDrop(e);
            Despawn(e);
        }

        /// <summary>普通掉落表（game.js:1061-1063）：未满级与满级两张权重表。</summary>
        void RollDrop(EnemyShip e)
        {
            var p = GameRoot.Instance?.Player;
            if (p == null) return;

            if (e.Elite)
            {
                SpawnCrate(e.transform.position, PickUpgradeType());
                for (int i = 0; i < 3; i++)
                    SpawnCrate((Vector3)(Vector2)e.transform.position + new Vector3(
                        Random.Range(-20, 20) * GameConfig.Px, Random.Range(-10, 10) * GameConfig.Px, 0f),
                    CrateKind.Gem);
                return;
            }
            // 陨石不进掉落表：80 % 概率直接撒 2 颗宝石（game.js:1047-1053）
            if (e.Kind == EnemyKind.Rock)
            {
                if (Random.value < 0.8f)
                    for (int i = 0; i < 2; i++)
                        SpawnCrate((Vector2)e.transform.position + new Vector2(
                            Random.Range(-10f, 10f) * GameConfig.Px,
                            Random.Range(-8f, 8f) * GameConfig.Px), CrateKind.Gem);
                return;
            }
            if (Random.value > e.Def.DropChance) return;

            bool maxed = p.Weapons.Active.Level >= 5;
            CrateKind type = CrateKind.Gem;
            if (!maxed)
            {
                float[] w = { 38, 17, 13, 6, 6, 5, 4, 11 };   // power shield drone prism swarm reflect life gem
                type = Weighted(w, new[]
                {
                    CrateKind.WeaponLevel, CrateKind.Shield, CrateKind.Drone, CrateKind.Prism,
                    CrateKind.Swarm, CrateKind.Reflect, CrateKind.Life, CrateKind.Gem
                });
            }
            else
            {
                float[] w = { 22, 20, 9, 9, 7, 4, 20, 9 };
                type = Weighted(w, new[]
                {
                    CrateKind.Shield, CrateKind.Drone, CrateKind.Prism, CrateKind.Swarm,
                    CrateKind.Reflect, CrateKind.Life, CrateKind.WeaponLevel, CrateKind.Gem
                });
            }

            SpawnCrate(e.transform.position, type);
            if (type == CrateKind.Gem)
                SpawnCrate((Vector3)(Vector2)e.transform.position + new Vector3(12f * GameConfig.Px, 0f, 0f), CrateKind.Gem);
        }

        CrateKind PickUpgradeType()
        {
            var p = GameRoot.Instance.Player;
            bool missingWeapon = !p.Weapons.Prism.Unlocked || !p.Weapons.Swarm.Unlocked;
            // Unity 独有保底：连着 3 次没掉到缺的武器线/僚机就必补（原版这个函数里没有 pity 分支）
            if (missingWeapon && _pityWeapon >= GameConfig.PityThreshold)
                return !p.Weapons.Prism.Unlocked ? CrateKind.Prism : CrateKind.Swarm;
            if (p.DroneCount < GameConfig.MaxDrones && _pityDrone >= GameConfig.PityThreshold) return CrateKind.Drone;
            // 以下逐条对齐 game.js:1078-1085（原版门槛是 level<5 且 40 %，不是 level<3 无条件）
            if (p.Weapons.Active.Level < 5 && Random.value < 0.4f) return CrateKind.WeaponLevel;
            if (p.Shields < GameConfig.MaxShields && Random.value < 0.45f) return CrateKind.Shield;
            if (p.DroneCount < GameConfig.MaxDrones && Random.value < 0.55f) return CrateKind.Drone;
            if (!p.Weapons.Prism.Unlocked && Random.value < 0.4f) return CrateKind.Prism;
            if (!p.Weapons.Swarm.Unlocked && Random.value < 0.4f) return CrateKind.Swarm;
            if (Random.value < 0.45f) return Random.value < 0.5f ? CrateKind.Prism : CrateKind.Reflect;
            CrateKind[] pool = { CrateKind.WeaponLevel, CrateKind.Prism, CrateKind.Shield, CrateKind.Life };
            return pool[Random.Range(0, pool.Length)];
        }

        /// <summary>周期补给（director.js collectSupply），含换线箱与两条保底计数。</summary>
        void CollectSupply()
        {
            var root = GameRoot.Instance;
            var p = root?.Player;
            if (p == null) return;

            bool missingWeapon = !p.Weapons.Prism.Unlocked || !p.Weapons.Swarm.Unlocked;
            CrateKind type;
            if (p.Weapons.Active.Level < 3) type = CrateKind.WeaponLevel;
            else if (missingWeapon && (_pityWeapon >= GameConfig.PityThreshold || Random.value < 0.35f))
                type = !p.Weapons.Prism.Unlocked ? CrateKind.Prism
                     : !p.Weapons.Swarm.Unlocked ? CrateKind.Swarm
                     : (Random.value < 0.5f ? CrateKind.Prism : CrateKind.Swarm);
            else if (p.Weapons.UnlockedCount() >= 2 && p.SwapIdle > GameConfig.SwapIdleForSwapCrate &&
                     Random.value < GameConfig.SwapCrateChance) type = CrateKind.Swap;
            else if (p.DroneCount < GameConfig.MaxDrones && _pityDrone >= GameConfig.PityThreshold) type = CrateKind.Drone;
            else if (p.Shields < GameConfig.MaxShields && Random.value < 0.4f) type = CrateKind.Shield;
            else if (p.DroneCount < GameConfig.MaxDrones && Random.value < 0.35f) type = CrateKind.Drone;
            else if (p.ReflectT <= 0f && Random.value < 0.25f) type = CrateKind.Reflect;
            else if (p.Hull <= 2 && Random.value < 0.3f) type = CrateKind.Life;
            else
            {
                float[] w = { 2, 2, 1, 1, 1, 1, 1 };
                type = Weighted(w, new[]
                {
                    CrateKind.WeaponLevel, CrateKind.WeaponLevel, CrateKind.Shield,
                    CrateKind.Swap, CrateKind.Prism, CrateKind.Swarm, CrateKind.Gems
                });
            }

            if (type == CrateKind.Prism || type == CrateKind.Swarm) _pityWeapon = 0;
            else if (missingWeapon) _pityWeapon++;
            if (type == CrateKind.Drone) _pityDrone = 0;
            else _pityDrone++;
            if (!missingWeapon) _pityWeapon = 0;

            float x = Random.Range(0.38f, 0.62f) * GameConfig.WorldWidth - GameConfig.PlayHalfWidth;
            if (type == CrateKind.Gems)
            {
                for (int i = 0; i < 4; i++)
                    SpawnCrate(new Vector3(Random.Range(-0.9f, 0.9f), GameView.SpawnY + i * 0.2f), CrateKind.Gem);
            }
            else SpawnCrate(new Vector3(x, GameView.SpawnY + 0.2f), type);
        }

        static CrateKind Weighted(float[] weights, CrateKind[] kinds)
        {
            float total = 0f;
            foreach (var w in weights) total += w;
            float roll = Random.value * total;
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= weights[i];
                if (roll <= 0f) return kinds[i];
            }
            return kinds[kinds.Length - 1];
        }

        void SpawnCrate(Vector3 pos, CrateKind kind)
        {
            var go = new GameObject("Crate_" + kind);
            go.transform.SetParent(_crateRoot, false);
            go.transform.position = pos;
            go.AddComponent<SupplyCrate>().Setup(kind);
        }

        public void OnCrateCollected(CrateKind kind)
        {
            if (kind == CrateKind.Prism || kind == CrateKind.Swarm || kind == CrateKind.WeaponLevel) _pityWeapon = 0;
            if (kind == CrateKind.Drone) _pityDrone = 0;
        }

        public void Despawn(EnemyShip e)
        {
            if (e == null) return;
            _enemies.Remove(e);
            Destroy(e.gameObject);
        }

        /// <summary>调试用：直接跳到下一星区（F5）。</summary>
        public void AdvanceZone() => StartSector(Zone + 1);

        /// <summary>
        /// 原版 onBossDefeated（game.js:1392-1436）：结算已经在 Boss.Die() 里按原版公式加过，
        /// 这里负责铺「通关必掉箱」、进 4.4 秒幕间，幕间结束才 advanceSector()。
        /// 之前这里既立刻开下一星区（玩家没时间收箱子），又额外加了一遍 500×(Zone+1) 的分。
        /// </summary>
        public void OnBossDefeated()
        {
            State = RunState.Intermission;
            _clearHold = GameConfig.SectorClearSeconds;
            DeploySectorSalvage();
        }

        /// <summary>通关必掉（game.js:1420-1440）：升级 + 缺的武器线 + 护盾/反射 + 僚机/命 + 换线 + 5 宝石。</summary>
        void DeploySectorSalvage()
        {
            var root = GameRoot.Instance;
            var p = root != null ? root.Player : null;
            if (p == null) return;

            var loot = new List<CrateKind> { CrateKind.WeaponLevel };
            loot.Add(!p.Weapons.Prism.Unlocked ? CrateKind.Prism
                     : !p.Weapons.Swarm.Unlocked ? CrateKind.Swarm
                     : CrateKind.WeaponLevel);
            loot.Add(p.Shields >= 2 ? CrateKind.Reflect : CrateKind.Shield);
            loot.Add(p.DroneCount < GameConfig.MaxDrones ? CrateKind.Drone : CrateKind.Life);
            if (p.Weapons.UnlockedCount() >= 2) loot.Add(CrateKind.Swap);
            if (p.Hull < 3) loot.Add(CrateKind.Life);

            // 摆在玩家上方 240px；原版纵向夹在画布 [0.28H, 0.72H]，换算到 Unity 就是 ±0.44×半高
            float px = p.transform.position.x;
            float py = Mathf.Clamp(p.transform.position.y - 240f * GameConfig.Px,
                                   -0.44f * GameView.HalfHeight, 0.44f * GameView.HalfHeight);
            float gapPx = Mathf.Min(56f, (GameConfig.RefW - 96f) / Mathf.Max(1, loot.Count - 1));
            float halfW = GameView.HalfWidth - 46f * GameConfig.Px;
            for (int i = 0; i < loot.Count; i++)
            {
                float off = (i - (loot.Count - 1) * 0.5f) * gapPx * GameConfig.Px;
                float x = Mathf.Clamp(px + off, -halfW, halfW);
                float arch = Mathf.Abs(i - (loot.Count - 1) * 0.5f) * 14f * GameConfig.Px;
                SpawnCrate(new Vector3(x, py + arch, 0f), loot[i]);
            }
            for (int i = 0; i < 5; i++)
            {
                float gx = Mathf.Clamp(px + Random.Range(-90f, 90f) * GameConfig.Px,
                                       -GameView.HalfWidth + 30f * GameConfig.Px,
                                       GameView.HalfWidth - 30f * GameConfig.Px);
                SpawnCrate(new Vector3(gx, py + Random.Range(-40f, 20f) * GameConfig.Px, 0f), CrateKind.Gem);
            }
        }

        /// <summary>原版 advanceSector（game.js:1452-1467）：清残留与弹幕、给 1.5 秒换区无敌，再开下一档剧本。</summary>
        void AdvanceSector()
        {
            for (int i = _enemies.Count - 1; i >= 0; i--) Despawn(_enemies[i]);
            _enemies.Clear();
            _pending.Clear();
            GameRoot.Instance?.ClearEnemyBullets(false);
            BossLaser.ClearAll();
            var p = GameRoot.Instance != null ? GameRoot.Instance.Player : null;
            if (p != null) p.GrantInvulnZoneChange();
            Boss = null;
            StartSector(Zone + 1);
        }

        public Transform NearestEnemy(Vector3 from)
        {
            Transform best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                if (e == null || !e.Targetable()) continue;
                float d = (e.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = e.transform; }
            }
            if (Boss != null && !Boss.Dying)
            {
                float d = (Boss.transform.position - from).sqrMagnitude;
                if (d < bestD) best = Boss.transform;
            }
            return best;
        }

        /// <summary>NOVA：全屏清弹（每颗 +10 分）、敌机 32 伤害、Boss 6% + 30。</summary>
        public void NovaBlast(Vector3 center)
        {
            var root = GameRoot.Instance;
            if (root == null) return;
            root.RegisterNova();
            root.ClearEnemyBullets(false);
            var pool = BulletPool.Instance;
            root.AddScore(0f);

            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                if (e == null) continue;
                e.TakeDamage(GameConfig.NovaEnemyDamage);
            }
            if (Boss != null && !Boss.Dying) Boss.TakeDamage(Mathf.RoundToInt(Boss.MaxHp * GameConfig.NovaBossDamageFrac) + GameConfig.NovaBossDamageFlat);
            Impact.Spawn(center, new Color(1f, 0.82f, 0.45f, 0.95f), 6.5f, 0.4f);
            CameraShake.Instance?.Shake(0.3f, 0.35f);
        }
    }
}
