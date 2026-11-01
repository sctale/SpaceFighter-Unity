using System.Collections.Generic;
using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// Boss 光束实体，对标网页版 entities.js:1628 的 Laser。
    /// 三段生命周期：warn（虚线预告，alpha 0.14→0.49）→ active（实光，可 sweep 扫射，判定命中）
    /// → 收尾 0.12s。宽度 10~16px、长度 = 屏高 ×2，全部按 GameConfig.Px 换算。
    /// 之前 Unity 版把「扫射光束/十字光束」用一坨子弹近似，手感完全不对：
    /// 子弹能躲，光束是要靠预告线读招、然后横向穿出去的。
    /// </summary>
    public class BossLaser : MonoBehaviour
    {
        /// <summary>场上所有光束，供 Boss 死亡时一并清场。</summary>
        public static readonly List<BossLaser> Active = new();

        SpriteRenderer _warn, _beam, _core, _muzzle;
        float _angle, _warnTime, _activeTime, _width, _length, _sweep, _t;
        Color _color;
        BossController _boss;
        bool _dead;

        public bool IsActive => _t >= _warnTime && _t <= _warnTime + _activeTime;

        /// <summary>网页版 boss.beam(ang, {warn, active, width, sweep})。</summary>
        public static void Spawn(Vector3 origin, float angleRad, float warn, float active,
                                 float widthPx, float sweepRadPerSec, Color color, BossController boss)
        {
            var go = new GameObject("BossLaser");
            go.transform.SetParent(boss != null ? boss.transform : null, false);
            go.transform.position = origin;
            go.transform.rotation = Quaternion.Euler(0f, 0f, angleRad * Mathf.Rad2Deg);

            var l = go.AddComponent<BossLaser>();
            l._angle = angleRad;
            l._warnTime = warn;
            l._activeTime = active;
            l._sweep = sweepRadPerSec;
            l._color = color;
            l._boss = boss;
            l._width = widthPx * GameConfig.Px;
            // 原版 length = SF.H * 2：不管朝哪个方向都能扫出整个画面
            l._length = GameView.HalfHeight * 4f;
            l.Build();
            Active.Add(l);
        }

        void Build()
        {
            var quad = RuntimeSprites.Square(8);
            var disc = RuntimeSprites.Disc(64);

            // 预告线：2.2px 宽的细线，从炮口指出去
            _warn = MakeChild("warn", quad, 10, new Color(1f, 0.55f, 0.62f, 0f));
            _warn.transform.localPosition = new Vector3(_length * 0.5f, 0f, 0f);
            _warn.transform.localScale = new Vector3(_length, 2.2f * GameConfig.Px, 1f);

            // 蓄光点：warn 期间在炮口呼吸
            _muzzle = MakeChild("muzzle", disc, 10, new Color(1f, 1f, 1f, 0f));
            _muzzle.transform.localScale = new Vector3(34f, 34f, 1f) * GameConfig.Px;

            // 光束：外层光晕 + 内芯白线
            _beam = MakeChild("beam", quad, 11, new Color(_color.r, _color.g, _color.b, 0f));
            _beam.transform.localPosition = new Vector3(_length * 0.5f, 0f, 0f);
            _beam.transform.localScale = new Vector3(_length, _width, 1f);

            _core = MakeChild("core", quad, 12, new Color(1f, 1f, 1f, 0f));
            _core.transform.localPosition = new Vector3(_length * 0.5f, 0f, 0f);
            _core.transform.localScale = new Vector3(_length, _width * 0.38f, 1f);
        }

        SpriteRenderer MakeChild(string name, Sprite sprite, int order, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            sr.color = color;
            return sr;
        }

        void Update()
        {
            var root = GameRoot.Instance;
            if (root == null || root.State != GameState.Playing) { Kill(); return; }

            float dt = Time.deltaTime;
            _t += dt;

            // 只有 active 期间才扫射，预告线是定死的——和原版一致，玩家靠预告线读方向
            if (_t > _warnTime && _t < _warnTime + _activeTime && Mathf.Abs(_sweep) > 0.0001f)
            {
                _angle += _sweep * dt;
                transform.rotation = Quaternion.Euler(0f, 0f, _angle * Mathf.Rad2Deg);
            }

            if (_t > _warnTime + _activeTime + 0.12f) { Kill(); return; }
            if (_boss != null && (_boss == null || _boss.Dying)) { Kill(); return; }

            float k = Mathf.Clamp01(_t / Mathf.Max(0.01f, _warnTime));
            bool active = IsActive;
            if (_warn != null)
            {
                _warn.enabled = !active;
                if (!active) _warn.color = new Color(1f, 0.55f, 0.62f, 0.14f + k * 0.35f);
            }
            if (_muzzle != null)
            {
                _muzzle.enabled = !active;
                if (!active)
                {
                    float pulse = 1f + Mathf.Sin(_t * 22f) * 0.18f;
                    _muzzle.transform.localScale = new Vector3(34f + k * 20f, 34f + k * 20f, 1f)
                        * GameConfig.Px * pulse;
                    _muzzle.color = new Color(_color.r, _color.g, _color.b, 0.2f + k * 0.3f);
                }
            }

            // env：进场 12/s、退场 8/s 的包络，和原版 draw() 一致
            float env = active
                ? Mathf.Clamp(Mathf.Min((_t - _warnTime) * 12f, (_warnTime + _activeTime - _t) * 8f), 0f, 1f)
                : 0f;
            if (_beam != null)
            {
                _beam.enabled = active;
                _beam.color = new Color(_color.r, _color.g, _color.b, env * 0.62f);
            }
            if (_core != null)
            {
                _core.enabled = active;
                _core.color = new Color(1f, 1f, 1f, env * 0.9f);
            }

            if (active) CheckHit(root.Player);
        }

        void CheckHit(PlayerShip player)
        {
            if (player == null) return;
            if (player.Invuln > 0f || player.Dashing) return;   // 原版：无敌帧和冲刺都能穿光束

            Vector2 p = player.transform.position;
            float dist = PointRayDistance(p, (Vector2)transform.position, _angle, _length);
            if (dist < _width * 0.5f + GameConfig.PlayerHitRadius)
                player.TakeHit();
        }

        /// <summary>点到射线的最短距离（原版 game.js:968 pointRayDistance）。</summary>
        static float PointRayDistance(Vector2 pt, Vector2 origin, float angle, float length)
        {
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 rel = pt - origin;
            float t = Vector2.Dot(rel, dir);
            if (t <= 0f) return rel.magnitude;
            if (t > length) return (pt - (origin + dir * length)).magnitude;
            return (pt - (origin + dir * t)).magnitude;
        }

        void Kill()
        {
            if (_dead) return;
            _dead = true;
            Active.Remove(this);
            Destroy(gameObject);
        }

        void OnDisable()
        {
            if (!_dead) { _dead = true; Active.Remove(this); }
        }

        /// <summary>Boss 被打爆时把场上的光束全部收掉（原版 boss.dying 即移除）。</summary>
        public static void ClearAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i] != null) Active[i].Kill();
            Active.Clear();
        }
    }
}
