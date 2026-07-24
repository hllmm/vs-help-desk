# Frontend Focused Evolution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Evolve the existing Turkish VS Help Desk portal into a more coherent, accessible, and responsive operations workspace without changing backend behavior or REST contracts.

**Architecture:** Keep the existing React SPA, routes, feature hooks, typed API modules, cookie/CSRF client, and page ownership. Implement a single responsive header panel, small page-local behavior improvements, semantic sender metadata, and CSS refinements through the existing token/style files; do not add a UI framework or state library.

**Tech Stack:** React 19.2.7; React Router 7.18.1; TypeScript 6.0.2; Vite 8.1.1; Vitest 4.1.10; Testing Library React 16.3.2; Playwright 1.61.1; Oxlint 1.71.0; CSS; locally bundled Sora Variable and Manrope Variable.

## Global Constraints

- Design source: `docs/superpowers/specs/2026-07-24-frontend-focused-evolution-design.md`.
- Preserve the React SPA → existing REST API architecture; modify no backend, controller, database, migration, or API contract file.
- Preserve HttpOnly-cookie authentication, `credentials: 'include'`, the readable CSRF cookie, and `X-CSRF-Token`; add no bearer token or sensitive browser storage.
- API statuses remain exactly `New`, `WaitingCustomerReply`, `CustomerReplied`, and `Resolved`.
- Visible labels remain `Yeni`, `Müşteri Bekleniyor`, `Müşteri Yanıtladı`, and `Çözüldü`; all visible portal copy remains Turkish.
- Preserve Support/Admin role behavior and Admin-only `/users` and `/parameters`.
- Preserve reply outcome fields `emailDelivered`, `ticketStateUpdated`, and `noticeCode`, including `smtp-delivery-failed` and `ticket-state-conflict`.
- Preserve chronological server message order and render message content as literal text; never add `dangerouslySetInnerHTML`.
- Keep the lifecycle rail as the signature visual; do not add KPI cards, fake analytics, fake data, or a sidebar.
- Add no runtime or development dependency.
- Meet 44 px control height, visible `:focus-visible`, text-plus-color states, semantic landmarks, reduced motion, and zero document overflow at 320×700, 390×844, 720×900, and 1440×900.
- Do not edit, stage, commit, overwrite, or revert the user-owned changes in `frontend/src/styles/parameters.css` and `frontend/e2e/admin.smoke.spec.ts`.
- Use an isolated worktree at execution time. Follow RED → GREEN → focused regression → commit for every task.

## File Structure

```text
frontend/src/
  components/
    Layout.tsx                         # responsive single-nav header panel
    Layout.test.tsx                    # mobile disclosure and role tests
  features/
    ticket-details/
      ticketDetailModel.ts             # sender label/tone metadata
      ticketDetailModel.test.ts
      TicketTimeline.tsx               # semantic sender presentation hooks
    tickets/
      TicketFilters.tsx                # clear-filter action
  pages/
    LoginPage.tsx                      # password visibility control
    LoginPage.test.tsx
    TicketListPage.tsx                 # loaded-count context and clear action
    TicketListPage.test.tsx
    TicketDetailPage.tsx               # created/resolved metadata
    TicketDetailPage.test.tsx
    UsersPage.tsx                      # text-plus-tone user state hooks
    UsersPage.test.tsx
  styles/
    tokens.css                         # semantic aliases and sizing scales
    base.css                           # shared controls/focus/notice refinement
    shell.css                          # desktop/mobile header layout
    login.css                          # password control and login composition
    tickets.css                        # toolbar, lifecycle, table/card hierarchy
    ticket-detail.css                  # sender/action/attachment hierarchy
    users.css                          # user state and dialog polish
frontend/e2e/
  portal.smoke.spec.ts                 # password and responsive menu behavior
  ticket-detail.smoke.spec.ts          # sender presentation and visual evidence
```

## Execution Baseline Gate

After `superpowers:using-git-worktrees` creates the isolated worktree, run:

```bash
git status --short
test -f docs/superpowers/specs/2026-07-24-frontend-focused-evolution-design.md
test -f frontend/src/components/Layout.test.tsx
test -f frontend/e2e/portal.smoke.spec.ts
cd frontend
npm ci
npm run lint
npm test
env -u VITE_API_BASE_URL npm run build
npx playwright test
cd ..
git status --short
```

Expected: both status commands print nothing; every required baseline artifact
exists; install, lint, Vitest, build, and all four Playwright projects pass. If
the baseline fails, stop and diagnose the failure before changing production
code.

---

### Task 1: Refine semantic tokens and implement the responsive header disclosure

**Files:**
- Modify: `frontend/src/components/Layout.test.tsx`
- Modify: `frontend/src/components/Layout.tsx`
- Modify: `frontend/src/styles/tokens.css`
- Modify: `frontend/src/styles/base.css`
- Modify: `frontend/src/styles/shell.css`

**Interfaces:**
- Consumes: `useAuth()`, `UserRole`, `useLocation()`, existing route paths.
- Produces: one navigation tree with desktop-visible and mobile-disclosed states; button name `Menüyü aç` / `Menüyü kapat`; `aria-expanded`; panel id `app-navigation-panel`.

