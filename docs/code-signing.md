# Authenticode コード署名（GitHub 配布向け）

LocalCompanion の **コード署名** は、Microsoft Store への出店とは別の仕組みです。

## 署名とは何か

- **Authenticode 署名** … `LocalCompanion.exe` にデジタル証明書で署名し、「この exe はこの発行者による改ざんされていないビルドです」と Windows に示します。
- **効果** … SmartScreen の警告が出にくくなったり、「発行者: ○○」と表示されたりします（証明書の種類・実績によります）。
- **配布先** … GitHub Releases の ZIP のままで問題ありません。Store に出す必要はありません。

## Microsoft Store との違い

| 項目 | Authenticode 署名 + GitHub ZIP | Microsoft Store |
|------|-------------------------------|-----------------|
| 配布 | 自分で ZIP を置く | Store 経由 |
| 審査 | なし（証明書の購入のみ） | あり |
| パッケージ | exe フォルダー展開 | 多くは MSIX |
| 手数料 | 証明書の年間費用 | Store の手数料体系 |

**結論:** 「GitHub に置いたまま、exe に署名だけ付けたい」は Authenticode で足ります。

## 必要なもの

1. **コード署名証明書（有料）** … DigiCert、Sectigo などの認証局から購入します。  
   - 個人・小規模: 標準コード署名  
   - SmartScreen の信頼が早いと言われる: EV コード署名（ハードウェアトークン付きなど）
2. **Windows SDK の `signtool.exe`** … Visual Studio / Windows SDK に含まれます。
3. **証明書ファイル（.pfx）とパスワード** … 発行時に CA から受け取ります。

無料の自己署名証明書では SmartScreen はほぼ改善しません。

## 署名の実行

証明書を用意したら、publish 後に次を実行します。

```powershell
$env:LOCALCOMPANION_SIGN_PFX_PATH = "C:\path\to\your-cert.pfx"
$env:LOCALCOMPANION_SIGN_PFX_PASSWORD = "証明書パスワード"

.\scripts\publish-win.ps1
.\scripts\sign-authenticode.ps1 -PublishRoot "bin\x64\Release\net10.0-windows10.0.26100.0\win-x64"
.\scripts\package-user-zip.ps1
```

`LOCALCOMPANION_SIGN_PFX_PATH` が設定されている場合、`publish-win.ps1` の末尾で自動的に `sign-authenticode.ps1` を呼びます。

### 環境変数（任意）

| 変数 | 説明 |
|------|------|
| `LOCALCOMPANION_SIGN_PFX_PATH` | .pfx のフルパス（必須） |
| `LOCALCOMPANION_SIGN_PFX_PASSWORD` | 証明書パスワード（未設定時はプロンプト） |
| `LOCALCOMPANION_SIGNTOOL_PATH` | `signtool.exe` のフルパス（未設定時は自動検索） |
| `LOCALCOMPANION_SIGN_TIMESTAMP_URL` | タイムスタンプ URL（既定: DigiCert） |

## セキュリティ

- `.pfx` とパスワードをリポジトリに **コミットしない** でください。
- CI で署名する場合は GitHub Actions の Secrets に格納します。

## 現状

証明書が未設定のビルドは従来どおり **未署名** です。`docs/Troubleshooting.md` の SmartScreen の説明が該当します。
