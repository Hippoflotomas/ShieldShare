using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShieldShare
{
    /// <summary>
    ///     One front-facing triangle of the shield mesh, known in two coordinate spaces:
    ///     <list type="bullet">
    ///         <item>Uv*: where the triangle sits in the mesh's own (vanilla) UV layout - this is where
    ///         the shader will look it up in the style atlas cell.</item>
    ///         <item>P*: where the triangle sits on the pack author's flat pattern image (a straight-on
    ///         "photo" of the shield face, normalised to 0..1).</item>
    ///     </list>
    /// </summary>
    internal struct FrontTriangle
    {
        public Vector2 UvA, UvB, UvC;
        public Vector2 PA, PB, PC;
    }

    /// <summary>
    ///     Everything we need to know about a base shield mesh to bake flat pattern images into
    ///     its vanilla UV layout. Pure math - no Unity textures - so it can be unit tested.
    /// </summary>
    internal sealed class FrontProjection
    {
        /// <summary>
        ///     A triangle is "front" when its average vertex normal points at least this much along the
        ///     face direction. Faces point along it (~0.9); rims point sideways (~0).
        /// </summary>
        public const float FrontThreshold = 0.5f;

        public readonly List<FrontTriangle> Front = new List<FrontTriangle>();
        public readonly List<Vector2[]> OtherUvTriangles = new List<Vector2[]>();
        public float MinX, MaxX, MinY, MaxY;
        /// <summary>-1 = the face points down local -Z (wood, banded), +1 = local +Z (silver, wood tower).</summary>
        public int FaceSign;
        public bool FlipVertical;
        public int FrontTrianglesOutsideUnitUv;
        public int TotalTriangles;

        public float AspectRatio => (MaxX - MinX) / Mathf.Max(MaxY - MinY, 0.0001f);

        /// <param name="faceSign">-1 if the outside of the shield faces local -Z, +1 if it faces +Z.</param>
        /// <param name="flipVertical">Turn the pattern upside down (for models built the other way up).</param>
        public static FrontProjection Build(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] triangles,
            int faceSign = -1, bool flipVertical = false)
        {
            if (vertices == null || uvs == null || triangles == null)
                throw new ArgumentNullException("Mesh data missing (vertices/uvs/triangles).");
            if (uvs.Length != vertices.Length)
                throw new ArgumentException($"Mesh has {vertices.Length} vertices but {uvs.Length} UVs - it has no usable UV layout.");

            bool haveNormals = normals != null && normals.Length == vertices.Length;
            faceSign = faceSign >= 0 ? 1 : -1;
            var proj = new FrontProjection { TotalTriangles = triangles.Length / 3, FaceSign = faceSign, FlipVertical = flipVertical };
            var frontTris = new List<int>();

            // Pass 1: classify each TRIANGLE (not vertex) as front or not. Deciding per triangle is what
            // stops rim triangles from being half-projected and smeared across the pattern.
            proj.MinX = float.MaxValue; proj.MaxX = float.MinValue;
            proj.MinY = float.MaxValue; proj.MaxY = float.MinValue;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (FacingZ(vertices, normals, haveNormals, a, b, c) * faceSign > FrontThreshold)
                {
                    frontTris.Add(t);
                    foreach (int i in new[] { a, b, c })
                    {
                        if (vertices[i].x < proj.MinX) proj.MinX = vertices[i].x;
                        if (vertices[i].x > proj.MaxX) proj.MaxX = vertices[i].x;
                        if (vertices[i].y < proj.MinY) proj.MinY = vertices[i].y;
                        if (vertices[i].y > proj.MaxY) proj.MaxY = vertices[i].y;
                    }
                }
                else
                {
                    proj.OtherUvTriangles.Add(new[] { uvs[a], uvs[b], uvs[c] });
                }
            }

            if (frontTris.Count == 0)
                return proj;

            float rangeX = Mathf.Max(proj.MaxX - proj.MinX, 0.0001f);
            float rangeY = Mathf.Max(proj.MaxY - proj.MinY, 0.0001f);

            // Pass 2: planar-project the front triangles onto the pattern image. Same mapping the old
            // ApplyPlanarFrontUV used (which was confirmed in game to show the image the right way round).
            foreach (int t in frontTris)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                var ft = new FrontTriangle
                {
                    UvA = uvs[a], UvB = uvs[b], UvC = uvs[c],
                    PA = proj.Planar(vertices[a], rangeX, rangeY),
                    PB = proj.Planar(vertices[b], rangeX, rangeY),
                    PC = proj.Planar(vertices[c], rangeX, rangeY),
                };
                if (!InUnit(ft.UvA) || !InUnit(ft.UvB) || !InUnit(ft.UvC))
                    proj.FrontTrianglesOutsideUnitUv++;
                proj.Front.Add(ft);
            }

            return proj;
        }

        /// <summary>Average normal Z of a triangle (or its geometric normal if the mesh has no normals).</summary>
        public static float FacingZ(Vector3[] vertices, Vector3[] normals, bool haveNormals, int a, int b, int c)
        {
            if (haveNormals)
                return (normals[a].z + normals[b].z + normals[c].z) / 3f;
            return Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized.z;
        }

        /// <summary>
        ///     Straight-on projection of the face onto the pattern image, as seen from outside the shield.
        ///     For a -Z face the viewer's right is +X; for a +Z face it is -X, so U is mirrored or the
        ///     artwork would come out back to front. V keeps the mapping confirmed in game on the wood shield.
        /// </summary>
        private Vector2 Planar(Vector3 v, float rangeX, float rangeY)
        {
            float u = (v.x - MinX) / rangeX;
            float vv = 1f - (v.y - MinY) / rangeY;
            if (FaceSign > 0) u = 1f - u;
            if (FlipVertical) vv = 1f - vv;
            return new Vector2(u, vv);
        }

        /// <summary>
        ///     Fraction of the triangles facing <paramref name="faceSign"/> whose UVs land on painted pixels
        ///     in ANY cell of the vanilla 4x4 style atlas. The vanilla artists only painted the outside face,
        ///     so the side with the higher fraction is the outside. Returns -1 if no triangles face that way.
        /// </summary>
        public static float PaintedFraction(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] triangles, int faceSign,
            Color32[] styleAtlas, int atlasSize, byte alphaThreshold = 32)
        {
            bool haveNormals = normals != null && normals.Length == vertices.Length;
            int facing = 0, painted = 0;
            int cell = atlasSize / 4;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (FacingZ(vertices, normals, haveNormals, a, b, c) * faceSign <= FrontThreshold)
                    continue;
                facing++;
                var centre = (uvs[a] + uvs[b] + uvs[c]) / 3f;
                int px = Mathf.Clamp((int)(Mathf.Clamp01(centre.x) * cell), 0, cell - 1);
                int py = Mathf.Clamp((int)(Mathf.Clamp01(centre.y) * cell), 0, cell - 1);
                for (int i = 0; i < 16; i++)
                {
                    if (styleAtlas[((i / 4) * cell + py) * atlasSize + (i % 4) * cell + px].a > alphaThreshold)
                    {
                        painted++;
                        break;
                    }
                }
            }
            return facing == 0 ? -1f : painted / (float)facing;
        }

        private static bool InUnit(Vector2 uv)
        {
            const float eps = 0.001f;
            return uv.x >= -eps && uv.x <= 1f + eps && uv.y >= -eps && uv.y <= 1f + eps;
        }
    }

    /// <summary>
    ///     Pixel-level helpers: triangle rasterising and texture sampling on plain Color32 arrays.
    ///     Arrays use Unity's layout: index = y * width + x, with y = 0 at the BOTTOM row.
    /// </summary>
    internal static class StyleBaker
    {
        public delegate void PixelVisitor(int x, int y, float wa, float wb, float wc);

        /// <summary>
        ///     Visits every pixel whose centre lies inside triangle (a, b, c), or within
        ///     <paramref name="padPixels"/> of its edges. a/b/c are in normalised 0..1 coordinates.
        ///     The small pad closes hairline gaps between neighbouring triangles.
        /// </summary>
        public static void RasterizeTriangle(Vector2 a, Vector2 b, Vector2 c, int width, int height,
            float padPixels, PixelVisitor visit)
        {
            var A = new Vector2(a.x * width, a.y * height);
            var B = new Vector2(b.x * width, b.y * height);
            var C = new Vector2(c.x * width, c.y * height);

            float area2 = Cross(B - A, C - A);
            if (Mathf.Abs(area2) < 1e-6f)
                return; // degenerate (zero-area) triangle

            // Altitudes: distance from each vertex to the opposite edge, in pixels. Used to turn
            // barycentric weights into "how many pixels outside this edge am I".
            float hA = Mathf.Abs(area2) / Mathf.Max((C - B).magnitude, 1e-6f);
            float hB = Mathf.Abs(area2) / Mathf.Max((A - C).magnitude, 1e-6f);
            float hC = Mathf.Abs(area2) / Mathf.Max((B - A).magnitude, 1e-6f);

            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x)) - padPixels));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x)) + padPixels));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y)) - padPixels));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y)) + padPixels));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var P = new Vector2(x + 0.5f, y + 0.5f);
                    float wa = Cross(C - B, P - B) / area2;
                    float wb = Cross(A - C, P - C) / area2;
                    float wc = 1f - wa - wb;

                    if (wa * hA < -padPixels || wb * hB < -padPixels || wc * hC < -padPixels)
                        continue;

                    visit(x, y, wa, wb, wc);
                }
            }
        }

        /// <summary>Bilinear sample with clamped edges. u, v in 0..1, v = 0 is the bottom row.</summary>
        public static Color32 SampleBilinear(Color32[] pixels, int width, int height, float u, float v)
        {
            float fx = Mathf.Clamp(u * width - 0.5f, 0f, width - 1);
            float fy = Mathf.Clamp(v * height - 0.5f, 0f, height - 1);
            int x0 = (int)fx, y0 = (int)fy;
            int x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
            float tx = fx - x0, ty = fy - y0;

            Color32 c00 = pixels[y0 * width + x0], c10 = pixels[y0 * width + x1];
            Color32 c01 = pixels[y1 * width + x0], c11 = pixels[y1 * width + x1];
            return new Color32(
                Lerp2(c00.r, c10.r, c01.r, c11.r, tx, ty),
                Lerp2(c00.g, c10.g, c01.g, c11.g, tx, ty),
                Lerp2(c00.b, c10.b, c01.b, c11.b, tx, ty),
                Lerp2(c00.a, c10.a, c01.a, c11.a, tx, ty));
        }

        /// <summary>
        ///     Bakes one flat pattern image into one cell of the style atlas, at the positions the
        ///     vanilla UVs of the shield FACE point to. Everything else in the cell is left untouched
        ///     (transparent), so the back, rim and strap keep showing the plain base texture.
        /// </summary>
        public static int BakeCell(FrontProjection projection, Color32[] pattern, int patternWidth, int patternHeight,
            Color32[] atlas, int atlasWidth, int cellX, int cellY, int cellSize)
        {
            int written = 0;
            foreach (var tri in projection.Front)
            {
                var t = tri;
                RasterizeTriangle(t.UvA, t.UvB, t.UvC, cellSize, cellSize, 0.75f, (x, y, wa, wb, wc) =>
                {
                    float u = t.PA.x * wa + t.PB.x * wb + t.PC.x * wc;
                    float v = t.PA.y * wa + t.PB.y * wb + t.PC.y * wc;
                    atlas[(cellY + y) * atlasWidth + cellX + x] = SampleBilinear(pattern, patternWidth, patternHeight, u, v);
                    written++;
                });
            }
            return written;
        }

        /// <summary>
        ///     Rasterises the front face as it appears on the flat pattern image (true = on the shield).
        ///     Used for generated icons and the author template.
        /// </summary>
        public static bool[] FrontMask(FrontProjection projection, int size)
        {
            var mask = new bool[size * size];
            foreach (var t in projection.Front)
                RasterizeTriangle(t.PA, t.PB, t.PC, size, size, 0.5f, (x, y, wa, wb, wc) => mask[y * size + x] = true);
            return mask;
        }

        /// <summary>Alpha-blends <paramref name="top"/> over <paramref name="bottom"/>.</summary>
        public static Color32 Over(Color32 top, Color32 bottom)
        {
            float ta = top.a / 255f, ba = bottom.a / 255f;
            float outA = ta + ba * (1f - ta);
            if (outA <= 0f) return new Color32(0, 0, 0, 0);
            Func<byte, byte, byte> mix = (tc, bc) => (byte)Mathf.Clamp(Mathf.RoundToInt((tc * ta + bc * ba * (1f - ta)) / outA), 0, 255);
            return new Color32(mix(top.r, bottom.r), mix(top.g, bottom.g), mix(top.b, bottom.b), (byte)Mathf.RoundToInt(outA * 255f));
        }

        public static void DrawLine(Color32[] pixels, int width, int height, Vector2 a, Vector2 b, Color32 color)
        {
            float x0 = a.x * width, y0 = a.y * height, x1 = b.x * width, y1 = b.y * height;
            int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0))) + 1;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                int x = Mathf.Clamp((int)(x0 + (x1 - x0) * t), 0, width - 1);
                int y = Mathf.Clamp((int)(y0 + (y1 - y0) * t), 0, height - 1);
                pixels[y * width + x] = color;
            }
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static byte Lerp2(byte c00, byte c10, byte c01, byte c11, float tx, float ty)
        {
            float top = c00 + (c10 - c00) * tx;
            float bottom = c01 + (c11 - c01) * tx;
            return (byte)Mathf.Clamp(Mathf.RoundToInt(top + (bottom - top) * ty), 0, 255);
        }
    }
}
