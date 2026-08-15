using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Okf.Spike.Vectorization;

/// <summary>
/// The sqlite-vec side of the spike: a `vec0` virtual table holding one row per
/// concept, plus the metadata a search result needs. Deliberately small — the point
/// is to prove the plumbing (extension load, create, insert, KNN), not to design the
/// production schema.
/// </summary>
internal sealed class VecStore : IDisposable
{
    private readonly SqliteConnection connection;

    private VecStore(SqliteConnection connection) => this.connection = connection;

    /// <summary>Opens (or creates) a store and loads the vec0 extension into it.</summary>
    public static VecStore Open(string path, int dimensions, bool reset = false)
    {
        if (reset && File.Exists(path))
        {
            File.Delete(path);
        }

        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        // Extension loading is off by default in Microsoft.Data.Sqlite; the property
        // toggles sqlite3_enable_load_extension around the load.
        connection.EnableExtensions(true);
        connection.LoadExtension(ResolveVec0Path());

        Execute(
            connection,
            $"""
            CREATE TABLE IF NOT EXISTS concepts (
                id        INTEGER PRIMARY KEY,
                bundle    TEXT NOT NULL,
                path      TEXT NOT NULL,
                title     TEXT NOT NULL,
                sha256    TEXT NOT NULL,
                UNIQUE (bundle, path)
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS concept_vectors USING vec0(
                concept_id INTEGER PRIMARY KEY,
                embedding  FLOAT[{dimensions.ToString(CultureInfo.InvariantCulture)}] distance_metric=cosine
            );
            """);

        return new VecStore(connection);
    }

    /// <summary>The sqlite-vec build actually loaded, straight from the extension.</summary>
    public string VecVersion() => (string)Scalar("SELECT vec_version();")!;

    /// <summary>The SQLite build underneath it.</summary>
    public string SqliteVersion() => (string)Scalar("SELECT sqlite_version();")!;

    /// <summary>Upserts one concept and its embedding.</summary>
    public void Put(string bundle, string path, string title, string sha256, ReadOnlySpan<float> embedding)
    {
        using var transaction = this.connection.BeginTransaction();

        using (var upsert = this.connection.CreateCommand())
        {
            upsert.CommandText = """
                INSERT INTO concepts (bundle, path, title, sha256)
                VALUES ($bundle, $path, $title, $sha256)
                ON CONFLICT (bundle, path) DO UPDATE SET title = $title, sha256 = $sha256;
                """;
            upsert.Parameters.AddWithValue("$bundle", bundle);
            upsert.Parameters.AddWithValue("$path", path);
            upsert.Parameters.AddWithValue("$title", title);
            upsert.Parameters.AddWithValue("$sha256", sha256);
            upsert.ExecuteNonQuery();
        }

        long id;
        using (var lookup = this.connection.CreateCommand())
        {
            lookup.CommandText = "SELECT id FROM concepts WHERE bundle = $bundle AND path = $path;";
            lookup.Parameters.AddWithValue("$bundle", bundle);
            lookup.Parameters.AddWithValue("$path", path);
            id = (long)lookup.ExecuteScalar()!;
        }

        using (var vector = this.connection.CreateCommand())
        {
            // vec0 accepts a BLOB of little-endian float32 for a FLOAT[n] column.
            // Delete-then-insert rather than UPSERT: vec0 raises "UPSERT not implemented
            // for virtual table" — a real constraint a production writer must design for.
            vector.CommandText = """
                DELETE FROM concept_vectors WHERE concept_id = $id;
                INSERT INTO concept_vectors (concept_id, embedding) VALUES ($id, $embedding);
                """;
            vector.Parameters.AddWithValue("$id", id);
            vector.Parameters.AddWithValue("$embedding", ToBlob(embedding));
            vector.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>K-nearest-neighbour query, cosine distance, nearest first.</summary>
    public IReadOnlyList<VecHit> Nearest(ReadOnlySpan<float> query, int k)
    {
        using var command = this.connection.CreateCommand();
        command.CommandText = """
            SELECT c.bundle, c.path, c.title, v.distance
            FROM concept_vectors v
            JOIN concepts c ON c.id = v.concept_id
            WHERE v.embedding MATCH $query AND k = $k
            ORDER BY v.distance;
            """;
        command.Parameters.AddWithValue("$query", ToBlob(query));
        command.Parameters.AddWithValue("$k", k);

        var hits = new List<VecHit>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            hits.Add(new VecHit(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3)));
        }

        return hits;
    }

    /// <summary>Every (bundle, path) -> sha256 already indexed, for invalidation checks.</summary>
    public Dictionary<string, string> Fingerprints()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var command = this.connection.CreateCommand();
        command.CommandText = "SELECT bundle, path, sha256 FROM concepts;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            map[reader.GetString(0) + " " + reader.GetString(1)] = reader.GetString(2);
        }

        return map;
    }

    public void Dispose() => this.connection.Dispose();

    private object? Scalar(string sql)
    {
        using var command = this.connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static byte[] ToBlob(ReadOnlySpan<float> values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        for (var i = 0; i < values.Length; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(i * sizeof(float)), values[i]);
        }

        return bytes;
    }

    /// <summary>
    /// Where the native extension is. The sqlite-vec NuGet package drops
    /// <c>vec0.so</c> beside the app via its runtimes/&lt;rid&gt;/native asset, so the
    /// bare name "vec0" resolves for a normal publish; an explicit path is used so
    /// the same code works from a NativeAOT publish directory and from `dotnet run`.
    /// </summary>
    private static string ResolveVec0Path()
    {
        var name = OperatingSystem.IsWindows() ? "vec0.dll"
            : OperatingSystem.IsMacOS() ? "vec0.dylib"
            : "vec0.so";

        var beside = Path.Combine(AppContext.BaseDirectory, name);
        return File.Exists(beside) ? beside : "vec0";
    }
}

/// <summary>One KNN hit.</summary>
internal readonly record struct VecHit(string Bundle, string Path, string Title, double Distance)
{
    /// <summary>Cosine similarity, i.e. what a fusion score would actually use.</summary>
    public double Similarity => 1.0 - Distance;
}
