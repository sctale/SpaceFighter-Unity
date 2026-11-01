using UnityEngine;

namespace AuroraRift
{
    /// <summary>
    /// 星区专属天空：10 个命名星区各有一套背景特征（巨行星/环带/极光/裂隙/岩带/恒星/双月/彗星/光纱/核心弧环）。
    /// 换区时重建，全部用程序化 sprite，不依赖美术资源。
    /// </summary>
    public class ZoneSky : MonoBehaviour
    {
        Transform _root;
        int _zone = -1;
        Camera _cam;

        static readonly Color[] BgColors =
        {
            GameConfig.Hex("0A1226"), // 巨行星
            GameConfig.Hex("0B1520"), // 环带
            GameConfig.Hex("08161A"), // 极光
            GameConfig.Hex("120A1E"), // 裂隙
            GameConfig.Hex("151009"), // 岩带
            GameConfig.Hex("1A1206"), // 恒星
            GameConfig.Hex("0A1024"), // 双月
            GameConfig.Hex("08161C"), // 彗星
            GameConfig.Hex("150A1A"), // 光纱
            GameConfig.Hex("1A0812"), // 核心弧环
        };

        void Awake()
        {
            _root = new GameObject("Sky").transform;
            _root.SetParent(transform, false);
            _cam = Camera.main;
        }

        void Update()
        {
            var dir = Director.Instance;
            if (dir == null || dir.Zone == _zone) return;
            _zone = dir.Zone;
            Build(_zone);
        }

        void Build(int zone)
        {
            foreach (Transform c in _root) Destroy(c.gameObject);
            int i = (zone - 1) % 10;
            if (_cam != null) _cam.backgroundColor = BgColors[i];

            switch (i)
            {
                case 0: // 巨行星
                    Disc(new Vector2(3f, 5.5f), 5.2f, GameConfig.Hex("1E3358"), 0.9f);
                    Disc(new Vector2(-2.5f, -4f), 1.6f, GameConfig.Hex("2A4468"), 0.55f);
                    break;
                case 1: // 环带
                    Disc(new Vector2(-1.5f, 5f), 3.4f, GameConfig.Hex("2B3A4E"), 0.9f);
                    for (int k = 0; k < 3; k++)
                        Ring(new Vector2(-1.5f, 5f), 5.2f + k * 0.9f, GameConfig.Hex("4A5E78"), 0.22f);
                    break;
                case 2: // 极光
                    for (int k = 0; k < 5; k++)
                        Band(new Vector2(-1.2f + k * 1.0f, 3.0f + k * 1.1f), new Vector2(22f, 2.6f),
                             GameConfig.Hex("2BD9A8"), 0.16f - k * 0.02f, -16f);
                    break;
                case 3: // 裂隙
                    Band(new Vector2(0f, 2f), new Vector2(1.6f, 16f), GameConfig.Hex("05030C"), 0.85f, 8f);
                    Band(new Vector2(0f, 2f), new Vector2(0.35f, 16f), GameConfig.Hex("B04CFF"), 0.35f, 8f);
                    break;
                case 4: // 岩带
                    for (int k = 0; k < 14; k++)
                    {
                        var rnd = new System.Random(k * 31 + 7);
                        float x = (float)rnd.NextDouble() * 12f - 6f;
                        float y = (float)rnd.NextDouble() * 14f - 5f;
                        Disc(new Vector2(x, y), 0.25f + (float)rnd.NextDouble() * 0.5f,
                             GameConfig.Hex("6B6152"), 0.5f, rock: true);
                    }
                    break;
                case 5: // 恒星
                    Disc(new Vector2(2.6f, 6f), 2.6f, GameConfig.Hex("FFC24B"), 0.95f);
                    Disc(new Vector2(2.6f, 6f), 4.2f, GameConfig.Hex("FF9A2E"), 0.18f);
                    break;
                case 6: // 双月
                    Disc(new Vector2(-2.8f, 5.6f), 1.7f, GameConfig.Hex("8FA4C9"), 0.85f);
                    Disc(new Vector2(1.2f, 6.4f), 1.1f, GameConfig.Hex("C3CBE0"), 0.75f);
                    break;
                case 7: // 彗星
                    Disc(new Vector2(2.8f, 6.2f), 1.0f, GameConfig.Hex("9BE8FF"), 0.9f);
                    Band(new Vector2(5.1f, 7.6f), new Vector2(5.2f, 0.5f), GameConfig.Hex("9BE8FF"), 0.22f, -35f);
                    break;
                case 8: // 光纱
                    for (int k = 0; k < 4; k++)
                        Band(new Vector2(0f, 1.0f + k * 2.4f), new Vector2(24f, 3.2f),
                             GameConfig.Hex("FF8AD1"), 0.14f - k * 0.02f, 4f - k * 3f);
                    break;
                default: // 核心弧环
                    for (int k = 0; k < 4; k++)
                        Ring(new Vector2(0f, 6f), 2.4f + k * 1.3f, GameConfig.Hex("FF6F91"), 0.35f - k * 0.06f);
                    break;
            }
        }

        void Disc(Vector2 pos, float radius, Color c, float alpha, bool rock = false)
        {
            var go = new GameObject("Feature");
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = rock ? RuntimeSprites.Rock(64) : RuntimeSprites.Disc(96);
            sr.color = new Color(c.r, c.g, c.b, alpha);
            sr.sortingOrder = -90;
            go.transform.localScale = Vector3.one * radius * 2f;
        }

        void Ring(Vector2 pos, float radius, Color c, float alpha)
        {
            var go = new GameObject("Ring");
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprites.Ring(128);
            sr.color = new Color(c.r, c.g, c.b, alpha);
            sr.sortingOrder = -91;
            go.transform.localScale = Vector3.one * radius * 2f;
        }

        void Band(Vector2 pos, Vector2 size, Color c, float alpha, float angle)
        {
            var go = new GameObject("Band");
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprites.SoftRect(64);
            sr.color = new Color(c.r, c.g, c.b, alpha);
            sr.sortingOrder = -92;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
        }
    }
}
