# itc-workflow

Drop-in files for the slice workflow. Copy into a repo root.

```
AGENTS.md                       working rules (Codex, Cursor, Hermes read this directly)
skills/<name>/SKILL.md          six skills; copy or symlink into .claude/skills/ and .cursor/skills/
docs/templates/                 blank PRD, PLAN, CURRENT-STATE and slice files the skills copy from
.claude/settings.json           Claude Code SessionStart hook: prints docs/CURRENT-STATE.md on startup, resume and compaction
.cursor/rules/current-state.mdc Cursor always-on rule that includes docs/CURRENT-STATE.md
```

## Loop

```
/itc-plan-project            brainstorm → docs/PRD.md, PLAN.md, CURRENT-STATE.md
/itc-next                    write → implement → test → review for one slice, stop at a gate
/itc-next phase              same, until the phase is done; then opens the phase PR
/itc-next 3                  up to three slices
```

Each step is also its own command: `/itc-write-next-slice`, `/itc-implement-slice NN`, `/itc-run-slice-tests NN`, `/itc-review-completed-slice NN`.

## Git

One branch per phase (`phase/N-name`), two commits per slice (`feat: slice NN <name>`, `docs: slice NN outcome`), push after each review, one PR per phase. The developer merges.

## Notes

- `disable-model-invocation: true` stays only on `itc-plan-project` and `itc-next`. The four chained skills have to be model-invocable so `itc-next` can call them in Claude Code.
- If `.claude/settings.json` already exists in the repo, merge the `hooks` block into it instead of replacing the file.
- Hermes has no session-start hook. AGENTS.md's "read CURRENT-STATE.md first" rule is the fallback there.