- [ ] **Step 1: Write RED mobile disclosure tests**

Add `userEvent` to the imports in `Layout.test.tsx` and add this helper and test:

```tsx
function stubViewport(isMobile: boolean) {
  const listeners = new Set<(event: MediaQueryListEvent) => void>()
  vi.stubGlobal(
    'matchMedia',
    vi.fn().mockImplementation((query: string) => ({
      matches: isMobile && query === '(max-width: 47.99rem)',
      media: query,
      onchange: null,
      addEventListener: (
        _type: 'change',
        listener: (event: MediaQueryListEvent) => void,
      ) => listeners.add(listener),
      removeEventListener: (
        _type: 'change',
        listener: (event: MediaQueryListEvent) => void,
      ) => listeners.delete(listener),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  )
}

it('opens the mobile navigation and returns focus on Escape', async () => {
  stubViewport(true)
  seedAuthenticatedUser('Admin')
  renderLayout('/users')
  const user = userEvent.setup()

  const trigger = await screen.findByRole('button', { name: 'Menüyü aç' })
  expect(trigger).toHaveAttribute('aria-expanded', 'false')
  expect(
    screen.queryByRole('navigation', { name: 'Ana menü' }),
  ).not.toBeInTheDocument()

  await user.click(trigger)
  expect(trigger).toHaveAccessibleName('Menüyü kapat')
  expect(trigger).toHaveAttribute('aria-expanded', 'true')
  expect(
    screen.getByRole('navigation', { name: 'Ana menü' }),
  ).toBeInTheDocument()

  await user.click(screen.getByRole('link', { name: 'Parametreler' }))
  expect(trigger).toHaveAccessibleName('Menüyü aç')
  expect(
    screen.queryByRole('navigation', { name: 'Ana menü' }),
  ).not.toBeInTheDocument()

  await user.click(trigger)
  await user.keyboard('{Escape}')
  expect(trigger).toHaveAccessibleName('Menüyü aç')
  expect(trigger).toHaveFocus()
})
```

Add a desktop assertion:

```tsx
it('keeps navigation visible without a menu trigger on desktop', async () => {
  stubViewport(false)
  seedAuthenticatedUser('Admin')
  renderLayout('/tickets')

  expect(
    await screen.findByRole('navigation', { name: 'Ana menü' }),
  ).toBeInTheDocument()
  expect(
    screen.queryByRole('button', { name: /Menüyü/ }),
  ).not.toBeInTheDocument()
})
```

- [ ] **Step 2: Run RED**

Run from `frontend/`:

```bash
npm test -- src/components/Layout.test.tsx
```

Expected: FAIL because no `Menüyü aç` button exists and the navigation is always rendered.

- [ ] **Step 3: Implement the single responsive navigation panel**

Replace `Layout.tsx` with:

```tsx
import {
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { Link, NavLink, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/authState'

const MOBILE_NAV_QUERY = '(max-width: 47.99rem)'

function useMobileNavigation(): boolean {
  const getSnapshot = () =>
    typeof window.matchMedia === 'function' &&
    window.matchMedia(MOBILE_NAV_QUERY).matches
  const [isMobile, setIsMobile] = useState(getSnapshot)

  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return
    const media = window.matchMedia(MOBILE_NAV_QUERY)
    const onChange = (event: MediaQueryListEvent) => setIsMobile(event.matches)
    setIsMobile(media.matches)
    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  }, [])

  return isMobile
}

export function Layout({ children }: { children: ReactNode }) {
  const { user, logout, isAuthenticated } = useAuth()
  const location = useLocation()
  const isMobile = useMobileNavigation()
  const [menuOpen, setMenuOpen] = useState(false)
  const menuButtonRef = useRef<HTMLButtonElement>(null)
  const previousPath = useRef(location.pathname)
  const navigationHidden = isMobile && !menuOpen

  useEffect(() => {
    if (previousPath.current !== location.pathname) {
      previousPath.current = location.pathname
      setMenuOpen(false)
    }
  }, [location.pathname])

  useEffect(() => {
    if (!isMobile) setMenuOpen(false)
  }, [isMobile])

  useEffect(() => {
    if (!menuOpen) return
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      setMenuOpen(false)
      queueMicrotask(() => menuButtonRef.current?.focus())
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [menuOpen])

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Ana içeriğe geç
      </a>
      <header className="app-header">
        <div className="brand">
          <Link to={isAuthenticated ? '/tickets' : '/login'}>
            VS Help Desk
          </Link>
          <span className="brand-sub">Destek operasyonları</span>
        </div>

        {isAuthenticated && isMobile ? (
          <button
            ref={menuButtonRef}
            type="button"
            className="button button--quiet app-header__menu-trigger"
            aria-expanded={menuOpen}
            aria-controls="app-navigation-panel"
            onClick={() => setMenuOpen((current) => !current)}
          >
            {menuOpen ? 'Menüyü kapat' : 'Menüyü aç'}
          </button>
        ) : null}

        {isAuthenticated && user ? (
          <div
            id="app-navigation-panel"
            className="app-header__panel"
            hidden={navigationHidden}
          >
            <nav className="app-nav" aria-label="Ana menü">
              <NavLink to="/tickets" className={({ isActive }) =>
                isActive
                  ? 'app-nav__link app-nav__link--active'
                  : 'app-nav__link'
              }>
                Talepler
              </NavLink>
              {user.role === 'Admin' ? (
                <>
                  <NavLink to="/users" className={({ isActive }) =>
                    isActive
                      ? 'app-nav__link app-nav__link--active'
                      : 'app-nav__link'
                  }>
                    Kullanıcılar
                  </NavLink>
                  <NavLink to="/parameters" className={({ isActive }) =>
                    isActive
                      ? 'app-nav__link app-nav__link--active'
                      : 'app-nav__link'
                  }>
                    Parametreler
                  </NavLink>
                </>
              ) : null}
            </nav>
            <div className="header-user">
              <span className="user-name">{user.fullName}</span>
              <span className="user-handle">@{user.username}</span>
              <button
                type="button"
                className="button button--quiet"
                onClick={() => void logout()}
              >
                Çıkış yap
              </button>
            </div>
          </div>
        ) : null}
      </header>
      <main id="main-content" className="app-main">
        {children}
      </main>
      <footer className="app-footer">
        VS Help Desk · Destek operasyonları
      </footer>
    </div>
  )
}
```

