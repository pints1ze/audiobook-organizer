---
name: itc-plan-project
description: Brainstorm a new app, replacement or major feature with the developer, then write the foundation for the slice loop (docs/PRD.md, docs/PLAN.md, docs/CURRENT-STATE.md). Use for "build a X app", "plan this project", "replace the legacy X app", or adding a large feature to an existing plan.
disable-model-invocation: true
---

# itc-plan-project

Turn an idea into a goal, a short product brief, and a phased plan. Don't write slices or code.

## Read first

- `AGENTS.md` for the stack and conventions. The plan assumes that stack unless the developer says otherwise.
- `docs/templates/PRD.md`, `docs/templates/PLAN.md`, `docs/templates/CURRENT-STATE.md`: the layouts you'll fill. Don't change their headings.
- Anything that already exists: `docs/PRD.md`, `docs/PLAN.md`, `docs/CURRENT-STATE.md`, notes the developer points to, and, for a replacement, the legacy app's code.
- A Figma Make export, if there is one (`reference/figma-make/`). It is design intent, not code to keep as-is: read it for screens, workflows and which shadcn components it uses, then treat it as the PRD's UI input. Expect hardcoded data, no auth, and accessibility gaps. Name it under **Sources** in PLAN.md so slices port screens from it one at a time.

If a plan already exists, you're extending it. Add or adjust phases, keep what's done, and don't rewrite history.

## 1. Brainstorm

Draw out what the developer actually wants before proposing anything.

- Ask about one area at a time, at most 3 questions per round. Areas, in order:
  1. **Why and who:** the problem, the users, what they do today.
  2. **Core workflows:** the 3–7 things users must be able to do. What's in v1 and what can wait.
  3. **Data and rules:** main records, business rules, anything calculated, imports and exports.
  4. **Access:** roles, who sees and does what.
  5. **Constraints:** integrations, legacy behavior to match, deadlines, hosting.
- Skip questions the docs or code already answer. For a replacement, read the legacy code and confirm what you found instead of asking.
- Offer 2–3 options when there's a real choice, with your pick and why.
- Play back your understanding in a few lines and let the developer correct it before writing anything.

Mark anything still unknown as `[NEEDS CLARIFICATION: question]` instead of guessing.

## 2. Write the docs

Show each doc in chat, in short chunks, before saving it.

### docs/PRD.md

Copy `docs/templates/PRD.md` and fill it. About 1–2 pages: problem, users, goal, core workflows (each marked v1 or later), data, rules, roles, constraints, out of scope, open questions.

### docs/PLAN.md

Copy `docs/templates/PLAN.md` and fill it.

- 3–10 phases. Each is a milestone someone could use or demo, not a technical layer.
- Order phases so each builds on the last and the riskiest unknowns come early.
- Phase 1 is always **Foundation**: scaffold, both test projects with one passing test each, health check, run scripts.
- Each phase gets one goal line and 1–3 "done when" lines. No file lists, prompts or estimates. Slices add that detail later.
- Every phase will get its own `phase/N-short-name` branch and one PR (AGENTS.md section 8). Size phases so a PR is reviewable: a phase that would take more than about 8 slices is two phases.

### docs/CURRENT-STATE.md

Copy `docs/templates/CURRENT-STATE.md`. Branch: none yet (`/itc-next` creates it). Next: slice 01, Foundation. Carry the PRD's open questions over.

### AGENTS.md

If its Project specifics section still has `{App}` placeholders or empty `<!-- -->` lines this plan answers (app description, extra packages, ports), propose the edits. Don't make them without an OK.

## Reply

A 3–5 line summary of the plan (goal, phases by name), any open questions that block phase 1 or 2, and the next step: `/itc-next` to run slice 01, or `/itc-write-next-slice` to only write it. Then stop.
