using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 星区背景：两层视差星空，滚动循环。每个星区一套主色（对应原版 10 个命名星区），
    /// 只做氛围，不参与任何判定。
    /// </summary>
    public class Starfield : MonoBehaviour
    {
        const int StarCount = 140;

        struct Star
        {
            public Transform Tr;
            public float Speed;
            public float Depth;
            public float Bright;
            public SpriteRenderer Sr;
        }

        Star[] _stars;
        SpriteRenderer[] _nebula;
        SpriteRenderer[] _zoneWash;
        Color _current = Color.white;
        Color _target = Color.white;
        int _lastZone = -1;

        static readonly Color[] ZoneTints =
        {
            new Color(0.16f, 0.28f, 0.45f), // 巨行星
            new Color(0.22f, 0.30f, 0.42f), // 环带
            new Color(0.14f, 0.38f, 0.34f), // 极光
            new Color(0.28f, 0.18f, 0.42f), // 裂隙
            new Color(0.32f, 0.26f, 0.20f), // 岩带
            new Color(0.42f, 0.30f, 0.14f), // 恒星
            new Color(0.18f, 0.24f, 0.40f), // 双月
            new Color(0.20f, 0.34f, 0.46f), // 彗星
            new Color(0.36f, 0.24f, 0.36f), // 光纱
            new Color(0.34f, 0.16f, 0.24f), // 核心弧环
        };

        void Awake()
        {
            var dot = RuntimeSprites.Disc(16);
            BuildNebula();
            _stars = new Star[StarCount];
            var root = new GameObject("Stars").transform;
            root.SetParent(transform, false);


            for (int i = 0; i < StarCount; i++)
            {
                var go = new GameObject("Star" + i);
                go.transform.SetParent(root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = dot;
                float depth = Random.value;
                sr.sortingOrder = -50 + (int)(depth * 10f);
                // 亮度三档：多数暗、少量中、约 1/8 是亮星（大小也放大），层次才像深空
                bool bright = Random.value < 0.12f;
                float brightK = bright ? 1f : Mathf.Lerp(0.35f, 0.8f, Random.value);
                float size = Mathf.Lerp(0.018f, 0.075f, depth) * (bright ? 1.9f : 1f);
                go.transform.localScale = Vector3.one * size;
                go.transform.position = new Vector3(
                    Random.Range(-GameConfig.WorldWidth, GameConfig.WorldWidth) * 0.75f,
                    Random.Range(-GameView.HalfHeight, GameView.HalfHeight),
                    0f);
                _stars[i] = new Star
                {
                    Tr = go.transform,
                    Depth = depth,
                    Speed = Mathf.Lerp(0.35f, 2.2f, depth),
                    Bright = brightK,
                    Sr = sr,
                };
            }
        }

        /// <summary>
        /// 第二层背景：几团大尺度柔光云。只在纵向极慢漂移 + 轻微横摆，
        /// 给"深空"一点体积感——之前只有单层星点，画面像一张渐变色卡。
        /// </summary>
        void BuildNebula()
        {
            var tex = ShipArt.Get("bullet_glow_soft", 1f) ?? RuntimeSprites.Disc(64);
            _nebula = new SpriteRenderer[7];
            var root = new GameObject("Nebula").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < _nebula.Length; i++)
            {
                var go = new GameObject("Cloud" + i);
                go.transform.SetParent(root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = tex;
                sr.sortingOrder = -60;
                // 拉长 + 斜置：圆光斑会被看成"糊了一块"，横向云带才像星云
                float w = Random.Range(10f, 26f);
                go.transform.localScale = new Vector3(w * Random.Range(1.25f, 2.2f), w * Random.Range(0.45f, 0.8f), 1f);
                go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-30f, 30f));
                go.transform.position = new Vector3(
                    Random.Range(-GameConfig.PlayHalfWidth, GameConfig.PlayHalfWidth),
                    Random.Range(-GameView.HalfHeight, GameView.HalfHeight), 0f);
                _nebula[i] = sr;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var dir = Director.Instance;
            if (dir != null && dir.Zone != _lastZone)
            {
                _lastZone = dir.Zone;
                _target = ZoneTints[(dir.Zone - 1) % ZoneTints.Length];
            }

            _current = Color.Lerp(_current, _target, 1f - Mathf.Exp(-0.8f * dt));

            // 星云：极慢下移 0.16 u/s + 横摆 0.05，出屏从顶部回卷
            if (_nebula != null)
            {
                for (int i = 0; i < _nebula.Length; i++)
                {
                    var sr = _nebula[i];
                    if (sr == null) continue;
                    var p = sr.transform.position;
                    p.y -= 0.16f * dt;
                    p.x += Mathf.Sin(Time.time * 0.05f + i) * 0.02f * dt;
                    if (p.y < -GameView.HalfHeight - 6f)
                        p.y = GameView.HalfHeight + 6f;
                    sr.transform.position = p;
                    var c = _current * 0.9f;
                    sr.color = new Color(c.r, c.g, c.b, 0.032f + 0.014f * (i % 3));
                }
            }

            for (int i = 0; i < _stars.Length; i++)
            {
                var s = _stars[i];
                var p = s.Tr.position;
                p.y -= s.Speed * dt;
                if (p.y < -GameView.HalfHeight - 0.5f)
                {
                    p.y = GameView.HalfHeight + 0.5f;
                    p.x = Random.Range(-GameConfig.WorldWidth, GameConfig.WorldWidth) * 0.75f;
                }
                s.Tr.position = p;
                var tint = Color.Lerp(Color.white, _current * 2.2f, 0.35f);
                s.Sr.color = new Color(tint.r, tint.g, tint.b, (0.18f + s.Depth * 0.5f) * s.Bright);
            }
        }
    }
}
