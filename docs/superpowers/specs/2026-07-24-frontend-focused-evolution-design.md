# Frontend Focused Evolution Design

**Date:** 2026-07-24

**Status:** Interaction direction approved; written-spec review pending

**Scope:** Existing React portal shell, login, ticket list, ticket detail, users, and parameters pages

## 1. Context

VS Help Desk already has a working Turkish React 19 SPA that communicates with
the ASP.NET Core backend through the existing REST API. The portal already
contains:

- HttpOnly-cookie authentication with CSRF protection;
- protected ticket list and ticket detail routes;
- Admin-only users and parameters routes;
- responsive ticket table/card rendering;
- a ticket lifecycle rail and shared status badge;
- ticket assignment, manual resolution, support reply, and attachment download;
- unit/component tests and Playwright coverage at multiple viewports.

This work is therefore an evolution of a functioning interface, not a rewrite.
The redesign must remove confirmed usability and consistency gaps while
preserving the current architecture, business behavior, Turkish product
language, and existing tests.

Two uncommitted user-owned changes currently exist in
`frontend/src/styles/parameters.css` and
`frontend/e2e/admin.smoke.spec.ts`. They fix the parameter-audit summary marker
position and must not be overwritten, reverted, staged, or committed as part of
this redesign.

## 2. Goals

- Make the portal feel like one coherent internal support workspace.
- Improve hierarchy, spacing, responsive navigation, and daily scanability.
- Preserve the existing lifecycle rail as the product's signature visual.
- Improve the login form with an accessible password-visibility control.
- Distinguish Customer, Support, and System messages without changing order.
- Make attachment, assignment, resolution, reply, users, and parameters
  controls visually consistent.
- Preserve explicit loading, refresh, empty, validation, partial-success, and
  error states.
- Keep the interface usable at 320, 390, 720, and 1440 CSS pixels.
- Preserve keyboard operation, visible focus, reduced-motion behavior, and
  screen-reader feedback.

## 3. Non-goals

- No backend, database, migration, controller, or REST contract changes.
- No Next.js, server-side rendering, or full-stack framework migration.
- No new status, role, priority, tag, SLA, channel, analytics, notification, or
  customer-portal feature.
- No new component framework, icon package, animation package, or remote font.
- No sidebar conversion; the current header-based shell remains the desktop
  navigation model.
- No ticket/message deletion, subject editing, manual reopening, or local-only
  state transition.
- No new reply-attachment upload workflow. Existing authenticated attachment
  downloads remain supported; upload behavior is outside this focused visual
  evolution.
- No display of assignee names in the list while the list API exposes only
  `assignedUserId` and no safe name mapping.
- No broad refactor of API hooks or pages whose behavior is already covered and
  working.

## 4. Alternatives Considered

### 4.1 Minimal gap patch

Add only the password toggle and System message label, leaving the surrounding
layout and CSS untouched.

This is low risk but would not resolve the cross-page spacing, feedback, mobile
navigation, and operational hierarchy inconsistencies targeted by the brief.

### 4.2 Focused evolution — chosen

Keep the current brand, header shell, route structure, data hooks, lifecycle
rail, and page responsibilities. Refine semantic tokens, introduce only a small
set of shared primitives where duplication is proven, and improve each page in
small testable slices.

This approach produces a visible improvement without discarding working,
well-tested behavior.

### 4.3 Structural redesign

Replace the shell with a desktop sidebar/mobile drawer and restructure every
page around a new component system.

This creates the largest visual change but also the most regression risk,
duplicates already-delivered UI work, and tends toward a generic SaaS
dashboard. It is rejected.

## 5. Product and Visual Direction

### 5.1 Product and audience

The product is a Turkish internal support operations portal. Its users
repeatedly scan incoming work, read chronological conversations, and take
careful state-changing actions. Orientation, status, and action clarity take
priority over marketing copy or decoration.

