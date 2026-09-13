namespace LocalCompanion.Models;

/// <summary>概念の種類（重みづけと表示に使う）。</summary>
public enum RagConceptKind
{
    /// <summary>本文から拾った語。</summary>
    Term,

    /// <summary>見出しの語。</summary>
    Heading,

    /// <summary>条番号（第235条）。</summary>
    Article,

    /// <summary>用語集・定義の見出し語（entry_key）。</summary>
    Entry,
}

/// <summary>1 チャンクに現れた概念と回数。</summary>
public sealed record RagConceptOccurrence(string Term, string NormTerm, RagConceptKind Kind, int Count);

/// <summary>1 チャンク分の抽出結果。</summary>
public sealed record RagChunkConcepts(long ChunkId, IReadOnlyList<RagConceptOccurrence> Concepts);

/// <summary>概念どうしの辺（source 単位。合計はクエリ側で SUM）。</summary>
public sealed record RagConceptEdgeDraft(string NormA, string NormB, string Kind, double Weight)
{
    public const string KindCooccurrence = "cooc";
    public const string KindRefers = "refers";
}

/// <summary>グラフ索引に渡す 1 チャンクの材料（rag_chunks の必要列だけ）。</summary>
public sealed record RagGraphChunkInput(
    long ChunkId,
    string Source,
    string HeaderText,
    string Text,
    string EntryKey,
    int ArticleMain,
    string DocKind);

/// <summary>コミュニティ 1 つ（level 0 が細かい束、level 1 がその親）。</summary>
public sealed record RagCommunity(
    long Id,
    int Level,
    long? ParentId,
    string Label,
    int Size,
    IReadOnlyList<long> MemberConceptIds);

/// <summary>設定画面に出すグラフの状況。</summary>
public sealed record RagGraphStats(int ConceptCount, int EdgeCount, int CommunityCount, string? BuiltAt);
