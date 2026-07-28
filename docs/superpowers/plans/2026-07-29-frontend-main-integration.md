# Frontend Main Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replay the completed focused-frontend work onto GitHub's squashed `main`, add the pending parameters layout regression fix, and merge it through a verified pull request without rewriting history.

**Architecture:** Work only on `feat/frontend-main-integration`, which starts at `origin/main` commit `ee6120c`. Replay the seven existing frontend commits as ordinary cherry-picks, add the local parameters fix test-first, then validate and merge PR 1 before any security branch is created.

**Tech Stack:** Git, GitHub CLI, React 19, TypeScript, Vite 8, Vitest 4, Playwright, .NET 10, PostgreSQL 16.

## Global Constraints

- Never force-push GitHub `main`.
- Never modify or delete the primary checkout's uncommitted files.
- Exclude `docs/internship-book/**`, the internship PDF branch, and `security_best_practices_report.md`.
- Preserve the original seven frontend commit subjects and order.
- Keep the legacy remote branch `feat/frontend-focused-evolution`.
- Merge only after local verification and GitHub CI pass.

---

### Task 1: Replay the focused frontend commits

**Files:**
- Modify: `frontend/e2e/admin.smoke.spec.ts`
- Modify: `frontend/e2e/portal.smoke.spec.ts`
- Modify: `frontend/e2e/ticket-detail.smoke.spec.ts`
- Modify: `frontend/e2e/ticket-resolution.smoke.spec.ts`
- Modify: `frontend/src/components/Layout.test.tsx`
- Modify: `frontend/src/components/Layout.tsx`
- Modify: `frontend/src/features/ticket-details/TicketTimeline.tsx`
- Modify: `frontend/src/features/ticket-details/ticketDetailModel.test.ts`
- Modify: `frontend/src/features/ticket-details/ticketDetailModel.ts`
- Modify: `frontend/src/features/tickets/TicketFilters.tsx`
- Modify: `frontend/src/pages/LoginPage.test.tsx`
- Modify: `frontend/src/pages/LoginPage.tsx`
- Modify: `frontend/src/pages/TicketDetailPage.test.tsx`
- Modify: `frontend/src/pages/TicketDetailPage.tsx`
- Modify: `frontend/src/pages/TicketListPage.test.tsx`
- Modify: `frontend/src/pages/TicketListPage.tsx`
- Modify: `frontend/src/pages/UsersPage.test.tsx`
- Modify: `frontend/src/pages/UsersPage.tsx`
- Modify: `frontend/src/styles/base.css`
- Modify: `frontend/src/styles/login.css`
- Modify: `frontend/src/styles/shell.css`
- Modify: `frontend/src/styles/ticket-detail.css`
- Modify: `frontend/src/styles/tickets.css`
- Modify: `frontend/src/styles/tokens.css`
- Modify: `frontend/src/styles/users.css`

**Interfaces:**
- Consumes: Existing commits `b46231f`, `2771010`, `ba3c105`, `43da2bd`, `3b5e415`, `f9fad7a`, and `b11ac71`.
- Produces: The focused frontend behavior and tests on a branch descended from `origin/main`.

- [ ] **Step 1: Verify the integration branch and clean worktree**

Run:

```bash
test "$(git branch --show-current)" = "feat/frontend-main-integration"
test "$(git rev-parse origin/main)" = "ee6120c6ce55ae2c00f3c9dfce1a346f7061535a"
test -z "$(git status --porcelain)"
```

Expected: all three commands exit `0`.

- [ ] **Step 2: Replay the seven commits in order**

Run:

```bash
git cherry-pick \
  b46231f \
  2771010 \
  ba3c105 \
  43da2bd \
  3b5e415 \
  f9fad7a \
  b11ac71
```

Expected: seven successful cherry-picks and no conflict.

- [ ] **Step 3: Verify commit identity and scope**

Run:

```bash
git log --format='%s' --reverse HEAD~7..HEAD
git diff --name-only HEAD~7..HEAD
git diff --check HEAD~7..HEAD
```

Expected: the seven original subjects appear in order; only the 25 listed
frontend files changed; `git diff --check` prints nothing.

- [ ] **Step 4: Run focused unit tests**

Run:

```bash
cd frontend
npm test -- \
  src/components/Layout.test.tsx \
  src/pages/LoginPage.test.tsx \
  src/pages/TicketDetailPage.test.tsx \
  src/pages/TicketListPage.test.tsx \
  src/pages/UsersPage.test.tsx \
  src/features/ticket-details/ticketDetailModel.test.ts
```

Expected: all selected Vitest files pass.

### Task 2: Add the parameters audit-list regression fix

**Files:**
- Test: `frontend/e2e/admin.smoke.spec.ts`
- Modify: `frontend/src/styles/parameters.css`

**Interfaces:**
- Consumes: `.parameters-audit__summary` rendered by `ParametersPage`.
- Produces: Mobile-safe list marker layout with `list-style-position: inside`.

- [ ] **Step 1: Add the failing Playwright assertion**

