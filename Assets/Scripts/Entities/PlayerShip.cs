using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 玩家：拖动/键盘移动、Shift 精准、护盾、擦弹、受伤掉级、NOVA 充能。
    /// 判定用自算圆，不挂 Collider——弹幕游戏里物理引擎只会带来抖动。
    /// </summary>
    public class PlayerShip : MonoBehaviour
    {
        public int Hull = 3;
        public int Shields = 1;
        public float Nova;
        public float Invuln;
        public float FieldTimer;          // 棱镜力场剩余时间
        public bool FieldUsedFatal;       // 力场是否已经抵消过一次致命

        public WeaponSystem Weapons { get; private set; }
        public bool Alive => Hull > 0;

        SpriteRenderer _hull, _shieldRing, _glow, _focus, _flameL, _flameR;
        Transform _visual, _gunMount;
        string _artLine = "";
        float _bank;
        readonly DroneShip[] _drones = new DroneShip[3];
        Vector3 _dragOrigin;
        Vector2 _target;
        bool _dragging;
        bool _precision;
        public bool TouchPrecision { get; private set; }
        float _spawnGrace = 2.2f;

        void Awake()
        {
            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);

            var hullGo = new GameObject("Hull");
            hullGo.transform.SetParent(_visual, false);
            _hull = hullGo.AddComponent<SpriteRenderer>();
            _hull.sortingOrder = 10;

            _flameL = MakeFlame(-1, 9);
            _flameR = MakeFlame(1, 9);

            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(transform, false);
            _glow = glowGo.AddComponent<SpriteRenderer>();
            _glow.sprite = ShipArt.Get("bullet_glow_soft", 2.9f) ?? RuntimeSprites.Disc(64);
            _glow.color = new Color(GameConfig.Teal.r, GameConfig.Teal.g, GameConfig.Teal.b, 0.22f);
            _glow.sortingOrder = 6;

            // 精准模式判定点
            var focusGo = new GameObject("FocusDot");
            focusGo.transform.SetParent(transform, false);
            _focus = focusGo.AddComponent<SpriteRenderer>();
            _focus.sprite = RuntimeSprites.Disc(16);
            _focus.color = Color.white;
            _focus.sortingOrder = 12;
            focusGo.transform.localScale = Vector3.one * 0.16f;
            _focus.enabled = false;

            var ringGo = new GameObject("ShieldRing");
            ringGo.transform.SetParent(transform, false);
            _shieldRing = ringGo.AddComponent<SpriteRenderer>();
            _shieldRing.sprite = ShipArt.Get("bullet_shield", 2.2f) ?? RuntimeSprites.Ring(96);
            _shieldRing.color = new Color(1f, 1f, 1f, 0.35f);
            _shieldRing.sortingOrder = 7;
            ringGo.transform.localScale = Vector3.one;

            var mount = new GameObject("GunMount");
            mount.transform.SetParent(transform, false);
            mount.transform.localPosition = new Vector3(0f, 0.32f, 0f);
            _gunMount = mount.transform;

            ApplyLineArt("pulse");

            Weapons = gameObject.AddComponent<WeaponSystem>();
            Weapons.Bind(this, _gunMount);
            SpawnDrones();
            gameObject.AddComponent<ThrusterTrail>().Bind(this);
            _target = transform.position;
        }

        SpriteRenderer MakeFlame(int side, int order)
        {
            var go = new GameObject(side < 0 ? "FlameL" : "FlameR");
            go.transform.SetParent(_visual, false);
            go.transform.localPosition = new Vector3(side * 0.129f, -0.43f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ShipArt.Get("bullet_flame", 0.44f) ?? RuntimeSprites.Disc(16);
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>机体随当前武器线换涂装（三种预渲染配色）。</summary>
        void ApplyLineArt(string id)
        {
            var line = GameConfig.Line(id);
            var sp = ShipArt.Get("player_" + id, 1.46f);
            if (sp != null)
            {
                _hull.sprite = sp;
                _hull.color = Color.white;
            }
            else
            {
                _hull.sprite = RuntimeSprites.Arrow(64);
                _hull.color = line.Tint;
            }
            _artLine = id;
            if (_flameL != null) _flameL.color = Color.Lerp(line.Tint, Color.white, 0.35f);
            if (_flameR != null) _flameR.color = Color.Lerp(line.Tint, Color.white, 0.35f);
        }

        /// <summary>开局的翼位僚机（原版最多 3 架，初始给 2 架）。</summary>
        void SpawnDrones()
        {
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Drone" + i);
                var d = go.AddComponent<DroneShip>();
                d.Bind(this, i);
                _drones[i] = d;
            }
        }

        public void TryNova()
        {
            if (Nova < 100f || !Alive) return;
            Nova = 0f;
            Director.Instance?.NovaBlast(transform.position, 6.2f, 26f);
        }

        public int DroneCount
        {
            get
            {
                int n = 0;
                foreach (var d in _drones) if (d != null) n++;
                return n;
            }
        }

        /// <summary>补给箱：补回一架失联僚机（最多 2 架）。</summary>
        public void RestoreDrone()
        {
            for (int i = 0; i < 2; i++)
            {
                if (_drones[i] != null) continue;
                var go = new GameObject("Drone" + i);
                var d = go.AddComponent<DroneShip>();
                d.Bind(this, i);
                _drones[i] = d;
                return;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (_spawnGrace > 0f) _spawnGrace -= dt;
            if (Invuln > 0f) Invuln -= dt;

            ReadInput();
            Move(dt);
            TickWeapons(dt);
            TickField(dt);
            CheckGrazeAndHits();
            UpdateVisuals();
        }

        void ReadInput()
        {
            // 触摸/鼠标：按下即把飞船拉到指针处（原版是跟手拖动）
            // 触屏：右下角为「精准」区域，按在那里不移动只收束弹道
            TouchPrecision = false;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.touches[i];
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) continue;
                if (InPrecisionZone(t.position)) TouchPrecision = true;
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (!InPrecisionZone(Input.mousePosition))
                {
                    _dragging = true;
                    _dragOrigin = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                }
            }
            if (Input.GetMouseButtonUp(0)) _dragging = false;

            if (_dragging)
            {
                Vector3 now = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                Vector3 delta = now - _dragOrigin;
                _dragOrigin = now;
                _target += (Vector2)delta;
            }

            var kb = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (kb.sqrMagnitude > 0.01f) _target += kb.normalized * GameConfig.PlayerSpeed * Time.deltaTime;

            if (Input.GetKeyDown(KeyCode.Alpha1)) Weapons.SwitchTo("pulse");
            if (Input.GetKeyDown(KeyCode.Alpha2)) Weapons.SwitchTo("prism");
            if (Input.GetKeyDown(KeyCode.Alpha3)) Weapons.SwitchTo("swarm");
            if (Input.GetKeyDown(KeyCode.Space)) TryNova();
            if (Input.GetKeyDown(KeyCode.Q) || Input.GetMouseButtonDown(1)) TryForceField();
        }

        /// <summary>右下角 28% 的圆形热区：按住 = 精准模式。</summary>
        public static bool InPrecisionZone(Vector2 screenPos)
        {
            float r = Mathf.Min(Screen.width, Screen.height) * 0.14f;
            Vector2 center = new Vector2(Screen.width * 0.84f, Screen.height * 0.16f);
            return Vector2.Distance(screenPos, center) <= r;
        }

        void Move(float dt)
        {
            bool precision = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || TouchPrecision;
            _precision = precision;
            Weapons.Precision = precision;
            float speed = GameConfig.PlayerSpeed * (precision ? GameConfig.PlayerPrecisionSpeedMul : 1f);
            Vector2 prev = transform.position;
            Vector2 cur = transform.position;
            if (_dragging) cur = Vector2.MoveTowards(cur, _target, speed * 2.4f * dt);
            else cur = Vector2.MoveTowards(cur, _target, speed * dt);

            float halfW = GameConfig.PlayHalfWidth - 0.35f;
            float minY = -GameConfig.OrthoSizeMin + 0.6f;
            float maxY = -GameConfig.OrthoSizeMin * GameConfig.PlayerBoundY;
            cur.x = Mathf.Clamp(cur.x, -halfW, halfW);
            cur.y = Mathf.Clamp(cur.y, minY, maxY);
            transform.position = cur;

            // 横向速度 → 侧倾/压缩，静止时回正
            float vx = (cur.x - prev.x) / Mathf.Max(1e-4f, dt);
            float wantBank = Mathf.Clamp(vx / Mathf.Max(1f, speed), -1f, 1f);
            _bank = Mathf.Lerp(_bank, wantBank, 1f - Mathf.Exp(-9f * dt));
            if (_visual != null)
            {
                _visual.localRotation = Quaternion.Euler(0f, 0f, -_bank * 7f);
                _visual.localScale = new Vector3(1f - Mathf.Abs(_bank) * 0.13f, 1f, 1f);
            }
        }

        void TickWeapons(float dt)
        {
            bool firing = Alive; // 原版自动开火
            Weapons.Tick(dt, firing);
            if (firing) Weapons.TryFire();
        }

        /// <summary>棱镜力场：消耗 1 层护盾，8 秒内吞噬附近弹幕，并抵消一次致命伤害。</summary>
        public void TryForceField()
        {
            if (!Alive || Shields <= 0 || FieldTimer > 0f) return;
            Shields--;
            FieldTimer = 8f;
            FieldUsedFatal = false;
            Invuln = Mathf.Max(Invuln, 0.4f);
            AudioDirector.Instance?.PlayPickup();
        }

        /// <summary>力场每帧吞掉范围内的敌弹（同时回充 NOVA）。</summary>
        void TickField(float dt)
        {
            if (FieldTimer <= 0f) return;
            FieldTimer -= dt;
            var pool = BulletPool.Instance;
            if (pool == null) return;
            const float r = 1.9f;
            for (int i = pool.Active.Count - 1; i >= 0; i--)
            {
                var b = pool.Active[i];
                if (b == null || b.Team != Team.Enemy) continue;
                if (Vector2.Distance(b.transform.position, transform.position) <= r)
                {
                    pool.Release(b);
                    AddNova(1.2f);
                }
            }
        }

        void CheckGrazeAndHits()
        {
            var pool = BulletPool.Instance;
            if (pool == null) return;
            Vector2 me = transform.position;

            for (int i = pool.Active.Count - 1; i >= 0; i--)
            {
                var b = pool.Active[i];
                if (b == null || b.Team != Team.Enemy) continue;
                float d = Vector2.Distance(me, b.transform.position);
                if (d <= b.Radius + GameConfig.PlayerHitRadius && Invuln <= 0f && _spawnGrace <= 0f)
                {
                    TakeHit();
                    pool.Release(b);
                }
                else if (d <= b.Radius + GameConfig.PlayerGrazeRadius && b.Life > GameConfig.BulletLifetime - 0.1f)
                {
                    AddNova(2.2f);
                    if (GameRoot.Instance != null) GameRoot.Instance.Stats.Graze++;
                }
            }
        }

        public void TakeHit()
        {
            if (Invuln > 0f) return;
            AudioDirector.Instance?.PlayHit();
            CameraShake.Instance?.Shake(0.16f, 0.28f);
            Impact.Spawn(transform.position, new Color(1f, 0.5f, 0.45f, 0.9f), 0.7f, 0.25f);
            if (Shields > 0)
            {
                Shields--;
                Invuln = GameConfig.InvulnAfterHit;
                return;
            }
            if (FieldTimer > 0f && !FieldUsedFatal)
            {
                // 力场挡下这一次致命伤害
                FieldUsedFatal = true;
                Invuln = GameConfig.InvulnAfterHit;
                AudioDirector.Instance?.PlayHit();
            CameraShake.Instance?.Shake(0.16f, 0.28f);
            Impact.Spawn(transform.position, new Color(1f, 0.5f, 0.45f, 0.9f), 0.7f, 0.25f);
                return;
            }

            Hull--;
            Invuln = GameConfig.InvulnAfterHit;
            Weapons.OnPlayerHit();
            AddNovaRaw(25f);
            if (GameRoot.Instance != null) GameRoot.Instance.Stats.Hits++;
            // 原版规则：被击中时僚机爆炸失联
            foreach (var d in _drones) if (d != null) d.Explode();
            if (Hull <= 0) GameRoot.Instance?.OnPlayerDead();
        }

        public void AddNova(float v)
        {
            float mul = Weapons.Active.Detuned ? 0.45f : 1f + Weapons.Resonance * 0.06f;
            Nova = Mathf.Clamp(Nova + v * mul, 0f, 100f);
        }

        public void AddNovaRaw(float v) => Nova = Mathf.Clamp(Nova + v, 0f, 100f);

        void UpdateVisuals()
        {
            var line = GameConfig.Line(Weapons.ActiveId);
            if (_artLine != Weapons.ActiveId) ApplyLineArt(Weapons.ActiveId);

            bool blink = Invuln > 0f && Mathf.Repeat(Invuln, 0.2f) < 0.1f;
            _hull.color = new Color(1f, 1f, 1f, blink ? 0.35f : 1f);

            // 尾焰抖动（失谐时明显变短）
            float detune = Weapons.Active.Detuned ? 0.55f : 1f;
            float flick = (0.78f + Mathf.Sin(Time.time * 38f) * 0.15f + Random.value * 0.10f) * detune;
            if (_flameL != null)
            {
                _flameL.transform.localScale = new Vector3(0.85f + Random.value * 0.10f, flick, 1f);
                _flameL.transform.localPosition = new Vector3(-0.129f, -0.40f - flick * 0.05f, 0f);
                _flameL.color = Color.Lerp(line.Tint, Color.white, 0.25f);
            }
            if (_flameR != null)
            {
                _flameR.transform.localScale = new Vector3(0.85f + Random.value * 0.10f, flick, 1f);
                _flameR.transform.localPosition = new Vector3(0.129f, -0.40f - flick * 0.05f, 0f);
                _flameR.color = Color.Lerp(line.Tint, Color.white, 0.25f);
            }

            bool field = FieldTimer > 0f;
            _shieldRing.enabled = Shields > 0 || field;
            if (field)
            {
                _shieldRing.transform.localScale = Vector3.one * (2.1f + Mathf.Sin(Time.time * 3f) * 0.08f);
                _shieldRing.color = new Color(line.Tint.r, line.Tint.g, line.Tint.b, 0.30f);
            }
            else
            {
                _shieldRing.transform.localScale = Vector3.one * (1f + 0.02f * Mathf.Sin(Time.time * 2.4f));
                _shieldRing.color = new Color(line.Tint.r, line.Tint.g, line.Tint.b, 0.10f + 0.07f * Shields);
            }
            _shieldRing.transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * 8f);

            if (_focus != null) _focus.enabled = _precision;
            if (_glow != null)
                _glow.color = new Color(line.Tint.r, line.Tint.g, line.Tint.b,
                    0.15f + Mathf.Sin(Time.time * 3f) * 0.04f);
        }
    }
}
