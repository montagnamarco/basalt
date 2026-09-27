# Basalt agent instructions

Read [SKILLS.md](SKILLS.md) once per session. It owns model routing, delegation,
context management, verification and commit workflow. This file adds repository
constraints. Do not load every reference below automatically.

## ChatGPT / Codex models

For ChatGPT / Codex sessions, use the OpenAI routing in [SKILLS.md](SKILLS.md).
The runtime exposed these exact model IDs on 2026-09-27:
`gpt-6-luna`, `gpt-6-sol`, `gpt-6-astra`, `gpt-5.6-terra`, `gpt-5.6-sol`.
Recheck the host's available models in each new session; this list is not a
guarantee of access in another account, API or ChatGPT model picker.

Prefer Luna for bounded simple work, Sol for ordinary implementation, and Astra
for difficult questions that justify escalation under SKILLS.md. The 5.6 models
remain available fallbacks. Use direct tools when no delegate is needed.
Set a delegate's exact model explicitly when the host supports it; never invent
an ID or assume a Markdown instruction switches the primary model.
Claude model IDs and profiles apply only in Claude Code; they are not Codex
delegate choices. Read the Claude adapter only for Claude-specific work or an
explicit user request.

## Non-negotiable boundaries

- Never publish anything: no `dotnet nuget push`, no `vsce publish`, no Visual
  Studio Marketplace or JetBrains Marketplace upload, no GitHub release. Never
  push without explicit user authorization. Propose the command instead.
- Never read or print API keys: the AI panel stores them in the OS keychain or
  DPAPI (`src/Basalt.Workspace/Ai/ApiKeyStore.cs`); tests never call a real
  model provider.
- Work on `master` unless told otherwise; preserve unrelated edits.
- Do not add, remove or upgrade NuGet, npm or Gradle dependencies, add
  projects to `Basalt.slnx` or create new top-level directories without
  authorization. Versions shared by several projects (`AvaloniaVersion`,
  `RoslynVersion`, package `VersionPrefix`) live in `Directory.Build.props`.
- Do not remove or weaken tests without approval. Do not install global tools,
  `dotnet new` templates or IDE extensions on the user's machine unasked.
- Working plans live in ignored `docs/planning/`; never commit them.

## Domain invariants

- **One parser everywhere.** The compiler (`Basalt.Razor.Vb`), the source
  generator, the language server and the IDE run the same parser. Never add a
  second opinion about the language (a regex, a grammar-only rule) where the
  parser can answer. TextMate grammars only colour until semantic tokens arrive.
- **Source mappings are load-bearing.** Every VB fragment taken from a template
  carries a mapping and an `#ExternalSource` directive. An off-by-one does not
  crash: it makes IntelliSense describe the token next door and breakpoints bind
  to the wrong line. Any change to a writer needs a round-trip mapping test.
- **Generated class shape is a public contract.** Views inherit
  `RazorPage(Of TModel)`, pages carry `[RazorCompiledItem]`, components derive
  from `ComponentBase`. ASP.NET Core and Blazor discover them unmodified; the
  integration surface is `AddVbViews()` and nothing is forked.
- **Compile time, not run time.** `.vbhtml`/`.vbrazor` become classes in the
  user's assembly. `.vbpage` in development is the only deliberate exception.
- **Roslyn stays off the interface thread.** Completion, diagnostics,
  classification and refactorings run in the background and marshal results to
  the Avalonia dispatcher.
- **Packages do not depend on the IDE.** `Basalt.Razor.Vb*` and the templates
  must work in Visual Studio, Rider and VS Code without Basalt installed.
- LSP positions are UTF-16 code units, lines and columns zero-based; template
  mappings are one-based. Convert at one boundary only.

## Coding essentials

- C# on net10.0 (`Basalt.Razor.Vb` is netstandard2.0 because it runs inside the
  compiler), nullable enabled, file-scoped conventions of the surrounding file.
  VB for samples, templates and the sample plugin. Kotlin for the Rider plugin,
  TypeScript for VS Code.
- Code, identifiers and comments are English. User-facing IDE strings go
  through `src/Basalt.Core/Localization` resources, never literals; Italian
  leftovers are defects to fix when touched.
- Readability over brevity; comments explain *why* and what was measured. The
  large shell files (`MainWindow.axaml.cs`, `CodeEditor.cs`, `DesignSurface.cs`)
  should shrink: put new features in their own class, not in them.
- Tests must fail without the change. When fixing a defect, reintroduce it once
  to prove the test catches it. Rendering claims are proved by rasterising and
  reading pixels; mapping claims by resolving a real position.

## Selective reference map

| When needed | Read |
| --- | --- |
| Codex delegation and model selection | [Shared routing](SKILLS.md) |
| Claude Code delegation and troubleshooting | [Claude adapter](docs/agent-claude.md) |
| Planning, handing off, recording evidence | [Workflow templates](docs/agent-workflow.md) |
| Razor for VB language, packages, templates | [README.md](README.md) sections on `.vbhtml`, Blazor, `.vbpage` |
| Editor extensions state | `extensions/README.md` and each extension's README |
| QuickBASIC cross-compiling | `src/Basalt.QuickBasic/CROSS-COMPILING.md` |

## Checks and local work

- Build: `dotnet build Basalt.slnx`; run the IDE: `dotnet run --project src/Basalt.Shell`.
- Tests: `dotnet test tests/Basalt.Tests` narrowed with
  `--filter "FullyQualifiedName~ClassName"`; HTTP integration:
  `dotnet test tests/Basalt.Web.Tests`. Debugger tests are skipped unless
  netcoredbg is found (`BASALT_NETCOREDBG`, `~/.basalt/debugger/<rid>/`, PATH).
- Packages: `build/pack.sh` (macOS/Linux) or `build/pack.ps1` (Windows) packs
  and verifies the four NuGet packages into `artifacts/`; verification installs
  Basalt.Templates, so use `--skip-test`/`-SkipTest` unless asked.
- Toolchain: `build/setup-dev.ps1 -Check` / `build/setup-dev.sh --check` lists
  what is present or missing without installing anything.
- Language server: `dotnet publish src/Basalt.Razor.Vb.LanguageServer -c Release
  -r <rid> --self-contained -o extensions/<extension>/server`.
- VS Code: `npm ci && npm run compile` in `extensions/vscode-vbrazor` (Node 18+).
  Rider: `./gradlew buildPlugin` in `extensions/rider-vbrazor` (JDK 21).
  Visual Studio: needs the "Visual Studio extension development" workload.
- Choose checks from the change; instruction-only edits need reference checks,
  not the suite. The suite must stay green on Windows, macOS and Linux: guard
  Unix-only behaviour (PTY, `file`, clang cross-compiling) explicitly.
