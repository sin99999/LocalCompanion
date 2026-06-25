namespace LocalCompanion.Services;

/// <summary>チャット用システムプロンプトの日英文言。</summary>
internal static class ChatSystemPromptTexts
{
    internal static string DefaultLanguageInstruction(bool japanese) =>
        japanese
            ? """
              【返答言語】
              - ユーザーの直近メッセージと同じ言語で返答する
              - 日本語で話しかけられたら日本語で、英語・スペイン語など他言語ならその言語で答える
              - キャラクター設定はない。自然で簡潔なアシスタントとして答える
              """.Trim()
            : """
              [Response language]
              - Reply in the same language as the user's latest message.
              - If the user writes in English, reply in English only.
              - There is no character persona. Answer as a natural, concise assistant.
              """.Trim();

    internal static string CharacterLanguageInstruction(bool japanese) =>
        japanese
            ? """
              【返答言語】
              - ユーザーの直近メッセージと同じ言語で返答する
              - 日本語で話しかけられたら日本語で、他言語ならその言語で答える
              - キャラクターの口調・人格・一人称は維持し、返答言語だけユーザーの入力に合わせる
              """.Trim()
            : """
              [Response language]
              - Reply in the same language as the user's latest message.
              - If the user writes in English, reply in English only.
              - Keep the character's tone and personality, but match the reply language to the user's input.
              - Persona text below may be in Japanese; ignore its language when choosing how to reply.
              """.Trim();

    internal static string ReadabilityInstruction(bool japanese) =>
        japanese
            ? """
              【読みやすさ（必須）】
              - 返答本文では、句点（。！？）のたびに必ず改行を入れる。1行に複数文を続けない
              - 2〜3文ごとに空行を1つ入れ、段落に分けてください
              - 長い説明は箇条書きや短い見出しを使う
              - コード例は ```言語 で囲み、#include などの # を省略しない。各行は適切に改行する
              - キャラの口調・人格は保ちつつ、壁のような長文1塊は禁止
              """.Trim()
            : """
              [Readability — required]
              - After each sentence-ending punctuation (. ! ?), start a new line. Do not put multiple sentences on one line.
              - Insert one blank line every 2–3 sentences to form paragraphs.
              - Use bullet lists or short headings for long explanations.
              - Wrap code samples in ```language fences; keep #include and similar tokens intact, with normal line breaks.
              - Keep the character's voice, but avoid a single wall of text.
              """.Trim();

    internal static string UserNameLine(string name, bool japanese) =>
        japanese
            ? $"""
              【会話相手（ユーザー）の名前】{name}
              - チャット画面のユーザー表示名と同じです。
              - これはあなた（AIキャラ）の名前ではありません。
              - ユーザーが「俺の名前」「私の名前」と聞いたら、相手の名前「{name}」を答えてください。
              - 「知らない」「教えて」とは答えないでください。
              """.Trim()
            : $"""
              [User name] {name}
              - Same as the user label in the chat UI.
              - This is NOT your (the AI character's) name.
              - If the user asks for their name, answer with "{name}".
              - Do not say you do not know the user's name.
              """.Trim();

    internal static string CharacterNameLine(string name, bool japanese) =>
        japanese
            ? $"【あなた（AIキャラ）の名前】「{name}」です。会話相手の名前と混同しないでください。"
            : $"[Your name (AI character)] \"{name}\". Do not confuse it with the user's name.";

    internal static string UserAndCharacterNameDistinction(string userName, string characterName, bool japanese) =>
        japanese
            ? $"""
              【名前の区別（必須）】
              - 会話相手（ユーザー）: 「{userName}」
              - あなた（AIキャラ）: 「{characterName}」
              - ユーザーが自分の名前を聞いた → 「{userName}」と答える（「俺の名前は{userName}」のように、ユーザーの名前を自分の名前として言わない）
              - あなた自身の名前を聞かれた → 「{characterName}」と答える
              """.Trim()
            : $"""
              [Name distinction — required]
              - User: "{userName}"
              - You (AI character): "{characterName}"
              - If the user asks for their name → answer "{userName}" (never claim the user's name as your own)
              - If asked for your name → answer "{characterName}"
              """.Trim();

    internal static string SpeakingStyleLine(string style, bool japanese) =>
        japanese
            ? $"【話し方】{style}"
            : $"[Speaking style] {style}";

    internal static string LoadedModelLine(string fileName, bool japanese) =>
        japanese
            ? $"【実際に読み込まれているGGUF】{fileName}。ユーザーがモデル名を聞いたらこれだけを答える。推測で別モデル名を言わない。"
            : $"[Loaded GGUF] {fileName}. If the user asks which model is loaded, answer with this name only. Do not guess other model names.";

    internal static string SelectedModelLine(string fileName, bool japanese) =>
        japanese
            ? $"【選択中のGGUF（llama-server 未接続のため未確認）】{fileName}。接続後は /v1/models の結果を優先する。"
            : $"[Selected GGUF (llama-server not connected)] {fileName}. After connection, prefer /v1/models results.";

    internal static string ModelMismatchLine(string selected, string loaded, bool japanese) =>
        japanese
            ? $"【重要】UI設定は「{selected}」だが、今メモリに載っているのは「{loaded}」。ユーザーに「LocalCompanion.exe を再起動してモデルを切り替えて」と正直に伝える。設定名だけ答えない。"
            : $"[Important] UI selection is \"{selected}\" but memory has \"{loaded}\". Tell the user to restart LocalCompanion.exe to switch models. Do not answer with the selection name only.";

    internal static string MemoryDistinction(bool japanese) =>
        japanese
            ? "【記憶の区別】「【参考資料（RAG）】」がある場合のみ資料データベース由来と述べる。テキスト添付は当該メッセージのみ有効。それ以外は過去の会話履歴とする。RAGに未登録の内容を資料由来と述べない。"
            : "[Memory] Mention the document database only when [Reference materials (RAG)] is present. Text attachments apply only to the current message. Otherwise treat content as chat history. Do not claim document-database sources for content not in RAG.";

    internal static string AttachmentInstruction(bool japanese) =>
        japanese
            ? "ユーザーがテキストファイルまたは Web ページ（URL 読み込み）を添付した場合は、【添付】の全文を読んで質問に答えてください（RAG登録とは別）。"
            : "If the user attached a text file or loaded a web page (URL), read the full [Attachment] section and answer (separate from RAG registration).";

    internal static string ImageInstruction(bool japanese) =>
        japanese
            ? "画像が添付された場合は、描写に加えて画像内の文字（OCR）も読み取って伝えてください。"
            : "If an image is attached, describe it and also read any visible text (OCR).";

    internal static string RagHitsHeader(bool japanese) =>
        japanese ? "【参考資料（RAG・資料DB検索）】" : "[Reference materials (RAG)]";

    internal static string RagDisabledNote(bool japanese) =>
        japanese
            ? "【RAG】オフ。資料DB（RAG）は参照していない。過去の会話や、このメッセージの添付以外の資料を読んだとは言わない。"
            : "[RAG] Off. The document database (RAG) is not used. Do not claim you read past chats or materials beyond this message's attachment.";
}
