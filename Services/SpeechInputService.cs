using LocalCompanion.Localization;
using LocalCompanion.Models;
using Windows.Foundation;
using Windows.Media.Capture;
using Windows.Media.SpeechRecognition;

namespace LocalCompanion.Services;

/// <summary>Windows 組み込み音声認識による音声入力（ローカル・クラウド API キー不要）。</summary>
public sealed class SpeechInputService
{
    // MediaCapture: no audio capture devices (0xC00DABE0)
    private const uint NoCaptureDevicesHResult = 0xC00DABE0;
    // Speech privacy statement declined (0x80045509)
    private const uint SpeechPrivacyDeclinedHResult = 0x80045509;

    private readonly AppAppearanceService _appearance;
    private readonly object _gate = new();
    private SpeechRecognizer? _recognizer;
    private IAsyncOperation<SpeechRecognitionResult>? _operation;
    private CancellationTokenSource? _sessionCts;

    public SpeechInputService(AppAppearanceService appearance)
    {
        _appearance = appearance;
    }

    public bool IsEnabled => _appearance.Current.SpeechInputEnabled;

    public bool IsListening
    {
        get
        {
            lock (_gate)
                return _sessionCts is not null;
        }
    }

    public event EventHandler? ListeningChanged;

    /// <summary>進行中の認識を止め、マイクを解放する。</summary>
    public void Cancel()
    {
        CancellationTokenSource? cts;
        IAsyncOperation<SpeechRecognitionResult>? operation;
        SpeechRecognizer? recognizer;
        lock (_gate)
        {
            cts = _sessionCts;
            _sessionCts = null;
            operation = _operation;
            _operation = null;
            recognizer = _recognizer;
            _recognizer = null;
        }

        TryCancelOperation(operation);
        TryDispose(cts);
        TryDispose(recognizer);
        ListeningChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 1 回分の音声認識。既に認識中ならキャンセルして null を返す（トグルOFF）。
    /// 戻り値の LanguageTag は実際に使った認識言語（フォールバック後）。
    /// </summary>
    public async Task<(string? Text, string? LanguageTag)> RecognizeOnceDetailedAsync(CancellationToken ct = default)
    {
        if (!IsEnabled)
            throw new LocalizedServiceException("Chat.SpeechInput.Failed");

        if (IsListening)
        {
            Cancel();
            return (null, null);
        }

        await EnsureMicrophoneAccessAsync().ConfigureAwait(true);

        SpeechRecognizer? recognizer = null;
        CancellationTokenSource? sessionCts = null;
        IAsyncOperation<SpeechRecognitionResult>? operation = null;
        string? languageTag = null;

        try
        {
            sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            (recognizer, languageTag) = await CreateRecognizerAsync(sessionCts.Token);

            lock (_gate)
            {
                _recognizer = recognizer;
                _sessionCts = sessionCts;
            }

            ListeningChanged?.Invoke(this, EventArgs.Empty);

            operation = recognizer.RecognizeAsync();
            lock (_gate)
                _operation = operation;

            using (sessionCts.Token.Register(() => TryCancelOperation(operation)))
            {
                var result = await operation.AsTask(sessionCts.Token);
                return (MapRecognitionResult(result), languageTag);
            }
        }
        catch (OperationCanceledException)
        {
            return (null, languageTag);
        }
        catch (LocalizedServiceException)
        {
            throw;
        }
        catch (Exception ex) when (IsMicrophoneAccessFailure(ex))
        {
            throw new LocalizedServiceException("Chat.SpeechInput.MicrophoneOff");
        }
        catch (Exception ex) when (IsNoCaptureDevice(ex))
        {
            throw new LocalizedServiceException("Chat.SpeechInput.NoMicrophone");
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_operation, operation))
                    _operation = null;
                if (ReferenceEquals(_recognizer, recognizer))
                    _recognizer = null;
                if (ReferenceEquals(_sessionCts, sessionCts))
                {
                    _sessionCts = null;
                    TryDispose(sessionCts);
                }
            }

