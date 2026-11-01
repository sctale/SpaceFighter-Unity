using System.Collections.Generic;
using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 关卡导演：按星区撒波次、维护敌人表、做玩家子弹→敌机判定、掉落与保底。
    /// 敌机种类随星区解锁（原版 10 星区循环的简化版规则）。
    /// </summary>
    public class Director : MonoBehaviour
    {
        public static Director Instance;

        readonly List<EnemyShip> _enemies = new List<EnemyShip>();
        readonly List<CrateKind> _recentDrops = new List<CrateKind>();
        Transform _root, _crateRoot;

        public int Zone { get; private set; } = 1;
        public int WaveInZone { get; private set; }
        public int TotalZones => 10;
        public float ZoneProgress { get; private set; }
        public BossController Boss { get; private set; }
        public Difficulty Diff => GameRoot.Instance != null ? GameRoot.Instance.Difficulty : Difficulties.Standard;
        public float EnemyBulletSpeed => 3.6f * Diff.BulletSpeed;

        /// <summary>原版「5 关一档弹幕强度」：每 5 个星区提升一档（影响齐射数与刷怪间隔）。</summary>
        public int DensityTier => 1 + (Zone - 1) / 5;

        float _spawnTimer;
        int _waveKills, _waveTarget;

        static readonly string[] ZoneNames =
        {
            "巨行星", "环带", "极光", "裂隙", "岩带", "恒星", "双月", "彗星", "光纱", "核心弧环"
        };

        public string ZoneName => ZoneNames[(Zone - 1) % ZoneNames.Length];
        public string WaveLabel => $"星区 {Zone}/10 · {ZoneName} · 波次 {WaveInZone}/3";
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
            BeginWave();

            // 调试验收：-boss 直接进 Boss 战，方便截图检查三台机体
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-boss") >= 0)
                SpawnBoss();
        }

        void BeginWave()
        {
            WaveInZone++;
            _waveKills = 0;
            _waveTarget = 6 + Zone * 2 + (DensityTier - 1) * 2;
            _spawnTimer = 0.6f;
            ZoneProgress = 0f;
            if (WaveInZone >= 3) SpawnBoss();
        }

        void SpawnBoss()
        {
            var go = new GameObject("Boss");
            go.transform.SetParent(_root, false);
            Boss = go.AddComponent<BossController>();
            Boss.Setup(Zone, Diff);
        }

        void Update()
        {
            var root = GameRoot.Instance;
            if (root == null || root.State != GameState.Playing) return;

            _spawnTimer -= Time.deltaTime;
            if (_spawnTimer <= 0f && _enemies.Count < 8 && Boss == null)
            {
                SpawnOne();
                float baseGap = Mathf.Max(0.25f, 1.05f - Zone * 0.05f);
                _spawnTimer = baseGap * 1.15f * Diff.EnemyRate * (0.7f + Random.value * 0.6f);
            }

            CollidePlayerBullets();
            ZoneProgress = _waveTarget > 0 ? Mathf.Clamp01((float)_waveKills / _waveTarget) : 0f;

            if (Boss == null && _waveKills >= _waveTarget)
            {
                if (WaveInZone >= 3)
                {
                    Zone++;
                    WaveInZone = 0;
                    root.AddScore(500f * Zone);
                }
                BeginWave();
            }
        }

        static EnemyKind PickKind(int zone)
        {
            // 随星区逐步解锁；权重按"种类越多、普通机越少"
            var pool = new List<EnemyKind> { EnemyKind.Drifter, EnemyKind.Wasp };
            if (zone >= 2) { pool.Add(EnemyKind.Splitter); pool.Add(EnemyKind.Lancer); }
            if (zone >= 3) { pool.Add(EnemyKind.Rock); pool.Add(EnemyKind.Orbiter); }
            if (zone >= 4) pool.Add(EnemyKind.Sniper);
            if (zone >= 5) pool.Add(EnemyKind.Minekeeper);
            if (zone >= 6) pool.Add(EnemyKind.Sidewinder);
            if (zone >= 7) pool.Add(EnemyKind.Carrier);
            if (zone >= 8) pool.Add(EnemyKind.Bulwark);
            return pool[Random.Range(0, pool.Count)];
        }

        void SpawnOne()
        {
            var kind = PickKind(Zone);
            var go = new GameObject("Enemy_" + kind);
            go.transform.SetParent(_root, false);
            float x = Random.Range(-GameConfig.PlayHalfWidth + 0.8f, GameConfig.PlayHalfWidth - 0.8f);
            go.transform.position = new Vector3(x, GameConfig.OrthoSizeMin - 1.2f, 0f);
            var e = go.AddComponent<EnemyShip>();
            e.Setup(kind, Zone, Diff);
            _enemies.Add(e);
        }

        public void SpawnShardling(Vector2 pos)
        {
            var go = new GameObject("Enemy_Shardling");
            go.transform.SetParent(_root, false);
            go.transform.position = pos + Vector2.down * 0.2f;
            var e = go.AddComponent<EnemyShip>();
            e.Setup(EnemyKind.Shardling, Zone, Diff);
            _enemies.Add(e);
        }

        void CollidePlayerBullets()
        {
            var pool = BulletPool.Instance;
            if (pool == null) return;

            for (int bi = pool.Active.Count - 1; bi >= 0; bi--)
            {
                var b = pool.Active[bi];
                if (b == null || b.Team != Team.Player) continue;

                for (int ei = _enemies.Count - 1; ei >= 0; ei--)
                {
                    var e = _enemies[ei];
                    if (e == null) continue;
                    if (Vector2.Distance(b.transform.position, e.transform.position) <= b.Radius + e.Radius)
                    {
                        bool killed = e.TakeDamage(b.Damage);
                        if (killed) _waveKills++;
                        if (!b.Piercing) { pool.Release(b); break; }
                    }
                }

                if (Boss != null && b != null && b.gameObject.activeSelf)
                {
                    if (Vector2.Distance(b.transform.position, Boss.transform.position) <= b.Radius + Boss.HitRadius)
                    {
                        Boss.TakeDamage(b.Damage);
                        if (!b.Piercing) pool.Release(b);
                    }
                }
            }
        }

        public void OnEnemyKilled(EnemyShip e)
        {
            RollDrop(e);
            Despawn(e);
        }

        /// <summary>掉落 + 保底：连续 3 次没出「缺失武器 / 僚机」时，第 4 次强制补出。</summary>
        void RollDrop(EnemyShip e)
        {
            var p = GameRoot.Instance?.Player;
            if (p == null) return;

            bool needDrone = p.DroneCount < 2;
            bool needWeapon = p.Weapons.Active.Level < 5;

            if (Random.value > e.Def.DropChance)
            {
                if (_recentDrops.Count >= 3 && (needDrone || needWeapon))
                {
                    SpawnCrate(transform.position, needDrone ? CrateKind.Drone : CrateKind.WeaponLevel);
                    _recentDrops.Clear();
                }
                return;
            }

            CrateKind kind;
            if (_recentDrops.Count >= 3 && needDrone) kind = CrateKind.Drone;
            else if (_recentDrops.Count >= 3 && needWeapon) kind = CrateKind.WeaponLevel;
            else
            {
                float r = Random.value;
                if (r < 0.34f) kind = CrateKind.WeaponLevel;
                else if (r < 0.58f) kind = CrateKind.Drone;
                else if (r < 0.82f) kind = CrateKind.Shield;
                else kind = CrateKind.Charge;
            }

            SpawnCrate(e.transform.position, kind);
            _recentDrops.Add(kind);
            if (_recentDrops.Count > 3) _recentDrops.RemoveAt(0);
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
            if (kind == CrateKind.WeaponLevel || kind == CrateKind.Drone) _recentDrops.Clear();
        }

        public void Despawn(EnemyShip e)
        {
            if (e == null) return;
            _enemies.Remove(e);
            Destroy(e.gameObject);
        }

        /// <summary>调试用：直接跳到下一星区（F5）。</summary>
        public void AdvanceZone()
        {
            Zone++;
            WaveInZone = 0;
            BeginWave();
        }

        public void OnBossDefeated()
        {
            Boss = null;
            _waveKills = _waveTarget;
        }

        public Transform NearestEnemy(Vector3 from)
        {
            Transform best = null;
            float bestD = float.MaxValue;
            foreach (var e in _enemies)
            {
                if (e == null) continue;
                float d = (e.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = e.transform; }
            }
            if (Boss != null)
            {
                float d = (Boss.transform.position - from).sqrMagnitude;
                if (d < bestD) best = Boss.transform;
            }
            return best;
        }

        /// <summary>NOVA 爆发：清掉范围内敌弹、对敌人与 Boss 造成伤害。</summary>
        public void NovaBlast(Vector3 center, float radius, float damage)
        {
            var pool = BulletPool.Instance;
            if (pool != null)
            {
                for (int i = pool.Active.Count - 1; i >= 0; i--)
                {
                    var b = pool.Active[i];
                    if (b == null || b.Team != Team.Enemy) continue;
                    if (Vector2.Distance(b.transform.position, center) <= radius) pool.Release(b);
                }
            }
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                if (e == null) continue;
                if (Vector2.Distance(e.transform.position, center) <= radius && e.TakeDamage(damage)) _waveKills++;
            }
            if (Boss != null && Vector2.Distance(Boss.transform.position, center) <= radius + Boss.HitRadius)
                Boss.TakeDamage(damage);
        }
    }
}
