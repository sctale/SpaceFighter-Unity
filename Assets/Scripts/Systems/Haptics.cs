using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 触感震动：轻 / 中 / 重三档。Android 走 VibrationEffect（API 26+），
    /// 老系统退回 vibrate(ms)；桌面/其他平台静默。暂停页可关闭。
    /// 需要 AndroidManifest 里的 VIBRATE 权限，由 BuildScript 的构建钩子自动补上。
    /// </summary>
    public static class Haptics
    {
        static AndroidJavaObject _vibrator;
        static int _sdk = -1;

        public static void Light() => Play(40L, 70);
        public static void Medium() => Play(90L, 120);
        public static void Heavy() => Play(150L, 200);

        static void Play(long ms, int amplitude)
        {
            var root = GameRoot.Instance;
            if (root != null && !root.HapticsEnabled) return;
            if (Application.platform != RuntimePlatform.Android) return;

            try
            {
                if (_vibrator == null)
                {
                    var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
                if (_vibrator == null) return;

                if (_sdk < 0)
                {
                    var version = new AndroidJavaClass("android.os.Build$VERSION");
                    _sdk = version.GetStatic<int>("SDK_INT");
                }

                if (_sdk >= 26)
                {
                    var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                    var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", ms, amplitude);
                    _vibrator.Call("vibrate", effect);
                }
                else
                {
                    _vibrator.Call("vibrate", ms);
                }
            }
            catch (System.Exception)
            {
                // 设备无马达 / 未授权时静默，绝不打断游戏
            }
        }
    }
}
