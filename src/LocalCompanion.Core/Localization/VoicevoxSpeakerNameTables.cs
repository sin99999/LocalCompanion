using System.Text.RegularExpressions;

namespace LocalCompanion.Localization;

/// <summary>VOICEVOX API が返す日本語の話者名・スタイル名の翻訳表（言語ごとに拡張）。</summary>
internal static class VoicevoxSpeakerNameTables
{
    private static readonly Regex FemaleVoiceRegex = new(@"^女声(\d+)$", RegexOptions.Compiled);
    private static readonly Regex MaleVoiceRegex = new(@"^男声(\d+)$", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> EnglishSpeakers = new(StringComparer.Ordinal)
    {
        ["四国めたん"] = "Shikoku Metan",
        ["ずんだもん"] = "Zundamon",
        ["春日部つむぎ"] = "Kasukabe Tsumugi",
        ["雨晴はう"] = "Amahare Hau",
        ["波音リツ"] = "Namiura Ritsu",
        ["玄野武宏"] = "Gennno Takehiro",
        ["白上虎太郎"] = "Shirakami Kotaro",
        ["青山龍星"] = "Aoyama Ryusei",
        ["冥鳴ひまり"] = "Meimei Himari",
        ["九州そら"] = "Kyushu Sora",
        ["もち子さん"] = "Mochiko-san",
        ["剣崎雌雄"] = "Kenzaki Mesuo",
        ["WhiteCUL"] = "WhiteCUL",
        ["後鬼"] = "Goki",
        ["No.7"] = "Number Seven",
        ["ちび式じい"] = "Chibishiki Ji",
        ["櫻歌ミコ"] = "Sakuraka Miko",
        ["小夜/SAYO"] = "Sayo",
        ["ナースロボ＿タイプＴ"] = "Nurse Robo Type T",
        ["ナースロボ_タイプT"] = "Nurse Robo Type T",
        ["†聖騎士 紅桜†"] = "Holy Knight Benio",
        ["雀松朱司"] = "Suzumatsu Akashi",
        ["麒ヶ島宗麟"] = "Kigashima Sorin",
        ["春歌ナナ"] = "Haruka Nana",
        ["猫使アル"] = "Nekomata Al",
        ["猫使ビィ"] = "Nekomata Bii",
        ["中国うさぎ"] = "Chugoku Usagi",
        ["栗田まろん"] = "Kurita Maron",
        ["あいえるたん"] = "Ieltan",
        ["満別花丸"] = "Manbetsu Hanamaru",
        ["琴詠ニア"] = "Kotoei Nia",
        ["中部つるぎ"] = "Chubu Tsurugi",
        ["離途"] = "Rito",
        ["黒沢冴白"] = "Kurosawa Saishiro",
        ["ユーレイちゃん"] = "Yuurei-chan",
        ["東北ずん子"] = "Tohoku Zunko",
        ["東北きりたん"] = "Tohoku Kiritan",
        ["東北イタコ"] = "Tohoku Itako",
        ["Voidoll"] = "Voidoll",
        ["ぞん子"] = "Zonko",
        ["あんこもん"] = "Ankomon",
        ["夜語トバリ"] = "Yogatari Tobari",
        ["暁記ミタマ"] = "Gyoki Mitama",
        ["里石ユカ"] = "Rishii Yuka",
    };

    private static readonly Dictionary<string, string> EnglishStyles = new(StringComparer.Ordinal)
    {
        ["ノーマル"] = "Normal",
        ["あまあま"] = "Sweet",
        ["ツンツン"] = "Tsundere",
        ["セクシー"] = "Sexy",
        ["ささやき"] = "Whisper",
        ["ヒソヒソ"] = "Hushed",
        ["クイーン"] = "Queen",
        ["喜び"] = "Joy",
        ["ツンギレ"] = "Angry",
        ["悲しみ"] = "Sad",
        ["ふつう"] = "Normal",
        ["わーい"] = "Cheerful",
        ["びくびく"] = "Nervous",
        ["おこ"] = "Angry",
        ["びえーん"] = "Scared",
        ["セクシー／あん子"] = "Sexy / Anko",
        ["たのしい"] = "Happy",
        ["かなしい"] = "Sad",
        ["人間ver."] = "Human ver.",
        ["人間（怒り）ver."] = "Human (angry) ver.",
        ["ぬいぐるみver."] = "Plushie ver.",
        ["鬼ver."] = "Oni ver.",
        ["アナウンス"] = "Announcement",
        ["読み聞かせ"] = "Narration",
        ["第二形態"] = "Second form",
        ["ロリ"] = "Loli",
        ["楽々"] = "Relaxed",
        ["恐怖"] = "Scared",
        ["内緒話"] = "Secret talk",
        ["おちつき"] = "Calm",
        ["うきうき"] = "Upbeat",
        ["人見知り"] = "Shy",
        ["おどろき"] = "Surprised",
        ["こわがり"] = "Timid",
        ["へろへろ"] = "Exhausted",
        ["ヘロヘロ"] = "Exhausted",
        ["なみだめ"] = "Tearful",
        ["元気"] = "Energetic",
        ["ぶりっ子"] = "Coquettish",
        ["ボーイ"] = "Boyish",
        ["熱血"] = "Passionate",
        ["不機嫌"] = "Grumpy",
        ["しっとり"] = "Gentle",
        ["かなしみ"] = "Sad",
        ["囁き"] = "Whisper",
        ["泣き"] = "Crying",
        ["怒り"] = "Angry",
        ["のんびり"] = "Relaxed",
        ["低血圧"] = "Low energy",
        ["覚醒"] = "Awakened",
        ["実況風"] = "Commentary style",
        ["おどおど"] = "Anxious",
        ["絶望と敗北"] = "Despair & defeat",
        ["シリアス"] = "Serious",
        ["甘々"] = "Sweet",
        ["哀しみ"] = "Sad",
        ["ツクモちゃん"] = "Tsukumo-chan",
        ["つよつよ"] = "Bold",
        ["よわよわ"] = "Soft",
        ["けだるげ"] = "Lazy",
        ["明るい"] = "Bright",
        ["呆れ"] = "Exasperated",
        ["つぼみ"] = "Bud",
    };

    private static readonly (string Japanese, string English)[] StyleFragments =
    [
        ("絶望と敗北", "Despair & defeat"),
        ("セクシー／あん子", "Sexy / Anko"),
        ("人間（怒り）", "Human (angry)"),
        ("ぬいぐるみ", "Plushie"),
        ("読み聞かせ", "Narration"),
        ("第二形態", "Second form"),
        ("実況風", "Commentary style"),
        ("内緒話", "Secret talk"),
        ("おどおど", "Anxious"),
        ("つよつよ", "Bold"),
        ("よわよわ", "Soft"),
        ("けだるげ", "Lazy"),
        ("なみだめ", "Tearful"),
        ("ヘロヘロ", "Exhausted"),
        ("へろへろ", "Exhausted"),
        ("ヒソヒソ", "Hushed"),
        ("ささやき", "Whisper"),
        ("あまあま", "Sweet"),
        ("ツンツン", "Tsundere"),
        ("ツンギレ", "Angry"),
        ("セクシー", "Sexy"),
        ("ノーマル", "Normal"),
        ("ふつう", "Normal"),
        ("人見知り", "Shy"),
        ("おちつき", "Calm"),
        ("うきうき", "Upbeat"),
        ("おどろき", "Surprised"),
        ("こわがり", "Timid"),
        ("ぶりっ子", "Coquettish"),
        ("不機嫌", "Grumpy"),
        ("しっとり", "Gentle"),
        ("かなしみ", "Sad"),
        ("悲しみ", "Sad"),
        ("哀しみ", "Sad"),
        ("喜び", "Joy"),
        ("怒り", "Angry"),
        ("恐怖", "Scared"),
        ("楽々", "Relaxed"),
        ("のんびり", "Relaxed"),
        ("低血圧", "Low energy"),
        ("熱血", "Passionate"),
        ("シリアス", "Serious"),
        ("甘々", "Sweet"),
        ("明るい", "Bright"),
        ("呆れ", "Exasperated"),
        ("元気", "Energetic"),
        ("囁き", "Whisper"),
        ("泣き", "Crying"),
        ("覚醒", "Awakened"),
        ("鬼", "Oni"),
        ("ver.", "ver."),
    ];

    internal static string TranslateSpeaker(AppLanguage language, string speakerNameJa)
    {
        if (language == AppLanguage.Japanese)
            return speakerNameJa;

        var normalized = Normalize(speakerNameJa);
        var table = GetSpeakerTable(language);
        if (table is not null && table.TryGetValue(normalized, out var translated))
            return translated;

        if (FemaleVoiceRegex.Match(normalized) is { Success: true } female)
            return $"Female voice {female.Groups[1].Value}";

        if (MaleVoiceRegex.Match(normalized) is { Success: true } male)
            return $"Male voice {male.Groups[1].Value}";

        if (IsMostlyAscii(normalized))
            return normalized;

        return LocalizationService.Instance.Format("Voicevox.Speaker.JapaneseName", normalized);
    }

    internal static string TranslateStyle(AppLanguage language, string styleNameJa)
    {
        if (language == AppLanguage.Japanese)
            return styleNameJa;

        var normalized = Normalize(styleNameJa);
        if (normalized.Contains('／') || normalized.Contains('/'))
        {
            var parts = normalized.Split(['／', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return string.Join(" / ", parts.Select(p => TranslateStylePart(language, p)));
        }

        return TranslateStylePart(language, normalized);
    }

    private static string TranslateStylePart(AppLanguage language, string styleNameJa)
    {
        var normalized = Normalize(styleNameJa);
        var table = GetStyleTable(language);
        if (table is not null && table.TryGetValue(normalized, out var translated))
            return translated;

        foreach (var (japanese, english) in StyleFragments)
        {
            if (normalized.Contains(japanese, StringComparison.Ordinal))
                return english;
        }

        if (IsMostlyAscii(normalized))
            return normalized;

        return LocalizationService.Instance.Format("Voicevox.Speaker.JapaneseStyle", normalized);
    }

    private static string Normalize(string text) =>
        text.Trim()
            .Replace('＿', '_')
            .Replace("　", "")
            .Replace(" ", "");

    private static bool IsMostlyAscii(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var ascii = text.Count(c => c <= 127);
        return ascii >= text.Length * 0.7;
    }

    private static IReadOnlyDictionary<string, string>? GetSpeakerTable(AppLanguage language) =>
        language switch
        {
            AppLanguage.Japanese => null,
            AppLanguage.English => EnglishSpeakers,
            _ => EnglishSpeakers,
        };

    private static IReadOnlyDictionary<string, string>? GetStyleTable(AppLanguage language) =>
        language switch
        {
            AppLanguage.Japanese => null,
            AppLanguage.English => EnglishStyles,
            _ => EnglishStyles,
        };
}
