#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AuroraRift.EditorTools
{
    /// <summary>
    /// Resources/Ships 下的 PNG 是程序化生成的高精度精灵：关压缩、关 mipmap、
    /// 双线性过滤，避免安卓 ASTC 把金属渐变压出块状色阶。
    /// </summary>
    public class ShipArtImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            string p = assetPath.Replace('\\', '/');

            // 应用图标：同样关压缩，否则桌面图标边缘会糊
            if (p.Contains("/Icons/"))
            {
                var ic = (TextureImporter)assetImporter;
                ic.textureType = TextureImporterType.Default;
                ic.alphaIsTransparency = true;
                ic.mipmapEnabled = false;
                ic.filterMode = FilterMode.Bilinear;
                ic.wrapMode = TextureWrapMode.Clamp;
                ic.textureCompression = TextureImporterCompression.Uncompressed;
                ic.sRGBTexture = true;
                ic.maxTextureSize = 1024;
                return;
            }

            if (!p.Contains("/Resources/Ships/")) return;

            var imp = (TextureImporter)assetImporter;
            imp.textureType = TextureImporterType.Default;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.sRGBTexture = true;
            imp.maxTextureSize = 1024;
        }
    }
}
#endif
