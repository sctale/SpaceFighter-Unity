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
            public SpriteRenderer Sr;
        }

        Star[] _stars;
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
                float size = Mathf.Lerp(0.02f, 0.09f, depth);
                go.transform.localScale = Vector3.one * size;
                go.transform.position = new Vector3(
                    Random.Range(-GameConfig.WorldWidth, GameConfig.WorldWidth) * 0.75f,
                    Random.Range(-GameConfig.OrthoSizeMin, GameConfig.OrthoSizeMin),
                    0f);
                _stars[i] = new Star
                {
                    Tr = go.transform,
                    Depth = depth,
                    Speed = Mathf.Lerp(0.35f, 2.2f, depth),
                    Sr = sr,
                };
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

            for (int i = 0; i < _stars.Length; i++)
            {
                var s = _stars[i];
                var p = s.Tr.position;
                p.y -= s.Speed * dt;
                if (p.y < -GameConfig.OrthoSizeMin - 0.5f)
                {
                    p.y = GameConfig.OrthoSizeMin + 0.5f;
                    p.x = Random.Range(-GameConfig.WorldWidth, GameConfig.WorldWidth) * 0.75f;
                }
                s.Tr.position = p;
                s.Sr.color = Color.Lerp(Color.white, _current * 2.2f, 0.35f) * new Color(1f, 1f, 1f, 0.35f + s.Depth * 0.65f);
            }
        }
    }
}
