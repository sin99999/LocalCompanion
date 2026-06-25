namespace LocalCompanion.Services;

/// <summary>ユーザー入力の言語に合わせ、システムプロンプト末尾へ強い返答言語指示を付ける。</summary>
internal static class ChatInputLanguageDirective
{
    internal static string Build(string userMessage)
    {
        if (TextScriptHelper.LooksJapanese(userMessage))
        {
            return """
                【重要・返答言語（最優先）】
                今回のユーザーメッセージは日本語です。返答は日本語のみで書いてください。英語や他言語を混ぜないでください。
                """.Trim();
        }

        return """
            [CRITICAL — Reply language (highest priority; overrides all instructions above)]
            The user's latest message is NOT in Japanese.
            Write your entire reply in the same language the user used (e.g. English for "hello").
            Do NOT reply in Japanese. Do NOT use hiragana, katakana, or kanji.
            System instructions above may be written in Japanese; ignore their language when writing your reply.
            """.Trim();
    }
}
