using System.IO;
using UnityEngine;

namespace ShieldShare
{
    internal static class TextureIO
    {
        /// <summary>
        ///     Loads a PNG or JPG (any capitalisation of the extension). Returns null on failure.
        ///     <paramref name="linear"/> must be true for data textures such as normal maps, which must
        ///     not be treated as sRGB colour.
        /// </summary>
        public static Texture2D Load(string path, bool linear = false, bool mipmaps = true)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipmaps, linear);
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path)))
            {
                Object.Destroy(tex);
                Jotunn.Logger.LogWarning($"[ShieldShare] '{path}' is not a readable PNG/JPG image.");
                return null;
            }
            tex.name = Path.GetFileNameWithoutExtension(path);
            return tex;
        }

        public static Texture2D FromPixels(Color32[] pixels, int width, int height, bool mipmaps)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipmaps);
            tex.SetPixels32(pixels);
            tex.Apply(mipmaps);
            return tex;
        }

        public static void SavePng(Color32[] pixels, int width, int height, string path)
        {
            var tex = FromPixels(pixels, width, height, false);
            try
            {
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
            }
            finally
            {
                Object.Destroy(tex);
            }
        }

        public static Sprite ToSprite(Texture2D tex)
        {
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
    }
}
