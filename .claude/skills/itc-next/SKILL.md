---
name: itc-next
description: Run the slice loop (write → implement → test → review) for the next slice and stop at a gate. "/itc-next" does one slice, "/itc-next phase" runs until the current phase ends, "/itc-next 3" runs up to three slices.
disable-model-invocation: true
---

# itc-next

Drive the loop. Each step runs exactly as its skill says; this skill decides only what runs next and when to stop.

## Before the loop

1. Read `AGENTS.md` and `docs/CURRENT-STATE.md`. No `docs/PLAN.md` → stop and recommend `/itc-plan-project`.
2. **Branch.** If CURRENT-STATE.md names none, or the current phase has no branch yet, create `phase/N-short-name` off `main`, check it out, and write it under **Branch** in CURRENT-STATE.md. Otherwise check out the named branch. `git status` must be clean; if not, stop and show it.
3. **How far.** No argument: one slice. A number: that many slices. `phase`: until the phase's done-when lines are met. Gates override all three.
4. **Resume.** If the last slice in `docs/slices/` is unfinished, start at the step it's missing instead of writing a new one: no `feat:` commit → implement; no `## Test results` → test; no `## Outcome` → review.

## Per slice

Run in order, each by its own skill:

1. `itc-write-next-slice`
2. `itc-implement-slice NN`. If this harness has subagents (Claude Code `Agent`, Codex), run it in a fresh one so the implementer sees only `AGENTS.md`, `docs/CURRENT-STATE.md`, the slice file and the files it names, not this conversation. Its prompt: "Follow `skills/itc-implement-slice/SKILL.md` for slice NN." Where there are no subagents (Cursor), run it inline.
3. `itc-run-slice-tests NN`
4. `itc-review-completed-slice NN`

After every step, check the gates. A gate hit ends the loop: don't start the next step, don't write the next slice.

## Gates (from AGENTS.md section 3)

1. A `[NEEDS CLARIFICATION]` inside the slice's scope.
2. A failing test not listed as accepted in CURRENT-STATE.md.
3. A "Done when" item that can't be met, or a slice that stopped mid-way.
4. A deviation marked **needs decision**, a new dependency, or a proposed change to AGENTS.md or to PLAN.md's phases.
5. The end of the phase.

## End of phase

When the review marks the phase done:

1. Make sure the branch is pushed.
2. Open the PR, phase branch → `main`. Title `Phase N: <name>`. Body: the phase goal, its done-when lines ticked, and one line per slice with its `feat:` commit sha.
   - Gitea: `tea pr create --base main --title "..." --description "..."`
   - GitHub: `gh pr create --base main --title "..." --body "..."`
   - Neither installed: print the branch name and the body so the developer opens it.
3. Stop. The next phase starts after the developer verifies the phase and merges.

## Reply

One line per slice run: number, name, status from its Outcome, both commit shas. Then the gate that stopped the loop (or "phase done, PR opened: link") and the one thing needed from the developer. Then stop.
