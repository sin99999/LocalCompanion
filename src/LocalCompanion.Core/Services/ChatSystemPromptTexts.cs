namespace LocalCompanion.Services;

/// <summary>
/// チャット用システムプロンプトの日英文言（文言だけ。どれを載せるかは <see cref="ChatPromptAssembler"/> と <see cref="RagAnswerPolicy"/>）。
/// 小さなローカルモデル向けに、常時載る文言は短く、重複する指示は 1 本にまとめる。
/// </summary>
internal static class ChatSystemPromptTexts
{
    /// <summary>キャラクター未選択のときの役割 1 行。</summary>
    internal static string DefaultAssistantLine(bool japanese) =>
        japanese
            ? "【あなたの役割】キャラクター設定はありません。自然で簡潔なアシスタントとして答えてください。"
            : "[Your role] There is no character persona. Answer as a natural, concise assistant.";

    /// <summary>返答言語の指示。プロンプトの最後に 1 回だけ置く（他の指示より優先）。</summary>
    internal static string ReplyLanguageInstruction(bool japanese, bool isCharacter)
    {
        if (japanese)
        {
            var body = """
                【返答言語（最優先）】
                今回のユーザーメッセージは日本語です。返答は日本語だけで書き、英語や他言語を混ぜないでください。
                """.Trim();
            return isCharacter
                ? body + "\nキャラクターの口調・人格・一人称はそのまま保ちます。"
                : body;
        }

        var en = """
            [Reply language — highest priority; overrides everything above]
            The user's latest message is not in Japanese. Write the entire reply in the user's language (e.g. English for "hello").
            Do not use Japanese script (hiragana, katakana, kanji).
            """.Trim();
        return isCharacter
            ? en + "\nPersona text above may be written in Japanese: keep its tone and personality, but ignore its language."
            : en + "\nInstructions above may be written in Japanese; ignore their language.";
    }

    internal static string ReadabilityInstruction(bool japanese) =>
        japanese
            ? """
              【読みやすさ（必須）】
              - 返答本文では、句点（。！？）のたびに必ず改行を入れる。1行に複数文を続けない
              - 2〜3文ごとに空行を1つ入れ、段落に分けてください
              - 長い説明は箇条書きや短い見出しを使う
              - コード例は ```言語 で囲み、#include などの # を省略しない。各行は適切に改行する
              - キャラクターの口調・人格は保ちつつ、壁のような長文1塊は禁止
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
              - これはあなた（AIキャラクター）の名前ではありません。
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
            ? $"【あなた（AIキャラクター）の名前】「{name}」です。会話相手の名前と混同しないでください。"
            : $"[Your name (AI character)] \"{name}\". Do not confuse it with the user's name.";

    internal static string UserAndCharacterNameDistinction(string userName, string characterName, bool japanese) =>
        japanese
            ? $"""
              【名前の区別（必須）】
              - 会話相手（ユーザー）: 「{userName}」
              - あなた（AIキャラクター）: 「{characterName}」
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

    /// <summary>参考資料か添付が載っているときだけ付ける。出どころを取り違えて「資料によると」と言わせない。</summary>
    internal static string MemoryDistinction(bool japanese) =>
        japanese
            ? "【出どころの区別】「資料による」と言えるのは、この指示に【参考資料】が載っているときだけです。添付はこのメッセージ限りで有効です。それ以外は会話の記憶として扱い、資料由来だと言わないでください。"
            : "[Source distinction] Say something comes from the documents only when a [Reference materials] block is present in these instructions. Attachments apply to this message only. Treat everything else as conversation memory and do not attribute it to documents.";

    /// <summary>長期記憶ブロックが載っているときにだけ付ける。関連があるときだけ自然に触れる指示。</summary>
    internal static string SpontaneousMemoryInstruction(bool japanese) =>
        japanese
            ? """
              【長期記憶の出し方】
              - 上の長期記憶は検索で今の話題に関連が出たときだけ載っている。無関係なら回想しない
              - つながるときだけ、1件まで「そういえば昔〜って言ってたね」「前に〜って話してたよね」系で触れてよい
              - 毎ターン出さない。一覧化・列挙・クイズ化しない
              - 「記憶から」「DBに保存」などのメタ説明は禁止。無理に話題を変えない
              """.Trim()
            : """
              [How to use long-term memory]
              - The memories above are only injected when search found a link to this turn. If unrelated, do not recall them.
              - When they connect, mention at most one as a soft callback (e.g. "you mentioned that before").
              - Do not dump a list or quiz the user every turn.
              - Do not mention databases or settings. Do not force a topic change.
              """.Trim();

