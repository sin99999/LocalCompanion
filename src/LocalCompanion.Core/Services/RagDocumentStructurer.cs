using LocalCompanion.Data;
using LocalCompanion.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalCompanion.Services;

/// <summary>ローカル LLM で PDF/HTML 等の生テキストを Markdown 風に構造化する。</summary>
public sealed class RagDocumentStructurer
{
    private const int DefaultWindowChars = 3500;

    private readonly LlamaServerClient _llama;
    private readonly LlamaOptions _opt;
    private readonly RagStructurerCache _cache;
    private readonly ILogger<RagDocumentStructurer> _log;

    public RagDocumentStructurer(
        LlamaServerClient llama,
        IOptions<LlamaOptions> opt,
        RagDatabase db,
        ILogger<RagDocumentStructurer> log)
    {
        _llama = llama;
        _opt = opt.Value;
        _cache = new RagStructurerCache(db.DataDirectory);
        _log = log;
    }

    public async Task<string> StructureAsync(
        string source,
        string text,
        RagDocumentKind docKind,
        RagIngestOptions options,
        CancellationToken ct)
    {
        if (!options.UseLlmStructurer || string.IsNullOrWhiteSpace(text))
            return text;

        if (IsAlreadyStructured(source, text))
            return text;

        if (options.SaveStructurerCache)
        {
            var cached = _cache.TryLoad(source, text);
            if (!string.IsNullOrWhiteSpace(cached))
                return cached;
        }

        if (!await _llama.PingAsync(ct))
        {
            _log.LogWarning("RAG structurer skipped: llama-server unavailable.");
            return text;
        }

        var window = Math.Clamp(_opt.RagStructurerWindowChars, 1500, 12000);
        var windows = SplitWindows(text, window);
        if (windows.Count == 0)
            return text;

        var parts = new List<string>(windows.Count);
        for (var i = 0; i < windows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var structured = await StructureWindowAsync(source, windows[i], i + 1, windows.Count, docKind, ct);
            parts.Add(structured);
        }

        var merged = string.Join("\n\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        if (string.IsNullOrWhiteSpace(merged))
            return text;

        if (options.SaveStructurerCache)
            _cache.Save(source, text, merged);

        return merged;
    }

    private async Task<string> StructureWindowAsync(
        string source,
        string windowText,
        int index,
        int total,
        RagDocumentKind docKind,
        CancellationToken ct)
    {
        var fileName = Path.GetFileName(source);
        var kindHint = docKind switch
        {
            RagDocumentKind.Legal => "法令・規程文書",
            RagDocumentKind.Glossary => "用語集",
            _ => "一般文書",
        };

        var system = """
            あなたは資料整形専用アシスタントです。入力テキストを RAG 取込向け Markdown に変換してください。
            - 見出しは # / ## / ### を使う
            - 第N条・条番号は原文どおり維持（推測で書き換えない）
            - 箇条書きは - を使う
            - 説明や前置きは出力しない。変換結果の Markdown のみ
            - 不明な部分は …（欠落）とせず原文をそのまま残す
            """;

        var user = $"""
            資料名: {fileName}
            種別: {kindHint}
            パート: {index}/{total}

            --- 入力 ---
            {windowText}
            """;

        try
        {
            var reply = await _llama.ChatAsync(
                [new ChatTurn("system", system), new ChatTurn("user", user)],
                temperature: 0.15,
                topP: 0.9,
                topK: 40,
                maxTokens: Math.Min(_opt.MaxOutputTokens, 4096),
                useReasoning: false,
                ct: ct);

            return StripCodeFence(reply);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "RAG structurer window {Index}/{Total} failed for {Source}", index, total, source);
            return windowText;
        }
    }

    internal static IReadOnlyList<string> SplitWindows(string text, int maxChars)
    {
        if (text.Length <= maxChars)
            return [text];

        var windows = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var len = Math.Min(maxChars, text.Length - start);
            if (start + len < text.Length)
            {
                var slice = text.AsSpan(start, len);
                var breakAt = slice.LastIndexOf("\n\n");
                if (breakAt > maxChars / 3)
                    len = breakAt;
            }

            windows.Add(text.Substring(start, len).Trim());
            start += len;
            while (start < text.Length && (text[start] == '\n' || text[start] == '\r'))
                start++;
        }

        return windows;
    }

    private static bool IsAlreadyStructured(string source, string text)
    {
        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (ext is ".md" or ".markdown")
            return true;

        var sample = text.Length > 4000 ? text[..4000] : text;
        var headingCount = sample.Split('\n').Count(l => l.TrimStart().StartsWith('#'));
        return headingCount >= 3;
    }

    private static string StripCodeFence(string reply)
    {
        var trimmed = reply.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
            return trimmed;

        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence <= firstNewline)
            return trimmed[(firstNewline + 1)..].Trim();

        return trimmed[(firstNewline + 1)..lastFence].Trim();
    }
}
