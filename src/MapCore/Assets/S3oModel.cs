using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace MapCore.Assets;

/// <summary>A model vertex: position (game units, x east, y up, z south), normal and texture coordinate.</summary>
public readonly record struct ModelVertex(Vector3 Position, Vector3 Normal, Vector2 Uv);

/// <summary>A model as triangles, with every piece moved to where it sits (S3O pieces are only offset from their parent).</summary>
public sealed record ModelMesh(IReadOnlyList<ModelVertex> Vertices, IReadOnlyList<int> Triangles, string? Texture1, string? Texture2, int Pieces)
{
    public int TriangleCount => Triangles.Count / 3;
}

/// <summary>
/// The S3O model format (the engine's own; rts/Rendering/Models/s3o.h and S3OParser): a 52-byte header ("Spring unit\0",
/// a version, radius, height, a centre, then offsets to the root piece, the collision data and the two texture names),
/// then pieces. A piece has a name, its children (an array of offsets), its vertices (position, normal, uv: 32 bytes
/// each), a primitive type (0 triangles, 1 triangle strips ended by -1, 2 quads), its index table and its offset from
/// its parent. The engine looks the texture names up under unittextures/.
/// </summary>
public static class S3oModel
{
    const int HeaderSize = 52, PieceSize = 52, VertexSize = 32;
    const int MaxPieces = 10_000, MaxVertices = 4_000_000, MaxIndices = 12_000_000;
    static ReadOnlySpan<byte> Magic => "Spring unit\0"u8;

    /// <summary>The two texture names (null where empty), or null if this isn't an S3O file.</summary>
    public static (string? Texture1, string? Texture2)? ReadTextures(byte[] data)
    {
        if (data.Length < HeaderSize || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            return null;
        return (Name(data, Int(data, 44)), Name(data, Int(data, 48)));
    }

    /// <summary>
    /// The whole model as triangles, or null if this isn't an S3O file. A file that says it is one but whose offsets
    /// or counts don't fit throws <see cref="InvalidDataException"/>; models are untrusted, so every offset is checked
    /// and a piece can't be visited twice (a loop of children would never end).
    /// </summary>
    public static ModelMesh? Load(byte[] data)
    {
        if (ReadTextures(data) is not var (t1, t2))
            return null;
        var vertices = new List<ModelVertex>();
        var triangles = new List<int>();
        var visited = new HashSet<int>();
        var pending = new Stack<(int Offset, Vector3 Origin)>();
        if (Int(data, 36) != 0) // 0: no pieces at all
            pending.Push((Int(data, 36), Vector3.Zero));
        while (pending.Count > 0)
        {
            var (offset, parent) = pending.Pop();
            Check(data, offset, PieceSize, "piece");
            if (!visited.Add(offset))
                throw new InvalidDataException("the model's pieces loop back on themselves");
            if (visited.Count > MaxPieces)
                throw new InvalidDataException($"the model has over {MaxPieces:N0} pieces");
            var origin = parent + new Vector3(Float(data, offset + 40), Float(data, offset + 44), Float(data, offset + 48));

            int childCount = Int(data, offset + 4), childTable = Int(data, offset + 8);
            if (childCount < 0 || childCount > MaxPieces)
                throw new InvalidDataException("a piece has an impossible number of children");
            if (childCount > 0)
                Check(data, childTable, childCount * 4L, "child table");
            for (int i = childCount - 1; i >= 0; i--)
                pending.Push((Int(data, childTable + i * 4), origin));

            int vertexCount = Int(data, offset + 12), vertexTable = Int(data, offset + 16);
            int primitive = Int(data, offset + 24), indexCount = Int(data, offset + 28), indexTable = Int(data, offset + 32);
            if (vertexCount < 0 || vertices.Count + (long)vertexCount > MaxVertices || indexCount < 0 || triangles.Count + (long)indexCount * 2 > MaxIndices)
                throw new InvalidDataException("a piece has an impossible number of vertices or indices");
            if (vertexCount == 0 || indexCount == 0)
                continue;
            Check(data, vertexTable, (long)vertexCount * VertexSize, "vertex table");
            Check(data, indexTable, indexCount * 4L, "index table");
            int first = vertices.Count;
            for (int i = 0; i < vertexCount; i++)
            {
                int v = vertexTable + i * VertexSize;
                vertices.Add(new ModelVertex(
                    origin + new Vector3(Float(data, v), Float(data, v + 4), Float(data, v + 8)),
                    new Vector3(Float(data, v + 12), Float(data, v + 16), Float(data, v + 20)),
                    new Vector2(Float(data, v + 24), Float(data, v + 28))));
            }
            int Index(int i) => Int(data, indexTable + i * 4);
            void Triangle(int a, int b, int c)
            {
                if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount || a == b || b == c || a == c)
                    return; // strip separators, degenerate triangles and bad indices draw nothing
                triangles.Add(first + a);
                triangles.Add(first + b);
                triangles.Add(first + c);
            }
            switch (primitive)
            {
                case 0:
                    for (int i = 0; i + 2 < indexCount; i += 3)
                        Triangle(Index(i), Index(i + 1), Index(i + 2));
                    break;
                case 1: // strips; -1 ends one, and every other triangle is wound the other way
                    for (int i = 0, start = 0; i + 2 < indexCount; i++)
                    {
                        int a = Index(i), b = Index(i + 1), c = Index(i + 2);
                        if (a == -1)
                        {
                            start = i + 1;
                            continue;
                        }
                        if ((i - start) % 2 == 0)
                            Triangle(a, b, c);
                        else
                            Triangle(b, a, c);
                    }
                    break;
                case 2:
                    for (int i = 0; i + 3 < indexCount; i += 4)
                    {
                        Triangle(Index(i), Index(i + 1), Index(i + 2));
                        Triangle(Index(i), Index(i + 2), Index(i + 3));
                    }
                    break;
                default:
                    throw new InvalidDataException($"a piece has an unknown primitive type {primitive}");
            }
        }
        return new ModelMesh(vertices, triangles, t1, t2, visited.Count);
    }