### 5.2 Visual thesis

The interface is a calm operations desk made from ice-tinted canvas, paper
surfaces, dark navy information, and precise petrol actions. Amber and coral
appear only when status or risk requires them.

### 5.3 Palette and tokens

The existing palette remains:

- Night `#102A43`: primary text and brand foundation.
- Petrol `#087F73`: primary action, active navigation, and focus.
- Ice `#F3F7F8`: application canvas.
- Paper `#FFFFFF`: primary working surfaces.
- Amber `#C27803`: waiting and warning states.
- Coral `#C2413B`: destructive and error states.

`tokens.css` will gain semantic aliases and missing scales for surface,
secondary surface, primary/muted text, border, focus ring, status tones,
spacing, type size, radius, shadow, transition, header size, and content width.
Existing variables will remain available until all usages are verified; the
redesign will not perform a blind token rename.

### 5.4 Typography

- Sora Variable remains the restrained display and heading face.
- Manrope Variable remains the body, form, and data face.
- Ticket numbers and operational metadata use tabular numerals.
- Headings use a compact scale; routine app pages do not use marketing-sized
  hero typography.

### 5.5 Signature element

The lifecycle rail remains the single memorable product element. It represents
the actual loaded ticket collection and doubles as a filter. It will not be
surrounded by KPI cards or fake dashboard metrics.

API status values remain exactly:

- `New`
- `WaitingCustomerReply`
- `CustomerReplied`
- `Resolved`

Visible Turkish labels remain:

- `Yeni`
- `Müşteri Bekleniyor`
- `Müşteri Yanıtladı`
- `Çözüldü`

Unknown server values remain readable and use a neutral fallback.

## 6. Application Shell

### 6.1 Desktop

At and above 48 rem, the current sticky header remains:

```text
┌───────────────────────────────────────────────────────────────┐
│ VS Help Desk   Talepler  Kullanıcılar  Parametreler  User/Exit│
├───────────────────────────────────────────────────────────────┤
│                        page workspace                         │
└───────────────────────────────────────────────────────────────┘
```

Navigation destinations remain role-aware. Support users see Tickets only;
Admin users also see Users and Parameters. The active destination is conveyed
by text and shape as well as color.

### 6.2 Mobile

Below 48 rem, the header becomes a compact disclosure layout:

```text
┌──────────────────────────────┐
│ VS Help Desk          Menü   │
├──────────────────────────────┤
│ [Talepler] [Admin links...]  │  ← shown only when expanded
│ Account identity / Çıkış yap │
└──────────────────────────────┘
│ page workspace               │
```

The trigger is a real button with `aria-expanded` and `aria-controls`.
Navigation remains a semantic `nav`. Activating a route closes the disclosure;
Escape also closes it and returns focus to the trigger. No modal focus trap is
needed because the disclosure remains in document flow. At 320 px, the
document must not scroll horizontally.

The skip link, `main` landmark, real authenticated identity, and logout action
remain available.

## 7. Page Designs

### 7.1 Login

The existing two-part mail-to-ticket composition remains on desktop and
collapses to a form-first single column on mobile. The HTML/CSS workflow
illustration remains the visual anchor; no stock image or fake metric is added.

The password field gains a visible `Parolayı göster` / `Parolayı gizle`
button. The button:

- uses `type="button"`;
- exposes the current action as its accessible name;
- does not alter the password value or autocomplete behavior;
- remains keyboard reachable and at least 44 px high;
- preserves invalid-credential focus return to the password field.

The existing request body, endpoint, safe return path, session-expiry notice,
loading state, and sanitized Turkish errors remain unchanged.

### 7.2 Ticket list

The page keeps the existing local search, status filter, lifecycle counts,
refresh model, semantic desktop table, and semantic mobile cards.

Improvements focus on:

