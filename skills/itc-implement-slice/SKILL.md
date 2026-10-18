---
name: itc-implement-slice
description: Implement one written slice from docs/slices/NN-name.md — tests first, tick the checklist as you go, commit the code as "feat: slice NN". Use for "implement slice NN" or "implement the next slice".
---

# itc-implement-slice

Build exactly what the slice says. Don't write the next slice, don't change docs other than the slice's own checkboxes, don't fix things the slice doesn't mention.

## Read first

1. `AGENTS.md`: stack, layout, the four check commands, the ladder in section 4.
2. `docs/CURRENT-STATE.md`: the branch, accepted failures, decisions.
3. The slice file, top to bottom. Then every file under its **Read first**.
4. The code each task touches, before changing it. Lazy about the solution, never about reading.

If no slice number is given, take **Next** from CURRENT-STATE.md.

Stop before writing code, and say which, if:
- the slice has a `[NEEDS CLARIFICATION]` in scope, or no **Done when** list (gate 1)
- the checked-out branch isn't the one in CURRENT-STATE.md, or `git status` isn't clean
- the slice looks bigger than the cap in AGENTS.md section 3 (gate 3)

## Work

For each task, in order:

1. Write the test from **Tests to add** that covers it. Run it. It must fail. (Pass-through code, markup and config: skip the test, say so.)
2. Write the minimum code that passes. First rung of the ladder that works.
3. Run that project's tests. Green.
4. Tick the task's box in the slice file.

Do the **Data**, **Auth** and **Accessibility** lines as written, including the denied case. Mark any deliberate shortcut: `// shortcut: <why> — revisit when <condition>`.

## Stop mid-way (gates 3 and 4) if

- a task needs a decision the slice doesn't make, a new dependency, or changes outside the slice's files
- a previously passing test fails and the cause isn't inside this slice
- the same fix has failed twice (AGENTS.md section 7)

Leave the work uncommitted. Report what's done, what's blocked and the one question. Don't improvise.

## Finish

1. Run all four checks from AGENTS.md. Everything passes except failures listed as accepted in CURRENT-STATE.md.
2. Re-read `git diff` once: debug code, commented-out blocks, TODOs you added, files outside the slice.
3. Tick every **Done when** item you can verify yourself. Leave manual-only ones unticked.
4. Commit the code (never push):

```
git add -A
git commit -m "feat: slice NN <name>"
```

`fix:` instead of `feat:` for a bug-fix slice; `chore:` only for the Foundation scaffold.

## Reply

Done when items met / not met, the four check results, the commit sha. Next: `/itc-run-slice-tests NN`. Then stop.
