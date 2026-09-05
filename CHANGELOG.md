# Changelog

## Two-factor authentication and refresh tokens

Adds an opt-in TOTP second factor and rotating refresh tokens with reuse detection.

### TOTP (RFC 6238)

* **`Helpers/TotpService.cs`** — HMAC-SHA1, 6 digits, 30-second step, matching what Google
  Authenticator, Microsoft Authenticator, Authy and 1Password default to. Implemented on
  `System.Security.Cryptography` with no extra package, and verified against all six
  RFC 6238 Appendix B test vectors rather than against itself.
* **`Helpers/Base32.cs`** — RFC 4648 codec for the `otpauth://` secret, tolerant of the
  spaces, dashes, padding and lower case a user might type. Verified against the RFC 4648
  vectors.
* **Clock drift** of one step either side is accepted. **Replay is not**: the last accepted
  time step is stored on the user, so a code still inside its 30-second window cannot be
  used twice.
* **Enrolment is two-phase.** `POST /totp/setup` stores a secret but leaves 2FA off;
  only `POST /totp/enable`, which requires a live code, turns it on. Closing the tab
  halfway through cannot lock you out.
* **Ten single-use recovery codes** are issued on enable, shown exactly once, stored as
  SHA-256 hashes, and accepted anywhere an authenticator code is.
* **The secret is encrypted at rest** (`Helpers/SecretProtector.cs`, ASP.NET Data
  Protection) — it is password-equivalent, since anyone holding it can mint codes forever.
* **Disabling 2FA re-checks the password** as well as a code, so a hijacked session cannot
  strip the second factor. Enabling or disabling revokes every existing session.

### Two-step login

`POST /api/auth/login` returns `{ requiresTotp: true, mfaToken }` instead of a session when
the account has 2FA on. The `mfaToken` is a five-minute JWT issued for a **different
audience** (`AITimesheet.Client.Mfa`), validated explicitly in `JwtService.TryReadMfaToken`
rather than by the bearer middleware — so a half-finished login can never be presented to a
resource endpoint, and an access token cannot be replayed at the TOTP step.

### Refresh tokens

* **`Entities/RefreshToken.cs` + `RefreshTokenService`** — 256-bit opaque tokens, stored
  only as SHA-256 hashes. A fast hash is correct here, unlike for passwords: there is no
  guessing attack against 256 random bits.
* **Rotation on every use.** The presented token is revoked and a successor issued in the
  same *family*.
* **Reuse detection.** Presenting an already-rotated token means a copy escaped, so the
  **entire family** is revoked — the attacker and the real user are both signed out, rather
  than both holding live credentials. An unrelated session in another family survives.
* **Transport is an httpOnly cookie** (`ait_rt`, `SameSite=Strict`, `Path=/api/auth`,
  `Secure` outside Development). Page JavaScript cannot read it, so an XSS bug cannot steal
  a 14-day credential.
* **Access tokens dropped from 60 to 15 minutes**, which is what makes the shorter window
  affordable.
* Sign-out, enabling 2FA and disabling 2FA all revoke server-side; expired rows are purged
  opportunistically, with a 30-day grace period so a replayed token still triggers reuse
  detection rather than merely looking unknown.

### Frontend

* **The access token now lives in a module variable, never `sessionStorage`.** A page reload
  restores the session through the refresh cookie instead.
* **Silent refresh**: a 401 on any call triggers one refresh and replays the original
  request. Concurrent 401s share a single in-flight refresh rather than stampeding.
* **`pages/Security.tsx`** — enrolment with a locally-rendered QR code (the secret never
  goes to a third-party QR service), the manual-entry key, recovery codes with a download,
  regeneration, and disable.
* **Two-step login screen**, with recovery codes accepted in the same field.
* Gateway CORS gained `AllowCredentials()` so the cookie is sent cross-origin; this is why
  the allowed origins are explicit rather than `AllowAnyOrigin`.
* Login rate limiting is now partitioned per client address, so one attacker cannot lock
  everyone out — and a brute-force run against a six-digit code is throttled.

### Tests

The suite grows from 49 to **101**, adding TOTP against the RFC vectors, base32 against the
RFC vectors, and refresh token rotation, reuse detection and family isolation (against a
hand-written in-memory repository, so the interaction between rotation and revocation is
exercised for real).

### Note

`ServiceLayer.SignInResult` was renamed `AuthenticationResult` — it collided with
`Microsoft.AspNetCore.Mvc.SignInResult` in the controller.

---

## Security and correctness hardening (previous release)

Fixes every issue found in the code review: four critical security defects, six correctness
bugs, and a set of maintainability problems. 66 files changed, 27 added, 1 removed.

