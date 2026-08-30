# GitHub Release 説明文テンプレ

Release 本文は **このファイルをベースに手書き**してください（「リリースノートを生成する」は使わない）。  
貼り付けは **IME 英数モード** 推奨。

---

## コピー用（日本語）

```markdown
## 動作環境（必須）

- Windows 10（ビルド 17763 以降）または Windows 11
- [.NET 10 Desktop Runtime（x64）](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)（**必須**。会話画面に使用。ZIP 非同梱。Windows 11 では通常 OS に含まれます。Edge 本体とは別コンポーネントです）

## インストール

1. 下の Assets から `LocalCompanion-{バージョン}.zip`（例: `LocalCompanion-1.2.4.zip`）をダウンロードして展開します。
2. `LocalCompanion\LocalCompanion.exe` を起動します。
3. 初回は言語選択・セットアップのあと、必要に応じて AI エンジン／既定モデルを取得します（ネット接続・空き容量が必要です）。

## このリリースの主な内容

- （CHANGELOG から 2〜5 行）

## 困ったとき

- ZIP 内の `README.md` と `docs\Troubleshooting.md`
- チャット画面が空のときは WebView2 Runtime を確認してください
```

---

## Paste template (English, optional)

```markdown
## Requirements

- Windows 10 (build 17763+) or Windows 11
- [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (**required** for the chat view; not bundled in the ZIP; usually present on Windows 11; separate from the Edge browser app)

## Install

1. Download `LocalCompanion-{version}.zip` (for example `LocalCompanion-1.2.4.zip`) from Assets and extract it.
2. Run `LocalCompanion\LocalCompanion.exe`.
3. On first launch, complete language/setup; the app may download the AI engine / default model (network and disk space required).

## Highlights

- (2–5 lines from CHANGELOG)

## Troubleshooting

- See `README.md` and `docs\Troubleshooting.md` in the ZIP
- If the chat area is blank, install or repair WebView2 Runtime
```
