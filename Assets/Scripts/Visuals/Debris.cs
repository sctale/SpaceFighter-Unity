using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 击破碎片：几片硬边多边形从机体位置飞散、旋转、缩小淡出。
    /// 形状固定 5 种（缓存精灵），每片单独随机速度/自转/寿命，
    /// 低特效模式下自动减半——它只是打击感，不参与任何判定。
    /// </summary>
    public class Debris : MonoBehaviour
    {
        public Vector2 Vel;
        public float Spin;
        public float Life = 0.62f;
        public float Drag = 2.4f;
        public float Scale0 = 0.1f;

        Color _base;
        SpriteRenderer _sr;
        float _t;

        static Sprite[] _shards;

        void Awake() => _sr = GetComponent<SpriteRenderer>();

        void Update()
        {
            float dt = Time.deltaTime;
            _t += dt;
            float k = Mathf.Clamp01(_t / Life);
            Vel *= Mathf.Exp(-Drag * dt);
            transform.position += (Vector3)(Vel * dt);
            transform.Rotate(0f, 0f, Spin * dt);
            transform.localScale = Vector3.one * (Scale0 * Mathf.Lerp(1f, 0.5f, k));
            if (_sr != null) _sr.color = new Color(_base.r, _base.g, _base.b, _base.a * (1f - k) * (1f - k));
            if (_t >= Life) Destroy(gameObject);
        }

        /// <summary>在 pos 炸开 count 片（默认 4），大小按机体尺寸给。</summary>
        public static void Burst(Vector3 pos, Color tint, float size, int count = 4, float hot = 0.45f)
        {
            var root = GameRoot.Instance;
            if (root != null && root.LowFx) count = Mathf.Max(2, count - 2);
            if (count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Debris");
                go.transform.position = pos + (Vector3)(Random.insideUnitCircle * size * 0.42f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Shard(i);
                sr.color = Color.Lerp(tint, Color.white, Random.Range(0.18f, hot));
                sr.sortingOrder = 19;

                var d = go.AddComponent<Debris>();
                d._base = sr.color;
                d.Scale0 = size * Random.Range(0.18f, 0.34f);
                go.transform.localScale = Vector3.one * d.Scale0;
                go.transform.rotation = Quaternion.Euler(0f, 0f, Random.value * 360f);

                float a = Random.value * Mathf.PI * 2f;
                float spd = Random.Range(1.6f, 3.4f) + size * 1.6f;
                d.Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * spd;
                d.Vel.y += Mathf.Lerp(0.9f, 2.4f, Random.value);   // 整体向下带一点
                d.Spin = Random.Range(120f, 520f) * (Random.value < 0.5f ? -1f : 1f);
                d.Life = Random.Range(0.44f, 0.74f);
            }
        }

        static Sprite Shard(int i)
        {
            if (_shards == null)
            {
                _shards = new[]
                {
                    RuntimeSprites.Poly(32, new[]
                    {
                        new Vector2(-0.85f, -0.55f), new Vector2(0.9f, -0.2f),
                        new Vector2(0.15f, 0.85f), new Vector2(-0.35f, 0.3f),
                    }),
                    RuntimeSprites.Poly(32, new[]
                    {
                        new Vector2(-0.7f, 0.75f), new Vector2(0.75f, 0.55f), new Vector2(0.35f, -0.9f),
                    }),
                    RuntimeSprites.Poly(32, new[]
                    {
                        new Vector2(-0.9f, 0.15f), new Vector2(0.2f, 0.9f),
                        new Vector2(0.85f, -0.45f), new Vector2(-0.1f, -0.8f),
                    }),
                    RuntimeSprites.Poly(32, new[]
                    {
                        new Vector2(-0.35f, 0.85f), new Vector2(0.9f, 0.35f), new Vector2(-0.8f, -0.6f),
                    }),
                    RuntimeSprites.Poly(32, new[]
                    {
                        new Vector2(-0.9f, -0.1f), new Vector2(-0.2f, 0.85f),
                        new Vector2(0.85f, 0.5f), new Vector2(0.4f, -0.85f),
                    }),
                };
            }
            return _shards[i % _shards.Length];
        }
    }
}