- [ ] **Step 4: Expand semantic tokens without removing existing names**

Append these aliases/scales inside `:root` in `tokens.css`:

```css
--color-canvas: var(--color-ice);
--color-surface: var(--color-paper);
--color-surface-subtle: #edf4f5;
--color-text-primary: var(--color-night);
--color-text-muted: var(--color-muted);
--color-border: var(--color-line);
--color-focus-ring: var(--color-petrol);
--color-success: #167c68;
--color-warning: var(--color-amber);
--color-danger: var(--color-coral);
--space-1: 0.25rem;
--space-2: 0.5rem;
--space-3: 0.75rem;
--space-4: 1rem;
--space-5: 1.5rem;
--space-6: 2rem;
--radius-surface: 1rem;
--shadow-raised: 0 18px 48px rgb(16 42 67 / 10%);
--transition-fast: 160ms ease;
--header-min-height: 4.5rem;
--content-max-width: 76rem;
```

Change the focus rule in `base.css` to use
`outline: 3px solid var(--color-focus-ring)`. Keep the reduced-motion rule
unchanged.

- [ ] **Step 5: Replace shell layout rules**

Update `shell.css` so desktop uses:

```css
.app-header {
  min-block-size: var(--header-min-height);
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  align-items: center;
  gap: var(--space-5);
}

.app-header__panel {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-4);
  min-inline-size: 0;
}

.app-header__panel[hidden] {
  display: none;
}

.app-header__menu-trigger {
  display: none;
}

.app-main {
  width: min(var(--content-max-width), 100%);
}
```

Replace the mobile header media rule with:

```css
@media (max-width: 47.99rem) {
  .app-header {
    grid-template-columns: minmax(0, 1fr) auto;
    align-items: center;
    padding-block: var(--space-3);
  }

  .app-header__menu-trigger {
    display: inline-flex;
    justify-self: end;
  }

  .app-header__panel {
    grid-column: 1 / -1;
    display: grid;
    gap: var(--space-3);
    padding-block-start: var(--space-3);
    border-block-start: 1px solid var(--color-border);
    animation: mobile-navigation-enter var(--transition-fast) both;
  }

  .app-nav {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(8rem, 1fr));
  }

  .app-nav__link {
    justify-content: center;
  }

  .header-user {
    justify-content: space-between;
    flex-wrap: wrap;
  }
}

@keyframes mobile-navigation-enter {
  from {
    opacity: 0;
    transform: translateY(-0.35rem);
  }

  to {
    opacity: 1;
    transform: translateY(0);
  }
}
```

- [ ] **Step 6: Run GREEN and focused regression**

```bash
npm test -- src/components/Layout.test.tsx
npm test -- src/components/Layout.test.tsx src/pages/LoginPage.test.tsx
npm run lint
```

Expected: all selected tests pass and Oxlint exits 0.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/components/Layout.tsx \
  frontend/src/components/Layout.test.tsx \
  frontend/src/styles/tokens.css \
  frontend/src/styles/base.css \
  frontend/src/styles/shell.css
