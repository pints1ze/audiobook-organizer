---
name: itc-review-completed-slice
description: Close out an implemented and tested slice by recording what actually changed in its slice file, updating docs/PLAN.md and docs/CURRENT-STATE.md, then committing the docs and pushing the phase branch. Use when a slice is done and the docs need to catch up before the next slice.
---

# itc-review-completed-slice

Make the docs match reality. Don't fix code, add features or write the next slice.

## Inputs

- `docs/slices/NN-name.md`, including its `## Test results`. If those are missing, say so and recommend `/itc-run-slice-tests`. Never invent results.
- **Git**, the source of truth for what changed: the slice's `feat:` commit (`git log --oneline` since the last `docs:` commit, `git show --stat <sha>`) plus `git status` / `git diff` for anything still uncommitted. Read the diffs of key files. If there's no `feat:` commit, the implement step didn't finish: say so and stop.
- `docs/PLAN.md`, `docs/CURRENT-STATE.md`, `docs/templates/CURRENT-STATE.md` and `AGENTS.md`.

## Compare plan to reality

- Was each "Done when" item met? Tick the box or say why not.
- Any changed file outside the slice's scope is a deviation. That includes leftovers like debug logging.
- Classify each deviation as **fine**, **improvement**, **needs decision**, **debt** or **bug**.
- Resolve any `[NEEDS CLARIFICATION]` the code now answers. Move the rest to Open questions.

## Write the outcome

Append to the slice file:

```markdown
## Outcome (YYYY-MM-DD, commit {feat sha})

**Status:** Implemented | Verified | Needs revision
**Summary:** 1–3 sentences on what was built.
**Changed:** N files (key ones listed)
**Deviations:** item: type, recommendation
**Follow-ups:** items for later slices
```

Status rules:
- **Verified** only when the developer confirms the slice works. Accepted test failures don't block it; unaccepted ones do. Ask instead of assuming.
- **Implemented** when all four checks pass but the developer hasn't confirmed. This is enough for the loop to continue to the next slice; the developer verifies at the end of the phase.
- **Needs revision** when a Done when item failed or an unaccepted regression remains (gate 2 or 3).

## Update PLAN.md

Status lives only here and in the slice file, so don't create a separate tracker.
- Set the phase status. A phase is **done** when its "done when" lines are met.
- Slot follow-ups into the right phase. Propose new phases or reorders to the developer instead of making them (gate 4).
- Tick a "Finished when" item when it's met.

## Update CURRENT-STATE.md

Rewrite it to match reality, from `docs/templates/CURRENT-STATE.md`. Keep it to about one page; it points to PLAN.md and the slices instead of repeating them.
- Branch, where things stand, current phase, last and next slice, verification.
- Open questions: add new ones, remove answered ones.
- Decisions: add any made during the slice, one line each.

If a stack, layout or command fact changed (new package, new folder convention), propose the AGENTS.md edit to the developer instead of making it (gate 4).

## Commit and push

Only when Status is **Implemented** or **Verified** and nothing above needs a decision:

```
git add docs
git commit -m "docs: slice NN outcome"
git push -u origin <branch from CURRENT-STATE.md>
```

Anything outside `docs/` still uncommitted is a deviation to report, not something to sweep into this commit. Never push to `main`; never force-push.

## Reply

Status, deviations needing a decision, both commit shas, whether the phase is now done. Recommend the next step: `/itc-next` or `/itc-write-next-slice`, or, if the phase is done, that `/itc-next` opens the PR. Then stop.
