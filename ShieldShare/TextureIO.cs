using System;
using System.IO;
using System.Reflection;
using Jotunn.Utils;
using UnityEngine;

namespace ShieldShare
{
    /// <remarks>
    ///     This class deliberately never names UnityEngine.ImageConversion in code. That type lives in
    ///     UnityEngine.ImageConversionModule, which Unity 6 builds against netstandard 2.1; a net48 project
    ///     only has netstandard 2.0, so a direct call fails to compile with CS1705. Loading goes through
    ///     Jotunn's AssetUtils wrapper instead, and PNG encoding is looked up by reflection.
    /// </remarks>
    internal static class TextureIO
    {
        private static MethodInfo encodeToPng;
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
            if (!AssetUtils.LoadImage(tex, File.ReadAllBytes(path)))
            {
                UnityEngine.Object.Destroy(tex);
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
                File.WriteAllBytes(path, EncodeToPng(tex));
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        private static byte[] EncodeToPng(Texture2D tex)
        {
            if (encodeToPng == null)
            {
                var type = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", true);
                encodeToPng = type.GetMethod("EncodeToPNG", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Texture2D) }, null);
                if (encodeToPng == null)
                    throw new MissingMethodException("UnityEngine.ImageConversion", "EncodeToPNG");
            }
            return (byte[])encodeToPng.Invoke(null, new object[] { tex });
        }

        public static Sprite ToSprite(Texture2D tex)
        {
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
    }
}
