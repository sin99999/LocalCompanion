namespace LocalCompanion.Models;

/// <summary>キャラクター自己改善の提案（未承認。保存は呼び出し側がユーザー同意後に行う）。</summary>
public sealed record CharacterSelfImproveProposal(
    string PresetFileName,
    string CharacterName,
    string CurrentPersona,
    string ProposedPersona,
    string Reason,
    string DiffPreview);