git commit -m "feat(frontend): refine responsive application shell"
```

---

### Task 2: Add an accessible password-visibility control and refine login composition

**Files:**
- Modify: `frontend/src/pages/LoginPage.test.tsx`
- Modify: `frontend/src/pages/LoginPage.tsx`
- Modify: `frontend/src/styles/login.css`

**Interfaces:**
- Consumes: existing controlled `password` string and login submit behavior.
- Produces: button names `Parolayı göster` and `Parolayı gizle`; input id `login-password`; no payload or focus-flow change.

- [ ] **Step 1: Write the failing visibility test**

Add:

```tsx
it('shows and hides the password without changing its value', async () => {
  const user = userEvent.setup()
  mockFetch((url) => {
    if (url.includes('/api/auth/me')) {
      return jsonResponse({ message: 'Unauthorized' }, 401)
    }
    return jsonResponse({ message: 'not found' }, 404)
  })
  renderAt('/login')

  const password = screen.getByLabelText('Parola')
  await user.type(password, 'secret-value')
  expect(password).toHaveAttribute('type', 'password')

  await user.click(screen.getByRole('button', { name: 'Parolayı göster' }))
  expect(password).toHaveAttribute('type', 'text')
  expect(password).toHaveValue('secret-value')

  await user.click(screen.getByRole('button', { name: 'Parolayı gizle' }))
  expect(password).toHaveAttribute('type', 'password')
  expect(password).toHaveValue('secret-value')
})
```

- [ ] **Step 2: Run RED**

```bash
npm test -- src/pages/LoginPage.test.tsx
```

Expected: FAIL because `Parolayı göster` does not exist.

- [ ] **Step 3: Implement the control**

Add:

```tsx
const [passwordVisible, setPasswordVisible] = useState(false)
```

Replace the password label block with:

```tsx
<div className="field">
  <label className="field__label" htmlFor="login-password">
    Parola
  </label>
  <div className="password-control">
    <input
      id="login-password"
      ref={passwordRef}
      name="password"
      type={passwordVisible ? 'text' : 'password'}
      autoComplete="current-password"
      value={password}
      onChange={(event) => setPassword(event.target.value)}
      required
    />
    <button
      type="button"
      className="button button--quiet password-control__toggle"
      onClick={() => setPasswordVisible((current) => !current)}
    >
      {passwordVisible ? 'Parolayı gizle' : 'Parolayı göster'}
    </button>
  </div>
</div>
```

- [ ] **Step 4: Add exact login styles**

Append:

```css
.field__label {
  font-size: 0.9rem;
  font-weight: 600;
}

.password-control {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: var(--space-2);
  min-inline-size: 0;
}

.password-control input {
  min-inline-size: 0;
}

.password-control__toggle {
  white-space: nowrap;
}

@media (max-width: 22.5rem) {
  .password-control {
    grid-template-columns: minmax(0, 1fr);
  }

  .password-control__toggle {
    inline-size: 100%;
  }
}
```

Change `.login-card` to use `border-radius: var(--radius-surface)` and
`box-shadow: var(--shadow-raised)`. Do not add another accent color or image.

- [ ] **Step 5: Run GREEN and regression**

```bash
npm test -- src/pages/LoginPage.test.tsx
npm test
npm run lint
```

Expected: all Vitest tests pass and Oxlint exits 0.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/LoginPage.tsx \
  frontend/src/pages/LoginPage.test.tsx \
  frontend/src/styles/login.css
git commit -m "feat(frontend): add accessible password visibility"
```

---

### Task 3: Clarify loaded ticket counts and add a single clear-filter action

**Files:**
- Modify: `frontend/src/pages/TicketListPage.test.tsx`
- Modify: `frontend/src/pages/TicketListPage.tsx`
- Modify: `frontend/src/features/tickets/TicketFilters.tsx`
- Modify: `frontend/src/styles/tickets.css`

**Interfaces:**
- Consumes: controlled `query` and `TicketStatusFilter`.
- Produces: `hasActiveFilters: boolean`, `onClear(): void`, button name `Filtreleri temizle`, and visible loaded-count context.

- [ ] **Step 1: Write failing list-page tests**

Add this test, using the existing `sampleTickets` fixture:

```tsx
it('explains loaded counts and clears search and status together', async () => {
  fetchTickets.mockResolvedValueOnce(sampleTickets)
  renderTicketsPage()
  const user = userEvent.setup()

  expect(
    await screen.findByRole('heading', {
      name: 'Yüklenen taleplerin durum dağılımı',
    }),
  ).toBeInTheDocument()
  expect(
    screen.getByText('Sayılar şu anda yüklenen talepleri gösterir.'),
  ).toBeInTheDocument()

  await user.type(screen.getByLabelText('Taleplerde ara'), 'eşleşmez')
  await user.selectOptions(screen.getByLabelText('Durum'), 'Resolved')
  await user.click(
    screen.getByRole('button', { name: 'Filtreleri temizle' }),
  )

  expect(screen.getByLabelText('Taleplerde ara')).toHaveValue('')
  expect(screen.getByLabelText('Durum')).toHaveValue('all')
  expect(screen.queryByRole('button', { name: 'Filtreleri temizle' }))
    .not.toBeInTheDocument()
})
```

- [ ] **Step 2: Run RED**

```bash
npm test -- src/pages/TicketListPage.test.tsx
```

Expected: FAIL because the context heading and clear button do not exist.

- [ ] **Step 3: Extend `TicketFilters`**

Add props:

```tsx
hasActiveFilters: boolean
onClear(): void
```

Replace the result/refresh tail with:

