using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

/// <summary>
/// システムプロンプトは「今のターンに関係する部品だけ」「返答言語は最後に 1 回」。
/// 添付・画像・出どころの指示が毎ターン載っていた退行と、言語指示が 2 本あった退行を防ぐ。
/// </summary>
public sealed class ChatPromptAssemblerTests
{
    private static readonly CharacterProfileDto Character = new("花子", "明るい相棒。", "タメ口", 0.8, 0.95, 40, 8192, 2048);
    private static readonly ModelRuntimeStatus Runtime = new("model.gguf", "model.gguf", ModelMismatch: false);

    private static ChatPromptContext Context(
        bool isCharacter = true,
        string? userName = null,
        string? memory = null,
        bool attachment = false,
        bool image = false,
        bool japanese = true,
        string message = "こんにちは") =>
        new(isCharacter, Character, userName, memory, Runtime, attachment, image, japanese, message, 10_000);

    [Fact]
    public void BuildCoreParts_NoAttachmentNoImage_OmitsThoseInstructions()
    {
        var text = string.Join("\n\n", ChatPromptAssembler.BuildCoreParts(Context()));

        Assert.DoesNotContain("【添付】", text);
        Assert.DoesNotContain("OCR", text);
        Assert.DoesNotContain("【出どころの区別】", text);
    }

    [Fact]
    public void BuildCoreParts_WithAttachmentAndImage_IncludesThoseInstructions()
    {
        var text = string.Join("\n\n", ChatPromptAssembler.BuildCoreParts(Context(attachment: true, image: true)));

        Assert.Contains("【添付】", text);
        Assert.Contains("OCR", text);
    }

    [Fact]
    public void Compose_LanguageInstructionIsLastAndAppearsOnce()
    {
        var ctx = Context();
        var parts = ChatPromptAssembler.BuildCoreParts(ctx);
        var text = ChatPromptAssembler.Compose(parts, ctx, hasRagBlock: false);

        var marker = "【返答言語（最優先）】";
        Assert.Equal(1, CountOf(text, marker));
        Assert.Equal(1, CountOf(text, "返答は日本語だけ"));
        Assert.True(text.LastIndexOf(marker, StringComparison.Ordinal) > text.LastIndexOf("【読みやすさ", StringComparison.Ordinal));
        Assert.EndsWith("そのまま保ちます。", text);
    }

    [Fact]
    public void Compose_SourceDistinctionOnlyWhenRagBlockOrAttachment()
    {
        var plain = Context();
        Assert.DoesNotContain("【出どころの区別】", ChatPromptAssembler.Compose(ChatPromptAssembler.BuildCoreParts(plain), plain, hasRagBlock: false));
        Assert.Contains("【出どころの区別】", ChatPromptAssembler.Compose(ChatPromptAssembler.BuildCoreParts(plain), plain, hasRagBlock: true));

        var attached = Context(attachment: true);
        Assert.Contains("【出どころの区別】", ChatPromptAssembler.Compose(ChatPromptAssembler.BuildCoreParts(attached), attached, hasRagBlock: false));
    }

    [Fact]
    public void BuildCoreParts_CharacterWithUserName_UsesOneNameBlock()
    {
        var text = string.Join("\n\n", ChatPromptAssembler.BuildCoreParts(Context(userName: "太郎")));

        Assert.Contains("【名前の区別（必須）】", text);
        Assert.DoesNotContain("【会話相手（ユーザー）の名前】", text);
        Assert.DoesNotContain("【あなた（AIキャラクター）の名前】", text);
        Assert.Contains("明るい相棒。", text);
        Assert.Contains("【話し方】タメ口", text);
        Assert.DoesNotContain("【あなたの役割】", text);
    }

    [Fact]
    public void BuildCoreParts_DefaultAssistant_HasRoleLineAndNoPersona()
    {
        var text = string.Join("\n\n", ChatPromptAssembler.BuildCoreParts(Context(isCharacter: false, userName: "太郎")));

        Assert.Contains("【あなたの役割】", text);
        Assert.Contains("【会話相手（ユーザー）の名前】太郎", text);
        Assert.DoesNotContain("明るい相棒。", text);
        Assert.DoesNotContain("【名前の区別", text);
    }

    [Fact]
    public void BuildCoreParts_MemoryBlock_AddsSpontaneousInstructionOnlyWhenPresent()
    {
        Assert.DoesNotContain("【長期記憶の出し方】", string.Join("\n\n", ChatPromptAssembler.BuildCoreParts(Context())));
        var withMemory = string.Join("\n\n", ChatPromptAssembler.BuildCoreParts(Context(memory: "【長期記憶】麦茶が好き")));
        Assert.Contains("麦茶が好き", withMemory);
        Assert.Contains("【長期記憶の出し方】", withMemory);
    }

    [Fact]
    public void Compose_EnglishUser_EndsWithEnglishLanguageBlock_NoJapaneseDuplicate()
    {
        var ctx = Context(japanese: false, message: "hello");
        var text = ChatPromptAssembler.Compose(ChatPromptAssembler.BuildCoreParts(ctx), ctx, hasRagBlock: false);

        Assert.Contains("[Reply language — highest priority", text);
        Assert.DoesNotContain("【返答言語", text);
        Assert.Equal(1, CountOf(text, "Reply language"));
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
