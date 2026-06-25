using LocalCompanion.Models;

namespace LocalCompanion.Services;

/// <summary>チャット用の現在有効キャラ（characters/*.json）へのアクセス。</summary>
public sealed class CharacterRepository
{
    private readonly CharacterPresetService _presets;

    public CharacterRepository(CharacterPresetService presets) => _presets = presets;

    public CharacterProfileDto Get() => _presets.GetActive();

    /// <summary>JSON に保存するだけ（チャットの選択は変えない）。</summary>
    public string Save(CharacterProfileDto p) => _presets.Save(p, activate: false);
}
