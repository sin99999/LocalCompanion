---
name: localcompanion-rag
description: >-
  LocalCompanion RAG pipeline — ingest, chunking, hybrid search, sqlite-vec,
  source filters, and startup.log diagnosis. Use when RAG returns nothing,
  wrong citations, vec0 errors, ingest failures, or penal-code/legal search bugs.
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
| 設定 | ルート `appsettings.json`（ChunkSize, RagTopK 等） |

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
