# models/ — GGUF モデル置き場



チャット用 **`.gguf`** をこのフォルダに置きます。Hugging Face 等からファイルを取得して配置してください。



`models/` は **`LocalCompanion.exe` と同じ階層** にあります（フォルダごと移動しても OK）。



リポジトリ直後は **README のみ** です。`.gguf` は次のどちらかで用意します:



- **初回起動（自動セットアップ）** … `models/` を空と見なしたとき、既定 GGUF **Gemma 4 E2B（QAT）** `gemma-4-E2B_q4_0-it.gguf` を 1 回だけ自動 DL

- **初回起動（フォルダ指定）** … 初回セットアップで別フォルダの GGUF を指定（読み取り専用・既定 DL なし）

- **手動** … 下記の手順で任意の GGUF を `models/` に配置、または **設定 → モデル** の「追加モデルフォルダ」で外部フォルダを指定



---



## 基本的な流れ



1. **`LocalCompanion.exe`** を起動（空なら初回 DL → モデル読み込み）

2. ⚙ **設定 → モデル** の状態が **OK** になるまで待つ

3. 別モデルに差し替えるときは `.gguf` をこのフォルダへ入れるか **追加モデルフォルダ** を指定し、⚙ **モデル** で **「モデルを適用（llama 再起動）」**



`selection.json` と `.default-model-bootstrap.json` は自動生成されます（手編集不要）。



---



## Hugging Face から取得する例



### サイト上でダウンロード



1. [Hugging Face の GGUF モデル](https://huggingface.co/models?library=gguf) などを開く

2. **Files and versions** から `.gguf` を Download

3. この `models/` フォルダへ移動

4. `LocalCompanion.exe` を再起動し、⚙ **モデル** で選択



### CLI



`LocalCompanion.exe` があるフォルダで:



```powershell

pip install huggingface_hub

huggingface-cli download REPO_ID ファイル名.gguf --local-dir .\models

```



---



## 量子化の目安



| 量子化 | ざっくり | メモ |

|--------|----------|------|

| **Q4_K_M** | 軽め・VRAM 節約 | 日常チャットの定番 |

| **Q5_K_M / Q6_K** | 中間 | 品質とサイズのバランス |

| **Q8_0 / Q8_K** | 重め | VRAM に余裕があるとき |

| **f16 / BF16** | かなり重い | VRAM 8 GB 未満では厳しいことが多い |



> **VRAM** … 6〜8 GB 級なら **7B Q4_K_M** から。OOM 時は量子化を下げ、⚙ でコンテキスト長を下げて **`LocalCompanion.exe` を再起動**。



---



## 追加モデルフォルダ（別の場所にある GGUF）



⚙ **モデル** タブの **追加モデルフォルダ** で、別ディレクトリの GGUF を一覧にマージできます。



- 指定フォルダは **読み取り専用**（ファイルの移動・書き込みは不要）

- 付属 `models/` と同名の GGUF がある場合は、付属側を優先

- 複数ある場合、初回フォルダ指定時は **最小サイズのチャット用 GGUF** を自動選択

- vision 用 mmproj が必要なときは **この `models/`（アプリ側）** に取得（ユーザーのフォルダには書き込みません）



設定は `data\model-library.json` に保存されます。



---



## 画像入力（vision）



本体 GGUF に加え **mmproj** 用 `.gguf` が必要なモデルがあります。



- 本体と mmproj を **同じ `models/`** に置く

- ⚙ **モデル** で mmproj を自動選択（初回は `ensure-mmproj.ps1` が補完を試行）

- ⚙ **設定 → モデル** の実行状態に **「画像入力 OK」** と出れば利用可（本体と mmproj は同じモデルファミリー用に揃える）



---



## フォルダ例（利用中）



```

models/

├── your-chat-model-Q4_K_M.gguf

├── mmproj-your-model-f16.gguf      … vision が必要な場合のみ

├── selection.json

└── .default-model-bootstrap.json   … 初回 DL 済みマーカー

```



---



## うまくいかないとき



| 症状 | 確認すること |

|------|----------------|

| 一覧に出ない | 拡張子 `.gguf`、⚙「一覧を更新」 |

| 初回 DL が失敗 | ネット → `LocalCompanion.exe` 再起動 |

| ロードが終わらない | サイズ・VRAM。軽い Q4 を試す |

| 画像が使えない | mmproj の有無、モデルが vision 対応か |

| モデル変更が反映されない | ⚙ **モデル** で **モデルを適用（llama 再起動）** |

| フォルダ移動 | `LocalCompanion.exe`・`models/`・`scripts/`・`characters/` を同じ階層で移動 |



---



*利用規約・商用可否は各リポジトリの LICENSE を確認してください。*