```tsx
<p className="ticket-result-count" aria-live="polite">
  {resultCount} sonuç
</p>
<div className="ticket-toolbar__actions">
  {hasActiveFilters ? (
    <button
      type="button"
      className="button button--quiet"
      onClick={onClear}
      disabled={isBusy}
    >
      Filtreleri temizle
    </button>
  ) : null}
  <button
    type="button"
    className="button button--quiet ticket-refresh"
    onClick={onRefresh}
    disabled={isBusy}
  >
    {isBusy ? 'Yenileniyor…' : 'Yenile'}
  </button>
</div>
```

- [ ] **Step 4: Wire the page and loaded-count context**

Pass:

```tsx
hasActiveFilters={query.trim() !== '' || selectedStatus !== 'all'}
onClear={() => {
  setQuery('')
  setSelectedStatus('all')
}}
```

Replace the lifecycle scroll wrapper with:

```tsx
<section
  className="ticket-lifecycle-region"
  aria-labelledby="ticket-lifecycle-title"
>
  <div className="ticket-lifecycle-region__heading">
    <h2 id="ticket-lifecycle-title">
      Yüklenen taleplerin durum dağılımı
    </h2>
    <p>Sayılar şu anda yüklenen talepleri gösterir.</p>
  </div>
  <div className="ticket-lifecycle-scroll">
    <TicketLifecycleRail
      counts={lifecycleCounts}
      value={selectedStatus}
      onChange={setSelectedStatus}
    />
  </div>
</section>
```

- [ ] **Step 5: Apply the toolbar/lifecycle hierarchy**

Use:

```css
.ticket-toolbar {
  grid-template-columns: minmax(0, 1.6fr) minmax(0, 0.9fr) auto auto;
  border-radius: var(--radius-surface);
  box-shadow: 0 1px 0 rgb(16 42 67 / 4%);
}

.ticket-toolbar__actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
  flex-wrap: wrap;
}

.ticket-lifecycle-region {
  display: grid;
  gap: var(--space-2);
}

.ticket-lifecycle-region__heading {
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: var(--space-3);
  flex-wrap: wrap;
}

.ticket-lifecycle-region__heading h2 {
  margin: 0;
  font-size: 1rem;
}

.ticket-lifecycle-region__heading p {
  color: var(--color-text-muted);
  font-size: 0.85rem;
}

.ticket-table tbody tr {
  transition:
    background-color var(--transition-fast),
    box-shadow var(--transition-fast);
}

.ticket-table tbody tr:focus-within {
  background: var(--color-petrol-soft);
  box-shadow: inset 3px 0 0 var(--color-petrol);
}
```

In the existing mobile rule, stretch `.ticket-toolbar__actions` and make its
buttons flex to fill available width. Preserve the table/card breakpoint.

- [ ] **Step 6: Run GREEN and focused regression**

```bash
npm test -- src/pages/TicketListPage.test.tsx \
  src/features/tickets/ticketListModel.test.ts
npm test
npm run lint
```

Expected: all tests pass and Oxlint exits 0.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/pages/TicketListPage.tsx \
  frontend/src/pages/TicketListPage.test.tsx \
  frontend/src/features/tickets/TicketFilters.tsx \
  frontend/src/styles/tickets.css
git commit -m "feat(frontend): clarify ticket list controls"
```

---

### Task 4: Add semantic System-message presentation and complete detail metadata

**Files:**
- Modify: `frontend/src/features/ticket-details/ticketDetailModel.test.ts`
- Modify: `frontend/src/features/ticket-details/ticketDetailModel.ts`
- Modify: `frontend/src/features/ticket-details/TicketTimeline.tsx`
- Modify: `frontend/src/pages/TicketDetailPage.test.tsx`
- Modify: `frontend/src/pages/TicketDetailPage.tsx`
- Modify: `frontend/src/styles/ticket-detail.css`

**Interfaces:**
- Consumes: `TicketMessageItem.senderType`, server-supplied message order, `createdAt`, `resolvedAt`.
- Produces: `MessageSenderTone`, `MessageSenderMeta`, `getMessageSenderMeta(senderType)`, `data-sender` values `customer|support|system|unknown`.

- [ ] **Step 1: Write failing sender metadata tests**

Replace the sender-label test with:

```tsx
describe('getMessageSenderMeta', () => {
  it('maps every supported sender to Turkish label and semantic tone', () => {
    expect(getMessageSenderMeta('Customer')).toEqual({
      label: 'Müşteri',
      tone: 'customer',
    })
    expect(getMessageSenderMeta('Support')).toEqual({
      label: 'Destek ekibi',
      tone: 'support',
    })
    expect(getMessageSenderMeta('System')).toEqual({
      label: 'Sistem',
      tone: 'system',
    })
    expect(getMessageSenderMeta('')).toEqual({
      label: 'Gönderen bilgisi yok',
      tone: 'unknown',
    })
  })
})
```

Update the import from `getMessageSenderLabel` to `getMessageSenderMeta`.

- [ ] **Step 2: Write the failing page-level order/presentation test**

Add:

```tsx
it('preserves server order and identifies System messages', async () => {
  fetchTicketDetails.mockResolvedValueOnce(
    sampleDetail({
      resolvedAt: '2026-07-20T11:00:00.000Z',
      messages: [
        sampleDetail().messages[0]!,
        {
          id: 'msg-system',
          senderType: 'System',
          userId: null,
          content: 'Talep otomatik olarak güncellendi.',
          isHtml: false,
          createdAt: '2026-07-20T09:15:00.000Z',
        },
        sampleDetail().messages[1]!,
      ],
    }),
  )
  renderDetail()

  const timeline = await screen.findByRole('list', { name: 'Mesaj geçmişi' })
  const items = within(timeline).getAllByRole('listitem')
  expect(items).toHaveLength(3)
  expect(items[0]).toHaveAttribute('data-sender', 'customer')
  expect(items[1]).toHaveAttribute('data-sender', 'system')
  expect(items[2]).toHaveAttribute('data-sender', 'support')
  expect(within(items[1]!).getByText('Sistem')).toBeInTheDocument()
  expect(screen.getByText('Oluşturuldu')).toBeInTheDocument()
  expect(screen.getByText('Çözüldü')).toBeInTheDocument()
})
```

- [ ] **Step 3: Run RED**

```bash
npm test -- src/features/ticket-details/ticketDetailModel.test.ts \
  src/pages/TicketDetailPage.test.tsx
