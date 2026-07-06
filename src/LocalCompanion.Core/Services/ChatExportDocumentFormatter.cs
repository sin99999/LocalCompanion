using System.Text.Json;
using System.Text.RegularExpressions;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>チャット回答を保存用文書（短いタイトル＋読みやすい本文）に LLM で整形する。</summary>
internal static class ChatExportDocumentFormatter
{
    private const int MaxTitleChars = 36;
    private const int MaxReplyInputChars = 12_000;
    private const int MaxBodyOutputTokens = 3072;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<ChatExportDocument> FormatAsync(
        LlamaServerClient llama,
        ChatExportRequest export,
        string chatReply,
        string[]? ragSources,
        bool japanese,
        CancellationToken ct)
    {
        var replyForPrompt = chatReply.Length > MaxReplyInputChars
            ? chatReply[..MaxReplyInputChars] + "\n\n…"
            : chatReply;

        var messages = new List<ChatTurn>
        {
            new("system", BuildSystemPrompt(export.Extension, japanese)),
            new("user", BuildUserPrompt(export, replyForPrompt, ragSources, japanese)),
        };

        try
        {
            var raw = await llama.ChatAsync(
                messages,
                temperature: 0.35,
                topP: 0.9,
                maxTokens: MaxBodyOutputTokens,
                useReasoning: false,
                ct: ct);

            if (TryParseResponse(raw, out var document))
                return document with { Title = NormalizeTitle(document.Title, export) };
        }
        catch
        {
            // 整形失敗時はチャット原文をそのまま保存する。
        }

        return CreateFallback(export, chatReply);
    }

    internal static bool TryParseResponse(string raw, out ChatExportDocument document)
    {
        document = new ChatExportDocument("", "");
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var json = ExtractJsonObject(raw);
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("title", out var titleEl)
                || !root.TryGetProperty("body", out var bodyEl))
                return false;

            var title = titleEl.GetString()?.Trim() ?? "";
            var body = bodyEl.GetString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
                return false;

            document = new ChatExportDocument(title, body);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildSystemPrompt(string extension, bool japanese)
    {
        var ext = ChatTextExportFormats.NormalizeExtension(extension);
        var bodyGuide = ext switch
        {
            ".txt" or ".log" or ".ini" or ".cfg" =>
                japanese
                    ? "本文はプレーンテキスト。見出しは【】や番号付きで。Markdown記法は使わない。"
                    : "Body is plain text. Use simple headings; no Markdown.",
            ".csv" =>
                japanese
                    ? "本文はCSVとして有効な表形式（ヘッダー行＋データ行）。"
                    : "Body must be valid CSV (header row + data rows).",
            ".json" =>
                japanese
                    ? "body は JSON 文字列としてパース可能な内容にする（エスケープを正しく）。"
                    : "body must be valid JSON text.",
            ".html" or ".htm" =>
                japanese
                    ? "body は HTML フラグメント（<h2> 見出し、<p> 段落、<ul> 箇条書き）。"
                    : "body is an HTML fragment with headings, paragraphs, and lists.",
            ".xml" =>
                japanese
                    ? "body は XML として整った内容にする。"
                    : "body must be well-formed XML content.",
            _ =>
                japanese
                    ? "本文は Markdown（# 見出し、段落、箇条書き、表）。会話調は避け、資料として読める形に。"
                    : "Body uses Markdown (headings, paragraphs, lists, tables). Document tone, not chat.",
        };

        var jsonShape = "{\"title\":\"...\",\"body\":\"...\"}";
        return japanese
            ? $"""
               あなたは調査メモをデスクトップ保存用に整形するエディタです。
               出力は必ず次の JSON のみ（説明・前置き・コードフェンス禁止）:
               {jsonShape}

               title:
               - {MaxTitleChars} 文字以内
               - 依頼内容を要約した短い題名（ファイル名に使う。記号は控えめ）
               - ユーザーの全文コピー禁止

               body:
               - {bodyGuide}
               - チャットの口調（「〜ですね」「保存しました」等）は除く
               - 依頼を分析し、要点・条文・手順などを整理して読みやすく
               - 事実は与えられた回答に忠実。無い内容は創作しない
               """.Trim()
            : $"""
               You format research notes for desktop export.
               Output JSON only (no prose, no code fences):
               {jsonShape}

               title: max {MaxTitleChars} chars, short summary for a filename
               body: {bodyGuide}
               Remove chat filler; organize for reading; stay faithful to the source answer.
               """.Trim();
    }

    private static string BuildUserPrompt(
        ChatExportRequest export,
        string chatReply,
        string[]? ragSources,
        bool japanese)
    {
        var sources = ragSources is { Length: > 0 }
            ? string.Join("\n", ragSources.Distinct(StringComparer.OrdinalIgnoreCase))
            : japanese ? "（なし）" : "(none)";

        return japanese
            ? $"""
               保存形式: {ChatTextExportFormats.NormalizeExtension(export.Extension)}
               ユーザーの依頼: {export.Query}

               チャットでの回答（整形の元）:
               {chatReply}

               参考にした資料:
               {sources}
               """.Trim()
            : $"""
               Format: {ChatTextExportFormats.NormalizeExtension(export.Extension)}
               User request: {export.Query}

               Chat answer (source material):
               {chatReply}

               Reference sources:
               {sources}
               """.Trim();
    }

    private static string ExtractJsonObject(string raw)
    {
        var fenced = Regex.Match(
            raw,
            @"```(?:json)?\s*(\{.*?\})\s*```",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (fenced.Success)
            return fenced.Groups[1].Value.Trim();

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start >= 0 && end > start)
            return raw[start..(end + 1)];

        return "";
    }

    private static string NormalizeTitle(string title, ChatExportRequest export)
    {
        if (!string.IsNullOrWhiteSpace(export.FileNameStem))
            return SanitizeTitleStem(export.FileNameStem);

        return SanitizeTitleStem(title);
    }

    private static string SanitizeTitleStem(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "export";

        var stem = value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            stem = stem.Replace(c, '_');

        stem = stem.Replace(' ', '_');
        stem = stem.Trim('_', '.', '。', '、', '！', '？', '!', '?');
        if (stem.Length > MaxTitleChars)
            stem = stem[..MaxTitleChars].TrimEnd('_', '.');

        return string.IsNullOrWhiteSpace(stem) ? "export" : stem;
    }

    private static ChatExportDocument CreateFallback(ChatExportRequest export, string chatReply)
    {
        var title = SanitizeTitleStem(export.FileNameStem);
        if (title == "export")
            title = SanitizeTitleStem(export.Query);
        return new ChatExportDocument(title, chatReply.Trim());
    }
}
