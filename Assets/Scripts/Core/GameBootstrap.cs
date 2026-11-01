using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 场景自举：相机、世界根节点、玩家、HUD、弹池、导演全部代码创建。
    /// 这样仓库里不需要提交任何手工场景/预制体，CLI 直接构建即可。
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoBoot()
        {
            if (FindAnyObjectByType<GameBootstrap>() != null) return;
            var go = new GameObject("GameBootstrap");
            go.AddComponent<GameBootstrap>();
        }

        /// <summary>世界根节点：重开时整棵销毁重建，不依赖 LoadScene。</summary>
        Transform _world;

        void Awake()
        {
            // 注意：这里不能 DontDestroyOnLoad(gameObject) 后又只靠 Awake 建世界——
            // 那样一旦走 LoadScene 重开，本对象活着但 Awake 不再执行，
            // 相机、玩家、HUD 全都不会重建，点「重新开始」就是黑屏。
            // 重开改成 GameBootstrap.Rebuild()：销毁 _world 整棵树再 SetupWorld()。
            Instance = this;
            SetupCamera();
            SetupWorld();
        }

        public static GameBootstrap Instance { get; private set; }

        /// <summary>原地重开一局：拆掉旧世界、重建。autoStart=false 时停在开始菜单。</summary>
        public static void Rebuild(bool autoStart = true)
        {
            var b = Instance;
            if (b == null || b._world == null) return;
            UnityEngine.Object.Destroy(b._world.gameObject);
            SetupCamera();
            b.SetupWorld();
            if (autoStart && GameRoot.Instance != null) GameRoot.Instance.StartRun();
        }

        static void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            cam.orthographicSize = GameView.ComputeHalfHeight(aspect);
            cam.transform.position = new Vector3(0f, 0f, -10f);
            if (cam.GetComponent<CameraShake>() == null) cam.gameObject.AddComponent<CameraShake>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = GameConfig.Hex("05060D");
            GameView.Refresh(cam);
        }

        void SetupWorld()
        {
            _world = new GameObject("World").transform;
            _world.SetParent(transform, false);

            var root = new GameObject("GameRoot").AddComponent<GameRoot>();
            root.transform.SetParent(_world, false);

            var stars = new GameObject("Starfield").AddComponent<Starfield>();
            stars.transform.SetParent(_world, false);
            var sky = new GameObject("ZoneSky").AddComponent<ZoneSky>();
            sky.transform.SetParent(_world, false);
            var audio = new GameObject("Audio").AddComponent<AudioDirector>();
            audio.transform.SetParent(_world, false);

            var pool = new GameObject("BulletPool").AddComponent<BulletPool>();
            pool.transform.SetParent(_world, false);

            var dir = new GameObject("Director").AddComponent<Director>();
            dir.transform.SetParent(_world, false);

            var playerGo = new GameObject("Player");
            playerGo.transform.SetParent(_world, false);
            var player = playerGo.AddComponent<PlayerShip>();
            playerGo.transform.position = new Vector3(0f, GameView.StartY, 0f);
            root.AttachPlayer(player);

            var hud = new GameObject("HudRoot").AddComponent<Hud>();
            hud.transform.SetParent(_world, false);

            DevShots.MaybeCreate();
        }
    }
}
