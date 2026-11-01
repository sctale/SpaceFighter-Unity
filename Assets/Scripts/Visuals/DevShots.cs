using System;
using System.IO;
using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 仅开发期：命令行 -shots 定时截图，供无人值守的构建验收。
    /// 用法：SpaceFighter.exe -shots -autoshot -zone 5 [-boss] [-tag zone5]
    /// 输出到工作目录 docs/shots/auto_*.png。
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

        public static void MaybeCreate()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-shots") < 0) return;
            var go = new GameObject("DevShots");
            DontDestroyOnLoad(go);
            var d = go.AddComponent<DevShots>();
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
            if (_index < Times.Length && _clock >= Times[_index])
            {
                string file = $"auto_{_tag}_{_index}.png";
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, file));
                Debug.Log("[Aurora] shot " + file + " t=" + _clock.ToString("F1"));
                _index++;
                if (_index >= Times.Length && _quitAfter) _quitAt = _clock + 1.2f;
            }
            if (_quitAt > 0f && _clock >= _quitAt)
            {
                _quitAt = -1f;
                Application.Quit();
            }
        }
    }
}
