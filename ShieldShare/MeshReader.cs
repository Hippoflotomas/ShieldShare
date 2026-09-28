using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShieldShare
{
    /// <summary>A plain copy of the mesh data the baker needs.</summary>
    internal sealed class MeshSnapshot
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;            // may be null
        public Vector2[] Uvs;
        public List<int[]> SubMeshTriangles = new List<int[]>();
        public bool FromGpu;
    }

    /// <summary>
    ///     Reads vertices, normals, UV0 and triangles from a mesh - including meshes that the game ships
    ///     without a CPU copy (Mesh.isReadable == false, e.g. the blackmetal and flametal shields).
    ///     Those only exist on the graphics card, so their vertex and index buffers are copied back
    ///     from the GPU and decoded by hand.
    /// </summary>
    internal static class MeshReader
    {
        public static MeshSnapshot Read(Mesh mesh)
        {
            return mesh.isReadable ? ReadFromCpu(mesh) : ReadFromGpu(mesh);
        }

        private static MeshSnapshot ReadFromCpu(Mesh mesh)
        {
            var snap = new MeshSnapshot
            {
                Vertices = mesh.vertices,
                Normals = mesh.normals,
                Uvs = mesh.uv,
            };
            for (int i = 0; i < mesh.subMeshCount; i++)
                snap.SubMeshTriangles.Add(mesh.GetSubMesh(i).topology == MeshTopology.Triangles ? mesh.GetTriangles(i) : new int[0]);
            return snap;
        }

        private static MeshSnapshot ReadFromGpu(Mesh mesh)
        {
            int vertexCount = mesh.vertexCount;
            var streams = new Dictionary<int, byte[]>();
            Func<int, byte[]> streamBytes = s =>
            {
                byte[] bytes;
                if (!streams.TryGetValue(s, out bytes))
                {
                    using (var buffer = mesh.GetVertexBuffer(s))
                    {
                        if (buffer == null)
                            throw new InvalidOperationException($"no GPU vertex buffer for stream {s}");
                        bytes = new byte[buffer.count * buffer.stride];
                        buffer.GetData(bytes);
                    }
                    streams[s] = bytes;
                }
                return bytes;
            };

            float[] pos = ReadAttribute(mesh, VertexAttribute.Position, 3, vertexCount, streamBytes);
            float[] nrm = ReadAttribute(mesh, VertexAttribute.Normal, 3, vertexCount, streamBytes);
            float[] uv0 = ReadAttribute(mesh, VertexAttribute.TexCoord0, 2, vertexCount, streamBytes);
            if (pos == null || uv0 == null)
                throw new InvalidOperationException("mesh has no positions or no UVs");

            var snap = new MeshSnapshot { FromGpu = true, Vertices = new Vector3[vertexCount], Uvs = new Vector2[vertexCount] };
            for (int i = 0; i < vertexCount; i++)
            {
                snap.Vertices[i] = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                snap.Uvs[i] = new Vector2(uv0[i * 2], uv0[i * 2 + 1]);
            }
            if (nrm != null)
            {
                snap.Normals = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                    snap.Normals[i] = new Vector3(nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2]);
            }

            int[] indices;
            using (var ib = mesh.GetIndexBuffer())
            {
                if (ib == null)
                    throw new InvalidOperationException("no GPU index buffer");
                var bytes = new byte[ib.count * ib.stride];
                ib.GetData(bytes);
                bool wide = mesh.indexFormat == IndexFormat.UInt32;
                indices = new int[bytes.Length / (wide ? 4 : 2)];
                for (int i = 0; i < indices.Length; i++)
                    indices[i] = wide ? (int)BitConverter.ToUInt32(bytes, i * 4) : BitConverter.ToUInt16(bytes, i * 2);
            }

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var desc = mesh.GetSubMesh(s);
                if (desc.topology != MeshTopology.Triangles)
                {
                    snap.SubMeshTriangles.Add(new int[0]);
                    continue;
                }
                var tris = new int[desc.indexCount];
                for (int i = 0; i < desc.indexCount; i++)
                {
                    int v = indices[desc.indexStart + i] + desc.baseVertex;
                    if (v < 0 || v >= vertexCount)
                        throw new InvalidOperationException($"index {v} out of range in sub-mesh {s}");
                    tris[i] = v;
                }
                snap.SubMeshTriangles.Add(tris);
            }
            return snap;
        }

        /// <summary>Decodes one vertex attribute into floats (wantDims per vertex), or null if the mesh lacks it.</summary>
        private static float[] ReadAttribute(Mesh mesh, VertexAttribute attr, int wantDims, int vertexCount, Func<int, byte[]> streamBytes)
        {
            if (!mesh.HasVertexAttribute(attr))
                return null;

            int stream = mesh.GetVertexAttributeStream(attr);
            int offset = mesh.GetVertexAttributeOffset(attr);
            int dims = mesh.GetVertexAttributeDimension(attr);
            var format = mesh.GetVertexAttributeFormat(attr);
            int stride = mesh.GetVertexBufferStride(stream);
            int size = FormatSize(format);
            byte[] data = streamBytes(stream);

            var result = new float[vertexCount * wantDims];
            int n = Math.Min(dims, wantDims);
            for (int v = 0; v < vertexCount; v++)
            {
                int start = v * stride + offset;
                for (int c = 0; c < n; c++)
                    result[v * wantDims + c] = Decode(data, start + c * size, format);
            }
            return result;
        }

        private static int FormatSize(VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                case VertexAttributeFormat.UInt32:
                case VertexAttributeFormat.SInt32:
                    return 4;
                case VertexAttributeFormat.Float16:
                case VertexAttributeFormat.UNorm16:
                case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt16:
                case VertexAttributeFormat.SInt16:
                    return 2;
                default:
                    return 1;
            }
        }

        private static float Decode(byte[] d, int i, VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32: return BitConverter.ToSingle(d, i);
                case VertexAttributeFormat.Float16: return Mathf.HalfToFloat(BitConverter.ToUInt16(d, i));
                case VertexAttributeFormat.UNorm16: return BitConverter.ToUInt16(d, i) / 65535f;
                case VertexAttributeFormat.SNorm16: return Math.Max(BitConverter.ToInt16(d, i) / 32767f, -1f);
                case VertexAttributeFormat.UInt16: return BitConverter.ToUInt16(d, i);
                case VertexAttributeFormat.SInt16: return BitConverter.ToInt16(d, i);
                case VertexAttributeFormat.UInt32: return BitConverter.ToUInt32(d, i);
                case VertexAttributeFormat.SInt32: return BitConverter.ToInt32(d, i);
                case VertexAttributeFormat.UNorm8: return d[i] / 255f;
                case VertexAttributeFormat.SNorm8: return Math.Max((sbyte)d[i] / 127f, -1f);
                case VertexAttributeFormat.UInt8: return d[i];
                case VertexAttributeFormat.SInt8: return (sbyte)d[i];
                default: throw new NotSupportedException("vertex format " + format);
            }
        }
    }
}
