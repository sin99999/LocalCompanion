# 開発への参加

LocalCompanion に関心をお寄せいただき、ありがとうございます。

ソースコードの改変、フォーク、プルリクエストは、事前の許可なく行えます。GitHub の Issue およびプルリクエストとしてご提出ください。

ソースコードの著作権は LocalCompanion Project に帰属します（[LICENSE](LICENSE) / MIT License）。利用および改変の条件は LICENSE に従ってください。

## 報告

| 種類 | 方法 |
|------|------|
| 不具合 | [Issue（不具合報告）](https://github.com/sin99999/LocalCompanion/issues/new?template=bug_report.yml) |
| 機能要望 | [Issue（機能要望）](https://github.com/sin99999/LocalCompanion/issues/new?template=feature_request.yml) |
| 起動・設定 | まず [docs/Troubleshooting.md](docs/Troubleshooting.md) をご確認ください |

再現手順、バージョン、オペレーティングシステムを記載していただけると、調査が円滑になります。

## 開発

```powershell
git clone https://github.com/sin99999/LocalCompanion.git
cd LocalCompanion
dotnet build LocalCompanion.csproj -c Debug -p:Platform=x64
.\scripts\run-debug-winui.ps1
dotnet test tests/LocalCompanion.Core.Tests/LocalCompanion.Core.Tests.csproj
```

- ビルドは `Platform=x64` が必須です。`dotnet build` だけでは llama / GGUF は初回起動時に取得されます。
- 実行および配布確認には [.NET 10 Desktop Runtime（x64）](https://dotnet.microsoft.com/download/dotnet/10.0) と [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)（必須）が必要です。
- コミットに `models/*.gguf`、`bin/`、`dist/`、個人キャラクター JSON は含めないでください。

## プルリクエスト

小さな差分を歓迎します。事前の連絡は不要です。利用者向けの画面文言および README は、ですます調で記載してください。

## 配布

利用者向け ZIP はメンテナが [GitHub Releases](https://github.com/sin99999/LocalCompanion/releases) で配布します。`git push` だけでは ZIP は更新されません。
