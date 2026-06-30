# キャラ設定（characters）

1キャラ = 1つの `.json` ファイルです。

`characters/` は **`LocalCompanion.exe` と同じ階層** にあります。

リポジトリ直後は **README.md** のみです。`selection.json` は初回起動時に自動作成されます（**Git には含めません**）。キャラ JSON は ⚙ **キャラ** で作成するか、手で UTF-8 の `.json` を置いてください。

## 使い方

| 場所 | 用途 |
|------|------|
| **左サイドバーのキャラ** | 会話で使うキャラを切り替え（**選択なし＝デフォルト** あり） |
| **設定 → キャラクター** | 登録・編集（保存してもサイドバーの選択は変わらない） |

1. `LocalCompanion.exe` を起動
2. 設定の **キャラクター** タブで名前・プロンプトを書いて **保存** → `名前.json`
3. 左サイドバーの **キャラ** から使うキャラを選ぶ
4. 削除は ⚙ の **「この名前の json を削除」**

## ファイル例

`AI.json`（UTF-8）:

```json
{
  "name": "AI",
  "persona": "あなたはAI。…",
  "speakingStyle": "",
  "temperature": 0.8,
  "topP": 0.95,
  "contextLength": 8192,
  "maxOutputTokens": 4096
}
```

- `temperature`: **0.0 〜 2.0**
- `topP`: **0.05 〜 1.0**

`selection.json` は選択中のファイル名（自動更新・手編集不要・**ローカル専用**）。

コンテキスト長を変えたあとは **`LocalCompanion.exe` を再起動** すると反映されます。
