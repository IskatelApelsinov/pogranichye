using System.Collections.Generic;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Small tileable textures generated in code (no imported assets): fur, cloth, leather, wood, hide.
    /// They are grey-scale detail maps meant to be multiplied by the part's base colour, so a flat-coloured
    /// primitive reads as a surface with grain instead of plastic. Cached per kind; 64×64, bilinear, repeat.
    /// </summary>
    public static class ProceduralTextures
    {
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public static Texture2D Get(string kind)
        {
            Texture2D texture;
            if (Cache.TryGetValue(kind, out texture) && texture != null) return texture;
            texture = Build(kind);
            Cache[kind] = texture;
            return texture;
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519u);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        // Tileable value noise: lattice wraps at `cells`.
        private static float Noise(float u, float v, int cells, int seed)
        {
            var x = u * cells;
            var y = v * cells;
            var x0 = Mathf.FloorToInt(x);
            var y0 = Mathf.FloorToInt(y);
            var fx = x - x0;
            var fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            var a = Hash(x0 % cells, y0 % cells, seed);
            var b = Hash((x0 + 1) % cells, y0 % cells, seed);
            var c = Hash(x0 % cells, (y0 + 1) % cells, seed);
            var d = Hash((x0 + 1) % cells, (y0 + 1) % cells, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static Texture2D Build(string kind)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.name = "Procedural " + kind;
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var u = x / (float)size;
                    var v = y / (float)size;
                    float value;
                    switch (kind)
                    {
                        case "fur": // short strokes running along V
                            value = 0.78f + 0.22f * Noise(u, v, 32, 11) * Noise(u, v * 0.25f, 8, 12) + 0.1f * Noise(u, v, 4, 13);
                            break;
                        case "cloth": // woven threads
                            value = 0.82f + 0.09f * Mathf.Sin(u * Mathf.PI * 2f * 16f) * Mathf.Sin(v * Mathf.PI * 2f * 16f) + 0.1f * Noise(u, v, 8, 21);
                            break;
                        case "leather": // soft blotches with fine pores
                            value = 0.8f + 0.14f * Noise(u, v, 6, 31) + 0.08f * Noise(u, v, 32, 32);
                            break;
                        case "wood": // grain along U with a few darker rings
                            value = 0.78f + 0.16f * Noise(u * 0.25f, v, 16, 41) + 0.08f * Mathf.Sin((v + 0.2f * Noise(u, v, 4, 42)) * Mathf.PI * 2f * 6f);
                            break;
                        default: // "hide": coarse, uneven
                            value = 0.8f + 0.2f * Noise(u, v, 8, 51);
                            break;
                    }
                    value = Mathf.Clamp01(value);
                    pixels[y * size + x] = new Color(value, value, value, 1f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(true, false);
            return texture;
        }
    }
}
