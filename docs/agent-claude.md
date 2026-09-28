# Claude Code adapter

The common rules live in [SKILLS.md](../SKILLS.md). This adapter covers Claude
Code in the Basalt repository; it does not configure Claude Desktop or the
account.

## Choosing a path

| Activity | Path |
| --- | --- |
| Known command or small, already-understood change | Coordinator and direct tools |
| Bounded independent exploration | `task-scout`, Haiku |
| Implementation with clear requirements and exclusive files | `task-builder`, Sonnet |
| Hard diagnosis or independent critical review | `critical-reviewer`, Opus 5.5 (effort xhigh) |
| A very hard question left open after the review | `frontier-reasoner`, Opus 5.5 (effort max) |

These are four available profiles, not four mandatory stages. For an ordinary
review the coordinator is enough. `critical-reviewer` receives a hard, bounded
question; `frontier-reasoner` runs the same model at maximum effort in a fresh
context, and is used only when the review produced evidence but did not settle
the same high-impact question. Before using it, tell the user what the review
established, the doubt that remains and the exact question. No permanent agent
for planning, refactoring or committing.

The files in `.claude/agents/` declare models and tools. The effective model
also depends on the runtime configuration: verify the delegation rather than
deducing it from the profile name. Explore does not guarantee Haiku. Ordinary
subagents have a separate context; forks inherit the conversation. Some
built-in agents omit the project instructions.
[Subagent documentation](https://code.claude.com/docs/en/sub-agents).

## Where Basalt splits naturally

| Area | Paths | Typical owner |
| --- | --- | --- |
| Razor for VB: parser, writers, mappings | `src/Basalt.Razor.Vb*` | coordinator (critical) |
| Language server | `src/Basalt.Razor.Vb.LanguageServer` | builder, one handler at a time |
| VS Code extension | `extensions/vscode-vbrazor` | builder (Node) |
| Visual Studio extension | `extensions/vs-vbrazor` | builder (Windows + VS SDK) |
| Rider plugin | `extensions/rider-vbrazor` | builder (JDK 17+, Gradle) |
| IDE shell, editor, panels | `src/Basalt.Shell` | builder; coordinator for threading |
| Workspace: Roslyn, MSBuild, debugging | `src/Basalt.Workspace` | builder; review for debugger |
| Avalonia designer | `src/Basalt.Designer` | builder |
| Templates and samples | `templates/`, `samples/` | builder |

Two delegates may run together only on disjoint rows of this table. A change
to the parser invalidates conclusions about the language server and the
writers: they share one grammar by design.

## Contract and quality

Pass goal, exclusive files, invariants, available evidence and the completion
criterion following the [common contract](agent-workflow.md). The coordinator
keeps doing useful independent work; default maximum one active delegate. Do
not forward the whole conversation and do not duplicate investigations or
tests.

Scout, reviewer and frontier reasoner have read-only tools. Save the final diff
to a local artifact and give its path along with the base revision and the
evidence; prepare it with a direct tool before delegating. A new change
invalidates conclusions about the parts it touched. The builder has Bash for
checks, so the ban on commits, publishing and spawning agents through a CLI is
also an operating rule, not an absolute sandbox.

Profiles do not preload other skills or separate memories. Long references are
read only when relevant. If a builder needs a tool its environment lacks — a
display for Avalonia rendering, the Visual Studio SDK, a JDK, netcoredbg, a
browser — the coordinator runs the relevant check or supplies the missing
material; a tool limit does not justify skipping a check.

## Start-up and verification

To load a new agents directory, restart the Claude Code session. Definitions
are native Markdown with YAML frontmatter.
[Format and loading reference](https://code.claude.com/docs/en/sub-agents).

The coordinator model remains a user/host choice. For an ordinary new session
`claude --model sonnet` is enough; `/model` shows and changes the choice. Do not
change global preferences automatically.
[Model configuration](https://code.claude.com/docs/en/model-config).

Local checks verify format, references and profile constraints. They do not
prove that the account can use a model, nor that tokens are saved: those need
evidence from real work. On the first delegation, check the effective model and
tools. If Opus 5.5 is not available to the account, state the adequate fallback
once.
