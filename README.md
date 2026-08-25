<div id="top"></div>

<h1>
  <img src="Assets/StoreLogo.png" alt="LocalCompanion" width="40" height="40" style="vertical-align: middle; margin-right: 10px;">
  LocalCompanion
</h1>

<p align="center"><strong>Windows 向けローカル LLM チャット — RAG・キャラクター設定・VOICEVOX 対応（クラウド API キー不要）</strong></p>

<p align="center">
  <a href="https://github.com/sin99999/LocalCompanion/actions/workflows/ci.yml"><img src="https://github.com/sin99999/LocalCompanion/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="License"></a>
  <a href="https://github.com/sin99999/LocalCompanion/releases"><img src="https://img.shields.io/github/v/release/sin99999/LocalCompanion?label=release" alt="Release"></a>
</p>

<p align="center">
  <a href="https://github.com/sin99999/LocalCompanion/releases">ダウンロード（Releases）</a> ·
  <a href="docs/Troubleshooting.md">トラブルシューティング</a> ·
  <a href="CONTRIBUTING.md">Contributing</a>
</p>

## 使用技術一覧

<p style="display: inline">
  <img src="https://img.shields.io/badge/-C%23-239120.svg?logo=csharp&style=for-the-badge&logoColor=white">
  <img src="https://img.shields.io/badge/-.NET_10-512BD4.svg?logo=dotnet&style=for-the-badge">
  <img src="https://img.shields.io/badge/-WinUI_3-0078D4.svg?logo=windows&style=for-the-badge">
  <img src="https://img.shields.io/badge/-SQLite-003B57.svg?logo=sqlite&style=for-the-badge&logoColor=white">
  <img src="https://img.shields.io/badge/-llama.cpp-000000.svg?style=for-the-badge">
</p>

## 目次