In `frontend/e2e/admin.smoke.spec.ts`, immediately after the
`Parametreler` heading visibility assertion, add:

```ts
await expect(page.locator('.parameters-audit__summary')).toHaveCSS(
  'list-style-position',
  'inside',
)
```

- [ ] **Step 2: Run the focused browser test and verify failure**

Run:

```bash
cd frontend
npx playwright test e2e/admin.smoke.spec.ts --grep "direct Admin bootstrap"
```

Expected: FAIL because the computed value is `outside`.

- [ ] **Step 3: Apply the minimal CSS correction**

Change `frontend/src/styles/parameters.css`:

```css
.parameters-audit__summary {
  list-style-position: inside;
}
```

Keep every other declaration in the existing rule unchanged.

- [ ] **Step 4: Re-run the focused browser test**

Run:

```bash
cd frontend
npx playwright test e2e/admin.smoke.spec.ts --grep "direct Admin bootstrap"
```

Expected: PASS.

- [ ] **Step 5: Commit the regression fix**

Run:

```bash
git add frontend/e2e/admin.smoke.spec.ts frontend/src/styles/parameters.css
git commit -m "fix(frontend): contain parameters audit markers"
```

Expected: one commit containing exactly the two files.

### Task 3: Validate and merge frontend PR 1

**Files:**
- Verify: `frontend/**`
- Verify: `src/**`
- Verify: `tests/**`
- Verify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: Completed Tasks 1-2 and the committed design/plan.
- Produces: Merged GitHub PR with `main` containing the frontend evolution.

- [ ] **Step 1: Run all frontend checks**

Run:

```bash
cd frontend
npm run lint
npm test
env -u VITE_API_BASE_URL npm run build
npm run test:e2e
```

Expected: lint exits `0`, 27 Vitest files/229 or more tests pass, build exits
`0`, and all Playwright tests pass.

- [ ] **Step 2: Start an isolated PostgreSQL verification service**

Run:

```bash
docker run -d --rm \
  --name vshelpdesk-frontend-pr-postgres \
  -e POSTGRES_USER=stajyer \
  -e POSTGRES_PASSWORD=ci_postgres_password \
  -e POSTGRES_DB=VS_HelpDesk_DB \
  -p 127.0.0.1:5432:5432 \
  postgres:16-alpine
docker exec vshelpdesk-frontend-pr-postgres \
  pg_isready -U stajyer -d VS_HelpDesk_DB
```

Expected: PostgreSQL reports `accepting connections`.

- [ ] **Step 3: Rehearse build, migration, and backend tests**

Run from the repository root:

```bash
dotnet restore VSHelpDesk.slnx
dotnet tool restore
dotnet build VSHelpDesk.slnx --no-restore -c Release
ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=VS_HelpDesk_DB;Username=stajyer;Password=ci_postgres_password' \
  dotnet ef database update \
  --project src/VSHelpDesk.Infrastructure \
  --startup-project src/VSHelpDesk.WebAPI \
  --configuration Release \
  --no-build
CI=true \
ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=VS_HelpDesk_DB;Username=stajyer;Password=ci_postgres_password' \
Auth__SigningKey='ci-signing-key-with-at-least-32-bytes!!' \
Jobs__ApiKey='ci-jobs-api-key-32-characters!!' \
SeedUser__Enabled=true \
SeedUser__Password='CiSeedPassword123!' \
SeedUser__Username=support \
SeedUser__FullName='CI Support' \
SeedUser__Email='support@vshelpdesk.local' \
  dotnet test VSHelpDesk.slnx --no-build -c Release --nologo
```

Expected: build has zero warnings/errors; migrations apply; at least 361 backend
tests pass with only the two opt-in IMAP tests skipped.

- [ ] **Step 4: Stop the temporary PostgreSQL service**

Run:

```bash
docker stop vshelpdesk-frontend-pr-postgres
```

Expected: the named container stops and is automatically removed.

- [ ] **Step 5: Review the exact PR diff**

Run:

```bash
git status --short --branch
git diff --check origin/main...HEAD
git diff --stat origin/main...HEAD
git log --oneline --reverse origin/main..HEAD
```

Expected: clean worktree; design/plan, seven replayed commits, and one parameters
fix commit; no internship or security-report files.

- [ ] **Step 6: Push and create PR 1**

Run:

```bash
git push -u origin feat/frontend-main-integration
gh pr create \
  --base main \
  --head feat/frontend-main-integration \
  --title "feat(frontend): integrate focused portal evolution" \
  --body-file docs/superpowers/specs/2026-07-29-frontend-security-main-integration-design.md
```

Expected: GitHub returns a pull-request URL.

- [ ] **Step 7: Wait for checks and merge without deleting the branch**

Run:

```bash
PR_NUMBER="$(gh pr view --json number --jq .number)"
gh pr checks "$PR_NUMBER" --watch
gh pr merge "$PR_NUMBER" --merge --delete-branch=false
git fetch origin
git merge-base --is-ancestor HEAD origin/main
```

Expected: all checks pass, PR state becomes `MERGED`, and the final ancestry
check exits `0`.