            TryDispose(recognizer);
            ListeningChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 1 回分の音声認識。既に認識中ならキャンセルして null を返す（トグルOFF）。
    /// マイク未許可などは <see cref="LocalizedServiceException"/> を投げる。
    /// 終了時は必ず SpeechRecognizer を破棄し、マイク／オーディオダッキングを解放する。
    /// </summary>
    public async Task<string?> RecognizeOnceAsync(CancellationToken ct = default)
    {
        var (text, _) = await RecognizeOnceDetailedAsync(ct);
        return text;
    }

    private static async Task<(SpeechRecognizer Recognizer, string LanguageTag)> CreateRecognizerAsync(
        CancellationToken ct)
    {
        Exception? lastError = null;
        foreach (var languageTag in EnumerateRecognizerLanguageCandidates())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var recognizer = new SpeechRecognizer(new Windows.Globalization.Language(languageTag));
                await recognizer.CompileConstraintsAsync();
                return (recognizer, languageTag);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
        }

        if (lastError is not null)
            throw lastError;

        throw new LocalizedServiceException("Chat.SpeechInput.Failed");
    }

    /// <summary>
    /// UI 言語を優先し、未インストールの音声パック（例: en-US）なら ja-JP 等へフォールバックする。
    /// </summary>
    private static IEnumerable<string> EnumerateRecognizerLanguageCandidates()
    {
        var preferred = LocalizationService.Instance.Current == AppLanguage.Japanese
            ? "ja-JP"
            : "en-US";

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in new[] { preferred, "ja-JP", "en-US" })
        {
            if (string.IsNullOrWhiteSpace(tag) || !seen.Add(tag))
                continue;
            yield return tag;
        }

        foreach (var language in SpeechRecognizer.SupportedGrammarLanguages)
        {
            var tag = language.LanguageTag;
            if (string.IsNullOrWhiteSpace(tag) || !seen.Add(tag))
                continue;
            yield return tag;
        }
    }

    private static string? MapRecognitionResult(SpeechRecognitionResult result)
    {
        switch (result.Status)
        {
            case SpeechRecognitionResultStatus.Success:
                return string.IsNullOrWhiteSpace(result.Text) ? null : result.Text.Trim();
            case SpeechRecognitionResultStatus.UserCanceled:
                return null;
            case SpeechRecognitionResultStatus.MicrophoneUnavailable:
                throw new LocalizedServiceException("Chat.SpeechInput.MicrophoneOff");
            default:
                throw new LocalizedServiceException("Chat.SpeechInput.Failed");
        }
    }

    /// <summary>
    /// プライバシー設定のマイク許可を確認する。成功時はすぐ破棄して掴みっぱなしにしない。
    /// </summary>
    private static async Task EnsureMicrophoneAccessAsync()
    {
        try
        {
            var capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Audio,
                MediaCategory = MediaCategory.Speech
            });
            capture.Dispose();
        }
        catch (UnauthorizedAccessException)
        {
            throw new LocalizedServiceException("Chat.SpeechInput.MicrophoneOff");
        }
        catch (Exception ex) when (IsMicrophoneAccessFailure(ex))
        {
            throw new LocalizedServiceException("Chat.SpeechInput.MicrophoneOff");
        }
        catch (Exception ex) when (IsNoCaptureDevice(ex))
        {
            throw new LocalizedServiceException("Chat.SpeechInput.NoMicrophone");
        }
    }

    private static bool IsMicrophoneAccessFailure(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
            return true;

        unchecked
        {
            var hr = (uint)ex.HResult;
            return hr == SpeechPrivacyDeclinedHResult
                   || hr == 0x80070005; // E_ACCESSDENIED
        }
    }

    private static bool IsNoCaptureDevice(Exception ex)
    {
        unchecked
        {
            return (uint)ex.HResult == NoCaptureDevicesHResult;
        }
    }

    private static void TryCancelOperation(IAsyncOperation<SpeechRecognitionResult>? operation)
    {
        try
        {
            operation?.Cancel();
        }
        catch
        {
            /* ignore */
        }
    }

    private static void TryDispose(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch
        {
            /* ignore */
        }
    }
}
