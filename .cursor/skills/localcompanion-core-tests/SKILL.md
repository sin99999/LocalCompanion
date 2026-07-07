---
name: localcompanion-core-tests
description: >-
  LocalCompanion Core unit test conventions — portable fixtures, no personal
  paths, InternalsVisibleTo, and coverage expectations. Use when adding or
  fixing tests in tests/LocalCompanion.Core.Tests/.
---

# LocalCompanion — Core テスト規約

## コマンド

```powershell
dotnet test tests/LocalCompanion.Core.Tests/LocalCompanion.Core.Tests.csproj
dotnet test tests/LocalCompanion.Core.Tests/LocalCompanion.Core.Tests.csproj --filter "FullyQualifiedName~ChatExport"
```

実装タスク後は **可能なら全件** 実行してから報告する。

## 禁止・非推奨

| NG | 代わり |
|----|--------|
| `C:\Users\...` / `H:\pg\...` 固定パス | `Path.GetTempPath()` + Guid、または `Fixtures/` |
| ファイル無しで `return;` のサイレント合格 | 埋め込み fixture で必ず検証 |
| メンテ persona 名をテストデータに | 中立名（`太郎` / `花子` / `Example`） |
| 外部ネットワーク・実 llama 依存 | モック or パースのみの単体テスト |

## 推奨パターン

```csharp
// 一時ディレクトリ
var dir = Path.Combine(Path.GetTempPath(), "lc-test-" + Guid.NewGuid().ToString("N"));
try { Directory.CreateDirectory(dir); /* ... */ }
finally { try { Directory.Delete(dir, true); } catch { } }
```

- 命名: `Method_Scenario_Expected`
- internal 実装のテスト: `InternalsVisibleTo` 済み（`LocalCompanion.Core.csproj`）
- 環境依存（vec0.dll）: 冒頭で `IsAvailable` を確認し、不可なら **Assert.Skip 相当**（xunit 2 では理由付き早期 return + コメント。可能なら fixture で代替）

## カバレッジの目安

機能を直したら **同 PR / 同セッションで** 最低 1 テスト:

| 領域 | 例 |
|------|-----|
| チャット保存 | `ChatExportRequestParserTests`, `ChatExportPendingStoreTests` |
| RAG vec | `RagSqliteVecTests` |
| パス解決 | `AppPathsTests` |
| プロンプト | `ChatSystemPromptTextsTests` |

## Fixtures

共有: `tests/LocalCompanion.Core.Tests/Fixtures/PenalCodeTestFixtures.cs`

刑法系テストはここから生成。実ファイル依存は **オプション**（`LOCPENALCODE_MD` 環境変数など）に留める。

## Agent の完了条件（テスト変更時）

1. `dotnet test` 全合格
2. 新規テストが「どの退行を防ぐか」を名前 or コメントで明示
3. CI / 他マシンでも赤くならないこと
