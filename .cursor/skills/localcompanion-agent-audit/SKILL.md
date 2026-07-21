---
name: localcompanion-agent-audit
description: >-
  Structured multi-agent audit ("妹作戦") for LocalCompanion — release risks,
  test portability, rules/skills gaps. Use when the user asks for a full review,
  妹作戦, pre-release audit, or "what could embarrass us if published".
---

# LocalCompanion — 妹作戦（監査）

## いつ使うか

- 公開前・大きな機能追加後の「漏れ洗い」
- 「全部確認して」「完璧にしたい」系の依頼
- テスト / rules / skills / 配布物の横断レビュー

## 役割分担（Task subagent）

定番4役（よく使う）。**人数上限ではない。** 不足役・アイ／ハルは `ren-sisters` と `H:\pg\Cursor\レンファミリー名簿.md` を見て追加可。

| 妹 | subagent_type | 担当 |
|----|---------------|------|
| コハク | `explore` medium+ | ファイル探索・個人パス・未追跡差分 |
| シオン | `explore` readonly | 公開リスク・リンク切れ・ZIP 同梱漏れ |
| ミオ | `explore` quick | ビルド・テスト・CHANGELOG 整合 |
| ララ | `explore` readonly | UI/README/製品文案（です・ます） |

**必要な役だけ並列可**（名簿全員の工場一括は禁止）。表のレン（親 Agent）が統合・優先度付け・修正実施。

## 監査チェックリスト

### P0 — 配布ブロッカー

- [ ] `package-user-zip.ps1` 成功
- [ ] ZIP に `AGENTS.md` / `.cursor` / 個人 GGUF が **入っていない**
- [ ] `appsettings.json` に絶対パスなし

### P1 — ユーザー体感

- [ ] RAG / 保存 / データパスの既知バグが再発していない
- [ ] `dotnet test` 全合格（個人パス依存テストなし）

### P2 — 公開の恥ずかしさ

- [ ] `docs/`・README に個人名・私的メモ
- [ ] テストに `C:\Users\SIN` 等
- [ ] git log の Author 整理（必要なら squash 提案のみ。force は依頼時）

### P3 — Agent 基盤

- [ ] `.cursor/rules` と skills が矛盾していない
- [ ] 触った領域に対応スキルがある
- [ ] `AGENTS.md` と `localcompanion.mdc` のコマンド・データパス一致
- [ ] 直近でホストを荒らす／惊吓せる操作があったら、個人 skill `host-safe-ops` または本スキルへ **再発防止を1項目追記した**（PDCA）

## 出力フォーマット（レン → しんちゃん）

1. **結論**（公開してよいか / 次に直す 3 件）
2. **表**（優先度・問題・ファイル）
3. **確認手順** 1〜3 行
4. commit / Release は **明示依頼時のみ**

## 修正の原則

- 最小 diff。監査で見つけたら **テスト or スキル or 1 ファイル修正** で閉じる
- 履歴書き換え・force push はユーザー依頼まで提案止まり
- 製品文案とチャット口調を混ぜない
- しんちゃんはコードを読めない前提 → **壊す操作より診断**。レガシーソフト連打・画面モード互換・Program Files 改変は `host-safe-ops` に従う

## 配布ドキュメント（ZIP）

- `CONTRIBUTING.md` は `publish-win.ps1` / `package-user-zip.ps1` で同梱必須（README 相対リンク切れ防止）
- Web 検索プライバシーは README / About / help で **DuckDuckGo・クエリ送信・WebSearchEnabled** を揃える
