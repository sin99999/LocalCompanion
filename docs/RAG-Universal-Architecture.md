# Universal RAG Architecture（LocalCompanion）

しんちゃんの理解①〜④を前提に、**「どんなファイルでも高精度 RAG」** へ向けた全体設計です。

## 現状の整理（①〜④との対応）

| 理解 | 仕組み上の理由 |
|------|----------------|
| ① URL / 生 PDF は精度が出にくい | チャット添付は RAG DB を通らず LLM プロンプトに直載せ。PDF はレイアウト・表・改行が崩れ、チャンク境界が弱い |
| ② Cursor で整えた .md は精度が上がる | 見出し階層・条番号・用語見出しが `RagStructuralChunker` と ingest メタデータ（`article_sort_key` 等）に効く |
| ③ フォーマットごとの正確性は保証しにくい | 読み取り（`RagDocumentReader`）→ 構造化 → 検索 → 応答が形式依存。法律だけ v3 で VERBATIM 経路あり |
| ④ 完璧な万能 RAG が欲しい | **同一パイプライン**で「読む → 正規化 → プロファイル判定 → 構造化 ingest → 意図別検索 → 高信頼時 LLM バイパス」が必要 |

## 目標アーキテクチャ

```
[任意ソース]                    [正規化レイヤ]              [RAG DB]
 URL / PDF / HTML / DOCX  ──►  RagDocumentNormalizer  ──►  rag_chunks
 生テキスト / .md (2次)         RagDocumentProfileDetector      ├─ 法律: article_sort_key, penalty_lead
                                RagStructuralChunker              ├─ 用語: entry_key, definition_lead
                                LegalFieldExtractor (法律時)      ├─ 共通: section_path, chunk_kind, doc_kind
                                RagGenericFieldExtractor          └─ FTS + sqlite-vec

[ユーザ質問]  ──►  RagQueryPlanner（意図分類）
                      ├─ Boundary / Article / Penalty  →  RagStructuredSearch  →  VERBATIM（LLM なし）
                      ├─ Definition / FAQ              →  RagStructuredSearch  →  VERBATIM（高信頼時）
                      └─ General                       →  RagHybridSearch      →  LLM Synthesis
```

**核心**: 法律で成功したパターン（ingest 時に列を埋める → クエリ意図 → SQL 直 lookup → 必要なら LLM スキップ）を **用語集・FAQ・一般 Markdown** にも横展開する。

## フェーズ計画

### Phase 1 — 汎用メタデータ基盤（v4、今回）

- `RagDocumentNormalizer`: 空白・改行の統一（全形式共通）
- `RagDocumentProfileDetector`: `legal` / `glossary` / `general` 自動判定
- DB 列: `entry_key`, `definition_lead`, `section_path`, `doc_kind`
- `RagGenericFieldExtractor`: 見出し＝用語、`Term — 説明` 等から定義を抽出
- `RagDefinitionQueryParser`: 「Xとは」「Xの意味」→ Definition 意図
- `RagStructuredSearch` + `RagVerbatimResponder`: 定義の高信頼 VERBATIM

### Phase 2 — 読み取り品質（**1.0.5 で拡張・V2 向け継続**）

- ✅ `RagHtmlStructuredExtractor` — HTML 見出し → `#` Markdown 風
- ✅ `RagDocumentStructurer` — ローカル LLM ウィンドウ分割整形（Settings トグル・デフォルト OFF）
- ✅ `RagStructurerCache` — `%LocalAppData%\LocalCompanionLlama\rag-cache\`
- ✅ Settings → URL を RAG 登録
- ✅ `IDocumentReader` レジストリ + `PdfLayoutDocumentReader`（Settings トグル）
- ✅ Shift_JIS テキスト検出
- OCR — 未実装

### Phase 3 — 検索・応答の汎用化（一部実装済み）

- **Advisory 意図** + **PersonaSynthesis** — 就業規則×税法など複数資料の相談（キャラ口調維持）
- **RagPersonaReferenceInstruction** — 遊び会話中の軽い条文参照（キャラ選択時・非フォーマル質問）
- キャラ選択時は VERBATIM バイパスを抑制（「贈賄の罰則は？」等フォーマル質問のみ機械引用）
- ✅ **FAQ 意図** + VERBATIM（Q/A ブロック ingest + `RagFaqQueryParser`）
- ✅ **`CitationFirst`** — 条文質問で引用優先プロンプト
- ✅ 短いテキスト添付 + RAG 併用（`RagLightAttachMaxChars`）

### Phase 4 — 入口の統一（一部実装済み）

- ✅ チャット URL → 「RAG に登録」ボタン
- ✅ ドラッグ＆ドロップ → テキストファイル添付
- 再 ingest 差分（同一 source の更新検知）— 未実装

### Phase 5 — 品質保証（一部実装済み）

- コーパス別ゴールデンクエリ（法律 / 用語集 / README）— テスト拡充中
- ✅ ingest レポート（チャンク数、doc_kind、definition/faq/article 件数）

## なぜ .md 化が効くか（②の技術的理由）

1. **構造が明示的** → `TryParseHeader` が章・条・`##` を安定検出
2. **ノイズが少ない** → PDF のヘッダ繰返し・改行分割が消える
3. **条番号の修正が可能** → PDF 変換の「章内第9条」問題を人間/Cursor が直せる
4. **embedding 品質** → `EmbeddingText` に見出しが載り、ベクトル検索が安定

万能 RAG でも **「内部表現は正規化 Markdown に近い形」** に寄せるのが最短ルート。自動変換は Phase 2 以降で段階的に。

## 添付 vs RAG 登録（①の答え）

| 経路 | DB | 検索 | 高信頼 VERBATIM |
|------|-----|------|-----------------|
| URL/PDF 添付 | なし | スキップ | 不可 |
| RAG 登録 | あり | ハイブリッド + 構造化 | 意図一致時のみ |

**完璧 RAG = 登録経路 + 正規化 ingest + 意図別検索** がセット。

## 関連ファイル

| 領域 | パス |
|------|------|
| 読み取り | `src/LocalCompanion.Core/Services/RagDocumentReader.cs` |
| 正規化 | `src/LocalCompanion.Core/Services/RagDocumentNormalizer.cs` |
| プロファイル | `src/LocalCompanion.Core/Services/RagDocumentProfileDetector.cs` |
| チャンク化 | `src/LocalCompanion.Core/Services/RagStructuralChunker.cs` |
| 法律メタ | `src/LocalCompanion.Core/Services/LegalFieldExtractor.cs` |
| 汎用メタ | `src/LocalCompanion.Core/Services/RagGenericFieldExtractor.cs` |
| クエリ | `src/LocalCompanion.Core/Services/RagQueryPlanner.cs` |
| 定義パース | `src/LocalCompanion.Core/Services/RagDefinitionQueryParser.cs` |
| 検索 | `src/LocalCompanion.Core/Services/RagStructuredSearch.cs`, `RagHybridSearch.cs` |
| VERBATIM | `src/LocalCompanion.Core/Services/RagVerbatimResponder.cs` |
| 統合 | `src/LocalCompanion.Core/Services/RagService.cs`, `ChatService.cs` |

## 再 ingest のお願い

スキーマ・抽出ロジックを変えたあとは、Settings → RAG から **該当ファイルを再登録** してください。起動時 backfill で既存 DB の一部は埋まりますが、最良の精度は再 ingest 後です。
