#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AuroraRift.EditorTools
{
    /// <summary>
    /// CLI 构建入口。场景不用手工保存——这里按需生成一个空场景，
    /// 运行时由 GameBootstrap 自举，因此命令行可直接出包。
    /// 用法：
    ///   Unity.exe -batchmode -quit -projectPath &lt;proj&gt; -executeMethod AuroraRift.EditorTools.BuildScript.BuildAndroid
    /// </summary>
    public static class BuildScript
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Aurora/Export Android Gradle Project")]
        public static void ExportAndroidProject()
        {
            EnsureScene();
            ConfigurePlayer();
            ConfigureAndroidTools();

            // headless 下 Unity 会把 Gradle JVM 堆算成 0（-Xmx0m）导致 Java 起不来，
            // 所以改成只导出 Gradle 工程，APK 由外部脚本用正确的堆参数构建。
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            const string outDir = "Builds/AndroidProject";
            System.IO.Directory.CreateDirectory(outDir);

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outDir,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[Aurora] EXPORT_RESULT={report.summary.result} errors={report.summary.totalErrors} dir={outDir}");
            if (report.summary.result == BuildResult.Succeeded) PatchAdaptiveIcon(outDir);
            if (Application.isBatchMode)
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        [MenuItem("Aurora/Build Android APK")]
        public static void BuildAndroid()
        {
            var result = Build(BuildTarget.Android, "Builds/SpaceFighter.apk");
            Finish(result);
        }

        [MenuItem("Aurora/Build Windows")]
        public static void BuildWindows()
        {
            var result = Build(BuildTarget.StandaloneWindows64, "Builds/Windows/SpaceFighter.exe");
            Finish(result);
        }

        static BuildReport Build(BuildTarget target, string output)
        {
            EnsureScene();
            ConfigurePlayer();
            if (target == BuildTarget.Android) ConfigureAndroidTools();

            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? "Builds");
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[Aurora] BUILD_RESULT={report.summary.result} " +
                      $"size={report.summary.totalSize} errors={report.summary.totalErrors} " +
                      $"output={output}");
            return report;
        }

        /// <summary>应用图标：母图 1024，Unity 自己缩到各密度。</summary>
        static void ApplyIcons()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Icons/icon_1024.png");
            if (icon == null)
            {
                Debug.LogWarning("[Aurora] 缺少 Assets/Icons/icon_1024.png，跳过图标设置");
                return;
            }
            PlayerSettings.SetIcons(NamedBuildTarget.Android, new[] { icon }, IconKind.Any);
        }

        /// <summary>
        /// Unity 只写传统 mipmap 图标；这里补一份 Android 8+ 的自适应图标
        /// （背景渐变 + 前景菱形环与舰船），避免桌面上被套一层白底。
        /// </summary>
        static void PatchAdaptiveIcon(string projectDir)
        {
            try
            {
                string res = Path.Combine(projectDir, "launcher", "src", "main", "res");
                if (!Directory.Exists(res))
                {
                    Debug.LogWarning("[Aurora] 找不到 launcher res 目录，跳过自适应图标");
                    return;
                }
                string drawable = Path.Combine(res, "drawable-nodpi");
                string anydpi = Path.Combine(res, "mipmap-anydpi-v26");
                Directory.CreateDirectory(drawable);
                Directory.CreateDirectory(anydpi);

                CopyIcon("icon_bg_1024.png", Path.Combine(drawable, "ic_launcher_bg.png"));
                CopyIcon("icon_fg_1024.png", Path.Combine(drawable, "ic_launcher_fg.png"));

                string nl = System.Environment.NewLine;
                string xml =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + nl +
                    "<adaptive-icon xmlns:android=\"http://schemas.android.com/apk/res/android\">" + nl +
                    "    <background android:drawable=\"@drawable/ic_launcher_bg\" />" + nl +
                    "    <foreground android:drawable=\"@drawable/ic_launcher_fg\" />" + nl +
                    "</adaptive-icon>" + nl;
                // Unity 的 launcher 清单引用的是 @mipmap/app_icon，所以文件名要对齐
                File.WriteAllText(Path.Combine(anydpi, "app_icon.xml"), xml);
                File.WriteAllText(Path.Combine(anydpi, "ic_launcher.xml"), xml);
                File.WriteAllText(Path.Combine(anydpi, "ic_launcher_round.xml"), xml);
                Debug.Log("[Aurora] 自适应图标已写入 launcher/src/main/res/mipmap-anydpi-v26");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Aurora] 自适应图标写入失败：" + e.Message);
            }
        }

        static void CopyIcon(string name, string dst)
        {
            string src = Path.Combine("Assets", "Icons", name);
            if (File.Exists(src)) File.Copy(src, dst, true);
        }

        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory("Assets/Scenes");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.Refresh();
                Debug.Log("[Aurora] created scene " + ScenePath);
            }

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == ScenePath))
            {
                list.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = list.ToArray();
            }
        }

        static void ConfigureAndroidTools()
        {
            // CLI 构建时把 SDK/NDK/JDK 指给 Unity；优先读环境变量，换机也能复用。
            string sdk = System.Environment.GetEnvironmentVariable("AURORA_ANDROID_SDK")
                         ?? @"C:\Users\HAOHAO\AppData\Local\Android\Sdk";
            string ndk = System.Environment.GetEnvironmentVariable("AURORA_ANDROID_NDK")
                         ?? System.IO.Path.Combine(sdk, "ndk", "27.2.12479018");
            string jdk = System.Environment.GetEnvironmentVariable("AURORA_ANDROID_JDK")
                         ?? @"C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK";
            UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath = sdk;
            UnityEditor.Android.AndroidExternalToolsSettings.ndkRootPath = ndk;
            UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath = jdk;
            // headless 下 Unity 会把 Gradle/JVM 最大堆算成 0，导致 "-Xmx0m" 起崩；
            // 这个属性才是真正落盘的设置（EditorPrefs 同名键是空的，无效）。
            UnityEditor.Android.AndroidExternalToolsSettings.maxJvmHeapSize = 4096;
            UnityEditor.Android.AndroidExternalToolsSettings.stopGradleDaemonsOnExit = true;
            Debug.Log($"[Aurora] Android tools sdk={sdk} ndk={ndk} jdk={jdk}");
        }

        /// <summary>
        /// 场景与 UI 全是运行时代码创建，shader 剥离会让界面变品红。
        /// 做法：在 Resources 里放两个材质，构建时按引用把 shader 带进包，运行时由 Hud 显式赋给 Text/Image。
        /// （不要改 ProjectSettings/GraphicsSettings.asset —— 实测会让批处理编辑器崩溃。）
        /// </summary>
        static void EnsureUiMaterials()
        {
            const string resDir = "Assets/Resources";
            System.IO.Directory.CreateDirectory(resDir);

            EnsureMat(resDir + "/AuroraUiText.mat", "UI/Default Font");
            EnsureMat(resDir + "/AuroraUiImage.mat", "UI/Default");
            AssetDatabase.Refresh();
        }

        static void EnsureMat(string path, string shaderName)
        {
            if (System.IO.File.Exists(path)) return;
            var sh = Shader.Find(shaderName);
            if (sh == null)
            {
                Debug.LogWarning("[Aurora] shader not found: " + shaderName);
                return;
            }
            var mat = new Material(sh);
            AssetDatabase.CreateAsset(mat, path);
            Debug.Log("[Aurora] created material " + path);
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "sctale";
            PlayerSettings.productName = "Aurora Rift";
            PlayerSettings.bundleVersion = "0.19.1";
            PlayerSettings.Android.bundleVersionCode = 20;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.sctale.spacefighter");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            // Strip Engine Code 会把原生 MonoBehaviour(class 115) 从包里剥掉：
            // 之后每次 AddComponent / ScriptableObject.CreateInstance 都报
            // "Could not produce class with ID 115"，IMGUI 每帧画字要建一次
            // RuntimeTextSettings，30 秒能刷 2.5 万条，顺带让 GUI.DoSetSkin 抛 NRE。
            // link.xml 保住 UnityEngine.MonoScript 也无效（实测），只能按 Unity
            // 的提示关掉引擎裁剪；代价是包体大约 +8MB，换界面不再抖异常。
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            ApplyIcons();
            EditorUserBuildSettings.buildAppBundle = false;
        }

        static void Finish(BuildReport report)
        {
            bool ok = report.summary.result == BuildResult.Succeeded;
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
#endif
