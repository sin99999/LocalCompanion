using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace LocalCompanion.Data;

/// <summary>同じ断片の質問を、次の取り込みで聞き直さない。</summary>
internal static class RagSearchQuestionCache
{
    public static void EnsureTable(SqliteConnection conn)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS rag_search_questions (
              passage_hash TEXT PRIMARY KEY,
              question TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public static string Hash(string passage)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(passage));
        return Convert.ToHexString(bytes);
    }

    public static string? TryLoad(SqliteConnection conn, string passageHash)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT question FROM rag_search_questions WHERE passage_hash = $h LIMIT 1";
        cmd.Parameters.AddWithValue("$h", passageHash);
        var value = cmd.ExecuteScalar();
        return value is string question ? question : null;
    }

    public static void Save(SqliteConnection conn, string passageHash, string question)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO rag_search_questions (passage_hash, question) VALUES ($h, $q)
            ON CONFLICT(passage_hash) DO UPDATE SET question = excluded.question
            """;
        cmd.Parameters.AddWithValue("$h", passageHash);
        cmd.Parameters.AddWithValue("$q", question);
        cmd.ExecuteNonQuery();
    }
}
