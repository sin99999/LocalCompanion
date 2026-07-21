---
name: localcompanion-winui-debug
description: >-
  Debug WinUI layout and UI bugs in LocalCompanion — parent layout chain,
  ScrollViewer width, git history on same file, margin pitfalls. Use when UI
  misalignment, clipping, scroll issues, or "something feels off" bug reports.
---

# LocalCompanion — WinUI 不具合・レイアウト

## いつ読むか

- 設定画面・チャット画面の **ズレ・見切れ・二重スクロール**
- 「なんか変」「5mm ずれた」など言語化が弱い報告
- スライダー・ScrollViewer・ナビ幅まわり

併用: `winui-design` skill（Fluent / XAML 一般）

## 森を見る（必須手順）

1. **親チェーンを上まで辿る**: `Page` → `Grid` / `TabView` → `ScrollViewer` → `StackPanel` → 対象コントロール
2. **誰が幅・高さを決めているか** を1文で書いてから編集
3. **最初の diff は親1要素 OR 子1要素だけ** — Margin の当てずっぽうは最後の手段
4. **`git log -p -- <同じファイル>`** で過去の同種修正・再発を確認

## よくある原因（LocalCompanion）

| 症状 | 疑う所 |
|------|--------|
| 右端で全体が左にズレ | Slider thumb のはみ出し → ScrollViewer の `ViewportWidth` 固定 |
| 設定タブで縦が揺れる | プレビュー文字サイズ → スクロールバー出現 → 幅ブレ |
| 左ナビ見切れ | 固定 15% ではなく **最長ラベルから幅計算**（`MainWindow.xaml.cs`） |
| チャットが末尾まで見えない | `ScrollViewer` / 自動スクロール / `ListView` 更新方式 |
| 長履歴でスクロール重い／選択で落ちる | **会話全体の巨大 `RichTextBlock` は禁止**（WinUI 既知 AV: microsoft-ui-xaml #7299 / #9442）。発言またぎの選択・コピーは **WebView2 の単一 DOM**（`ChatConversationView`＋`ChatConversationHtmlBuilder`）。Cursor/VS Code と同じ Chromium 選択 |
| 全文選択 vs リンク | HTML の `<a href>`＋`NavigationStarting` で外部ブラウザ。CSS `cursor: pointer` |
| ListView 複数 TextBlock またぎ選択 | **WinUI 仕様上不可**（フォーカスは1要素）。カスタムオーバーレイは非推奨 |

## 編集前チェック

- [ ] `SettingsPage.xaml` / `ChatPage.xaml` / `MainWindow.xaml` を **開いてから** 1行 patch
- [ ] 製品 UI 文言は `LocalizationResources.cs`（日英ペア）
- [ ] 変更後: `dotnet build LocalCompanion.csproj -c Debug -p:Platform=x64`

## 確認

```powershell
.\scripts\run-debug-winui.ps1
```

exe 直ダブルクリックは WinApp SDK 未登録で落ちることがある → 開発確認は上記。

## 再発したら

再発しやすい落とし穴は `docs/Troubleshooting.md` または CHANGELOG に1行追記を提案（理由付き・最小）。