```

Expected: FAIL because `getMessageSenderMeta`, `data-sender`, `Sistem`, and the
new metadata labels do not exist.

- [ ] **Step 4: Implement sender metadata**

Replace the label function with:

```tsx
export type MessageSenderTone =
  | 'customer'
  | 'support'
  | 'system'
  | 'unknown'

export type MessageSenderMeta = {
  label: string
  tone: MessageSenderTone
}

export function getMessageSenderMeta(senderType: string): MessageSenderMeta {
  switch (senderType) {
    case 'Customer':
      return { label: 'Müşteri', tone: 'customer' }
    case 'Support':
      return { label: 'Destek ekibi', tone: 'support' }
    case 'System':
      return { label: 'Sistem', tone: 'system' }
    default:
      return { label: 'Gönderen bilgisi yok', tone: 'unknown' }
  }
}
```

In `TicketTimeline.tsx`, import `getMessageSenderMeta`, create
`const sender = getMessageSenderMeta(message.senderType)`, set:

```tsx
<li
  key={message.id}
  className="ticket-timeline__item"
  data-sender={sender.tone}
>
```

and render `{sender.label}` in the sender span. Do not sort or clone `messages`.

- [ ] **Step 5: Add creation and resolution facts**

In `TicketDetailPage.tsx`, extend `ticket-detail__customer` after email with:

```tsx
<div>
  <dt className="ticket-detail__label">Oluşturuldu</dt>
  <dd>
    <time dateTime={detail.createdAt}>
      {formatTicketDetailDate(detail.createdAt)}
    </time>
  </dd>
</div>
{detail.resolvedAt ? (
  <div>
    <dt className="ticket-detail__label">Çözüldü</dt>
    <dd>
      <time dateTime={detail.resolvedAt}>
        {formatTicketDetailDate(detail.resolvedAt)}
      </time>
    </dd>
  </div>
) : null}
```

- [ ] **Step 6: Apply sender/action hierarchy**

Add:

```css
.ticket-timeline__item {
  display: flex;
}

.ticket-timeline__message {
  inline-size: min(100%, 42rem);
  border-radius: var(--radius-surface);
}

.ticket-timeline__item[data-sender='customer'] {
  justify-content: flex-start;
}

.ticket-timeline__item[data-sender='support'] {
  justify-content: flex-end;
}

.ticket-timeline__item[data-sender='support']
  .ticket-timeline__message {
  border-color: color-mix(in srgb, var(--color-petrol) 28%, white);
  background: var(--color-petrol-soft);
}

.ticket-timeline__item[data-sender='system'] {
  justify-content: center;
}

.ticket-timeline__item[data-sender='system']
  .ticket-timeline__message {
  inline-size: min(100%, 34rem);
  padding-block: var(--space-3);
  border-style: dashed;
  background: var(--color-surface-subtle);
  text-align: center;
  box-shadow: none;
}

.ticket-timeline__item[data-sender='system']
  .ticket-timeline__meta {
  justify-content: center;
}

.ticket-timeline__item[data-sender='unknown']
  .ticket-timeline__message {
  border-style: dashed;
}
```

Normalize the assignment, resolution, and reply panel radii to
`var(--radius-surface)` and use `var(--shadow-raised)` only for the confirmation
dialog, not for every panel.

- [ ] **Step 7: Run GREEN and detail regression**

```bash
npm test -- src/features/ticket-details/ticketDetailModel.test.ts \
  src/pages/TicketDetailPage.test.tsx \
  src/features/ticket-details/TicketReplyForm.test.tsx \
  src/features/ticket-details/TicketResolutionPanel.test.tsx \
  src/features/ticket-details/TicketAssignmentPanel.test.tsx
