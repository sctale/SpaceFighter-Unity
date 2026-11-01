using UnityEngine;

namespace AuroraRift
{
    /// <summary>尾焰拖尾：按帧间隔在机体后方落点，颜色跟随当前武器线并逐渐淡出。</summary>
    public class ThrusterTrail : MonoBehaviour
    {
        PlayerShip _owner;
        float _timer;

        public void Bind(PlayerShip owner) => _owner = owner;

        void Update()
        {
            var root = GameRoot.Instance;
            if (_owner == null || root == null || root.State != GameState.Playing) return;
            if (root.LowFx) return;

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 0.035f;

            var line = GameConfig.Line(_owner.Weapons.ActiveId);
            var go = new GameObject("Trail");
            go.transform.position = _owner.transform.position + Vector3.down * 0.22f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ShipArt.Get("bullet_glow_soft", 0.34f) ?? RuntimeSprites.Disc(16);
            sr.color = new Color(line.Tint.r, line.Tint.g, line.Tint.b, 0.55f);
            sr.sortingOrder = 7;
            float s = Random.Range(0.10f, 0.16f) * (_owner.Weapons.Active.Detuned ? 0.6f : 1f);
            go.transform.localScale = new Vector3(s, s * 1.6f, 1f);
            go.AddComponent<FadeAndDie>().Life = 0.32f;
        }
    }

    /// <summary>通用"淡出即销毁"。</summary>
    public class FadeAndDie : MonoBehaviour
    {
        public float Life = 0.4f;
        SpriteRenderer _sr;
        float _t;

        void Awake() => _sr = GetComponent<SpriteRenderer>();

        void Update()
        {
            _t += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(_t / Life);
            if (_sr != null)
            {
                var c = _sr.color;
                _sr.color = new Color(c.r, c.g, c.b, c.a * k);
            }
            if (_t >= Life) Destroy(gameObject);
        }
    }
}
