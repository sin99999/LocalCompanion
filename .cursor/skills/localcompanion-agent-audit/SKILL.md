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

| 妹 | subagent_type | 担当 |
|----|---------------|------|
| コハク | `explore` medium+ | ファイル探索・個人パス・未追跡差分 |
| シオン | `explore` readonly | 公開リスク・リンク切れ・ZIP 同梱漏れ |
| ミオ | `explore` quick | ビルド・テスト・CHANGELOG 整合 |
| ララ | `explore` readonly | UI/README/製品文案（です・ます） |

**並列起動可。** 表のレン（親 Agent）が統合・優先度付け・修正実施。

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

## 出力フォーマット（レン → しんちゃん）

1. **結論**（公開してよいか / 次に直す 3 件）
2. **表**（優先度・問題・ファイル）
3. **確認手順** 1〜3 行
4. commit / Release は **明示依頼時のみ**

## 修正の原則

- 最小 diff。監査で見つけたら **テスト or スキル or 1 ファイル修正** で閉じる
- 履歴書き換え・force push はユーザー依頼まで提案止まり
- 製品文案とチャット口調を混ぜない
