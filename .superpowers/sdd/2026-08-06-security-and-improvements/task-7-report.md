# Task 7 Report — Durable audit for user-admin operations (SEC-009)

**Status:** DONE

**Brief:** `/home/a/Projects/vs-help-desk/.superpowers/sdd/2026-08-06-security-and-improvements/task-7-brief.md`

## Changes

- `src/VSHelpDesk.Domain/Entities/UserAuditEvent.cs` (new) — Append-only audit entity with `Id, ActorUserId, TargetUserId, EventType (Created/RoleChanged/ActiveChanged/PasswordReset), BeforeRole/AfterRole, BeforeIsActive/AfterIsActive, CreatedAt, CorrelationId`. Constructor validates actor/target non-empty and EventType whitelist, normalizes timestamps to UTC, never stores password/hash. Private setter + factory matches `ParameterChangeLog` style.
- `src/VSHelpDesk.Infrastructure/Persistence/Configurations/UserAuditEventConfiguration.cs` (new) — `ToTable("UserAuditEvents")`, `HasKey(Id)`, `ValueGeneratedNever()`, `EventType` max 32, role max 32, `CreatedAt` `timestamp with time zone`, `CorrelationId` max 64, index `(TargetUserId, CreatedAt)` + index `ActorUserId`.
- `src/VSHelpDesk.Application/Abstractions/Persistence/IApplicationDbContext.cs:23` — Added `IQueryable<UserAuditEvent> UserAuditEvents => Enumerable.Empty<UserAuditEvent>().AsQueryable()` with default implementation to avoid breaking existing FakeDb test doubles (20+ fakes in unit/integration tests). Concrete `ApplicationDbContext` exposes `DbSet<UserAuditEvent>`.
- `src/VSHelpDesk.Infrastructure/Persistence/ApplicationDbContext.cs:12-27` — Added `DbSet<UserAuditEvent> UserAuditEvents => Set<UserAuditEvent>()` and explicit interface mapping `IQueryable<UserAuditEvent> IApplicationDbContext.UserAuditEvents => UserAuditEvents`. Existing `OnModelCreating` provider-aware timestamp fallback (`timestamp with time zone` → null for InMemory/SQLite) already covers new entity; no extra branch needed.
- `src/VSHelpDesk.Application/Features/Users/CreateUser/CreateUserHandler.cs:11-49` — Injected `ICurrentUserService? currentUserService`, `TimeProvider? timeProvider`, `IApplicationDbContext? dbContext` (optional nullable with defaults for backwards compat; DI resolves `TimeProvider.System` singleton and `HttpCurrentUserService` scoped). After `userRepository.AddAsync`, before `SaveChangesAsync`, appends `UserAuditEvent(ActorUserId=userId, TargetUserId=user.Id, EventType="Created", AfterRole=user.Role.ToString(), CreatedAt=timeProvider.GetUtcNow().UtcDateTime)` if authenticated actor exists. Single `SaveChanges` covers user + audit atomically, append-only, never touches password/hash.
- `src/VSHelpDesk.Application/Features/Users/UpdateUser/UpdateUserHandler.cs:11-64` — Added `ICurrentUserService?` + `TimeProvider?`. Captures `beforeRole`/`beforeIsActive` before mutation, `afterRole`/`afterIsActive` after `AssignRole`/`Activate`/`Deactivate`. Inside same `TransactionScope` + advisory lock, emits `RoleChanged` if role differs and `ActiveChanged` if active differs, each as separate `UserAuditEvent` with shared `now` (provider-aware `TimeProvider`). Both rows added via `dbContext.Add` before single `SaveChanges`. If neither changes, no audit row.
- `src/VSHelpDesk.Application/Features/Users/SetUserPassword/SetUserPasswordHandler.cs:12-27` — Added same three optional deps. After `ReplacePasswordHash` + `Update`, emits `PasswordReset` audit (`BeforeRole/AfterRole` null) with `ActorUserId` and `CreatedAt` from `TimeProvider`. Audit row contains no password, no hash — verified by property name absence and JSON serialization check.
- `src/VSHelpDesk.Infrastructure/Persistence/Migrations/20260806000952_AddUserAuditLog.cs` + `Designer.cs` (new) — `dotnet ef migrations add AddUserAuditLog --project src/VSHelpDesk.Infrastructure --startup-project src/VSHelpDesk.WebAPI` (with `ConnectionStrings__DefaultConnection` + `Database__Provider=Postgres` env for design-time factory). Creates `UserAuditEvents` table with `uuid` keys, `character varying(32)` event/role, `boolean` active columns, `timestamp with time zone` `CreatedAt`, `character varying(64)` `CorrelationId`, PK `Id`, index `ActorUserId`, composite index `TargetUserId,CreatedAt`. `ApplicationDbContextModelSnapshot.cs` updated.
- `tests/VSHelpDesk.WebAPI.IntegrationTests/Controllers/UsersAuditTests.cs` (new) — 4 integration tests via `CustomWebApplicationFactory` (InMemory) + `CookieAuthTestHelper.LoginAsAdminAsync`:
  - `CreateUser_emits_audit_event` — asserts single `Created` row, `ActorUserId==admin`, `AfterRole==Support`, `CreatedAt` recent, no secret.
  - `UpdateUser_role_change_emits_audit_event` — creates Support, clears audit, PUT role Admin, asserts single `RoleChanged` with `BeforeRole=Support, AfterRole=Admin`.
  - `UpdateUser_active_change_emits_audit_event` — similar, asserts `ActiveChanged` with `BeforeIsActive=true, AfterIsActive=false`.
  - `SetPassword_does_not_log_secret` — creates user, clears, POST `/password`, asserts single `PasswordReset`, checks entity has no `Password`/`PasswordHash` properties and JSON does not contain raw password.