npm test
npm run lint
```

Expected: all tests pass and Oxlint exits 0.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/features/ticket-details/ticketDetailModel.ts \
  frontend/src/features/ticket-details/ticketDetailModel.test.ts \
  frontend/src/features/ticket-details/TicketTimeline.tsx \
  frontend/src/pages/TicketDetailPage.tsx \
  frontend/src/pages/TicketDetailPage.test.tsx \
  frontend/src/styles/ticket-detail.css
git commit -m "feat(frontend): distinguish ticket conversation senders"
```

---

### Task 5: Strengthen text-plus-tone user states without changing admin behavior

**Files:**
- Modify: `frontend/src/pages/UsersPage.test.tsx`
- Modify: `frontend/src/pages/UsersPage.tsx`
- Modify: `frontend/src/styles/users.css`

**Interfaces:**
- Consumes: existing `UserRole`, `isActive`, role select, and active checkbox.
- Produces: `data-role="Support|Admin"` on the select and
  `data-state="active|inactive"` on the text-bearing checkbox label.

- [ ] **Step 1: Write the failing semantic-state test**

Add to the ready-state test or create:

```tsx
it('uses text and semantic state metadata for role and activity', async () => {
  listUsers.mockResolvedValueOnce(sampleUsers)
  renderUsersPage()

  const role = await screen.findByLabelText('admin rolü')
  expect(role).toHaveAttribute('data-role', 'Admin')

  const adminRow = screen.getByRole('row', { name: /Admin Kullanıcısı/ })
  const activeText = within(adminRow).getByText('Aktif')
  expect(activeText.closest('label')).toHaveAttribute(
    'data-state',
    'active',
  )
  expect(
    screen.getByRole('heading', { name: 'Kullanıcı listesi' }),
  ).toBeInTheDocument()
})
```

- [ ] **Step 2: Run RED**

```bash
npm test -- src/pages/UsersPage.test.tsx
```

Expected: FAIL because the data attributes do not exist.

- [ ] **Step 3: Add semantic data hooks**

Add to the role select:

```tsx
data-role={user.role}
```

Change the active label opening tag to:

```tsx
<label
  className="users-table__active"
  data-state={user.isActive ? 'active' : 'inactive'}
>
```

Replace:

```tsx
{showResults ? (
  <>
    <h2 className="users-workspace__section-title">Kullanıcı listesi</h2>
    <div className="users-table-view">
```

Keep the existing table as the child of `users-table-view`, then replace the
existing final wrapper close with:

```tsx
    </div>
  </>
) : null}
```

No event handler, request, confirmation, error, or role behavior changes.

- [ ] **Step 4: Add exact text-plus-tone styles**

Append:

```css
.users-table__select[data-role='Admin'] {
  border-color: color-mix(in srgb, var(--color-petrol) 32%, white);
  background: var(--color-petrol-soft);
}

.users-table__active {
  min-block-size: var(--control-min-height);
  padding-inline: var(--space-3);
  border: 1px solid var(--color-border);
  border-radius: 999px;
  background: var(--color-surface-subtle);
}

.users-table__active[data-state='active'] {
  border-color: color-mix(in srgb, var(--color-success) 30%, white);
  background: color-mix(in srgb, var(--color-success) 10%, white);
  color: color-mix(
    in srgb,
    var(--color-success) 78%,
    var(--color-text-primary)
  );
}

.users-table__active[data-state='inactive'] {
  color: var(--color-text-muted);
}

.users-dialog {
  border-radius: var(--radius-surface);
  box-shadow: var(--shadow-raised);
}

.users-workspace__section-title {
  margin: var(--space-2) 0 0;
  font-size: 1rem;
}
```

Do not modify `parameters.css`. The new semantic tokens in Task 1 provide the
cross-page consistency for Parameters while the user-owned marker fix remains
outside this branch.

- [ ] **Step 5: Run GREEN and admin regression**

```bash
npm test -- src/pages/UsersPage.test.tsx src/pages/ParametersPage.test.tsx
npm test
npm run lint
```

Expected: all tests pass and Oxlint exits 0.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/UsersPage.tsx \
  frontend/src/pages/UsersPage.test.tsx \
  frontend/src/styles/users.css
git commit -m "feat(frontend): clarify admin user states"
```

---

### Task 6: Extend production-browser coverage and collect visual evidence

**Files:**
- Modify: `frontend/e2e/portal.smoke.spec.ts`
- Modify: `frontend/e2e/ticket-detail.smoke.spec.ts`

**Interfaces:**
- Consumes: existing Playwright projects and REST route fixtures.
- Produces: browser proof for password toggle, mobile navigation, System
  messages, no overflow, keyboard behavior, reduced motion, and screenshots.

- [ ] **Step 1: Update keyboard expectations for the password button**

In the existing keyboard test, after typing the password, replace the direct
login-button expectation with:

```ts
await page.keyboard.press('Tab')
await expect(
  page.getByRole('button', { name: 'Parolayı göster' }),
).toBeFocused()
await assertFocusVisibleOutline(page)
await page.keyboard.press('Enter')
await expect(page.getByLabel('Parola')).toHaveAttribute('type', 'text')
await page.keyboard.press('Tab')
await expect(
  page.getByRole('button', { name: 'Giriş yap' }),
).toBeFocused()
await page.keyboard.press('Enter')
```

- [ ] **Step 2: Add responsive menu assertions to the portal smoke**

After `loginThroughUi(page)` in the responsive smoke:

```ts
const viewport = page.viewportSize()
expect(viewport).not.toBeNull()
const menuTrigger = page.getByRole('button', { name: 'Menüyü aç' })

