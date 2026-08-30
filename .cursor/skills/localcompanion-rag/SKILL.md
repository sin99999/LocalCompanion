---
name: localcompanion-rag
description: >-
  LocalCompanion RAG pipeline — ingest, chunking, hybrid search, sqlite-vec,
  source filters, and startup.log diagnosis. Use when RAG returns nothing,
  wrong citations, vec0 errors, ingest failures, or penal-code/legal search bugs.
  Also use when Corpus2Skill / Don't Retrieve Navigate / skill-tree RAG is proposed.
---

# LocalCompanion — RAG

## いつ読むか

- チャットで RAG が効かない・出典がおかしい
- `startup.log` に `vec0` / `knn` / `LIMIT` / `rag` エラー
- ingest・チャンク・法律条文検索の修正
- 新しいドキュメント種別の取り込み設計

## データとログ

| 起動 | RAG DB |
|------|--------|
| Debug | `%LocalAppData%\LocalCompanionLlama\rag.db` |
| 配布 ZIP | `{exe}\data\rag.db` |

ログ: 同ディレクトリの `startup.log`

## パイプライン（Core）

```
読取 RagDocumentReader
  → 正規化 RagDocumentNormalizer
  → 種別 RagDocumentProfileDetector
  → 構造化 RagStructuralChunker (+ RagTextChunker overflow)
  → DB rag_chunks + embedding
  → 検索 RagService.HybridSearchAsync (FTS + vec0)
```

主要ファイル:

| 領域 | パス |
|------|------|
| 検索統合 | `src/LocalCompanion.Core/Services/RagService.cs` |
| vec0 KNN | `src/LocalCompanion.Core/Data/RagSqliteVec.cs` |
| FTS | `src/LocalCompanion.Core/Data/RagSqliteFts.cs` |
| チャンク | `src/LocalCompanion.Core/Services/RagStructuralChunker.cs` |
| SoftTopic 軽い床 | `RagConversationGate` + `RagSoftHitRanker` |
| 設定 | ルート `appsettings.json`（ChunkSize, RagTopK 等） |

## 測る（黄金セット）

- 改善は **測る→品質**。感覚の％だけで勝った顔をしない
- 回帰: `tests/.../RagGoldenSetTests`（約30問、llama なし）。ケース追加は `Fixtures/RagGoldenCases.cs`
- SoftTopic は弱ヒット落としのあと、語の重なりで軽い並べ替え（交差エンコーダ常駐はしない）
- 直前が条文でも、今の発話に「条」が無ければ履歴をくっつけない（`RagSearchQueryComposer`）
- RiskCaution で法令ヒットがあるときは `RagRiskCautionResponder` で罰則を機械引用（LLM 合成より先）
- 犯罪家族は窃盗／殺人／略取／性犯罪を分けてヒットを選ぶ。危険話題の履歴は別罪名をくっつけない（`RagSearchQueryComposer`）

## sqlite-vec の落とし穴

- KNN には `k = ?` 制約が必須（`LIMIT` だけでは JOIN 時に失敗することがある）
- 資料フィルタ（`sourcesFilter`）付き検索は **CTE で KNN を分離** してから `rag_chunks` と JOIN
- `vec0.dll` は WinUI 出力に同梱。テストでは NuGet native を CopyToOutput

エラー例: `A LIMIT or 'k = ?' constraint is required on vec0 knn queries`

## 調査手順

1. `startup.log` で RAG / vec エラーを確認
2. `rag.db` のチャンク数・source 名（UI の RAG 一覧と一致するか）
3. 資料フィルタ ON 時と OFF 時で再現が分かれるか
4. `.md` 整備済み資料 vs 生 PDF で差があるか（設計上想定内のことが多い）

## テスト

- 個人パス（`C:\Users\...`）は **禁止**。`tests/.../Fixtures/` か埋め込み文字列
- vec 統合: `RagSqliteVecTests.cs`
- 法律チャンク: `PenalCodeTestFixtures.cs`

```powershell
dotnet test tests/LocalCompanion.Core.Tests/LocalCompanion.Core.Tests.csproj --filter "FullyQualifiedName~Rag"
```

## 変更時の完了条件

1. 該当 RAG テスト追加 or 更新
2. `dotnet test` 成功
3. 再現手順（ingest 元・質問文・期待する出典）を 1〜3 行で残す

## Corpus2Skill は置き換えない（📅 2026-08-29）

論文: arXiv 2604.14572 *Don't Retrieve, Navigate*（Corpus2Skill）。文書をクラスタして目次の木にし、検索の代わりにエージェントが辿る。単一ドメインの短い記事では当たることがある。長い契約の原文引用では、論文自身が通常の検索に負けると書いている。

LC はチャンク＋FTS＋sqlite-vec。法律・定義・FAQ は機械引用（LLM を飛ばす）。埋め込みは手元の llama。ファイルは 1 本ずつ登録。クラウドの Skills API も、全部作り直しのコンパイルも合わない。

**やらない:** 検索を捨てて SKILL.md の木に置き換えること。

**後で摘んでよい（置換ではない）:** いまある見出し・資料種別で先に範囲を絞ってから、今の検索をする。本文を要約で置き換えない。入れるなら、混在コーパスの法律質問で原文が落ちないかを現行と比べてから。WixQA の勝ち表だけでは合格にしない。

実装（📅 2026-08-29）: `RagShelfCatalog`（資料名・見出しパスの目次）→ 狭い棚でハイブリッド検索 → 語が重ならない／棚が外れたら最大3回まで範囲を広げる。全部外れならヒットなし（推測で埋めない）。検索そのものは捨てない。法律の SQL 機械引用は先に、ヒント資料が空なら全資料でもう一度。

条番号つき質問（📅 2026-08-29・本人「好き」）: 聞かれた条が無いのに近い別条を全文で返さない。`RagArticleHitFilter` で番号一致だけ残す。構造化が空ならハイブリッドに落とさない。機械引用ミスは「登録資料を探しましたが、第N条は見つかりませんでした。」

条番号のふわっと質問（📅 2026-08-29）: 「刑法4条はなーんだ？」のように原文指定が無くても `Verbatim`。キャラクター会話でも LLM に渡さず機械引用する（`RagArticleAnswerPolicy`）。「関連する条」として別条の罪名を足さない。平易化 cue で CitationFirst に落とすと再発するので、条番号質問は verbatim を維持する。複数条（「4条と104条」）は聞かれたキーだけ残して並べる。条番号の無い「国外犯」は罰則トピック／SoftTopic と Web スキップ。相槌の直後は `RagHistoryTopicPicker` で直前の法令発話を拾う。

口語のネット調査（📅 2026-08-30・本人「好き」）: `ネットで` / `調べて` だけ見ると `ネットやらから探して` が Web に乗らず、RAG 担当口で「登録資料だけ」になる。`ChatAgentResearchEnricher` の明示 Web は `ネットから` / `ネットやらから` も含む。Web 結果が付いたときは「登録資料しか調べられない」と言わせない（`AttachmentInstruction`）。法令・「資料から検索」は従来どおり RAG。
