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
  → DB rag_chunks + embedding + FTS(bigram) + 概念グラフ(RagGraphIndexer)
  → 検索 RagService.HybridSearchAsync (FTS + vec0 + graph lane)
     俯瞰(Overview) は RagGraphSearch（コミュニティ単位・LLM 予算内）
```

主要ファイル:

| 領域 | パス |
|------|------|
| 検索統合 | `src/LocalCompanion.Core/Services/RagService.cs` |
| vec0 KNN | `src/LocalCompanion.Core/Data/RagSqliteVec.cs` |
| FTS（CJK bigram） | `src/LocalCompanion.Core/Data/RagSqliteFts.cs` + `RagFtsSegmenter` |
| チャンク | `src/LocalCompanion.Core/Services/RagStructuralChunker.cs` |
| 概念グラフ DB | `src/LocalCompanion.Core/Data/RagSqliteGraph.cs`（rag_concepts / rag_concept_edges / rag_communities） |
| 概念グラフ構築 | `RagGraphIndexer` → `RagConceptExtractor` + `RagLegalReferenceExtractor` + `RagCooccurrenceGraphBuilder` + `RagCommunityDetector` |
| 分かち書き | `IRagWordSegmenter`（Core: `HeuristicWordSegmenter` / シェル: `WinRtWordSegmenter` = Windows 同梱 WordsSegmenter） |
| グラフ第 3 レーン | `RagGraphLane`（隣接語で FTS 拡張 + 同コミュニティを RRF に加点、重み `RagWeightGraph`） |
| 俯瞰検索 | `RagGraphSearch` + `RagGraphPrompts` + `IRagGraphLlm`（テストは `FakeGraphLlm`） |
| 根拠門番 | `RagEvidenceGate`（合図語なしの Probe。質問語が資料に実在するヒットだけ） |
| 回答ルール | `ChatPromptAssembler` + `RagAnswerPolicy` + `RagPromptBudget` + `ChatSystemPromptTexts` |
| SoftTopic 軽い床 | `RagConversationGate` + `RagSoftHitRanker` |
| 設定 | ルート `appsettings.json`（ChunkSize, RagTopK, RagWeightGraph, RagGraph* 等） |

## 概念グラフ（📅 2026-09-14）

- **索引に LLM も埋め込みも使わない**（FastGraphRAG 寄り）。名詞句は分かち書き＋ストップワード、辺は「文 3 つの窓」の共起（見出し語との対は重み 2）と条文参照（第N条から第M条まで／前条／次条）。コミュニティは決定的ラベル伝播 2 階層
- 構築は ingest 直後（source 差し替え）と初回起動（`RagSqliteGraph.BuildVersion` が古いとき `MainWindow.BuildRagGraphIfNeededAsync`）。設定 RAG の「概念グラフを再構築」でも作り直せる。失敗は `rag_graph_meta.graph_last_error` に残り、設定に表示される。**ingest は止めない**
- 検索は 3 段: (1) 一般質問は第 3 レーン（`RagGraphLane`）で静かに効く、(2) 俯瞰（`RagQueryIntent.Overview`: 全体／テーマ／概要／比較／違い、条番号なし）は `RagGraphSearch`（Refine→FTS 候補→コミュニティ票数順→関連判定 0/1 を予算 `RagGraphRelevanceBudget`／`RagGraphTimeBudgetMs` 内→要点 3 行）、(3) LLM が死んでいれば代表チャンクだけ返す
- 比較（違い／比較）は罰則・定義の機械引用より先に Overview へ。「罰則の概要」は罰則のまま（Overview の全体系 cue は罰則・定義・FAQ の後、資料一覧の前）
- 1 文だけの条文チャンクは共起が 1 になり枝刈り（既定 2）で消える → 見出し語の対を 2 にして骨を残している。`minEdgeWeight` を上げるときはここを思い出す
- 評価: `RagGraphIndexerTests`（ゴールデン資料で 第243条→第235条 の refers 辺）、`RagGraphLaneTests`、`RagGraphSearchTests`（Fake LLM で予算・退化・該当なし）、ゴールデン `overview-*` / `multihop-*`

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