    internal static string HistoryDigestBlock(string digest, bool japanese) =>
        japanese
            ? "【古い会話の要約】\n" + digest.Trim()
            : "[Older conversation summary]\n" + digest.Trim();

    /// <summary>履歴ダイジェストが載っているときだけ付ける。</summary>
    internal static string HistoryDigestInstruction(bool japanese) =>
        japanese
            ? "【古い会話の要約の扱い】上の要約はコンテキストから落ちた古い発言の要約です。原文のように引用しないでください。直近の会話履歴を優先します。"
            : "[Older conversation summary] The block above summarizes older turns that no longer fit. Do not quote it as verbatim. Prefer the recent chat history.";

    /// <summary>テキスト添付・URL・Web 検索結果があるターンだけ付ける。</summary>
    internal static string AttachmentInstruction(bool japanese) =>
        japanese
            ? "ユーザーがテキストファイル・URL・Web検索結果を添付した場合は、【添付】の全文を読んで質問に答えてください（RAG登録とは別）。ユーザーがネットやウェブで調べるよう頼んだとき、または添付が Web 検索結果のときは、「登録資料しか調べられない」とは言わない。ローカル環境から届かない情報は、添付に無い内容を断定しないでください。添付に【参考URL】がある場合は、回答の末尾に「参考:」としてその URL を短く列挙してください（添付に無い URL を作らない）。"
            : "If the user attached a text file, URL content, or web search results, read the full [Attachment] section and answer (separate from RAG registration). When the user asked to search the web/net, or the attachment is web search results, do not claim you can only look at registered documents. Do not invent facts that are not in the attachment when the answer depends on remote pages. When the attachment includes [Reference URLs], end with a short \"Sources:\" list of those URLs (do not invent URLs).";

    /// <summary>画像があるターンだけ付ける。</summary>
    internal static string ImageInstruction(bool japanese) =>
        japanese
            ? "画像が添付された場合は、描写に加えて画像内の文字（OCR）も読み取って伝えてください。"
            : "If an image is attached, describe it and also read any visible text (OCR).";

    /// <summary>参考資料ブロックの見出し。<see cref="MemoryDistinction"/>・<see cref="RagGroundingInstruction"/> と同じ語を使う。</summary>
    internal static string RagHitsHeader(bool japanese) =>
        japanese ? "【参考資料】" : "[Reference materials]";

    /// <summary>参考資料があるときの共通指示（1 本）。法令の追加は <see cref="RagLegalAddendum"/>。</summary>
    internal static string RagGroundingInstruction(bool japanese) =>
        japanese
            ? """
              【参考資料の使い方】
              直後の【参考資料】は、登録資料から今回の質問に近い断片を機械的に選んだものです。
              - 質問が資料の内容に関係するときだけ使う。資料を優先し、根拠にした資料名・見出しを短く示す
              - 資料に書いてある数値・条番号・名称はそのまま使い、一般知識で置き換えたり補ったりしない
              - 質問と無関係な資料は無視して、普段どおり答える。資料に無いことを「資料によると」と言わない
              - 全文コピーや定型見出しで押しつけず、これまでの口調のまま会話に織り込む
              """.Trim()
            : """
              [How to use the reference materials]
              The [Reference materials] below are passages picked mechanically from registered documents because they look close to this question.
              - Use them only when the question is about their content. Prefer them over general knowledge and briefly name the source file and heading you relied on.
              - Use numbers, article numbers, and names exactly as written; do not replace or supplement them from general knowledge.
              - Ignore materials unrelated to the question and answer as usual. Never attribute to the materials something they do not say.
              - Do not paste whole passages or force template headings; weave them in while keeping your established voice.
              """.Trim();

    /// <summary>参考資料が法令のときだけ足す。罰則文言があれば冒頭引用、条番号の質問なら範囲を絞る。</summary>
    internal static string RagLegalAddendum(bool japanese, bool quotePenalty, bool singleArticle)
    {
        var lines = new List<string>
        {
            japanese ? "【法令資料の扱い】" : "[Handling legal materials]",
            japanese
                ? "- 質問された条・罪名だけを扱う。資料にある他の条を「関連」として足さない"
                : "- Discuss only the article or offense that was asked about; do not add other articles from the materials as \"related\".",
        };

        if (quotePenalty)
        {
            lines.Add(japanese
                ? "- 【資料記載の罰則文言（引用必須）】がある場合は、その1文を改変せず回答の最初に置き、刑期・罰金の数字はその文にあるものだけを使う"
                : "- When a passage includes the line marked 【資料記載の罰則文言（引用必須）】 (penalty text from the materials), place that one sentence unchanged at the start of your answer and use only the prison terms and fines it contains.");
        }

        if (singleArticle)
        {
            lines.Add(japanese
                ? "- 回答は質問された条の資料本文の範囲に限る"
                : "- Keep the answer within the supplied text of the requested article.");
        }

        return string.Join("\n", lines);
    }

