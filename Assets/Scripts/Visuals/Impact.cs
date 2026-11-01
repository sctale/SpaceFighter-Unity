using UnityEngine;

namespace AuroraRift
{
    /// <summary>短促的爆点/火光：放大 + 淡出，0.18 秒自毁。</summary>
    public class Impact : MonoBehaviour
    {
        public Color Tint = Color.white;
        public float Size = 0.35f;
        public float Life = 0.18f;

        SpriteRenderer _sr;
        float _t;

        void Awake() => _sr = GetComponent<SpriteRenderer>();

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / Life);
            transform.localScale = Vector3.one * Size * Mathf.Lerp(0.45f, 1.9f, k);
            if (_sr != null) _sr.color = new Color(Tint.r, Tint.g, Tint.b, Tint.a * (1f - k));
            if (_t >= Life) Destroy(gameObject);
        }

        public static void Spawn(Vector3 pos, Color tint, float size = 0.35f, float life = 0.18f)
        {
            var go = new GameObject("Impact");
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ShipArt.Get("bullet_glow_soft", 1f) ?? RuntimeSprites.Disc(32);
            sr.color = tint;
            sr.sortingOrder = 21;
            var im = go.AddComponent<Impact>();
            im.Tint = tint;
            im.Size = size;
            im.Life = life;
        }
    }

    /// <summary>相机震动：受击/击破时给一点反馈，幅度与时长都很小，避免晕动。</summary>
    public class CameraShake : MonoBehaviour
    {
        public static CameraShake Instance;

        float _amp, _left, _duration;
        Vector3 _base;

        void Awake()
        {
            Instance = this;
            _base = transform.localPosition;
        }

        public void Shake(float amplitude, float duration)
        {
            _amp = Mathf.Max(_amp, amplitude);
            _duration = Mathf.Max(_duration, duration);
            _left = Mathf.Max(_left, duration);
        }

        void LateUpdate()
        {
            if (_left <= 0f) { transform.localPosition = _base; return; }
            _left -= Time.unscaledDeltaTime;
            float k = _duration > 0f ? _left / _duration : 0f;
            var offset = Random.insideUnitCircle * (_amp * k);
            transform.localPosition = _base + new Vector3(offset.x, offset.y, 0f);
            if (_left <= 0f) transform.localPosition = _base;
        }
    }
}