1. [LocalCompanion とは](#localcompanion-とは)
2. [動作環境](#動作環境)
3. [フォルダ構成（配布 ZIP）](#フォルダ構成配布-zip)
4. [起動と終了](#起動と終了)
5. [初回起動について](#初回起動について)
6. [主な機能](#主な機能)
7. [データの保存場所](#データの保存場所)
8. [読み上げ（VOICEVOX）](#読み上げvoicevox)
9. [よくある質問](#よくある質問)
10. [困ったとき](#困ったとき)
11. [ライセンス・変更履歴](#ライセンス変更履歴)

## LocalCompanion とは

Windows 向けのローカル AI チャットアプリケーションです。会話・資料検索（RAG）・音声読み上げを、原則として **お使いの PC 内だけ** で処理します。クラウド API キーは不要です。

ソースの改変・fork は [LICENSE](LICENSE)（MIT）の範囲で自由です。著作権は LocalCompanion Project に帰属します。

<p align="right">(<a href="#top">トップへ</a>)</p>

## 動作環境

| 項目 | 要件 |
|------|------|
| OS | Windows 10 ビルド 17763 以降 |
| ランタイム | [.NET 10 Desktop Runtime（x64）](https://dotnet.microsoft.com/download/dotnet/10.0)（未導入の場合は 1 回インストール） |
| WebView2 | [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)（**必須**。会話画面の表示に使用。Windows 11 では通常 OS に含まれます。Windows 10 でも多くの環境で導入済みですが、無い場合は Evergreen ランタイムをインストールしてください。ZIP には同梱しません） |
| ネットワーク | 初回起動時（AI エンジンと既定モデルのダウンロード。自前 GGUF 指定時はモデル取得をスキップ）。任意の更新確認で GitHub にアクセスすることがあります |
| 読み上げ | 任意。[VOICEVOX](https://voicevox.hiroshiba.jp/) を別途インストール |

WinUI の実行に必要なファイルはアプリに同梱されています。別途「開発者モード」をオンにする必要はありません。**WebView2 Runtime は同梱されない**ため、上記のとおり別途必要です（Edge 本体とは別コンポーネントです）。

<p align="right">(<a href="#top">トップへ</a>)</p>

## フォルダ構成（配布 ZIP）

GitHub Releases の Assets から **`LocalCompanion-{バージョン}.zip`**（例: `LocalCompanion-1.2.2.zip`）をダウンロードして展開します。

ZIP を展開すると、おおむね次の構成になります（`data\` と `tools\` は初回起動後に作成されます）。

| パス | 内容 |
|------|------|
| `LocalCompanion.exe` | アプリ本体 |
| `appsettings.json` | 既定の接続先・RAG 設定など |
| `Assets\` | アイコンなど |
| `scripts\` | llama-server 補助スクリプト |
| `models\` | 付属の GGUF 置き場（初回ダウンロード先・mmproj の取得先） |
| `characters\` | キャラクター JSON |
| `docs\` | トラブルシューティング・ヘルプ HTML |
| `data\` | 会話・RAG・各種設定（実行時に自動作成） |
| `tools\llama-cpp\` | llama-server（初回起動時に自動取得） |

くわしいモデル配置は [models/README.md](models/README.md)、キャラクター設定は [characters/README.md](characters/README.md) をご覧ください。

<p align="right">(<a href="#top">トップへ</a>)</p>

## 起動と終了

### 起動

1. 配布フォルダ内の **`LocalCompanion.exe`** をダブルクリックします。
2. 初回は言語選択・セットアップ表示のあと、チャット画面が開きます（数分かかることがあります）。
3. すでに起動しているときに exe を再度開くと案内が表示され、2 つ目のウィンドウは開きません。

### 終了

ウィンドウ右上の **×** で閉じます。裏で AI エンジン（llama-server）などの終了処理が行われます。ウィンドウが消えたあとも、タスクマネージャー上のプロセスがすぐ消えない場合がありますが、通常は短時間で終了します。

<p align="right">(<a href="#top">トップへ</a>)</p>

## 初回起動について

初回起動時の流れは次のとおりです。

1. **言語選択**（日本語 / English）
2. **初回セットアップ**（説明と任意の GGUF フォルダ指定）
3. **自動準備**（プログレス表示）

### 自動セットアップ（「次へ」を選んだ場合）

| 内容 | 説明 |
|------|------|
| llama-server | ローカル AI 推論エンジン（llama.cpp） |
| 既定のチャットモデル | Google **Gemma 4 E2B（QAT）** `gemma-4-E2B_q4_0-it.gguf`（`models\` が空の場合） |
| vision 用ファイル（mmproj） | 画像入力に必要な場合（アプリ側の `models\` に取得） |

お使いの GPU を確認し、適した設定で llama-server を起動します。

### お手持ちの GGUF がある場合

初回セットアップ画面で **フォルダを選ぶ** と、指定フォルダ内の GGUF を読み取り専用で利用できます（フォルダ内のファイルは変更しません）。複数ある場合は **ファイルサイズが最小のチャット用 GGUF** を自動選択します。既定モデルのダウンロードは行いません。

vision 用の mmproj が必要な場合は、**アプリ側の `models\` フォルダ** に取得します（ユーザーの GGUF フォルダには書き込みません）。

2 回目以降は、すでに準備済みの場合はすぐ使い始められます。自動セットアップを選んだ初回は **インターネット接続** が必要です。

<p align="right">(<a href="#top">トップへ</a>)</p>

## 主な機能

| 機能 | 説明 |
|------|------|
| チャット | ローカル AI と会話。キャラクター設定・会話履歴に対応（**デフォルト AI** の会話は左の履歴一覧に表示されません） |
| 資料検索（RAG） | 登録したテキスト資料を検索し、回答に反映 |
| Web 参照 | メッセージ内の URL 自動取得、「調べて」などの調査意図での Web 検索（キー不要。オフ可） |
| 長期記憶 | 名前付きキャラクターごとに短い事実を内部保存し、そのキャラクターの会話で自然に思い出して返す（**設定 → 基本設定**）。デフォルト AI には長期記憶はありません |
| 音声入力 | Windows の音声認識で入力（**設定 → 基本設定** で有効化） |
| モデル管理 | 付属 `models\` と、任意で指定する **追加モデルフォルダ** を一覧表示。適用で llama を再起動 |
| 読み上げ | VOICEVOX で返答を音声再生（任意） |
| 画面言語 | 日本語 / 英語 |

### チャットのオプション（＋メニュー）

| 項目 | 説明 |
|------|------|
| **資料検索** | 登録した RAG 資料を回答に反映 |
| **推論** | モデルの思考過程を表示（オフにすると応答が安定しやすい場合があります） |
| **履歴** | 同じスレッドの過去のやり取りをモデルに渡す（オフにすると毎回単発の質問になります） |

URL 添付・メッセージ内 URL の取得上限は **2 MB** です。Web 検索で開く各ページは最大約 **512 KB** です（いずれも RAG 取込とは別の上限です）。メッセージに `https://...` を書くと自動で内容を取り込みます。「調べて」などの調査意図では、キー不要の Web 検索（既定: DuckDuckGo HTML）も行います（`appsettings.json` の `WebSearchEnabled` でオフにできます）。

<p align="right">(<a href="#top">トップへ</a>)</p>

## データの保存場所

会話履歴・RAG 資料・各種設定は、通常 **exe と同じフォルダ内の `data\`** に保存されます。  
`data\` に書き込めない場合（例: Program Files 配下）は、`%LocalAppData%\LocalCompanionLlama\` に保存されます。  
Visual Studio や `dotnet build` からの起動（`bin\Debug` など）も、配布 ZIP とは異なり `%LocalAppData%\LocalCompanionLlama\` を使います。

| ファイル（例） | 内容 |
|----------------|------|
| `rag.db` | 会話履歴・RAG インデックス |
| `language-settings.json` | 画面の言語 |
| `character-settings.json` | 応答の調整設定 |
| `model-library.json` | 追加モデルフォルダなどのライブラリ設定 |

アプリ内 **設定 → このアプリについて** から、データのバックアップ（ZIP 書き出し）ができます。

**注意:** `LocalCompanion.exe` ごとフォルダを移動・コピーする場合は、`data\` フォルダも一緒に移してください（LocalAppData 側を使っている場合は、そちらのフォルダを確認してください）。

<p align="right">(<a href="#top">トップへ</a>)</p>

## 読み上げ（VOICEVOX）

読み上げ機能は **VOICEVOX**（無料の日本語音声合成ソフト）を別途インストールすると使えます。

1. [VOICEVOX 公式サイト](https://voicevox.hiroshiba.jp/) からインストール
2. LocalCompanion を **再起動**（または設定画面を開き直す）— インストールを検出すると **VOICEVOX** タブが表示されます
3. ⚙ **設定 → VOICEVOX** で「**読み上げを有効**」をオンにし、話者を選択（変更は自動保存されます）

VOICEVOX **未インストール時は VOICEVOX タブは表示されません**。チャット・資料検索など **他の機能は通常どおり** 使えます。

### Installing VOICEVOX (for English-speaking users)

The official VOICEVOX installer is **Japanese only**. If you cannot read the screens:

1. Use a translation tool (for example Google Lens) on the installer text.
2. You can usually proceed by **clearing every checkbox** on each screen and clicking **Next** until installation finishes.

Then **restart LocalCompanion** (or reopen Settings) so the **VOICEVOX** tab appears. Open **Settings** → **VOICEVOX**, enable speech, and choose a speaker. Changes are saved automatically.

<p align="right">(<a href="#top">トップへ</a>)</p>

## よくある質問

**Q. 会話や資料はインターネットに送信されますか。**  
A. 会話・RAG・添付の本文処理は PC 内で完結します。任意の更新確認で GitHub にアクセスすることがあり、VOICEVOX 利用時は VOICEVOX の更新確認でも GitHub にアクセスすることがあります。初回セットアップや mmproj 取得では Hugging Face 等へアクセスすることがあります。URL 読み込み時や、メッセージ内の URL・「調べて」などの調査意図による Web 検索時は、その先のサイトへアクセスします。既定の Web 検索は DuckDuckGo（HTML）で、検索語がそのサービスへ送られます（`appsettings.json` の `WebSearchEnabled` でオフにできます）。音声入力は Windows の音声認識を使うため、環境によってはオンライン認識になることがあります。

**Q. 起動時に「Windows によって PC が保護されました」と出ます。**  
A. コード署名を行っていないため、初回に表示されることがあります。「詳細情報」→「実行」で起動できます。

**Q. exe をダブルクリックしても起動しません。**  
A. [.NET 10 Desktop Runtime（x64）](https://dotnet.microsoft.com/download/dotnet/10.0) のインストールが必要な場合があります。すでに起動している場合は案内が出て終了します。先に開いているウィンドウをご確認ください。

**Q. 「すでに起動しています」と出ます。**  
A. LocalCompanion は同時に 1 つだけ起動できます。先に開いているウィンドウをご利用ください。何も見えない場合は、タスクバーや別のデスクトップをご確認ください。

**Q. チャット画面が真っ白／会話が表示されません。**  
A. [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) が未導入の可能性があります（必須コンポーネントです。Edge ブラウザ本体とは別です）。Windows 11 では通常入っています。無い場合は上記公式ページから Evergreen ランタイムをインストールしてください。

**Q. 初回のダウンロードが失敗します。**  
A. ネットワーク環境や、短時間に何度も起動し直したことによる制限が考えられます。しばらく待ってから再試行してください。お手持ちの GGUF がある場合は、初回セットアップでフォルダを指定すると既定モデルのダウンロードを省略できます。

**Q. 別の場所にある GGUF を使えますか。**  
A. 初回セットアップ、または **設定 → モデル** の「追加モデルフォルダ」で指定できます。指定フォルダは読み取り専用で、mmproj はアプリ側の `models\` に取得します。

**Q. 読み上げが動きません。**  
A. VOICEVOX のインストールと、設定画面での有効化が必要です。タブが無い場合は VOICEVOX を入れたあとアプリを再起動してください。設定の変更は自動で保存されます。読み上げが不要なら、インストールしなくても問題ありません。

<p align="right">(<a href="#top">トップへ</a>)</p>

## 困ったとき

くわしい手順は [docs/Troubleshooting.md](docs/Troubleshooting.md) をご覧ください。

| 症状 | まず確認すること |
|------|------------------|
| 起動しない | .NET 10 Desktop Runtime（x64）が入っているか。すでに起動していないか |
| チャット画面が空／真っ白 | [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) が入っているか（必須） |
| チャットが使えない | **設定 → モデル** の状態が「OK」になるまで数分待つ。ポート 8080 が他アプリや残った llama-server に使われていないか |
| 初回ダウンロード失敗 | インターネット接続。しばらく時間をおいて再試行。自前 GGUF があればフォルダ指定でスキップできます |
| DLL エラー | [Visual C++ 再頒布可能パッケージ（x64）](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist) のインストール |

設定画面の **このアプリについて → ログフォルダーを開く** から、問題報告用のログを確認できます。

<p align="right">(<a href="#top">トップへ</a>)</p>

## ライセンス・変更履歴

| ドキュメント | 内容 |
|--------------|------|
| [LICENSE](LICENSE) | 本リポジトリのライセンス（MIT・著作権は LocalCompanion Project） |
| [SECURITY.md](SECURITY.md) | セキュリティ上の連絡 |
| [CHANGELOG.md](CHANGELOG.md) | 変更履歴 |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 報告・開発のしかた |
| [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) | サードパーティライセンス |
| [docs/Troubleshooting.md](docs/Troubleshooting.md) | トラブルシューティング（全文） |
| [docs/help/](docs/help/) | アプリ内ヘルプ用 HTML |

<p align="right">(<a href="#top">トップへ</a>)</p>