if (viewport!.width <= 767.84) {
  await expect(menuTrigger).toBeVisible()
  await expect(
    page.getByRole('navigation', { name: 'Ana menü' }),
  ).toHaveCount(0)
  await menuTrigger.click()
  await expect(
    page.getByRole('navigation', { name: 'Ana menü' }),
  ).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(menuTrigger).toBeFocused()
} else {
  await expect(menuTrigger).toHaveCount(0)
  await expect(
    page.getByRole('navigation', { name: 'Ana menü' }),
  ).toBeVisible()
}
```

Reuse the existing `viewport` variable later in the test rather than declaring
it twice.

Before the keyboard test searches for the logout button, add:

```ts
const keyboardViewport = page.viewportSize()
expect(keyboardViewport).not.toBeNull()
if (keyboardViewport!.width <= 767.84) {
  await page.getByRole('button', { name: 'Menüyü aç' }).click()
}
```

- [ ] **Step 3: Add a System message to the detail fixture**

In the ticket-detail REST fixture, add one message in chronological position:

```ts
{
  id: 'message-system',
  senderType: 'System',
  userId: null,
  content: 'Talep otomatik olarak güncellendi.',
  isHtml: false,
  createdAt: '2026-07-20T09:20:00.000Z',
}
```

In the ready-detail smoke, assert:

```ts
const timelineItems = page.locator('.ticket-timeline__item')
await expect(timelineItems).toHaveCount(3)
await expect(timelineItems.nth(0)).toContainText(HTML_LOOKING_CONTENT)
await expect(timelineItems.nth(1)).toHaveAttribute(
  'data-sender',
  'system',
)
await expect(timelineItems.nth(2)).toContainText(
  'Merhaba, yazıcı kuyruğunu kontrol ediyoruz.',
)
await expect(timelineItems.nth(2).locator('time')).toHaveAttribute(
  'dateTime',
  '2026-07-20T10:00:00.000Z',
)

const systemMessage = page.locator(
  '.ticket-timeline__item[data-sender="system"]',
)
await expect(systemMessage).toContainText('Sistem')
await expect(systemMessage).toContainText(
  'Talep otomatik olarak güncellendi.',
)
```

Remove the old count of `2`, the old Support assertion at `nth(1)`, and the old
Support timestamp assertion at `nth(1)`. Keep the Customer literal-HTML,
attachment, reply, assignment, and resolution assertions unchanged.

- [ ] **Step 4: Run focused Playwright tests**

```bash
npm run build
npx playwright test e2e/portal.smoke.spec.ts --project=mobile-320
npx playwright test e2e/portal.smoke.spec.ts --project=desktop-1440
npx playwright test e2e/ticket-detail.smoke.spec.ts --project=mobile-390
npx playwright test e2e/ticket-detail.smoke.spec.ts --project=desktop-1440
```

Expected: all focused browser tests pass; attachments contain the updated
screenshots; no console/page/request failure assertion fires.

- [ ] **Step 5: Run the complete frontend gate**

```bash
npm ci
npm run lint
npm test
env -u VITE_API_BASE_URL npm run build
npx playwright test
```

Expected:

- `npm ci` exits 0 without changing `package.json` or `package-lock.json`;
- Oxlint exits 0;
- every Vitest test passes;
- TypeScript and Vite production build exit 0;
- every Playwright test passes in `desktop-1440`, `tablet-720`, `mobile-390`,
  and `mobile-320`.

- [ ] **Step 6: Confirm scope and user-owned files**

Run from repository root:

```bash
git diff --check
git diff --name-only "$(git merge-base HEAD main)"..HEAD
git status --short
```

Expected: no backend/database/API file appears; no redesign commit contains
`frontend/src/styles/parameters.css` or `frontend/e2e/admin.smoke.spec.ts`;
status is clean in the isolated worktree.

- [ ] **Step 7: Commit browser coverage**

```bash
git add frontend/e2e/portal.smoke.spec.ts \
  frontend/e2e/ticket-detail.smoke.spec.ts
git commit -m "test(frontend): cover focused evolution in browsers"
```

---

## Final Review Checklist

- [ ] Compare every changed behavior against
  `docs/superpowers/specs/2026-07-24-frontend-focused-evolution-design.md`.
- [ ] Confirm no status, role, API route, payload, auth, or reply-outcome change.
- [ ] Confirm mobile navigation closes on route activation and Escape.
- [ ] Confirm lifecycle counts still use searched loaded data before status
  filtering.
- [ ] Confirm timeline preserves input order and literal text rendering.
- [ ] Confirm no new dependency or large generic component system.
- [ ] Confirm all visible added copy is Turkish.
- [ ] Confirm screenshots at all four projects show no overflow or covered
  content.
- [ ] Confirm user-owned `parameters.css` and admin E2E changes remain outside
  every redesign commit.
