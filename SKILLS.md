# Agent workflow

Shared operating rules. Read once per session with AGENTS.md; reread only
changed sections. User instructions and repository-specific constraints take
precedence. Optimize total tokens per verified result, including handoffs and
rework. Staying within the user's usage limits is a first-class constraint.
Use actual usage telemetry when available; never invent token counts or savings.

## Choose the execution path

1. Define the outcome, current evidence, affected scope and completion check.
2. Use a direct tool for a known search, edit, formatter, build or test. Do not
   spend a model turn or spawn an agent merely to run a command or summarize it.
3. Prefer GPT-6 Sol for new Codex sessions when the host offers it. It owns
   ordinary implementation, debugging, planning, integration and review.
   Claude Code follows its separate adapter in CLAUDE.md where present.

| Work | Preferred runtime | Initial reasoning |
| --- | --- | --- |
| Bounded, low-risk work with an objective result | `gpt-6-luna` | low |
| Normal session ownership and everyday engineering | `gpt-6-sol` | medium |
| Ordinary work when GPT-6 Sol is unavailable | `gpt-5.6-terra` | medium |
| Coding work when GPT-6 Sol is unavailable and Terra is inadequate | `gpt-5.6-sol` | medium or high |
| A bounded high-consequence question Sol cannot resolve | `gpt-6-astra` | high only when justified |

Move bounded work from Luna to Sol when a concrete failed attempt or an upfront
invariant spanning several subsystems shows Luna is inadequate. Escalate to Astra only
when an evidence-producing Sol attempt leaves the same question unresolved, or
when the known consequence and complexity make Sol plainly inadequate. State
the evidence and exact bounded question to the user before each escalation.
Do not manufacture failed attempts or route an entire feature to a stronger
model for a question that can be isolated. A sensitive filename, task size,
large diff, long test suite or lack of time is not enough by itself. Return
routine implementation and verification to Sol or direct tools.

These are routing defaults, not mandatory stages. An explicit user model choice
takes precedence. The host selects the primary model when a session starts:
AGENTS.md and SKILLS.md cannot change it mid-session. Select an available model
in the host for new sessions; delegating does not change the primary model's
cost. Set delegate models explicitly so they do not inherit
an expensive primary by accident. Use an available adequate model if a preferred
one is unavailable, and disclose that fallback once. Other providers use their
own available models and adapters, not invented Codex model IDs.

## Delegate only when it pays for itself

- Default to one owner. Delegate a bounded, independent task only when useful
  local work can continue alongside it. Default maximum: one active delegate;
  add another only for measured independence and a clear benefit. Compare the
  whole cost: setup/context + implementation + review/rework, not model price alone.
- State role and exact model briefly before delegation; set the model explicitly
  when supported. A reused agent retains its model and accumulated context.
  Reuse only for the same task/domain, never an expensive agent solely because idle.
- Pass outcome, evidence locations, exclusive files, constraints, checks and done
  condition. Use a fresh minimal context (`fork_turns="none"` when available),
  not the full conversation. Reference relevant files instead of copying them.
- The delegate reads the compact root instructions and only pertinent references.
  Roles: scout reads; builder implements; investigator diagnoses/reviews;
  tidier makes specified mechanical changes. A reviewer examines the final diff
  read-only. Roles are assignments, not permanently active agents. Delegates do
  not spawn further delegates unless the coordinator assigns a bounded subtask.
  No lengthy boilerplate role prompts; commit operations stay with the change owner.
- Reports contain changed files, concrete findings, check results, unresolved
  issues and the next required action. Aim for 150–250 words; retain essential evidence.
- Never duplicate an active delegate's edits, investigation or test run. Review
  its diff and evidence; repeat a check only if stale, incomplete or contradicted.
- After two unproductive attempts without new evidence, stop the retry loop:
  isolate a reproducer and escalate that question, not the entire conversation.
  Missing credentials, unavailable services or unclear requirements need resolution,
  not a stronger model guessing. Return to the ordinary tier after diagnosis.

## Keep context useful

- Start with git status, the current task/state and relevant paths. Use `rg`
  and targeted excerpts; expand only when dependencies require it. Never dump
  whole logs or instructions repeatedly. Batch independent reads when possible.
- Load specialist guidance from AGENTS.md's reference map only for affected work.
  Discover a missing tool once per session; remember its absence until capabilities change.
- Put full logs/screenshots in artifacts; report counts, errors and paths. Avoid
  rapid polling: wait for running checks, respecting required user updates.
- Keep one short current-state file per initiative in ignored `docs/planning/` (normally
  40–80 lines). It points to the detailed plan, evidence and commits. Update it
  at milestone boundaries, handoff or interruption, not after every command.
- Preserve historical evidence separately; do not rewrite or reread the full
  history on every turn. Validate state against the working tree when resuming.
  A fresh session is useful at a completed milestone, not as a ritual mid-task.

## Scope, verification and completion

- Complete the authorized milestone and its necessary dependencies. Record
  unrelated discoveries in the backlog; do not turn every finding into a new
  project. Explain a material scope change before continuing with necessary work.
  Refactor only what the outcome requires; do not mix speculative cleanup into a fix.
- Simple changes need a short task contract, not a planning ceremony. Multi-commit
  work has a plan in ignored `docs/planning/`: outcome, items with files/steps/checks,
  completion criteria, open questions and a dated evidence register.
- Use the smallest check that proves the behavior, then affected regression tests
  at the integration boundary. Run a broader suite for shared changes or an
  explicit request. Do not run the full suite after each small commit by default.
- Test failures, changed dependencies or new concerns invalidate prior evidence.
  Record command, source revision/dirty scope, environment, outcome and artifact.
  Reuse only evidence whose relevant code and environment still match.
- Use headless browser tests for affected UI flows and inspect screenshots when
  layout matters. Do not repeat an entire journey for a backend-only change if
  narrower checks prove it. Never omit source-mapping, debugger or generated-code checks
  merely to save tokens. Never substitute mocks for proof of real concurrency.
- For changed source mappings, generated class shape, parser grammar, debugger
  protocol or concurrency guarantees, obtain an independent read-only review before commit
  when an appropriate reviewer is available. Pass the diff and invariants, not the
  author's full reasoning. The coordinator can check integration while it runs.
  If independent review is unavailable, explicitly record that gap and perform
  a separate invariant review; never label self-review independent.
- Completion requires the requested behavior, meaningful assertions and resolved
  correctness findings. Inspect the actual diff and test evidence, not an agent's
  claim or test count alone. Add missing coverage; do not weaken tests to pass.
- Keep readable code: explicit English names, focused functions, one statement
  per line, logical spacing, useful phase comments, no clever compressed logic.
  Follow project formatters and review changed code visually.
- Commit each completed feature/fix with its tests, separate from unrelated work.
  Use a short imperative English subject that says what the user gains ("Put
  rulers on the design surface"), then a prose body explaining why and what was
  measured; no co-author/generated trailers.
  Never push unless explicitly authorized. Never mark unverified work complete.
- User-facing updates explain findings and decisions, not routine tool narration.
  Final reports state outcome, relevant checks and actual limits concisely.

## Templates and evaluation

Use [workflow templates](docs/agent-workflow.md) when planning, handing off or
recording evidence; load only the needed template. They are operating contracts,
not a new framework, API service or automatic model switcher.
Evaluate future work by completed outcomes, duplicated reads/checks, handoff count,
rework and available usage telemetry. Do not invent token counts or savings.
