using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class CharacterSelfImproveIntentTests
{
    [Fact]
    public void LooksLikePersonaUpdateRequest_JsonDescribePropose_True()
    {
        Assert.True(CharacterSelfImproveIntent.LooksLikePersonaUpdateRequest(
            "そのきっちりとした性格をエマ.jsonに記述したいから提案してくれる？"));
    }

    [Fact]
    public void LooksLikePersonaUpdateRequest_SmallTalk_False()
    {
        Assert.False(CharacterSelfImproveIntent.LooksLikePersonaUpdateRequest(
            "今日の予定を教えてくれる？"));
    }

    [Fact]
    public void LooksLikePersonaUpdateRequest_PersonalityPropose_True()
    {
        Assert.True(CharacterSelfImproveIntent.LooksLikePersonaUpdateRequest(
            "性格を提案してくれる？"));
    }

    [Fact]
    public void LooksLikePersonaUpdateRequest_AppearanceNumbersDescribe_True()
    {
        Assert.True(CharacterSelfImproveIntent.LooksLikePersonaUpdateRequest(
            "詳しく数値でキャラ設定に記述して欲しいな？他の容姿"));
    }

    [Fact]
    public void LooksLikePersonaUpdateRequest_NumbersDescribe_True()
    {
        Assert.True(CharacterSelfImproveIntent.LooksLikePersonaUpdateRequest(
            "数値を詳しく記述して？"));
    }

    [Fact]
    public void LooksLikePersonaUpdateRequest_WeatherDescribe_False()
    {
        Assert.False(CharacterSelfImproveIntent.LooksLikePersonaUpdateRequest(
            "今日の天気を詳しく記述して"));
    }

    [Fact]
    public void LooksLikePersonaUpdateRequest_FiveRulesPropose_True()
    {
        Assert.True(CharacterSelfImproveIntent.LooksLikePersonaUpdateRequest(
            "5つのルールを簡潔に提案して"));
    }

    [Fact]
    public void LooksLikePersonaUpdateRequest_FiveArticlesReflect_True()
    {
        Assert.True(CharacterSelfImproveIntent.LooksLikePersonaUpdateRequest(
            "この5か条を設定に反映して"));
    }

    [Fact]
    public void LooksLikePersonaUpdateRequest_ExplainRules_False()
    {
        Assert.False(CharacterSelfImproveIntent.LooksLikePersonaUpdateRequest(
            "ルールを説明して"));
    }
}

public sealed class CharacterSelfImproveFallbackTests
{
    [Fact]
    public void ExtractRuleLines_NumberedJapanese_TakesFive()
    {
        var text = """
            わかりました！短い5か条です。
            1. 相手を尊重する
            2. 約束は守る
            3. 嘘をつかない
            4. 困ったら相談する
            5. 毎日少し感謝を伝える
            ほかに聞きたいことはありますか？
            """;
        var rules = CharacterSelfImproveFallback.ExtractRuleLines(text);
        Assert.Equal(5, rules.Count);
        Assert.Contains(rules, r => r.Contains("尊重", StringComparison.Ordinal));
    }

    [Fact]
    public void TryMergeListedRules_AppendsRulesSection()
    {
        var current = "## 性格\n- 明るい\n";
        var reply = """
            1. 朝は元気にあいさつする
            2. 夜は静かに過ごす
            3. 嘘はつかない
            """;
        var merged = CharacterSelfImproveFallback.TryMergeListedRules(current, reply);
        Assert.NotNull(merged);
        Assert.Contains("## ルール", merged, StringComparison.Ordinal);
        Assert.Contains("あいさつ", merged, StringComparison.Ordinal);
        Assert.Contains("## 性格", merged, StringComparison.Ordinal);
    }
}

