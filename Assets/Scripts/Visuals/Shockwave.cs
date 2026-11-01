using UnityEngine;

namespace AuroraRift
{
    /// <summary>Boss 阶段切换用的扩散光环：放大 + 淡出后自毁。</summary>
    public class Shockwave : MonoBehaviour
    {
        float _t;
        const float Life = 0.6f;
        SpriteRenderer _sr;

        void Awake() => _sr = GetComponent<SpriteRenderer>();

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / Life);
            transform.localScale = Vector3.one * Mathf.Lerp(1.5f, 9f, k);
            if (_sr != null) _sr.color = new Color(1f, 0.7f, 0.4f, 0.7f * (1f - k));
            if (_t >= Life) Destroy(gameObject);
        }
    }
}