- clearer heading-to-toolbar spacing;
- a single compact control band instead of nested card chrome;
- stronger subject/number/customer hierarchy;
- a consistent status badge and absolute date accessible from relative time;
- clearer initial loading, background refresh, true empty, filtered empty, and
  retry states;
- visible row/card link focus and safe wrapping for long content.

Counts continue to describe the currently loaded API result after text search
and before status filtering. The UI does not call a new analytics endpoint and
does not imply that a loaded count is a server-wide total.

### 7.3 Ticket detail

Desktop remains a primary conversation column plus a secondary action rail.
Mobile stacks the conversation before the action controls so reading context
precedes mutation.

The header shows only available server data: original subject, ticket number,
status, customer identity, creation/last-activity information, assignment, and
resolution metadata where present. No priority, tags, SLA, organization, or
editable subject is introduced.

Messages remain in the chronological order supplied by the backend, which
orders them by `CreatedAt`. The frontend will preserve that order and add
sender-specific presentation:

- Customer: left-aligned paper message with `Müşteri` label.
- Support: right-aligned petrol-tinted message with `Destek ekibi` label.
- System: compact centered timeline event with `Sistem` label.
- Unknown: neutral message with `Gönderen bilgisi yok`.

Sender distinction never relies on color alone. Message content continues to
render as literal text; `dangerouslySetInnerHTML` is not introduced.

Attachments remain under their owning message and use authenticated Blob
downloads. Cards show original filename and available size; filenames wrap
safely. Internal paths and tokens are never exposed.

Assignment, resolution, and reply controls share one action-panel treatment.
Their existing request, conflict, disabled, refresh, and feedback semantics
remain intact.

Support-reply outcomes preserve the existing response contract:
`emailDelivered`, `ticketStateUpdated`, and `noticeCode`. Both
`smtp-delivery-failed` and `ticket-state-conflict` remain partial-success
warnings. A saved message stays visible even if email delivery or the later
state update fails. The client never forces a local status transition.

### 7.4 Users

The Admin-only route and all existing capabilities remain. The current page is
large, so visual sections will be clarified without changing its API model:

- user list and editor receive distinct headings and spacing;
- active/inactive and Support/Admin values use text-plus-tone badges;
- create, edit, and password-reset actions use consistent field and feedback
  treatment;
- the existing last-active-Admin guard and message remain prominent;
- destructive-looking state changes retain confirmation where already
  required.

Username remains immutable after creation. No invitations, teams,
departments, deletion, or new roles are added.

### 7.5 Parameters

The Admin-only route, allow-listed parameters, validation, audit history, and
payloads remain unchanged. The existing user-owned audit-summary marker fix is
treated as baseline work and will not be absorbed into redesign commits.

The redesign may adjust page structure and shared form styling around that
change, but must not expose environment secrets or introduce free-form
parameters. Parameter descriptions, validation ranges, save feedback, and
audit entries remain clear at mobile widths.

## 8. Shared UI Boundaries

New reusable components are allowed only when at least two real call sites
benefit. Expected candidates are:

- `PageHeader`: title, supporting text, and optional action area.
- `FeedbackNotice`: consistent status/alert semantics for safe UI copy.
- `FormField`: label, description, validation, and control association.
- `StatusBadge`: a visual primitive used by ticket and admin status mappings.
- `MobileNavigation`: shell-specific disclosure behavior.

Existing ticket components remain feature-owned. Business mappings and
mutation logic stay in feature models/hooks, not generic visual components.
There will be no wrapper component for every HTML element.

## 9. Data and Error Flow

The existing flow remains:

```text
Page
  → feature hook
    → typed API module
      → shared REST client
        → ASP.NET Core API
```

Mutations render server-confirmed outcomes and refresh through existing hooks.
No mock production data, new client cache, global state library, optimistic
message duplication, or frontend business-rule engine is introduced.

The shared API client continues to:

