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

            if (_laser && !_laserCast && _clock > 4.4f)
            {
                var boss = Director.Instance != null ? Director.Instance.Boss : null;
                if (boss != null)
                {
                    _laserCast = true;
                    boss.DebugCast(BossMove.LaserSweep);   // 内部再分时发出两条扫射光束
                    Debug.Log("[AURORA-LASER] cast LaserSweep t=" + _clock.ToString("F1"));
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