## TDD Execution

### 1. Failing test before fix
```
dotnet test --filter "UsersAudit" -v
# no compilation yet — Entity `UserAuditEvent` missing, IApplicationDbContext.UserAuditEvents missing → build FAIL (20 errors on FakeDb)
```

### 2. Implement entity + handlers
```
dotnet build
# Build succeeded after adding default interface method for UserAuditEvents.
dotnet ef migrations add AddUserAuditLog
# Done. To undo this action, use 'ef migrations remove'
```

### 3. Passing verification after fix
```
dotnet test --filter "UsersAudit" -v n
# Passed VSHelpDesk.WebAPI.IntegrationTests.Controllers.UsersAuditTests.CreateUser_emits_audit_event [266 ms]
# Passed VSHelpDesk.WebAPI.IntegrationTests.Controllers.UsersAuditTests.UpdateUser_role_change_emits_audit_event [...]
# Passed VSHelpDesk.WebAPI.IntegrationTests.Controllers.UsersAuditTests.UpdateUser_active_change_emits_audit_event [...]
# Passed VSHelpDesk.WebAPI.IntegrationTests.Controllers.UsersAuditTests.SetPassword_does_not_log_secret [266 ms]
# Test Run Successful. Total tests: 4, Passed: 4

dotnet test --filter "UsersApi" -v n
# Passed 7/7 — GetUsers, PostUsers CreateSupport+Duplicate400, LastAdminDemote400, TwoAdminsDemoteOne, PostPassword etc. — no regression

dotnet build
# Build succeeded. 0 Warning(s) 0 Error(s)
```

## Commits

- `security(audit): durable audit for user admin events (SEC-009)` — 11 files
```
 src/VSHelpDesk.Application/Abstractions/Persistence/IApplicationDbContext.cs
 src/VSHelpDesk.Application/Features/Users/CreateUser/CreateUserHandler.cs
 src/VSHelpDesk.Application/Features/Users/SetUserPassword/SetUserPasswordHandler.cs
 src/VSHelpDesk.Application/Features/Users/UpdateUser/UpdateUserHandler.cs
 src/VSHelpDesk.Domain/Entities/UserAuditEvent.cs
 src/VSHelpDesk.Infrastructure/Persistence/ApplicationDbContext.cs
 src/VSHelpDesk.Infrastructure/Persistence/Configurations/UserAuditEventConfiguration.cs
 src/VSHelpDesk.Infrastructure/Persistence/Migrations/20260806000952_AddUserAuditLog.Designer.cs
 src/VSHelpDesk.Infrastructure/Persistence/Migrations/20260806000952_AddUserAuditLog.cs
 src/VSHelpDesk.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs
 tests/VSHelpDesk.WebAPI.IntegrationTests/Controllers/UsersAuditTests.cs
```

## Concerns

- Domain←App←Infra respected: `UserAuditEvent` lives in `Domain`, handlers depend only on `IApplicationDbContext` + `ICurrentUserService` + `TimeProvider` abstractions, Infra provides EF configuration and migration.
- Provider-aware timestamp: `HasColumnType("timestamp with time zone")` with fallback in `ApplicationDbContext.OnModelCreating` (non-Npgsql → `SetColumnType(null)`) matches existing `User.CreatedAt` pattern; InMemory tests use `TimeProvider.System.GetUtcNow().UtcDateTime`.
- Append-only: no update/delete path for `UserAuditEvent`; `Remove` is generic but no handler calls it. Retention job can be added later (index on `TargetUserId,CreatedAt` already present).
- CorrelationId currently `null` — handlers pass `null` to keep scope minimal; future middleware could inject `HttpContext.TraceIdentifier` via `IHttpContextAccessor` if needed, column already has max 64.
- Optional injection (`ICurrentUserService?`, `TimeProvider?`, `IApplicationDbContext?`) keeps existing direct constructions in unit tests working; real DI (scoped `HttpCurrentUserService`, singleton `TimeProvider.System`, scoped `ApplicationDbContext`) resolves them. If `ActorUserId` is empty/null (system/seed path), audit is skipped rather than throwing, preserving seed behavior.
- No password/hash ever persisted in audit: entity has no such properties, handler never copies `command.Password`, tests assert property absence and JSON non-containment.