- use same-origin `/api` by default;
- send `credentials: 'include'`;
- read the existing CSRF cookie for unsafe requests;
- send `X-CSRF-Token`;
- redirect protected `401` responses to the session-expired login flow;
- avoid storing a bearer token in JavaScript storage.

Errors remain concise and actionable. Raw exception text, SQL errors, stack
traces, internal paths, credentials, and unsafe backend messages are not shown.

## 10. Accessibility and Responsive Behavior

All affected views will preserve or add:

- semantic headings, landmarks, lists, tables, links, buttons, and forms;
- visible labels and accessible descriptions;
- visible `:focus-visible` treatment;
- 44 px minimum interactive targets;
- keyboard-operable navigation, filters, dialogs, downloads, and mutations;
- meaningful live-region feedback without duplicate announcements;
- text-plus-color status communication;
- safe wrapping of email addresses, subjects, bodies, and filenames;
- no keyboard traps;
- reduced-motion fallbacks;
- no document overflow at 320, 390, 720, or 1440 px.

Relative time, where used, must retain an absolute machine-readable
`dateTime` and an accessible absolute date/time.

## 11. Motion

Motion remains restrained and CSS-based:

1. One short coordinated page-entry transition for major ready states.
2. A compact disclosure transition for mobile navigation.
3. Small state transitions for filters, dialogs, and feedback that strengthen
   affordance.

No continuous ambient animation, parallax, animation dependency, or decorative
loading loop is added. `prefers-reduced-motion: reduce` disables non-essential
motion.

## 12. Testing Strategy

Behavior changes follow RED → GREEN → REFACTOR:

- Login component tests cover show/hide password without losing value,
  submission, loading, invalid credentials, and keyboard use.
- Layout tests cover role-aware links, mobile disclosure state, Escape, route
  activation, and logout.
- Ticket model/component tests cover exact statuses, System sender mapping,
  order preservation, attachment ownership, and unknown fallbacks.
- Page tests preserve loading, refreshing, empty, retry, reply partial-success,
  assignment, resolution, user, and parameter behavior.
- Existing E2E scenarios are extended rather than replaced.

Browser validation covers 320×700, 390×844, 720×900, and 1440×900. It checks:

- no document overflow;
- responsive navigation and page composition;
- keyboard focus order and visible focus;
- long subject, email, message, and filename fixtures;
- dialog/disclosure keyboard behavior;
- reduced motion;
- console errors;
- representative before/after screenshots for visual review.

No second test framework or visual-test dependency is added.

## 13. Implementation Sequence

1. Establish a clean isolated worktree and run the existing frontend baseline.
2. Add failing tests for semantic tokens and mobile navigation behavior.
3. Refine tokens, base styles, shell, and shared primitives.
4. Add failing tests and implement the login password control.
5. Refine the ticket list without changing data-fetching semantics.
6. Add failing tests and implement System-message presentation and detail
   workspace refinements.
7. Refine users and parameters without absorbing user-owned changes.
8. Extend responsive/accessibility E2E coverage.
9. Run lint, unit/component tests, production build, and Playwright.
10. Review the final diff for scope expansion and unintended API changes.

## 14. Acceptance Criteria

- Existing routes, API payloads, roles, statuses, and lifecycle rules are
  unchanged.
- All visible portal copy remains Turkish.
- The desktop header remains the shell; mobile navigation is usable at 320 px.
- The lifecycle rail remains accurate and is not presented as analytics.
- Password visibility is accessible and does not change submission behavior.
- Customer, Support, System, and unknown messages are distinguishable while
  chronological order is preserved.
- Attachments remain associated with the owning message and download securely.
- Reply partial-success outcomes remain truthful.
- Users and parameters remain Admin-only.
- Existing and new frontend tests pass.
- `npm run lint`, `npm test`, `npm run build`, and Playwright complete
  successfully, or any environment limitation is reported exactly.
- The final diff contains no backend, database, or API contract modification.
- User-owned uncommitted changes remain untouched.