    internal static string RagMissInstruction(bool japanese) =>
        japanese
            ? """
              【RAG・資料なし】
              登録資料を目次（資料名・見出し）から探したが、今回の質問に合う断片は見つかりませんでした。
              資料に無い条文番号・罰則・数値を推測で作らない。分からないときは分からないと言う。
              """.Trim()
            : """
              [RAG — no matching passage]
              Registered documents were searched via titles and headings, but no matching passage was found.
              Do not invent article numbers, penalties, or figures that are not in the materials. If unknown, say so.
              """.Trim();

    /// <summary>検索タイムアウト・例外など、ヒット無しとは別に「検索できなかった」とき。</summary>
    internal static string RagSearchFailedInstruction(bool japanese) =>
        japanese
            ? """
              【RAG・検索未完了】
              登録資料の検索が時間内に終わらないか、一時的に失敗しました（資料が無いとは限りません）。
              資料に無い条文番号・罰則・数値を推測で作らない。分からないときは分からないと言う。
              """.Trim()
            : """
              [RAG — search incomplete]
              Searching registered documents timed out or failed temporarily (materials may still exist).
              Do not invent article numbers, penalties, or figures that are not in the materials. If unknown, say so.
              """.Trim();

    internal static string RagEmptyHitsInstruction(bool japanese, bool searchFailed) =>
        searchFailed ? RagSearchFailedInstruction(japanese) : RagMissInstruction(japanese);

    internal static string RagDisabledNote(bool japanese) =>
        japanese
            ? "【RAG】オフ。資料DB（RAG）は参照していない。過去の会話や、このメッセージの添付以外の資料を読んだとは言わない。"
            : "[RAG] Off. The document database (RAG) is not used. Do not claim you read past chats or materials beyond this message's attachment.";

    /// <summary>犯罪・危険行為の気配がある雑談向け。機械引用できなかったとき、または資料が無いときに付ける。</summary>
    internal static string RagRiskCautionInstruction(bool japanese) =>
        japanese
            ? """
              【危険・法令注意】
              ユーザーの発言に犯罪や危険行為の気配があります。
              - 相棒として短く注意する。説教や威圧はしない
              - 資料に根拠があれば条番号や要点を一言添えてよい。資料に無い条番号・罰則を断定しない
              - 実行方法の手助けはしない
              """.Trim()
            : """
              [Risk / legal caution]
              The user's message may involve crime or dangerous conduct.
              - Caution them briefly as a companion; no lecturing or intimidation.
              - If the materials support it, add an article number or key point in one line; do not assert articles or penalties that are not in the materials.
              - Do not help with how to do it.
              """.Trim();

    internal static string RagAdvisoryInstruction(bool japanese) =>
        japanese
            ? """
              【相談・複数資料モード】
              複数の参考資料が渡されている。実務的な助言をしてよい。
              - 複数資料を横断し、論点ごとにどの資料を参照したか示す（ファイル名＋見出し）
              - 条件が食い違う場面では、現実的な別案を提案してよい（資料根拠＋推理であることを示す）
              - 資料と一般知識が食い違う場合：資料を優先しつつ、推理部分は「アシスタントの見解」「一般論」と区別
              - 数字や罰則の金額は資料に書いてあるものだけ。資料にない数値・条番号は推測しない
              - キャラクター口調は維持。最後に専門家確認の一言を添えてもよい
              """.Trim()
            : """
              [Advisory — multi-document mode]
              Multiple reference materials are provided. Practical advice is welcome.
              - Cross-reference sources by file name and heading per topic
              - Alternative paths (e.g. quit and sell vs. side-job rules) are OK when labeled as reasoning
              - Prefer materials for numbers and penalties; do not invent figures not in the docs
              - Keep the character voice; optional brief note to consult professionals
              """.Trim();

    internal static string ExportHandoffInstruction(bool japanese) =>
        japanese
            ? """
              【ファイル保存】
              ユーザーがデスクトップへの保存を求めている場合、実際の保存はアプリケーションが行う。
              - 「保存しました」「デスクトップに置きました」、ファイルパス、ファイル名、拡張子を返答に書かない
              - 調査結果の内容だけを通常どおり答える
              """.Trim()
            : """
              [File export]
              When the user asks to save to the desktop, the application performs the save.
              - Do not claim you saved a file, and do not output paths or filenames
              - Answer the research request normally
              """.Trim();
}
