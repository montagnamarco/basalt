# Operating contracts for agents

Use only the relevant template. They cut down context hand-offs; they do not
require a new document or agent for every command. The operating rules stay in
[SKILLS.md](../SKILLS.md).

## Choosing a path

| Situation | Path | When to change |
| --- | --- | --- |
| Find a handler, run the build or a known test | Direct tool | The result opens an unresolved question |
| Mechanical change or local bug with a proven cause | Small model, or the current owner if the hand-off costs more | Requirements or behaviour become uncertain |
| Bounded feature, clear requirements | Ordinary owner | Complex invariants or a diagnosis that stops progressing |
| Diagnosis across modules or a bounded critical review | Reviewer tier | Architectural ambiguity or unresolved risk |
| Subtle race, mapping drift, high-risk architectural decision | Frontier tier on the critical question | Return to ordinary implementation after the diagnosis |
| Tool unavailable (VS SDK, JDK, display, debugger) | Check the capability and the dependency | A bigger model does not remove the block |
| Two substantial independent tasks | One delegate with exclusive files | No duplicated reads, writes or shared build output |

Markdown does not change the model. The coordinator uses the selector the host
really exposes; delegation tools set the runtime explicitly. If the owner is
already on the right model, it works directly. Do not create an agent per phase
or copy the coordinator's history at every hand-off. Reasoning effort grows with
a concrete difficulty, not with the number of files.

## Task / delegation contract

```text
Outcome: observable result; what ends the task.
Role / runtime / reasoning: explicit choice and a one-sentence reason.
Evidence: measured symptom and file/test references; not a hypothetical diagnosis.
Ownership: files that may be changed; shared files that are read-only.
Constraints: only the exceptions/conditions beyond AGENTS.md and SKILLS.md.
Checks: commands required, reserved resources, reusable existing evidence.
Deliver: diff, outcomes with counts and artifacts, limits; no autonomous widening.
```

As a guide: 200–400 words for the brief, 150–250 for the reply. Do not cut
essential invariants or evidence to meet a length. If the diagnosis changes,
report the evidence and revise the contract before continuing. A direct action
needs only its result and its check in one sentence.

## Quality without a mandatory chain

- **Simple:** owner → change → relevant check → commit.
- **Ordinary:** implementation and tests in the same assignment; the coordinator
  reviews the diff, with no separate planner if requirements are clear.
- **Critical** (mappings, parser grammar, generated class shape, debugger
  protocol, UI threading): diagnosis/contract if needed → implementation →
  independent read-only review while the coordinator checks integration → owner
  fixes and reruns invalidated evidence → commit.
- **Blocked:** isolate the missing dependency or evidence. Do not raise the
  model, the number of agents or the prompt length to make up for a missing tool.

### Reviewer contract

```text
Read-only scope: final diff, necessary dependent files and base revision.
Invariants: behaviour to preserve, error cases and domain constraints.
Evidence: commands, results and artifacts already produced; no ritual reruns.
Deliver: defects with file/line, consequence and proof; or no defect found,
         stating what was examined and what remains unverified.
```

A review looks for defects and missing coverage, not for taste. A green run
does not close unresolved correctness findings; a large number of tests does not
replace proof of the invariant. Tests must check observable effects, including
negative ones — in this repository a rendering claim is proved by rasterising and
reading the pixels, and a mapping claim by resolving a real position through it.

## Resume state: docs/planning/<initiative>-state.md

```text
Active goal and completion criterion:
Detailed plan and current items:
Branch / last verified commit / changes in progress:
Relevant decisions and invariants:
Files to read first:
Valid checks: command, revision/scope, environment, result, artifact.
Open work: files, owner, next step.
Active resources: processes, debug sessions, agents and their state.
Known limits: what is NOT proven or complete.
```

Normally 40–80 lines, not a new chronological log. Update at the end of a
milestone or before a pause. On resuming compare branch, commit and git status:
a short state file does not override the current code.

## Verification register

```text
Check: name of the behaviour or invariant.
Source: commit; or baseline + list of relevant dirty files.
Environment: OS, SDK, editor host version, display/headless.
Command: reproducible command without credentials.
Result: exit code, tests/asserts, residual errors; screenshot if UI.
Artifact: path of the full log or structured result.
Invalidated by: files/dependencies/configuration whose change requires a rerun.
```

A new commit does not automatically invalidate every proof; a change to a shared
dependency (the parser, the mapping table, Roslyn version) can invalidate many.