---

## 1. Authorization: identity now comes from the token

**The defect.** `JwtService` wrote `NameIdentifier`, `Email` and `Role` into every token and
nothing ever read them back — a grep for `User.Claims` / `HttpContext` across the backend
returned zero hits. All six controllers took the acting user from a route parameter or a
request body instead, behind a bare `[Authorize]` that only proved *some* valid token
existed. Any authenticated user could read, edit, submit or approve anyone's timesheet by
changing an id, and `ApprovalController.Decide` let an employee approve their own week.

**The fix.**

- **Added `Extensions/ClaimsPrincipalExtensions.cs`** — `GetUserId()` reads
  `ClaimTypes.NameIdentifier`; `IsManager()` reads the role claim.
- **Added `Controllers/ApiControllerBase.cs`** — `[ApiController]`, `[Authorize]`,
  `CurrentUserId`, `IsManager`, and a `Denied()` helper returning RFC 7807 403s. All six
  controllers now derive from it.
- **Added `Entities/Roles.cs`** — role name constants so `[Authorize(Roles = …)]` and
  `IsInRole()` cannot drift apart by a typo.
- **Removed `UserId` from `GenerateTimesheetRequest`, `ConnectAccountRequest` and
  `ChatRequest`.** The client can no longer assert an identity even in principle.
- **`ApprovalController`** — `[Authorize(Roles = Roles.ManagerOrAdmin)]` on the whole
  controller, plus `Decide` resolves the timesheet's owner through the identity service
  and rejects it unless `employee.ManagerId == CurrentUserId`. It also refuses to decide
  anything that is not currently `Submitted`, and records the deciding manager.
- **`AnalyticsController`** — manager-only; the team comes from the token. `GET /team`
  needs no id at all.
- **`TimesheetController`** — ownership checks on every action; a manager may read a direct
  report's sheets but only the owner may edit or submit. Edits are refused once a sheet
  leaves `Draft`/`Generated`. Reading someone else's sheet returns 404, not 403, so ids
  cannot be probed.
- **`IntegrationController`** — all actions operate on the caller's own connections. Writing
  an OAuth token into another user's account is no longer expressible.
- **`ActivityController`** — own activity, or a direct report's for a manager.
- Legacy `/{managerId}` and `/{userId}` routes are kept so old links do not 404, but they
  now verify the id matches the caller.

## 2. Authentication: real credentials, no self-service accounts

**The defect.** `POST /api/auth/login` accepted an email and a display name, created the
account if it did not exist, and granted the `Manager` role to any address containing the
substring `"manager"`. No password, anywhere.

**The fix.**

- **Added `Helpers/PasswordHasher.cs`** — PBKDF2-HMAC-SHA256, 210,000 iterations, 16-byte
  per-user salt, 32-byte key, constant-time comparison via `CryptographicOperations.FixedTimeEquals`.
  Stored in one self-describing column (`v1.{iterations}.{salt}.{key}`) so the work factor
  can be raised later without a schema change. Uses only `System.Security.Cryptography` —
  no new package.
- **`AuthService.LoginAsync` rewritten** — unknown email is a failed login, not a new
  account; the role comes from the stored row; deactivated accounts are rejected; every
  failure returns the same 401 so the endpoint cannot be used to enumerate addresses. A
  dummy hash is verified on unknown emails to equalise response timing.
- **Deleted `FindFirstManagerAsync`** and its nine lines of stream-of-consciousness comments,
  along with the discarded `GetByManagerIdAsync(Guid.Empty)` call above it. Replaced by
  `IUserRepository.GetFirstByRoleAsync`.
- **Seeded demo users by migration** with a real reporting line: Sarah Jenkins (Manager) and
  Priya Sharma (Employee, reporting to Sarah), both `Demo@123`. The README previously
  claimed `sarah@company.com` was a manager, but the substring rule made her an Employee
  assigned to an auto-generated phantom `manager@company.com`, so nobody could approve
  anything.
- **Login is rate limited** to 10 requests/minute.
- `AuthController` is now `[Authorize]` by default with `[AllowAnonymous]` opt-in;
  `GET /me/{userId}` became `GET /me` and returns the caller's own profile.

## 3. Internal endpoints closed

**The defect.** `internal/users/{id}` and `internal/users/manager/{id}` had no
authentication, and the gateway's `api/auth/{**catch-all}` route forwarded straight to
them — so an unauthenticated request to the public ingress returned the name, email, role
and reporting line of any user or whole team.

**The fix.** Two layers:

- **Gateway** — `app.Map("/api/auth/internal/{**rest}", () => Results.NotFound())` registered
  before `MapReverseProxy()`. The literal-prefixed route out-ranks the proxy's catch-all,
  so the request is answered at the ingress and never forwarded.
- **Added `Security/InternalApiKeyAttribute.cs`** — requires `X-Internal-Api-Key`, compared
  in constant time, and fails closed if the key is unconfigured. `IdentityServiceClient`
  sends it on every call.

## 4. Secrets removed from the repository

**The defect.** The JWT signing key was in both `appsettings.json` files *and* printed in
the README; the database password was in both connection strings and `docker-compose.yml`;
provider OAuth tokens were stored as plaintext `text` columns.

**The fix.**

- Both `appsettings.json` files ship empty placeholders. Values come from
  `dotnet user-secrets` locally and `Jwt__Key` / `ConnectionStrings__DefaultConnection` /
  `Internal__ApiKey` elsewhere. Both services **refuse to start** without them, and the JWT
  key is rejected below 32 bytes.
- **Added `scripts/set-dev-secrets.sh` and `.ps1`** — generate strong local keys and load
  everything into user-secrets in one command. `<UserSecretsId>` added to both csproj files.
- **Added `.env.example`**; `docker-compose.yml` now reads `${POSTGRES_PASSWORD:?…}` and
  fails loudly rather than shipping a working password. Added a `pg_isready` healthcheck.
- **Added `ITokenProtector` / `TokenProtector`** — provider tokens encrypted at rest with
  ASP.NET Data Protection, decrypted only at fetch time. Disconnecting now clears the
  credential rather than leaving it in a disabled row.
- The old JWT key is burned; `.gitignore` extended.

---

## 5. Week-boundary bug (duplicate timesheets)

`Dashboard.tsx` computed Monday in local time then serialised with `toISOString()` (UTC).
East of UTC before ~05:30 IST that returned the previous **Sunday**, so the duplicate check
in `Generate` missed and the same week produced two timesheets — with the provider queries
covering Sunday–Saturday.

- **Added `frontend/src/utils/week.ts`** — formats the local date parts directly, no UTC
  round-trip. `formatDayLabel` likewise stops the browser reinterpreting `YYYY-MM-DD` as UTC.
- **Added `ServiceLayer/WeekCalculator.cs`** — the server snaps *any* received date to that
  week's Monday, so a future client bug cannot recreate this.
- **Added a unique index on `timesheets (UserId, WeekStartDate)`** — the database now enforces
  one timesheet per user per week. The migration collapses pre-existing duplicates first.

## 6. Activities duplicated on every regenerate

`Generate` called `AddRangeAsync` unconditionally and deleted only the *timesheet*, so every
regeneration left another full copy of the week's commits, tickets and meetings behind —
degrading the chat context and activity list each time.

- **Added `ServiceLayer/Implementations/TimesheetGenerationService.cs`** — the workflow moved
  out of the controller. It fetches, deduplicates, calls the AI, and then does the deletes
  and inserts inside **one transaction**.
- **Added `IUnitOfWork` / `UnitOfWork`** using `CreateExecutionStrategy()` so the transaction
  is retry-safe. Previously the delete and the insert were separate `SaveChanges` calls, so
  a failure mid-way left the user with no timesheet at all.
- **Added `IActivityRepository.DeleteForUserAndRangeAsync`** — regeneration replaces the
  week rather than appending to it.
- **`Deduplicate`** collapses the same item reported twice, keyed on
  `(Source, ExternalReference ?? Title, Date)`, case-insensitively.
- The AI call now happens *before* the transaction opens, so an outbound HTTP request never
  holds a database transaction open.
- **Added an index on `activities (UserId, ActivityDate)`** matching the actual query shape.

## 7. Silent mock fallbacks removed

Every integration caught all exceptions and returned mock data, so an expired token, a 500
from Jira and a DNS failure were indistinguishable from a healthy connection.

