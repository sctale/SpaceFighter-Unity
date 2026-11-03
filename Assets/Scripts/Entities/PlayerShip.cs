using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 玩家：触屏抓取偏移驾驶、键盘移动、Shift/精准环精准、冲刺与相位、
    /// 护盾→力场→舰体的受击顺序、擦弹、星爆与超频。
    /// 判定用自算圆，不挂 Collider——弹幕游戏里物理引擎只会带来抖动。
    /// 数值全部对齐网页版 entities.js / game.js（见 GameConfig 注释）。
    /// </summary>
    public class PlayerShip : MonoBehaviour
    {
        public int Hull = GameConfig.StartLives;
        public int Shields = GameConfig.StartShields;
        public float Nova;
        /// <summary>无敌剩余秒数。出生就给原版那 1.6 秒（entities.js:388），不再用单独的 spawnGrace。</summary>
        public float Invuln = GameConfig.InvulnOnSpawn;
        public float ReflectT;            // 棱镜力场（补给箱给的 8 秒，不是手动技能）
        public float Overdrive;           // 星爆后的超频窗口
        public float DashT, DashCd;
        public bool FocusHeld;            // 精准模式

        public WeaponSystem Weapons { get; private set; }
        public bool Alive => Hull > 0;
        public bool Dashing => DashT > 0f;
        public int DroneCount
        {
            get { int n = 0; foreach (var d in _drones) if (d != null) n++; return n; }
        }

        SpriteRenderer _hull, _shieldRing, _glow, _focus, _flameL, _flameR;
        Transform _visual, _gunMount;
        string _artLine = "";
        float _bank;
        readonly DroneShip[] _drones = new DroneShip[GameConfig.MaxDrones];
        Vector2 _target, _dragOffset;
        bool _dragging;
        int _fingerId = -1;
        Vector2 _lastMove = new Vector2(0f, -1f);
        float _swapIdle;

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
            _glow.sprite = ShipArt.Get("bullet_glow_soft", 1.55f) ?? RuntimeSprites.Disc(64);
            _glow.color = new Color(GameConfig.Teal.r, GameConfig.Teal.g, GameConfig.Teal.b, 0.16f);
            _glow.sortingOrder = 6;

            var focusGo = new GameObject("FocusDot");
            focusGo.transform.SetParent(transform, false);
            _focus = focusGo.AddComponent<SpriteRenderer>();
            _focus.sprite = RuntimeSprites.Disc(16);
            _focus.color = Color.white;
            _focus.sortingOrder = 12;
            focusGo.transform.localScale = Vector3.one * 0.11f;
            _focus.enabled = false;

            var ringGo = new GameObject("ShieldRing");
            ringGo.transform.SetParent(transform, false);
            _shieldRing = ringGo.AddComponent<SpriteRenderer>();
            _shieldRing.sprite = ShipArt.Get("bullet_shield", 1.34f) ?? RuntimeSprites.Ring(96);
            _shieldRing.color = new Color(1f, 1f, 1f, 0.35f);
            _shieldRing.sortingOrder = 7;

            var mount = new GameObject("GunMount");
            mount.transform.SetParent(transform, false);
            mount.transform.localPosition = new Vector3(0f, 0.48f * 0.5f, 0f);
            _gunMount = mount.transform;

            ApplyLineArt("pulse");

            Weapons = gameObject.AddComponent<WeaponSystem>();
            Weapons.Bind(this, _gunMount);
            for (int i = 0; i < GameConfig.StartDrones; i++) AddDrone(i == 0 ? "gunner" : "lance");
            gameObject.AddComponent<ThrusterTrail>().Bind(this);
            _target = transform.position;
        }

        SpriteRenderer MakeFlame(int side, int order)
        {
            var go = new GameObject(side < 0 ? "FlameL" : "FlameR");
            go.transform.SetParent(_visual, false);
            go.transform.localPosition = new Vector3(side * 0.118f, -0.39f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ShipArt.Get("bullet_flame", 0.40f) ?? RuntimeSprites.Disc(16);
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>机体随当前武器线换涂装（三种预渲染配色）。</summary>
        void ApplyLineArt(string id)
        {
            var line = GameConfig.Line(id);
            var sp = ShipArt.Get("player_" + id, 1.08f);
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
        }

        /// <summary>
        /// 原版开局没有僚机：entities.js:385 是 this.drones = []，僚机只能从补给箱拿。
        /// 之前这里白送 gunner + lance，等于开局火力 ×1.5、把 drone 掉落的期望价值打没，
        /// 还让开始界面停了两架没人管的飞机（真机截图里那架"多出来的敌机"就是它们）。
        /// </summary>
        public DroneShip AddDrone(string kind)
        {
            if (DroneCount >= GameConfig.MaxDrones) return null;
            int slot = 0;
            for (int i = 0; i < _drones.Length; i++)
            {
                if (_drones[i] != null) continue;
                slot = i;
                break;
            }
            var go = new GameObject("Drone" + slot);
            var d = go.AddComponent<DroneShip>();
            d.Bind(this, slot, kind);
            _drones[slot] = d;
            return d;
        }

        /// <summary>补给箱：补回一架失联僚机（最多 3 架）。</summary>
        public void RestoreDrone()
        {
            // 优先补一个当前没有的种类（原版 pickDrone 的 unique 偏好）：
            // 以前这里传 null，Bind 一律落回 gunner，三个挂点永远是同一种僚机。
            var used = new System.Collections.Generic.HashSet<string>();
            foreach (var d0 in _drones) if (d0 != null) used.Add(d0.Kind);
            string pick = GameConfig.PickDroneKind(used, Random.Range(0, 4));

            for (int i = 0; i < _drones.Length; i++)
            {
                if (_drones[i] != null) continue;
                var go = new GameObject("Drone" + i);
                var d = go.AddComponent<DroneShip>();
                d.Bind(this, i, pick);
                _drones[i] = d;
                return;
            }
        }

        /// <summary>挨一发舰体伤害只炸掉一架僚机（原版 applyHitPenalty 的行为）。</summary>
        public void DestroyOneDrone()
        {
            for (int i = _drones.Length - 1; i >= 0; i--)
            {
                if (_drones[i] == null) continue;
                _drones[i].Explode();
                _drones[i] = null;
                return;
            }
        }

        /// <summary>
        /// 切后台/失焦时调用：清掉触屏拖拽与精准按住状态。
        /// 不然回到前台后 _fingerId 还指着那根早已抬起的手指，船会跟着不动。
        /// </summary>
        public void ClearInputState()
        {
            _fingerId = -1;
            _dragging = false;
            FocusHeld = false;
        }

        void Update()
        {
            var g = GameRoot.Instance;
            if (g != null && g.State != GameState.Playing)
            {
                // 菜单/暂停/结算时冻住机体，但控件仍要能点（Hud 会自己处理）
                UpdateVisuals();
                return;
            }
            float dt = Time.deltaTime;
            if (Invuln > 0f) Invuln -= dt;
            if (DashCd > 0f) DashCd = Mathf.Max(0f, DashCd - dt);
            if (DashT > 0f) DashT -= dt;
            if (ReflectT > 0f) ReflectT = Mathf.Max(0f, ReflectT - dt);
            if (Overdrive > 0f)
            {
                Overdrive -= dt;
                if (Overdrive < 0f) Overdrive = 0f;
            }
            // 星核心自己会慢慢回，打架只是回得更快
            if (Overdrive <= 0f && Alive) Nova = Mathf.Min(100f, Nova + GameConfig.NovaRegenPerSecond * dt);
            _swapIdle += dt;

            ReadInput();
            Move(dt);
            TickWeapons(dt);
            TickReflect();
            CheckGrazeAndHits();
            UpdateVisuals();
        }

        public float SwapIdle => _swapIdle;

        /// <summary>换线成功后由 WeaponSystem 调用：停留计时归零，下一次换线要重新攒够 1.2 秒。</summary>
        public void ResetSwapIdle() { _swapIdle = 0f; }

        void ReadInput()
        {
            // ---- 触屏：单指锁 + 控件优先，避免第二根手指抢方向盘 ----
            bool touchUsed = false;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.touches[i];
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    if (_fingerId == t.fingerId) { _fingerId = -1; _dragging = false; }
                    continue;
                }
                if (_fingerId < 0 && t.phase == TouchPhase.Began)
                {
                    if (Controls.ConsumePress(t.position)) { _fingerId = -1; touchUsed = true; continue; }
                    _fingerId = t.fingerId;
                    BeginDrag(t.position);
                }
                else if (_fingerId == t.fingerId)
                {
                    UpdateDrag(t.position);
                }
                touchUsed = true;
            }
            FocusHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                        || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                        || Controls.PrecisionHeld;

            if (!touchUsed)
            {
                // ---- 桌面：鼠标在窗口内即跟随（与网页版一致），按下不改变跟随方式 ----
                if (Input.GetMouseButtonDown(0))
                {
                    if (Controls.ConsumePress(Input.mousePosition)) _dragging = false;
                    else BeginDrag(Input.mousePosition);
                }
                if (Input.GetMouseButton(0) && _dragging) UpdateDrag(Input.mousePosition);
                if (Input.GetMouseButtonUp(0)) _dragging = false;
                if (!_dragging)
                {
                    Vector2 mp = Input.mousePosition;
                    if (mp.x >= 0 && mp.y >= 0 && mp.x <= Screen.width && mp.y <= Screen.height && Camera.main != null)
                    {
                        Vector2 wp = Camera.main.ScreenToWorldPoint(mp);
                        _target = new Vector2(wp.x, Mathf.Clamp(wp.y, GameView.MoveBottomY, GameView.MoveTopY));
                        _dragging = false;
                    }
                }

                if (Input.GetKeyDown(KeyCode.Alpha1)) Weapons.SwitchTo("pulse");
                if (Input.GetKeyDown(KeyCode.Alpha2)) Weapons.SwitchTo("prism");
                if (Input.GetKeyDown(KeyCode.Alpha3)) Weapons.SwitchTo("swarm");
                // 原版 q/e/f 都映到同一个 weapon 动作 = 循环换线（core.js:114）
                if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E)
                    || Input.GetKeyDown(KeyCode.F)) Weapons.CycleWeapon();
                // 与网页版一致：空格冲刺、X/J 星爆（不是空格）
                if (Input.GetKeyDown(KeyCode.Space)) TryDash();
                if (Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.J)) TryNova();
            }
            Weapons.Precision = FocusHeld;
        }

        void BeginDrag(Vector2 screenPos)
        {
            if (Camera.main == null) return;
            Vector2 ship = transform.position;
            Vector2 pt = Camera.main.ScreenToWorldPoint(screenPos);
            _dragging = true;
            // 抓取偏移：允许把机体“抬”到指尖上方，拇指不会挡住飞船
            float dx = Mathf.Clamp(ship.x - pt.x, -GameConfig.DragMaxLateralPx * GameConfig.Px,
                                   GameConfig.DragMaxLateralPx * GameConfig.Px);
            float dy = Mathf.Clamp(ship.y - pt.y, -GameConfig.DragDropPx * GameConfig.Px,
                                   GameConfig.DragLiftPx * GameConfig.Px);
            _dragOffset = new Vector2(dx, dy);
        }

        void UpdateDrag(Vector2 screenPos)
        {
            if (Camera.main == null || !_dragging) return;
            Vector2 pt = Camera.main.ScreenToWorldPoint(screenPos);
            _target = new Vector2(pt.x + _dragOffset.x, pt.y + _dragOffset.y);
        }

        void Move(float dt)
        {
            float speed = FocusHeld ? GameConfig.PlayerFocusSpeed : GameConfig.PlayerSpeed;
            Vector2 prev = transform.position;
            Vector2 cur = prev;

            var kb = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (kb.sqrMagnitude > 0.01f)
            {
                _dragging = false;
                cur += kb.normalized * speed * dt;
                _lastMove = kb.normalized;
            }
            else if (_dragging)
            {
                // 跟手：1-exp(-30dt)，不是定速插值
                float k = 1f - Mathf.Exp(-GameConfig.DragFollowRate * dt);
                Vector2 want = new Vector2(Mathf.Clamp(_target.x, -GameView.MoveHalfWidth, GameView.MoveHalfWidth),
                                           Mathf.Clamp(_target.y, GameView.MoveBottomY, GameView.MoveTopY));
                cur = Vector2.Lerp(cur, want, k);
            }

            if (DashT > 0f)
            {
                cur += _dashDir * GameConfig.DashSpeed * dt;
                _lastMove = _dashDir;
            }

            cur.x = Mathf.Clamp(cur.x, -GameView.MoveHalfWidth, GameView.MoveHalfWidth);
            cur.y = Mathf.Clamp(cur.y, GameView.MoveBottomY, GameView.MoveTopY);
            transform.position = cur;

            Vector2 moved = cur - prev;
            if (moved.sqrMagnitude > 1e-6f) _lastMove = moved.normalized;

            float vx = moved.x / Mathf.Max(1e-4f, dt);
            float wantBank = Mathf.Clamp(vx / Mathf.Max(1f, speed), -1f, 1f);
            _bank = Mathf.Lerp(_bank, wantBank, 1f - Mathf.Exp(-9f * dt));
            if (_visual != null)
            {
                _visual.localRotation = Quaternion.Euler(0f, 0f, -_bank * 7f);
                _visual.localScale = new Vector3(1f - Mathf.Abs(_bank) * 0.13f, 1f, 1f);
            }
        }

        Vector2 _dashDir = Vector2.up;

        /// <summary>冲刺：0.17 秒位移、1.7 秒冷却、期间无敌 0.36 秒，可穿弹幕触发「相位」。</summary>
        public bool TryDash()
        {
            if (DashCd > 0f || !Alive) return false;
            Vector2 dir = _lastMove;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;   // 没动就统一向前冲
            _dashDir = dir.normalized;
            DashT = GameConfig.DashTime;
            DashCd = GameConfig.DashCooldown;
            Invuln = Mathf.Max(Invuln, GameConfig.DashInvuln);
            AudioDirector.Instance?.PlayDash();
            Impact.Spawn(transform.position, new Color(0.41f, 0.96f, 0.86f, 0.9f), 0.55f, 0.22f);
            CameraShake.Instance?.Shake(0.05f, 0.12f);
            return true;
        }

        public void TryNova()
        {
            if (Nova < 100f || Overdrive > 0f || !Alive) return;
            Nova = 0f;
            Overdrive = GameConfig.OverdriveTime;
            Invuln = Mathf.Max(Invuln, GameConfig.InvulnAfterNova);
            // 原版星爆：100ms 停顿 + 0.4 秒 0.45 倍速，清屏那一下要有「炸开再落地」的节奏
            GameRoot.Instance?.Hitstop(GameConfig.FreezeNova);
            GameRoot.Instance?.Slowmo(GameConfig.SlowNovaDur, GameConfig.SlowNovaScale);
            Director.Instance?.NovaBlast(transform.position);
        }

        /// <summary>Boss 换阶段时给玩家一段明确的安全窗口。</summary>
        public void GrantInvulnBossPhase() => Invuln = Mathf.Max(Invuln, GameConfig.InvulnOnBossPhase);

        /// <summary>换星区时的保护帧。</summary>
        public void GrantInvulnZoneChange() => Invuln = Mathf.Max(Invuln, GameConfig.InvulnOnZoneChange);

        /// <summary>补给箱：棱镜力场 8 秒，吞噬圈内敌弹并抵消一次致命伤害。</summary>
        public void GrantReflect()
        {
            ReflectT = Mathf.Max(ReflectT, GameConfig.ReflectDuration);
            AudioDirector.Instance?.PlayPickup();
        }

        void TickWeapons(float dt)
        {
            bool firing = Alive;
            Weapons.Tick(dt);
            if (firing) Weapons.TryFire();
            if (Weapons.ActiveId != _artLine) ApplyLineArt(Weapons.ActiveId);
        }

        /// <summary>力场圈内吞弹（每颗回充一点星爆）。</summary>
        void TickReflect()
        {
            if (ReflectT <= 0f) return;
            var pool = BulletPool.Instance;
            if (pool == null) return;
            float r = 50f * GameConfig.Px;
            for (int i = pool.Active.Count - 1; i >= 0; i--)
            {
                var b = pool.Active[i];
                if (b == null || b.Team != Team.Enemy) continue;
                if (Vector2.Distance(b.transform.position, transform.position) <= r)
                {
                    pool.Release(b);
                    AddNova(GameConfig.NovaOnPointDefense * 0.42f);
                }
            }
        }

        void CheckGrazeAndHits()
        {
            var pool = BulletPool.Instance;
            if (pool == null) return;
            // 原版的判定中心不是机身坐标，而是沿屏幕下方偏 2px（game.js:885/899/929）
            Vector2 me = (Vector2)transform.position - Vector2.up * (2f * GameConfig.Px);
            bool safe = Invuln > 0f;

            // 相位：只有冲刺位移的那 0.17 秒里穿过弹幕才算（game.js:873）。
            // 之前写的是「任何无敌帧」，等于受创 2.8 秒、星爆 0.9 秒、护盾 1.3 秒
            // 都能站着刷星爆和分数，把擦弹经济整个吹走了。
            if (DashT > 0f)
            {
                for (int i = pool.Active.Count - 1; i >= 0; i--)
                {
                    var b = pool.Active[i];
                    if (b == null || b.Team != Team.Enemy || b.Phased) continue;
                    if (Vector2.Distance(me, b.transform.position) <= GameConfig.PhaseRadius)
                    {
                        b.Phased = true;
                        AddNova(GameConfig.PhaseNovaPerBullet);
                        GameRoot.Instance?.RegisterPhase();
                    }
                }
            }

            if (safe) return;

            for (int i = pool.Active.Count - 1; i >= 0; i--)
            {
                var b = pool.Active[i];
                if (b == null || b.Team != Team.Enemy) continue;

                // 线段扫掠（原版 segClosest）：点位判定会让 330px/s 的针弹在低帧率下
                // 从 4.6px 判定圈边上整颗穿过去，实测就是"明明贴着弹却不掉血"。
                float d2 = SegClosestSq(b.PrevPos, b.transform.position, me);
                float hitR = GameConfig.PlayerHitRadius + b.Radius;
                float grazeR = GameConfig.PlayerGrazeRadius + b.Radius;

                if (!b.Grazed && d2 < grazeR * grazeR && d2 > hitR * hitR)
                {
                    b.Grazed = true;
                    AddNova(GameConfig.NovaOnGraze);
                    GameRoot.Instance?.RegisterGraze();
                }
                else if (d2 < hitR * hitR)
                {
                    // 先收掉这颗弹再结算受击：TakeHit 里的力场/复活会顺手清屏，
                    // 那之后 pool.Active 已经不是原来那个列表了（真机上就是这里
                    // 抛的 ArgumentOutOfRangeException），所以本帧到此为止。
                    pool.Release(b);
                    TakeHit();
                    return;
                }
            }
        }

        /// <summary>点到线段的最近距离平方（原版 game.js 的 segClosest）。</summary>
        static float SegClosestSq(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-9f) return (p - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            Vector2 c = a + ab * t;
            return (p - c).sqrMagnitude;
        }

        /// <summary>受击顺序：力场 → 护盾 → 舰体（与网页版 playerHit 一致）。</summary>
        public void TakeHit()
        {
            if (Invuln > 0f || !Alive) return;
            AudioDirector.Instance?.PlayHit();
            var root = GameRoot.Instance;

            if (ReflectT > 0f)
            {
                ReflectT = 0f;
                Invuln = Mathf.Max(Invuln, GameConfig.InvulnAfterReflect);
                AddNovaRaw(GameConfig.NovaOnReflectSave);
                CameraShake.Instance?.Shake(0.16f, 0.24f);
                Impact.Spawn(transform.position, new Color(1f, 0.82f, 0.48f, 0.95f), 0.9f, 0.3f);
                root?.ClearEnemyBullets(true);
                root?.Hitstop(GameConfig.FreezeHit);            // 原版这里钉 50ms
                root?.Announce("FIELD", "棱镜力场 · 抵消一次致命", 1.4f);
                return;
            }

            if (Shields > 0)
            {
                Shields--;
                Invuln = Mathf.Max(Invuln, GameConfig.InvulnAfterShield);
                AddNovaRaw(GameConfig.NovaOnShieldBreak);
                CameraShake.Instance?.Shake(0.11f, 0.2f);
                Impact.Spawn(transform.position, new Color(0.61f, 0.55f, 1f, 0.9f), 0.7f, 0.25f);
                root?.Hitstop(GameConfig.FreezeHit);
                return;
            }

            Hull--;
            Invuln = Mathf.Max(Invuln, GameConfig.InvulnAfterHullHit);
            AddNovaRaw(GameConfig.NovaOnHullHit);
            Weapons.OnPlayerHit();
            DestroyOneDrone();
            if (root != null)
            {
                root.RegisterHullHit();
                root.ClearEnemyBullets(true);
                // 挨一发舰体重击要「慢下来给人看清」：0.3 秒 0.5 倍速 + 50ms 停顿（game.js:1257-1258）
                root.Slowmo(GameConfig.SlowHitDur, GameConfig.SlowHitScale);
                root.Hitstop(GameConfig.FreezeHit);
                if (Hull > 0) root.Announce("HULL BREACH", "舰体受损 · 武器降级", 1.5f, true);
                else { root.Slowmo(GameConfig.SlowDeathDur, GameConfig.SlowDeathScale); root.Announce("SIGNAL LOST", "战机信号中断", 1.8f, true); }
            }
            CameraShake.Instance?.Shake(0.24f, 0.34f);
            Impact.Spawn(transform.position, new Color(1f, 0.44f, 0.57f, 0.95f), 1.1f, 0.34f);
            // 掉命后回到出生点，避免停在弹幕里
            if (Hull > 0)
            {
                transform.position = new Vector3(0f, GameView.RespawnY, 0f);
                _target = transform.position;
                _dragging = false;
                _fingerId = -1;
            }
            if (Hull <= 0) root?.OnPlayerDead();
        }

        public void AddNova(float v)
        {
            if (Overdrive > 0f) return;
            var st = Weapons != null ? Weapons.Active : null;
            float mul = st != null && st.Detuned
                ? GameConfig.DetunedNovaMul
                : 1f + (Weapons != null ? Weapons.Resonance * GameConfig.ResonancePerStackCharge : 0f);
            float diffMul = GameRoot.Instance != null ? GameRoot.Instance.Difficulty.Nova : 1f;
            Nova = Mathf.Clamp(Nova + v * mul * diffMul, 0f, 100f);
        }

        /// <summary>挨打的充能不受线状态影响（原版 pain-charging）。</summary>
        public void AddNovaRaw(float v)
        {
            if (Overdrive > 0f) return;
            Nova = Mathf.Clamp(Nova + v, 0f, 100f);
        }

        void UpdateVisuals()
        {
            var line = GameConfig.Line(Weapons.ActiveId);
            bool blink = Invuln > 0f && Mathf.Repeat(Invuln, 0.2f) < 0.1f;
            _hull.color = new Color(1f, 1f, 1f, blink ? 0.35f : 1f);

            float detune = Weapons.Active != null && Weapons.Active.Detuned ? 0.55f : 1f;
            float od = Overdrive > 0f ? 1.35f : 1f;
            float flick = (0.78f + Mathf.Sin(Time.time * 38f) * 0.15f + Random.value * 0.10f) * detune * od;
            var flameCol = Color.Lerp(line.Tint, Color.white, Overdrive > 0f ? 0.6f : 0.25f);
            if (_flameL != null)
            {
                _flameL.transform.localScale = new Vector3(0.85f + Random.value * 0.10f, flick, 1f);
                _flameL.color = flameCol;
            }
            if (_flameR != null)
            {
                _flameR.transform.localScale = new Vector3(0.85f + Random.value * 0.10f, flick, 1f);
                _flameR.color = flameCol;
            }

            bool field = ReflectT > 0f;
            _shieldRing.enabled = Shields > 0 || field || Overdrive > 0f;
            if (field)
            {
                _shieldRing.transform.localScale = Vector3.one * (1.38f + Mathf.Sin(Time.time * 3f) * 0.05f);
                _shieldRing.color = new Color(line.Tint.r, line.Tint.g, line.Tint.b, 0.34f);
            }
            else if (Overdrive > 0f)
            {
                _shieldRing.transform.localScale = Vector3.one * (1.22f + Mathf.Sin(Time.time * 9f) * 0.05f);
                _shieldRing.color = new Color(1f, 0.82f, 0.45f, 0.30f);
            }
            else
            {
                _shieldRing.transform.localScale = Vector3.one * (1f + 0.02f * Mathf.Sin(Time.time * 2.4f));
                _shieldRing.color = new Color(line.Tint.r, line.Tint.g, line.Tint.b, 0.08f + 0.05f * Shields);
            }
            _shieldRing.transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * 8f);

            if (_focus != null) _focus.enabled = FocusHeld;
            if (_glow != null)
                _glow.color = new Color(line.Tint.r, line.Tint.g, line.Tint.b,
                    (Overdrive > 0f ? 0.24f : 0.11f) + Mathf.Sin(Time.time * 3f) * 0.03f);
        }
    }
}
