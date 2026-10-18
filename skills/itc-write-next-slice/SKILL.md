---
name: itc-write-next-slice
description: Write the next small, verifiable slice toward docs/PLAN.md into docs/slices/NN-name.md. Use when the developer asks for the next slice.
---

# itc-write-next-slice

Write one slice. Don't implement anything.

## Read first

1. `AGENTS.md` for stack, layout, commands, the slice size cap and the gates. Never take commands, paths or ports from memory.
2. `docs/CURRENT-STATE.md`, then `docs/PLAN.md`.
3. `docs/templates/slice.md`: the layout to fill. Don't change its headings.
4. The last slice in `docs/slices/`, especially its `## Outcome` (follow-ups often change what comes next).
5. The source files the new slice will touch. For a replacement project, the matching legacy code named in PLAN.md; for a Figma Make project, the matching screen in `reference/figma-make/`. A slice that ports a screen pulls in only the `components/ui/` files that screen uses.

If the previous slice has no `## Outcome`, stop. It needs `/itc-review-completed-slice` first.

## No plan?

If `docs/PLAN.md` is missing, or the requested work isn't in it, stop and recommend `/itc-plan-project`. Small follow-ups that fit an existing phase are fine to slice.

## Pick the next slice

Take the next unfinished piece of the current phase unless the last Outcome or the developer says otherwise. A good slice:
- produces one observable result (a user can do X, an endpoint returns Y)
- fits in one agent session and one `feat:` commit
- doesn't depend on an unanswered business question

**Size cap (AGENTS.md section 3): at most 5 tasks and about 8 files touched.** Count the files before you save. Over the cap → split, write only the first piece, and list the rest under Notes so the next writer picks them up. A slice can also cover several tiny phases if that stays under the cap.

## Check before writing

Make sure the slice doesn't contradict PLAN.md, CURRENT-STATE.md (open questions, decisions), AGENTS.md or the real code. Fix each conflict in the slice, or mark it `[NEEDS CLARIFICATION: question]`. Don't hand over a slice with a marker in scope: narrow the slice or ask (gate 1).

## Slice file

Copy `docs/templates/slice.md` to `docs/slices/NN-short-name.md`. Two-digit number; a name that says what it does (`07-violation-crud`, not `07-seventh-slice`). The file is the implementation prompt: no separate prompt block, no time estimates. A junior dev should be able to follow it.

Fill every section. Tests to add: 2–5 tests by behavior and location; for a bug fix the failing test comes first. Data, Auth and Accessibility lines get a real answer or "none." Manual checks: steps automation can't cover; for replacements, legacy vs new behavior to compare.

Add the slice number to its phase's **Slices:** line in PLAN.md.

## Reply

Slice path, its phase, one line on why it's next, the task and file counts, and anything narrowed or still open. Then stop.
