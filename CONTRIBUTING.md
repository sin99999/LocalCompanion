# Contributing

LocalCompanion への関心ありがとうございます。

**改変・fork・プルリクエストは事前の許可なしで構いません。**「手伝っていいですか？」のような確認は不要です。issue や PR をそのまま送ってください。

ソースコードの**著作権は LocalCompanion Project に帰属**します（[LICENSE](LICENSE) / MIT License）。利用・改変の条件は LICENSE に従ってください。

## 報告

| 種類 | 方法 |
|------|------|
| 不具合 | [Issue（不具合報告）](https://github.com/sin99999/LocalCompanion/issues/new?template=bug_report.yml) |
| 機能要望 | [Issue（機能要望）](https://github.com/sin99999/LocalCompanion/issues/new?template=feature_request.yml) |
| 起動・設定 | まず [docs/Troubleshooting.md](docs/Troubleshooting.md) |

再現手順・バージョン・OS を書いていただけると助かります。

## 開発

```powershell
git clone https://github.com/sin99999/LocalCompanion.git
cd LocalCompanion
dotnet build LocalCompanion.csproj -c Debug -p:Platform=x64
.\scripts\run-debug-winui.ps1
dotnet test tests/LocalCompanion.Core.Tests/LocalCompanion.Core.Tests.csproj
```

- 詳細は [AGENTS.md](AGENTS.md)（開発者向け）
- コミットに `models/*.gguf`・`bin/`・`dist/`・個人キャラ JSON は含めないでください

## プルリクエスト

小さな差分を歓迎します。事前連絡は不要です。UI 文言・README は利用者向け（です・ます）でお願いします。

## 配布

利用者向け ZIP はメンテナが [GitHub Releases](https://github.com/sin99999/LocalCompanion/releases) で配布します。`git push` だけでは ZIP は更新されません。