- **`IIntegrationService` now returns `IntegrationFetchResult`** (activities + error) instead
  of a bare list. All four integrations rewritten: real error classification (401 → "reconnect
  this provider"), logging, and no mock fallback.
- **`Connection` gained `LastError` and `LastSyncedAt`**, surfaced in `ConnectionStatusDto`
  and shown on the Connect Accounts screen as an amber dot with the reason.
- **Added `DemoActivityFactory`** — a user who has connected nothing gets *one* coherent
  sample week. Previously every integration's mock ran at once: the Jira and Azure DevOps
  mocks both emitted `JiraTicket` rows, so a fresh user got five overlapping "tickets" from
  two providers plus duplicate PRs, with inflated hours.
- **Auth headers moved to `HttpRequestMessage`** instead of mutating
  `HttpClient.DefaultRequestHeaders` — one user's credential can no longer ride along on
  another user's request.
- **Added `ActivitySource.WorkItem`** — Azure DevOps items were recorded as `JiraTicket`,
  making the two providers indistinguishable everywhere downstream.
- Guarded `sha?[..7]`, which threw on a short or missing commit SHA.
- Jira issues now use their own `updated` timestamp instead of all being pinned to Monday.

## 8. Missing-hour prompts reach the screen

`BuildFallbackResult` generated prompts like *"only 3.5 hours on Tuesday — did you work on
documentation?"*, the AI prompt explicitly asked for them, and `TimesheetController` then
discarded the whole field.

- `Timesheet.MissingHourPrompts` persisted as JSON (value converter + `ValueComparer`),
  returned in `TimesheetDto`, and rendered in `TimesheetReview.tsx`.
- The fallback generator now emits a row for **every working day**, so an empty day is
  something the employee can fill in rather than a row that silently does not exist.
- Model output is validated and clamped (0–24h) before it is persisted; unparseable
  responses fall back instead of producing an empty timesheet.

## 9. `database/schema.sql` rewritten

It claimed to mirror the migrations and did not: snake_case columns vs the generated
PascalCase, `TIMESTAMP` vs `TIMESTAMPTZ`, `uuid_generate_v4()` defaults with the
`CREATE EXTENSION` line commented out, and foreign keys to `users` — a table in a *different
database*. Following the README and running it first produced tables EF could not migrate
over. It is now accurate, clearly marked as reference documentation, and explains why the
cross-database FKs cannot exist.

## 10. Error handling, validation and observability

- `AddProblemDetails()` + `UseExceptionHandler()` in both services. `LoginAsync` no longer
  throws a `NullReferenceException` on `{"fullName":"x"}` — there is validation now.
- DataAnnotations on every request DTO (`[Required]`, `[EmailAddress]`, `[Range(0,24)]`,
  length limits).
- `/health` endpoints in all three services, plus YARP active health checks.
- HSTS + HTTPS redirection outside development; `ClockSkew = TimeSpan.Zero` on token
  validation.
- Migrations are opt-in via `Database:AutoMigrate` (default on only in Development) and log
  failures instead of throwing an unexplained startup error.
- `IdentityServiceClient` caches lookups for 60s and logs failures rather than swallowing
  them — the approvals and analytics screens hit it on every request.
- Integrations registered by concrete type so each gets its own named `HttpClient`.
  Registering all four as `AddHttpClient<IIntegrationService, T>` made them share one
  configuration entry keyed on the interface name.

## 11. Frontend

- **Added `auth/AuthContext.tsx`** — one source of truth for the session. The token is held
  in a module variable instead of being re-read and re-parsed out of `sessionStorage` on
  every request, and a stored session past its expiry is discarded on load.
- **401 interceptor** ends the session and shows the login screen. An expired JWT used to
  produce silently failed requests and a page stuck on "Loading…".
- **`describeApiError`** unwraps RFC 7807 bodies, so validation and authorization messages
  actually reach the user. Every page now has error state.
- **Manager-only routing** — `ManagerRoute` guard plus conditional nav. Analytics was
  previously shown to everyone and returned a blank page for employees.
- **Login takes a password**, and the demo credentials are on the screen.
- **Removed `public/avatar_priya.jpg`** — it was hardcoded, so every user saw Priya's face.
  Replaced with generated initials.
- Types tightened (`Role`, `TimesheetStatus`, `TeamAnalytics`, `ProblemDetails`); the API
  layer no longer sends user ids; added a per-employee analytics table, an edit-cancel
  button, and hours input clamped to 0–24.

## 12. Tests

**Added `backend/AITimesheet.Tests`** (xUnit, wired into the solution) — 49 tests covering:

- `PasswordHasher` — round-trip, wrong passwords, malformed stored hashes, salt uniqueness,
  and a guard that the **seeded migration hashes still verify against the documented demo
  password**, so the seed data cannot drift from the README.
- `WeekCalculator` — every day of the week, month and year boundaries, idempotence, and the
  specific value the old buggy client produced.
- `Deduplicate` — case-insensitive reference matching, cross-provider ids kept distinct,
  same item on different days kept, title fallback.
- `BuildFallbackResult` — one row per working day, category hours summing to the total,
  Azure DevOps work items counting as development, missing-hour prompt thresholds, weekend
  handling.
- `ClaimsPrincipalExtensions` — the basis of the entire authorization model.

`.http` scratch files replaced with real request collections, including the negative cases
(wrong password, unknown email, cross-user access, employee self-approval, blocked internal
endpoint).
