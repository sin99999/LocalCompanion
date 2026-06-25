# AGENTS.md

Instructions for coding agents working in this repository.

## Project overview

- **LocalCompanion**: Windows desktop app for local LLM chat, RAG, optional VOICEVOX TTS, and character personas. No cloud API keys required.
- **UI**: WinUI 3 (native XAML). Business logic lives in `src/LocalCompanion.Core/`; the repo root is the WinUI shell.
- **Inference**: `llama-server` (installed via scripts / native host). Default chat model: Gemma 4 E2B (GGUF).
- **Localization**: User-facing strings are in Japanese and English (`LocalizationResources.cs`). Keep new UI copy consistent with existing tone in each language.

## Commands

Run from the repository root (PowerShell):

```powershell
dotnet build LocalCompanion.csproj -c Debug -p:Platform=x64
.\scripts\run-debug-winui.ps1
.\scripts\publish-win.ps1
.\scripts\package-user-zip.ps1
```

- Always pass `-p:Platform=x64` (or another configured platform) when building.
- `dotnet build` does **not** download llama.cpp or GGUF weights. That happens on **first app launch** (`CompanionStartup`).
- Release version: `LocalCompanion.csproj` → `<Version>`. Keep `CHANGELOG.md` in sync before release.

## Testing

- Core unit tests: `dotnet test tests/LocalCompanion.Core.Tests/LocalCompanion.Core.Tests.csproj`
- At minimum, verify `dotnet build` succeeds.
- Manual checks: `run-debug-winui.ps1`, or the published exe under `dist\LocalCompanion\`.

## Code style

- Prefer **small, focused diffs**. Match existing naming and patterns.
- **Shipped text** (UI, errors, README, `docs/help/*.html`): clear, neutral product copy. No slang, emoji, or maintainer-specific notes.
- **WinUI layout**: Trace the parent chain (Page → panels → ScrollViewer → content) before tweaking margins on a single control.

## User data locations

| How the app is launched | User data directory |
|-------------------------|---------------------|
| Debug build (`bin\...\win-x64\LocalCompanion.exe`) | `%LocalAppData%\LocalCompanionLlama\` |
| Published / ZIP layout (exe beside `scripts/` and `models/`) | `{exe directory}\data\` |

Notable files under the data directory:

- `rag.db` — RAG index
- `language-settings.json` — UI language (created after first-run language choice)
- `character-settings.json` — sampling / context sliders (feeds llama-server `-c` when set)

## llama-server and context

- Context size (`-c`) comes from `appsettings.json` and/or `character-settings.json`.
- `LlamaServerNativeHost` caps context for safety (e.g. values above 24576 → 16384; large multimodal models may cap at 12288).
- Chat history: `ChatService` loads recent session messages within the context budget when **History** is enabled in chat options.
- Default assistant (no character preset): sessions use internal key `__default_ai__`; they are **not** listed in the sidebar conversation history.

## RAG

- Ingestion: `RagStructuralChunker` (headings, chapter/article markers) then `RagTextChunker` for overflow (default 900 chars, overlap 128, sentence-aware splits).
- Config: `ChunkSize`, `ChunkOverlap`, `RagTopK` in `appsettings.json`.
- Embeddings: local `llama-server` `/v1/embeddings` only.

## Boundaries

- Do **not** commit `models/*.gguf`, `characters/selection.json`, `bin/`, `obj/`, or `dist/`.
- Do **not** put absolute paths or secrets in `appsettings.json` (validated by `publish-win.ps1`).
- Do **not** add API keys, tokens, or personal paths to this file or to the codebase.
- Official distribution is a **framework-dependent ZIP** (`package-user-zip.ps1`). Do not switch the project to `PublishSingleFile` / `PublishTrimmed` for releases without an explicit maintainer decision.
- Do not bundle third-party weights or personal character JSON in release artifacts.

## Git

- Do not commit or push unless the maintainer asks.
- Use separate commits when changes cover unrelated topics.
- Commit messages may be in Japanese or English; state **why**, not only what.

## Gotchas

- Debug vs published builds use **different data directories** — confirm which layout when reproducing bugs.
- Distribution ZIP requires **.NET 10 Desktop Runtime (x64)** on the target machine; runtime install guidance is in README and `docs/help/`.
- VOICEVOX is optional and not bundled; chat works without it (TTS disabled).
- First launch may download llama.cpp and the default GGUF (network required).
- Pre-release checklist: [docs/公開前チェックリスト.md](docs/公開前チェックリスト.md)
- **GitHub Releases** host the user ZIP; `git push` alone does not. A **Private** repo still requires login to download.
- Pasting release notes on GitHub with Japanese IME on may corrupt wording (e.g. お手持ち → お急ぎ); paste in alphanumeric mode or via Notepad first.
- Do not push dozens of local dev commits as-is for a public-facing repo; squash or ship ZIP-only via Releases when a clean history is required.

## Agent skills and rules (this repo)

| Resource | When to use |
|----------|-------------|
| [.cursor/rules/localcompanion.mdc](.cursor/rules/localcompanion.mdc) | Always — build, data paths, completion criteria |
| [.cursor/rules/winui-xaml.mdc](.cursor/rules/winui-xaml.mdc) | Editing `*.xaml` / `*.xaml.cs` |
| [.cursor/skills/localcompanion-release/](.cursor/skills/localcompanion-release/SKILL.md) | ZIP, publish, GitHub Release, note about distribution |
| [.cursor/skills/localcompanion-winui-debug/](.cursor/skills/localcompanion-winui-debug/SKILL.md) | Layout bugs, ScrollViewer, alignment |
| User skill `winui-design` | Fluent layout and XAML review |
| User skill `winui-dev-workflow` | Build and `run-debug-winui.ps1` |

Cursor User Rules (Ren persona, handover, diary) live in the maintainer's Cursor settings — not in this repo.

## Further reading

- [README.md](README.md)
- [docs/Troubleshooting.md](docs/Troubleshooting.md)
- [docs/help/](docs/help/) (localized HTML for About dialog)
- [CHANGELOG.md](CHANGELOG.md)
