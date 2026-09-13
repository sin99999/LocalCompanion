namespace LocalCompanion.Services;

/// <summary>
/// 俯瞰検索が使う短い LLM 呼び出し（thinking は常に off、平文で受ける）。テストは Fake を差す。
/// 失敗・時間切れは例外ではなく null で返す（呼び出し側が退化版へ落とす）。
/// </summary>
public interface IRagGraphLlm
{
    Task<string?> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct);
}

/// <summary>llama-server 経由の実装。</summary>
public sealed class LlamaGraphLlmAdapter : IRagGraphLlm
{
    private readonly LlamaServerClient _llama;

    public LlamaGraphLlmAdapter(LlamaServerClient llama)
    {
        _llama = llama;
    }

    public async Task<string?> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct)
    {
        try
        {
            var turns = new List<ChatTurn>
            {
                new("system", system),
                new("user", user),
            };
            return await _llama.ChatAsync(turns, temperature: 0.1, topP: 0.9, maxTokens: maxTokens, useReasoning: false, ct: ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
