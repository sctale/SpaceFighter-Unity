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

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            SetupCamera();
            SetupWorld();
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
            float halfH = Mathf.Max(GameConfig.OrthoSizeMin, GameConfig.PlayHalfWidth / Mathf.Max(0.2f, aspect));
            cam.orthographicSize = halfH;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            if (cam.GetComponent<CameraShake>() == null) cam.gameObject.AddComponent<CameraShake>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = GameConfig.Hex("05060D");
        }

        void SetupWorld()
        {
            var root = new GameObject("GameRoot").AddComponent<GameRoot>();

            new GameObject("Starfield").AddComponent<Starfield>();
            new GameObject("ZoneSky").AddComponent<ZoneSky>();
            new GameObject("Audio").AddComponent<AudioDirector>();

            var pool = new GameObject("BulletPool").AddComponent<BulletPool>();
            pool.transform.SetParent(transform, false);

            var dir = new GameObject("Director").AddComponent<Director>();
            dir.transform.SetParent(transform, false);

            var playerGo = new GameObject("Player");
            var player = playerGo.AddComponent<PlayerShip>();
            playerGo.transform.position = new Vector3(0f, -GameConfig.OrthoSizeMin * 0.55f, 0f);
            root.AttachPlayer(player);

            var hud = new GameObject("HudRoot").AddComponent<Hud>();
            hud.transform.SetParent(transform, false);

            DevShots.MaybeCreate();
        }
    }
}
