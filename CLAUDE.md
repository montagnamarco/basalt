# Claude entry point

@AGENTS.md
@SKILLS.md

These compact files are the shared source of truth. References in AGENTS.md
are loaded only when relevant; do not import the full reference library here.

## Claude Code routing adapter

### Available models

Exact IDs, never date-suffixed: `claude-opus-5-5`, `claude-sonnet-5`,
`claude-haiku-4-5`. The bare aliases `sonnet` and `haiku` in a profile resolve
to the current generation. Do not invent IDs; if a profile names a model the
host cannot resolve, disclose the fallback once and continue with an adequate
available model.

Opus 5.5 is the reasoning tier and replaces both Opus 5 and Fable: it reasons
better than Fable at about half the cost, so neither older model is routed to.

### Who owns the session

The primary model is fixed when the session starts and this file cannot change
it. Work with the owner you have, and route *delegated* work by the table below.
An Opus-owned session is not a licence to keep routine implementation in the
owner: that is precisely the work to hand to `task-builder`.

| Delegated work | Profile | Model |
| --- | --- | --- |
| Bounded reading, locating evidence, repetitive low-risk processing | `task-scout` | `claude-haiku-4-5` |
| Implementing a bounded change with known requirements and exclusive files | `task-builder` | `claude-sonnet-5` |
| Independent review of a critical diff; a difficult bounded diagnosis | `critical-reviewer` | `claude-opus-5-5` (xhigh) |
| One unresolved, high-consequence question after a reviewer attempt | `frontier-reasoner` | `claude-opus-5-5` (max) |

There is no stronger model above Opus 5.5, so `frontier-reasoner` is not a
bigger model: it is the same one at `max` effort, in a fresh context, given a
single question and the evidence the first attempt produced. Use it only when
that attempt left the same high-consequence question open.

### Effort, before reaching for a bigger model

`effort` (`low`/`medium`/`high`/`xhigh`/`max`) trades thoroughness for tokens
within one model, and is the first lever to try. Use `low` for scouting and
mechanical work, `high` for ordinary implementation, `xhigh` or `max` only where
correctness outweighs cost — source mappings, the Razor parser grammar, the
shape of generated classes, the debug adapter protocol.

### What to delegate

Delegate a bounded, independent task when the delegate's file set does not
overlap the owner's and useful local work can continue alongside it. Natural
seams in this repository: one editor extension (`extensions/vscode-vbrazor`,
`extensions/vs-vbrazor`, `extensions/rider-vbrazor`), one language-server
handler, one designer feature, one project template, a scouting question, a
review.

Keep with the owner: changes to `Basalt.Razor.Vb` parsing or source mapping,
to the shape of generated view/component classes, and any change whose
contract you would spend longer writing than implementing.

Default maximum two active delegates, on disjoint directories. Pass outcome,
evidence locations, exclusive files, constraints, checks and the done condition
— a short contract, not the conversation. Reports state changed files, concrete
findings, check results and what remains.

### Review is not optional where it is prescribed

For changed source mappings, generated class shape, parser grammar, debugger
protocol or threading between Roslyn and the UI, obtain a `critical-reviewer`
pass on the final diff before committing. Pass the diff and the invariants, not
the author's reasoning. Self-review is never independent review.

### Cost discipline

Minimize total tokens per verified result. Default to one owner; avoid
duplicated context and repeated checks. Use direct tools for known commands,
lookups, formatters and tests — never spawn an agent to run a command or
summarize its output. Finish small work locally when the handoff would cost
more than the work.

Use ordinary fresh-context subagents, not conversation forks. Never assume a
built-in Explore/Plan agent loaded project instructions. Never invent Codex tool
parameters in Claude Code. Do not reread shared instructions already in context.
Read [Claude workflow](docs/agent-claude.md) when delegating or troubleshooting
model selection.
