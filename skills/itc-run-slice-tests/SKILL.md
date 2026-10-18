---
name: itc-run-slice-tests
description: Run the full test suite plus the slice's manual checks for an implemented slice and record the results in its slice file. Use when the developer says "run the tests", "verify the slice" or "test slice NN".
---

# itc-run-slice-tests

Run checks and report them. Never fix code, edit tests, change data to make something pass, or commit. The review step commits these results.

## Inputs

- `AGENTS.md` for the four check commands, ports and run scripts.
- `docs/slices/NN-name.md` for Done when, Tests to add and Manual checks.
- `docs/CURRENT-STATE.md` for the branch and for failures the developer has already accepted.

If no slice number is given, take **Last** from CURRENT-STATE.md; if that's ambiguous, ask. Confirm the branch in CURRENT-STATE.md is checked out before running anything.

## Run, in order

Record every result and keep going after a failure.

1. **Static:** build, then typecheck.
2. **Full suite:** backend and frontend tests. Always the whole suite, not just this slice's tests.
   - If a test project doesn't exist, report **No test project** as the headline. It's a gap to fix, not "N/A."
   - Is each test in "Tests to add" present and passing? If not, mark it **Missing**.
   - A previously passing test that now fails is a **regression**. Lead the report with it.
   - Failures listed as accepted in CURRENT-STATE.md show as **Failed (accepted)**. Never accept a new one yourself (gate 2).
3. **Manual checks:** only if the API and client are running. If they aren't, give the start commands from AGENTS.md and mark these **Not run**. If browser tools are available, use them for UI steps and `fetch` for API steps. Take routes from the slice, not memory. For legacy comparisons, record both results side by side. Delete any test data you create, or report it.
4. **Data:** don't run SQL against the database. List the verification queries for the developer.

Every result must come from a command run in this session. Anything not run is **Not run**, never a pass.

## Record

Append to the slice file, replacing any earlier `## Test results`, using the table in `docs/templates/slice.md`:

```markdown
## Test results (YYYY-MM-DD)

**Regressions:** none | list them

| Check | Result | Notes |
|---|---|---|
| Build | Passed | |
| Typecheck | Passed | |
| Backend tests | 42 passed, 0 failed | |
| Frontend tests | 18 passed, 1 failed | name: reason |
| New tests | 4 of 4 present, passing | |
| Manual: step | Passed / Failed / Not run | |
```

## Reply

Regressions or missing test projects first, then a one-line summary (passed / failed / not run / missing), then stop. Fixes are a separate step the developer asks for.
