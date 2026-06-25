---
name: localcompanion-release
description: >-
  LocalCompanion distribution, ZIP packaging, GitHub Releases, and public
  visibility. Use when packaging user ZIP, publishing to GitHub, writing release
  notes, push vs squash, Private/Public repo, or note article about releases.
---

# LocalCompanion — 配布・GitHub・Release

## いつ読むか

- `package-user-zip.ps1` / `publish-win.ps1` を実行・修正するとき
- GitHub Release 作成、Release 説明文の執筆
- `git push` 前（開発用 commit をそのまま載せない判断）
- note / README の配布向け文案

## コマンド（リポ root・PowerShell）

```powershell
dotnet build LocalCompanion.csproj -c Debug -p:Platform=x64
.\scripts\publish-win.ps1
.\scripts\package-user-zip.ps1
dotnet test tests/LocalCompanion.Core.Tests/LocalCompanion.Core.Tests.csproj
```

- バージョンは `LocalCompanion.csproj` の `<Version>` のみ。`CHANGELOG.md` と揃える。
- `appsettings.json` に絶対パスを書かない（`publish-win.ps1` が検証）。

## データディレクトリ（バグ再現で必ず確認）

| 起動方法 | データ |
|----------|--------|
| Debug（`bin\...\LocalCompanion.exe`） | `%LocalAppData%\LocalCompanionLlama\` |
| 配布 ZIP（exe 横に `scripts/`） | `{exe ディレクトリ}\data\` |

初回テストで「履歴が残っている」→ 多くは **AppData 共有** か **publish 成果物の混在**。

## GitHub の二層

| 層 | 内容 | 利用者 |
|----|------|--------|
| リポ本体 | ソース + `git log` | 開発者 |
| **Releases** | ビルド済み ZIP + 説明 | **配布の本丸** |

`git push` だけでは ZIP は出ない。Release に ZIP を添付する。

### Private vs Public

- **Private**: ZIP DL に GitHub ログイン + 権限（Collaborator 等）
- **Public**: Release URL は **アカウント不要** で DL 可（シークレット窓で確認）

Release を Publish しても **リポが Private のまま** ならログイン必須。

### 開発履歴を外に見せない

ローカルに細かい commit が多数ある場合、GitHub 公開前に:

1. 現状を squash / orphan で **1〜3 本の製品向けメッセージ** にまとめる
2. または **Releases に ZIP のみ**（ソースは載せない／空リポのまま）

`git log` の Author（`SIN <sin@local>` 等）も公開前に整理を検討。

## Release 説明文

- **README 全文コピペは非推奨**（長い・相対パス画像 `Assets/...` は表示されない・リンク切れ）
- 製品文案（です・ます）。ギャル口調・絵文字は **チャットのみ**
- 貼り付けは **IME 英数モード** 推奨（「お手持ち」→「お急ぎ」等の誤変換防止）
- 「リリースノートを生成する」は commit 一覧が混ざるので **基本使わない**

短いテンプレの要素: 動作環境 / インストール（ZIP 展開→exe）/ 初回 DL / 主な機能 / `data\` の場所 / 困ったときは ZIP 内 `README.md` と `docs\`

## note 記事

公開してよい: 開発の流れ・技術構成・失敗談・ローカル完結の動機  
公開しない: 恋愛ネタ・個人パス（`H:\...`）・キャラ persona 本文・引き継ぎ・日記の中身

## チェックリスト

- [docs/公開前チェックリスト.md](docs/公開前チェックリスト.md)
- [docs/Troubleshooting.md](docs/Troubleshooting.md)
