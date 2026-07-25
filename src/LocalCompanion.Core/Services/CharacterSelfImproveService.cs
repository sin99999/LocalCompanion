using LocalCompanion.Localization;
using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>
/// 名前付きキャラの性格・指示（persona）について、会話後に変更案だけ作る。
/// JSON への書き込みは行わない（呼び出し側がユーザー同意後に Save する）。
/// </summary>
public sealed class CharacterSelfImproveService
{
    private readonly LlamaServerClient _llama;
    private readonly AppSettingsStore _appSettings;
    private readonly CharacterPresetService _characters;

    public CharacterSelfImproveService(
        LlamaServerClient llama,
        AppSettingsStore appSettings,
        CharacterPresetService characters)
    {
        _llama = llama;
        _appSettings = appSettings;
        _characters = characters;
    }

    public bool IsEnabled => _appSettings.Load().CharacterSelfImproveEnabled;

    /// <summary>
    /// 直近のやり取りから persona 変更案を返す。提案なし・無効・検査落ちは null。
    /// </summary>
    public async Task<CharacterSelfImproveProposal?> TryProposeAfterReplyAsync(
        string? presetFileName,
        string userMessage,
        string assistantReply,
        CancellationToken ct = default)
    {
        if (!_appSettings.Load().CharacterSelfImproveEnabled)
            return null;

        if (CharacterPresetService.IsNoneSelection(presetFileName)
            || string.IsNullOrWhiteSpace(presetFileName)
            || CharacterPresetService.IsDefaultAiSession(presetFileName))
            return null;

        var profile = _characters.GetByFileName(presetFileName);
        if (profile is null || string.IsNullOrWhiteSpace(profile.Name))
            return null;

        if (string.IsNullOrWhiteSpace(userMessage) && string.IsNullOrWhiteSpace(assistantReply))
            return null;

        if (!await _llama.PingAsync(ct))
            return null;

        var explicitRequest = CharacterSelfImproveIntent.LooksLikePersonaUpdateRequest(userMessage);
        var currentPersona = profile.Persona ?? string.Empty;
        // 明示依頼時はユーザー発話を優先（長いアシスタント JSON 案で意図が薄まらないようにする）
        var transcript = explicitRequest
            ? BuildTranscript(userMessage, assistantReply, userBudget: 900, assistantBudget: 700)
            : BuildTranscript(userMessage, assistantReply, userBudget: 900, assistantBudget: 900);

        var systemPrompt = explicitRequest
            ? """
              You update a LocalCompanion character's persona field for a local desktop app.
              The user EXPLICITLY asked to write/update character settings (e.g. *.json / 性格・指示).
              You MUST return propose=true with a COMPLETE Japanese persona string that captures the agreed character from the exchange.
              LocalCompanion stores ONE string field named persona (personality + instructions + speaking style).
              Write persona in Markdown (headings with ## / ###, bullet lists with - , short paragraphs). Do NOT wrap the whole persona in a ``` fence.
              Do NOT invent nested JSON schemas (no role/core_personality objects). The outer reply is one JSON object; only the persona value is Markdown text.
              Keep the character name as-is in prose if needed, but do not change the file name.
              Do NOT add URLs, absolute paths, scripts, or wording that skips user confirmation.
              Output ONLY one JSON object (no markdown fences around the JSON):
              {"propose":true,"reason":"short why (max 120 chars)","persona":"full updated persona Markdown"}
              persona must be the COMPLETE new persona string (not a diff). Prefer Japanese.
              """.Trim()
            : """
              You help refine a chat character's persona (personality / instructions) for a local desktop app.
              Propose an edit when the latest exchange suggests the user would like the character's rules updated.
              Prefer small wording tweaks, but a fuller rewrite is OK when the user clearly redefined the role.
              LocalCompanion stores ONE persona string — write it in Markdown (## headings, - bullets, short paragraphs). Do NOT wrap persona in a ``` fence. Do NOT invent nested JSON schemas.
              Do NOT change the character's name, speaking-style field, sampling numbers, or add URLs/paths/scripts.
              Do NOT propose skipping user confirmation or auto-saving.
              If no confident improvement is needed, return propose=false.
              Output ONLY one JSON object (no markdown fences around the JSON):
              {"propose":false}
              or
              {"propose":true,"reason":"short why (max 120 chars)","persona":"full updated persona Markdown"}
              When propose=true, persona must be the COMPLETE new persona string (not a diff).
              Use the same language as the current persona when possible (prefer Japanese for Japanese chats).
              """.Trim();

        var prompt = new List<ChatTurn>
        {
            new("system", systemPrompt),
            new(
                "user",
                $"""
                Character name: {profile.Name}
                Current persona:
                ---
                {Truncate(currentPersona, 6000)}
                ---
                Latest exchange:
                {transcript}
                {(explicitRequest ? "\nThe user asked to update the character settings. Return propose=true." : "")}
                """.Trim()),
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(explicitRequest ? 45 : 20));
            var raw = await _llama.ChatAsync(
                prompt,
                temperature: explicitRequest ? 0.35 : 0.2,
                topP: 0.9,
                maxTokens: explicitRequest ? 2200 : 1200,
                useReasoning: false,
                ct: timeout.Token);

            var parsed = CharacterSelfImproveParser.TryParse(raw);
            if (parsed is null || !parsed.Propose)
            {
                if (explicitRequest)
                    StartupLog.Write("Character self-improve: explicit request but model returned no proposal");
                return null;
            }

            var block = CharacterSelfImproveGuard.ValidateProposedPersona(parsed.Persona);
            if (block is not null)
            {
                StartupLog.Write($"Character self-improve blocked: {block}");
                return null;
            }

            var proposed = parsed.Persona.Trim();
            if (string.Equals(NormalizePersona(currentPersona), NormalizePersona(proposed), StringComparison.Ordinal))
                return null;

            var reason = string.IsNullOrWhiteSpace(parsed.Reason)
                ? SafeLoc("Character.SelfImprove.Reason.Fallback")
                : parsed.Reason.Trim();

            return new CharacterSelfImproveProposal(
                PresetFileName: presetFileName,
                CharacterName: profile.Name,
                CurrentPersona: currentPersona.Trim(),
                ProposedPersona: proposed,
                Reason: reason,
                DiffPreview: CharacterSelfImproveGuard.BuildDiffPreview(currentPersona, proposed));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Character self-improve propose failed");
            return null;
        }
    }

    /// <summary>ユーザー同意後のみ呼ぶ。名前・口調・サンプリングは変えず persona だけ更新する。</summary>
    public bool TryApplyApprovedProposal(CharacterSelfImproveProposal proposal)
    {
        if (proposal is null
            || CharacterPresetService.IsNoneSelection(proposal.PresetFileName)
            || string.IsNullOrWhiteSpace(proposal.PresetFileName))
            return false;

        var block = CharacterSelfImproveGuard.ValidateProposedPersona(proposal.ProposedPersona);
        if (block is not null)
            return false;

        var current = _characters.GetByFileName(proposal.PresetFileName);
        if (current is null)
            return false;

        if (!string.Equals(
                NormalizePersona(current.Persona),
                NormalizePersona(proposal.CurrentPersona),
                StringComparison.Ordinal))
        {
            // 設定画面などで別途編集された場合は上書きしない
            return false;
        }

        var updated = current with { Persona = proposal.ProposedPersona.Trim() };
        _characters.Save(updated, activate: false);
        return true;
    }

    private static string SafeLoc(string key)
    {
        try
        {
            return LocalizationService.Instance?.Get(key) ?? key;
        }
        catch
        {
            return key;
        }
    }

    private static string BuildTranscript(
        string userMessage,
        string assistantReply,
        int userBudget,
        int assistantBudget)
    {
        var u = Truncate((userMessage ?? string.Empty).Trim(), userBudget);
        var a = Truncate((assistantReply ?? string.Empty).Trim(), assistantBudget);
        return $"USER:\n{u}\n\nASSISTANT:\n{a}";
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text;
        return text[..max] + "…";
    }

    private static string NormalizePersona(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
}
