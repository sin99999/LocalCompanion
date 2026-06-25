# 変更履歴（CHANGELOG）

本ファイルは LocalCompanion の利用者向け変更履歴です。
バージョンは `LocalCompanion.csproj` の `<Version>` と一致させます。

## 1.0.0 - 2026-06-11

最初の公開候補バージョンです（GitHub 公開時にバージョンを確定）。

### 追加

- ローカル LLM（llama-server）とのストリーミングチャット
- キャラクター設定（名前・性格・話し方・生成パラメータ）
- RAG（PDF / Word / テキスト / HTML の資料検索）
- 画像添付と Vision 入力(mmproj 対応モデル使用時)
- URL 読み込み（Web ページ本文の 1 回限り添付）
- VOICEVOX 連携による読み上げ（自動起動・終了、話者選択）
- 初回起動時の自動セットアップ（llama.cpp・既定モデル・mmproj の取得）
- 日本語 / 英語 UI
- 設定画面からのバージョン表示・ライセンス情報・ログフォルダーへのアクセス
- 会話・設定・RAG データのバックアップ（ZIP エクスポート）

### 変更

- 初回ダウンロードの既定チャットモデルを Google の `gemma-4-E2B_q4_0-it.gguf` に変更
- 画像入力用 mmproj を同リポジトリの `gemma-4-E2B-it-mmproj.gguf` に揃えた

### 修正

- GPU 買い替え時に llama.cpp バックエンドを自動で入れ替える
- CUDA / AMD / Vulkan で VRAM が足りない場合、GPU レイヤー数を下げて再試行する
- 既定モデルサイズに合わせて起動前のメモリ見積もりを更新した

### 配布

- 公開 ZIP（framework-dependent）: `.\scripts\package-user-zip.ps1`
- .NET 同梱版: `.\scripts\publish-win.ps1 -BundleAllRuntimes`

---

書式の目安: 「追加 / 変更 / 修正 / 削除」の見出しでまとめ、利用者に影響のある内容のみを記載します。