    static void Check(byte[] data, int offset, long length, string what)
    {
        if (offset < HeaderSize || offset + length > data.Length)
            throw new InvalidDataException($"the model's {what} lies outside the file");
    }

    static int Int(byte[] data, int offset) =>
        offset >= 0 && offset + 4 <= data.Length ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset))
            : throw new InvalidDataException("the model is cut short");

    static float Float(byte[] data, int offset) =>
        offset >= 0 && offset + 4 <= data.Length ? BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset))
            : throw new InvalidDataException("the model is cut short");

    static string? Name(byte[] data, int offset)
    {
        if (offset <= 0 || offset >= data.Length)
            return null;
        int end = Array.IndexOf(data, (byte)0, offset);
        var text = Encoding.Latin1.GetString(data, offset, (end < 0 ? data.Length : end) - offset).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// A one-piece S3O (triangles), with its header's radius, height and centre worked out from the vertices (the
    /// bounding box's half diagonal, top and middle) as model tools write them.
    /// </summary>
    public static byte[] Write(string texture1, string texture2, string pieceName, IReadOnlyList<ModelVertex> vertices, IReadOnlyList<int> triangles)
    {
        if (vertices.Count == 0 || triangles.Count % 3 != 0 || triangles.Any(i => i < 0 || i >= vertices.Count))
            throw new ArgumentException("A model needs vertices, and whole triangles that index them.");
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in vertices)
        {
            min = Vector3.Min(min, v.Position);
            max = Vector3.Max(max, v.Position);
        }
        var data = Make(texture1, texture2, [(-1, Vector3.Zero, vertices.ToArray(), triangles.ToArray())]);
        var named = new byte[data.Length + Encoding.Latin1.GetByteCount(pieceName) + 1];
        data.CopyTo(named, 0);
        Encoding.Latin1.GetBytes(pieceName).CopyTo(named, data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(named.AsSpan(Int(named, 36)), data.Length); // the root piece's name
        var mid = (min + max) / 2;
        BinaryPrimitives.WriteSingleLittleEndian(named.AsSpan(16), (max - min).Length() / 2);
        BinaryPrimitives.WriteSingleLittleEndian(named.AsSpan(20), max.Y);
        BinaryPrimitives.WriteSingleLittleEndian(named.AsSpan(24), mid.X);
        BinaryPrimitives.WriteSingleLittleEndian(named.AsSpan(28), mid.Y);
        BinaryPrimitives.WriteSingleLittleEndian(named.AsSpan(32), mid.Z);
        return named;
    }

    /// <summary>A minimal S3O with the given texture names and no pieces (for tests).</summary>
    public static byte[] Make(string texture1, string texture2) => Make(texture1, texture2, []);

    /// <summary>
    /// An S3O with the given texture names and pieces (for tests): each piece is triangles, with its offset from its
    /// parent (the piece at <c>Parent</c>, -1 for the root, which must come first).
    /// </summary>
    public static byte[] Make(string texture1, string texture2, IReadOnlyList<(int Parent, Vector3 Offset, ModelVertex[] Vertices, int[] Triangles)> pieces)
    {
        var body = new List<byte>(new byte[HeaderSize]);
        void PutInt(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(body).Slice(at), value);
        void PutFloat(int at, float value) => BinaryPrimitives.WriteSingleLittleEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(body).Slice(at), value);
        int Append(byte[] bytes)
        {
            int at = body.Count;
            body.AddRange(bytes);
            return at;
        }
        var pieceAt = new int[pieces.Count];
        for (int p = 0; p < pieces.Count; p++)
            pieceAt[p] = Append(new byte[PieceSize]);
        for (int p = 0; p < pieces.Count; p++)
        {
            var (_, offset, verts, tris) = pieces[p];
            var children = Enumerable.Range(0, pieces.Count).Where(c => pieces[c].Parent == p).ToList();
            int childTable = Append(new byte[children.Count * 4]);
            for (int c = 0; c < children.Count; c++)
                PutInt(childTable + c * 4, pieceAt[children[c]]);
            int vertexTable = Append(new byte[verts.Length * VertexSize]);
            for (int v = 0; v < verts.Length; v++)
            {
                var (pos, normal, uv) = verts[v];
                float[] values = [pos.X, pos.Y, pos.Z, normal.X, normal.Y, normal.Z, uv.X, uv.Y];
                for (int k = 0; k < 8; k++)
                    PutFloat(vertexTable + v * VertexSize + k * 4, values[k]);
            }
            int indexTable = Append(new byte[tris.Length * 4]);
            for (int i = 0; i < tris.Length; i++)
                PutInt(indexTable + i * 4, tris[i]);
            int at = pieceAt[p];
            PutInt(at + 4, children.Count);
            PutInt(at + 8, childTable);
            PutInt(at + 12, verts.Length);
            PutInt(at + 16, vertexTable);
            PutInt(at + 24, 0);
            PutInt(at + 28, tris.Length);
            PutInt(at + 32, indexTable);
            PutFloat(at + 40, offset.X);
            PutFloat(at + 44, offset.Y);
            PutFloat(at + 48, offset.Z);
        }
        int t1 = Append(Encoding.Latin1.GetBytes(texture1 + "\0"));
        int t2 = Append(Encoding.Latin1.GetBytes(texture2 + "\0"));
        Magic.ToArray().CopyTo(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(body));
        PutInt(36, pieces.Count > 0 ? pieceAt[0] : 0);
        PutInt(44, t1);
        PutInt(48, t2);
        return [.. body];
    }
}
