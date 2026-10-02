namespace LocalCompanion.Services;

/// <summary>
/// 取り込み中だけ、断片につき1回聞く。検索中・チャット中は呼ばない。
/// null は聞けなかった（次の取り込みでやり直す）。空文字は聞いて捨てた。
/// </summary>
internal sealed class RagSearchQuestionWriter
{
    public const int MinPassageChars = 24;
    private const int MaxPassageChars = 1500;

    private readonly LlamaServerClient _llama;

    public RagSearchQuestionWriter(LlamaServerClient llama) => _llama = llama;

    public async Task<string?> TryWriteAsync(string passage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(passage) || passage.Trim().Length < MinPassageChars)
            return "";

        var clipped = passage.Trim();
        if (clipped.Length > MaxPassageChars)
            clipped = clipped[..MaxPassageChars];

        string reply;
        try
        {
            reply = await _llama.ChatAsync(
                [
                    new ChatTurn("system", """
                        資料の断片から、その断片を読めば答えられる質問を1つ作る。
                        断片にその言葉が無くてもよい。断片に無い数字や条番号は入れない。
                        出力は次の2行だけ。説明は書かない。
                        QUESTION: （1行の疑問文）
                        GROUNDED: YES または NO
                        質問が作れないときは QUESTION: NONE と GROUNDED: NO
                        """),
                    new ChatTurn("user", clipped),
                ],
                temperature: 0.1,
                topP: 0.9,
                topK: 40,
                maxTokens: 80,
                useReasoning: false,
                ct: ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }

        return RagSearchQuestion.TryAccept(clipped, reply) ?? "";
    }
}
