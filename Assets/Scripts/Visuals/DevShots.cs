using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 仅开发期：命令行 -shots 定时截图、-dump 打印场景体检，供无人值守的构建验收。
    /// 用法：SpaceFighter.exe -shots -autoshot -zone 5 [-boss] [-tag zone5]
    ///       SpaceFighter.exe -dump
    /// 截图输出到工作目录 docs/shots/auto_*.png，体检输出到 stdout 日志。
    /// </summary>
    public class DevShots : MonoBehaviour
    {
        static readonly float[] Times = { 1.6f, 3.2f, 5.0f, 7.0f };
        float _clock;
        int _index;
        bool _quitAfter;
        string _dir;
        string _tag;
        string _move;
        bool _probe;
        bool _weaponApplied;
        string _weapon;
        string _lvl;
        bool _quit;
        float _probeT;
        float _quitAt = -1f;
        bool _startNextFrame;
        bool _shots;
        bool _dump;
        bool _dumped;
        bool _rebuild;
        bool _rebuilt;
        bool _laser;
        bool _laserCast;
        bool _pause;      // 拍暂停页
        bool _dead;       // 拍结算页
        bool _forced;

        public static void MaybeCreate()
        {
            var args = Environment.GetCommandLineArgs();
            bool shots = Array.IndexOf(args, "-shots") >= 0;
            bool dump = Array.IndexOf(args, "-dump") >= 0;
            bool laser = Array.IndexOf(args, "-laser") >= 0;
            bool pause = Array.IndexOf(args, "-pause") >= 0;
            bool dead = Array.IndexOf(args, "-dead") >= 0;
            if (!shots && !dump && !laser && !pause && !dead) return;
            var go = new GameObject("DevShots");
            var d = go.AddComponent<DevShots>();
            d._shots = shots;
            d._dump = dump;
            d._rebuild = Array.IndexOf(args, "-rebuild") >= 0;
            d._laser = laser;
            d._pause = pause;
            d._dead = dead;
            d._quitAfter = Array.IndexOf(args, "-autoshot") >= 0;
            d._dir = Path.Combine(Directory.GetCurrentDirectory(), "docs", "shots");
            d._tag = Arg(args, "-tag") ?? ("zone" + (Arg(args, "-zone") ?? "1"));
            d._move = Arg(args, "-move");
            d._probe = Array.IndexOf(args, "-probe") >= 0;
            d._weapon = Arg(args, "-weapon");
            d._lvl = Arg(args, "-lvl");
            d._quit = Array.IndexOf(args, "-quit") >= 0;
            Directory.CreateDirectory(d._dir);
        }

        static string Arg(string[] args, string key)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        void Update()
        {
            _clock += Time.unscaledDeltaTime;

            if (_dump && !_dumped && _clock > 1.2f)
            {
                _dumped = true;
                DumpScene();
                if (!_shots) { Application.Quit(); return; }
            }

            // 「重新开始」链路体检：拆树重建后必须还能拿到 GameRoot/玩家/HUD，
            // 否则就是真机上那个黑屏（旧实现走 LoadScene，引导对象不再 Awake）。
            if (_rebuild && !_rebuilt && _clock > 4.0f)
            {
                _rebuilt = true;
                GameBootstrap.Rebuild();
                var g = GameRoot.Instance;
                Debug.Log("[AURORA-REBUILD] gameRoot=" + (g != null) +
                          " state=" + (g != null ? g.State.ToString() : "-") +
                          " player=" + (g != null && g.Player != null) +
                          " cam=" + (Camera.main != null) +
                          " director=" + (Director.Instance != null));
            }

            if (!_weaponApplied && !string.IsNullOrEmpty(_weapon) && _clock > 3.4f &&
                GameRoot.Instance != null && GameRoot.Instance.Player != null)
            {
                var player = GameRoot.Instance.Player;
                if (_weapon != "pulse") player.Weapons.GrantLine(_weapon);
                player.Weapons.SwitchTo(_weapon);
                if (int.TryParse(_lvl, out int wantLv) && wantLv > 0)
                    player.Weapons.Active.Level = Mathf.Clamp(wantLv, 1, 5);
                _weaponApplied = true;
                Debug.Log("[AURORA-WEAPON] " + _weapon + " lv" + player.Weapons.Active.Level);
            }
            if (_probe)
            {
                _probeT += Time.unscaledDeltaTime;
                if (_probeT >= 0.5f) { _probeT = 0f; Probe(); }
            }
            if (_quit && _quitAt <= 0f) _quitAt = _clock + 26f;
            if (_quitAt > 0f && _clock >= _quitAt) { _quitAt = -1f; Application.Quit(); }

            if (_laser && !_laserCast && _clock > 4.4f)
            {
                var boss = Director.Instance != null ? Director.Instance.Boss : null;
                if (boss != null)
                {
                    _laserCast = true;
                    // -move <名字> 指定招式，逐招拍验收图（默认拍双生扫射，因为它同时验证光束）
                    var mv = BossMove.Sweep;
                    if (!string.IsNullOrEmpty(_move) &&
                        System.Enum.TryParse<BossMove>(_move, true, out var parsed)) mv = parsed;
                    boss.DebugCast(mv);
                    Debug.Log("[AURORA-LASER] cast " + mv + " (" + BossMoves.Name(mv) +
                              ") t=" + _clock.ToString("F1"));
                }
            }

            // 界面拍图用：强制进暂停页 / 结算页
            if ((_pause || _dead) && !_forced && _clock > 3.4f)
            {
                _forced = true;
                var g = GameRoot.Instance;
                if (g != null && g.State == GameState.Playing)
                {
                    if (_pause) g.TogglePause();
                    else
                    {
                        var p = g.Player;
                        if (p != null) { p.Invuln = 0f; p.Shields = 0; p.ReflectT = 0f; p.Hull = 1; p.TakeHit(); }
                    }
                }
            }

            if (!_shots) return;

            if (_startNextFrame) { _startNextFrame = false; GameRoot.Instance?.StartRun(); return; }
            if (_index < Times.Length && _clock >= Times[_index])
            {
                string file = $"auto_{_tag}_{_index}.png";
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, file));
                Debug.Log("[Aurora] shot " + file + " t=" + _clock.ToString("F1"));
                // 第 0 张是开始菜单，之后自动开局拍实战
                if (_index == 0) _startNextFrame = true;
                _index++;
                if (_index >= Times.Length && _quitAfter) _quitAt = _clock + 1.2f;
            }
            if (_quitAt > 0f && _clock >= _quitAt)
            {
                _quitAt = -1f;
                Application.Quit();
            }
        }

        /// <summary>
        /// 每 0.5 秒打一行运行时遥测：世界状态、弹数、蓄能、Boss 的出招状态。
        /// 用来回答"Boss 到底有没有出招""子弹有没有生出来"这类只有跑起来才知道的问题。
        /// </summary>
        void Probe()
        {
            var g = GameRoot.Instance;
            var d = Director.Instance;
            int pb = 0, eb = 0;
            var pool = BulletPool.Instance;
            if (pool != null)
            {
                foreach (var b in pool.Active)
                {
                    if (b == null) continue;
                    if (b.Team == Team.Player) pb++; else eb++;
                }
            }
            var boss = d != null ? d.Boss : null;
            string bossInfo = boss == null
                ? "boss=-"
                : $"boss hp={boss.Hp:F0}/{boss.MaxHp:F0} ph={boss.Phase + 1} casting={boss.Casting} " +
                  $"tell={boss.TelegraphLeft:F2} timer={boss.TimerLeft:F2} stun={boss.StunLeft:F2} " +
                  $"gauge={boss.BreakLeft:F0} cast=[{boss.CastLabel}]";
            float charge = g != null && g.Player != null ? g.Player.Weapons.Active.Charge : 0f;
            Debug.Log($"[PROBE] t={_clock:F1} state={(g != null ? g.State.ToString() : "-")} " +
                      $"score={(g != null ? g.Score : 0f):F0} pb={pb} eb={eb} charge={charge:F0} " +
                      $"zone={(d != null ? d.Zone : 0)} run={(d != null ? d.State.ToString() : "-")} {bossInfo}");
        }

        /// <summary>
        /// 打印所有仍在渲染的 SpriteRenderer：对象路径、世界坐标、精灵名、缩放、层级。
        /// 用来抓"菜单里凭空多出个东西"这类看不见来源的问题。
        /// </summary>
        void DumpScene()
        {
            var sb = new StringBuilder();
            sb.Append("[AURORA-DUMP] state=").Append(GameRoot.Instance != null ? GameRoot.Instance.State.ToString() : "?")
              .Append(" renderers:");
            foreach (var r in FindObjectsOfType<SpriteRenderer>())
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r.sprite == null) continue;
                if (r.color.a < 0.02f) continue;
                float sz = r.bounds.size.magnitude;
                if (sz < 0.02f) continue;
                sb.Append('\n').Append(r.name).Append(" | sprite=").Append(r.sprite.name)
                  .Append(" | pos=").Append(r.transform.position.x.ToString("F2")).Append(',')
                        .Append(r.transform.position.y.ToString("F2"))
                  .Append(" | size=").Append(sz.ToString("F2"))
                  .Append(" | order=").Append(r.sortingOrder)
                  .Append(" | a=").Append(r.color.a.ToString("F2"))
                  .Append(" | path=").Append(HierarchyPath(r.transform));
            }
            Debug.Log(sb.ToString());
        }

        static string HierarchyPath(Transform t)
        {
            var sb = new StringBuilder();
            while (t != null) { sb.Insert(0, t.name); t = t.parent; if (t != null) sb.Insert(0, '/'); }
            return sb.ToString();
        }
    }
}
