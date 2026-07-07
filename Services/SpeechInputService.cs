using LocalCompanion.Localization;
using LocalCompanion.Models;
using Windows.Media.SpeechRecognition;

namespace LocalCompanion.Services;

/// <summary>Windows 組み込み音声認識による音声入力（ローカル・クラウド API キー不要）。</summary>
public sealed class SpeechInputService
{
    private readonly AppAppearanceService _appearance;
    private SpeechRecognizer? _recognizer;
    private int _languageGeneration = -1;

    public SpeechInputService(AppAppearanceService appearance)
    {
        _appearance = appearance;
    }

    public bool IsEnabled => _appearance.Current.SpeechInputEnabled;

    public async Task<string?> RecognizeOnceAsync(CancellationToken ct = default)
    {
        if (!IsEnabled)
            return null;

        var recognizer = await GetRecognizerAsync(ct);
        if (recognizer is null)
            return null;

        var result = await recognizer.RecognizeWithUIAsync();
        return result.Status == SpeechRecognitionResultStatus.Success
            ? result.Text?.Trim()
            : null;
    }

    private async Task<SpeechRecognizer?> GetRecognizerAsync(CancellationToken ct)
    {
        var langGen = LocalizationService.Instance.Current == AppLanguage.Japanese ? 0 : 1;
        if (_recognizer is not null && _languageGeneration == langGen)
            return _recognizer;

        _recognizer?.Dispose();
        _recognizer = null;

        try
        {
            var languageTag = langGen == 0 ? "ja-JP" : "en-US";
            _recognizer = new SpeechRecognizer(new Windows.Globalization.Language(languageTag));
            await _recognizer.CompileConstraintsAsync();
            _languageGeneration = langGen;
            return _recognizer;
        }
        catch
        {
            return null;
        }
    }
}