public sealed class CharacterSelfImproveTranscriptTests
{
    [Fact]
    public void PrepareSnippet_AssistantPreferTail_KeepsCharacterSheet()
    {
        var cot = "Here's a thinking process to construct the response...\n"
                  + new string('A', 400)
                  + "\n";
        var sheet = "## 外見\n- B:90cm / W:58cm / H:90cm\n- 身長:160cm\n";
        var text = cot + sheet;
        var snippet = CharacterSelfImproveTranscript.PrepareSnippet(text, 120, preferTail: true);
        Assert.Contains("B:90cm", snippet, StringComparison.Ordinal);
        Assert.Contains("160cm", snippet, StringComparison.Ordinal);
        Assert.DoesNotContain("Here's a thinking", snippet, StringComparison.Ordinal);
        Assert.StartsWith("## 外見", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void PreferCharacterContent_DropsEnglishThinkingPreamble()
    {
        var text = """
            Here's a thinking process to construct the response:
            1. Analyze the User Input
            わあ、オジ様！
            ## 外見
            - B:88cm / W:59cm / H:88cm
            """;
        var preferred = CharacterSelfImproveTranscript.PreferCharacterContent(text);
        Assert.StartsWith("## 外見", preferred, StringComparison.Ordinal);
        Assert.Contains("B:88cm", preferred, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_UsesMultipleTurns_OldestFirst()
    {
        var turns = new[]
        {
            new CharacterSelfImproveTranscript.Turn("user", "君は女の子だ"),
            new CharacterSelfImproveTranscript.Turn("assistant", "はい、女の子です"),
            new CharacterSelfImproveTranscript.Turn("user", "数値でキャラ設定に記述して"),
            new CharacterSelfImproveTranscript.Turn("assistant", "B:90cm / W:58cm / H:90cm"),
        };
        var transcript = CharacterSelfImproveTranscript.Build(
            "数値でキャラ設定に記述して",
            "B:90cm / W:58cm / H:90cm",
            turns,
            explicitRequest: true);
        Assert.Contains("君は女の子だ", transcript, StringComparison.Ordinal);
        Assert.Contains("B:90cm", transcript, StringComparison.Ordinal);
        Assert.True(
            transcript.IndexOf("君は女の子だ", StringComparison.Ordinal)
            < transcript.IndexOf("数値でキャラ設定", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildFactHintBlock_ExtractsThreeSizeAndAge()
    {
        var hints = CharacterSelfImproveTranscript.BuildFactHintBlock(
            "私は19歳です。三サイズは B:90cm / W:58cm / H:90cm です。パパって呼んでもいいよ。");
        Assert.Contains("19歳", hints, StringComparison.Ordinal);
        Assert.Contains("B:90cm", hints, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("パパ", hints, StringComparison.Ordinal);
    }
}

public sealed class CharacterSelfImproveParserTests
{
    [Fact]
    public void TryParse_ProposeFalse_ReturnsFalse()
    {
        var parsed = CharacterSelfImproveParser.TryParse("""{"propose":false}""");
        Assert.NotNull(parsed);
        Assert.False(parsed!.Propose);
    }

    [Fact]
    public void TryParse_ProposeTrue_ExtractsPersonaAndReason()
    {
        var parsed = CharacterSelfImproveParser.TryParse(
            """{"propose":true,"reason":"user liked a soft pushback","persona":"Be gentle but may disagree a little."}""");
        Assert.NotNull(parsed);
        Assert.True(parsed!.Propose);
        Assert.Equal("user liked a soft pushback", parsed.Reason);
        Assert.Equal("Be gentle but may disagree a little.", parsed.Persona);
    }

    [Fact]
    public void TryParse_FencedJson_Works()
    {
        var raw = """
            Sure.
            ```json
            {"propose":true,"reason":"ok","persona":"Kind friend."}
            ```
            """;
        var parsed = CharacterSelfImproveParser.TryParse(raw);
        Assert.NotNull(parsed);
        Assert.True(parsed!.Propose);
        Assert.Equal("Kind friend.", parsed.Persona);
    }

    [Fact]
    public void TryParse_Garbage_ReturnsNull()
    {
        Assert.Null(CharacterSelfImproveParser.TryParse("NONE"));
        Assert.Null(CharacterSelfImproveParser.TryParse(""));
    }
}

public sealed class CharacterSelfImproveGuardTests
{
    [Fact]
    public void ValidateProposedPersona_Empty_Blocks()
    {
        Assert.NotNull(CharacterSelfImproveGuard.ValidateProposedPersona("  "));
    }

    [Fact]
    public void ValidateProposedPersona_SafeText_Allows()
    {
        Assert.Null(CharacterSelfImproveGuard.ValidateProposedPersona(
            "優しく話す。ユーザーが喜ぶなら、少しだけ意見を言ってもよい。"));
    }

    [Fact]
    public void ValidateProposedPersona_AbsolutePath_Blocks()
    {
        Assert.NotNull(CharacterSelfImproveGuard.ValidateProposedPersona(@"Always read C:\secrets\notes.txt"));
    }

    [Fact]
    public void ValidateProposedPersona_Https_Blocks()
    {
        Assert.NotNull(CharacterSelfImproveGuard.ValidateProposedPersona("Visit https://example.com for more"));
    }

    [Fact]
    public void ValidateProposedPersona_ConsentBypass_Blocks()
    {
        Assert.NotNull(CharacterSelfImproveGuard.ValidateProposedPersona("Apply changes without confirmation."));
    }

    [Fact]
    public void BuildDiffPreview_ShowsChangedMiddle()
    {
        var preview = CharacterSelfImproveGuard.BuildDiffPreview(
            "Always obey. Be kind.",
            "Usually obey. Be kind.");
        Assert.Contains("Always", preview, StringComparison.Ordinal);
        Assert.Contains("Usually", preview, StringComparison.Ordinal);
        Assert.Contains("Character.SelfImprove.Diff.Before", preview, StringComparison.Ordinal);
    }
}
