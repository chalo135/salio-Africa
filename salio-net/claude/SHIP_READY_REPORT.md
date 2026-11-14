# Home screen, menu and production readiness: report

Run: 10–13 November 2026 (the agents were stopped by usage limits several times and resumed). Nothing was committed or pushed. Every change is uncommitted in the working tree.

## In one paragraph

Phases 1 to 4 are built. **Phase 5 (QA) was not run: you told me to skip it.** So nobody has tested the finished web app against the finished API together, and nothing has been tried on a real tablet or the real VPS. What each agent saw for itself is marked below; the 18 QA checks are listed as not run. My own last checks: `npm run build` clean, `npm run lint` clean, `dotnet test` **291 passed, 0 failed** (257 before). No migration. No protected file and no guard test file was edited (their last-modified dates are all 9 November or earlier). The deploy runbook is `deploy/DEPLOY.md`.

**Read first:** your old database password is in pushed git history and still works on the dev database (see "Committed secrets"). And a sale can be double-posted on a bad connection (see "What I would not ship", item 1).

---

## Phase 1: the home screen

Checked by the web agent in headless Chromium against a **stubbed** API, not the real one.

- **Empty shelf.** The Sell screen is replaced by "Your shelf is empty" / "Put your parts in and you can start selling.", two cards side by side ("Add a part", "Import a list"), then the line "You don't have to put everything in now. Add a part the first time someone asks for it — Salio will never stop you making a sale." No search box.
- **Invoice card.** Shows only when `GET /api/supplier-invoices/reader-status` returns `real: true`. Today it always returns false, so there are two cards.
- **With parts.** "+ Add parts" beside the search opens a sheet with the same choices. The quick-add on an empty search is unchanged.
- **Count.** "148 parts on the shelf" in small grey text under the search. The shelf is re-read whenever Sell is returned to.
- **Cashier.** Only "Add a part" on the empty shelf; "+ Add parts" opens quick-add directly.
- **Import in an empty shop.** Step 1 reads "Start with a file that has the right columns / Download it, fill in your parts, and bring it back here."

Files (all `web/src/`): `App.tsx`, `ImportScreen.tsx`, `QuickAddForm.tsx` changed; `AddPartsChoices.tsx` new.

## Phase 2: the menu

**Does the sidebar fit at 1024x768? Yes.** Measured in headless Chromium, Owner, Shop group open with all six entries; the figures are content height / available height of the only region that could scroll.

| Viewport | Install button not showing | Install button showing |
|---|---|---|
| 1024x768 | 537 / 537 | 491 / 491 |
| 1024x700 | 481 / 481 | 435 / 435 |
| 1024x650 | 431 / 431 | 385 / 385 |

No scroll in any of the six. About 22px is spare in the tightest case.

**Owner's entries**
- Top: Sell, Today, Owed
- Shop: Parts, Stock in, Opening stock, Import parts, Parts needing a cost (badge), Read a supplier invoice (only when the reader is real, or in a dev build)
- Books: Month end, Tax to file, Money out
- Settings: People, What Salio calls what you sell
- Bottom: Install (only when Chrome offers it), Lock, Sign out

**Cashier's entries:** Sell, Today, then Lock and Sign out. No group headings.

**Missing or different from your brief**
- **Shop details:** no such screen exists, so there is no entry.
- **Cashier and Owed:** you wrote "a Cashier sees the top three". Owed is Owner-only in the existing role rules, so a Cashier sees Sell and Today. I did not widen it.
- **Import history** is no longer in the menu; it is reached from the Import screen.
- **"Count the stock"** (a disabled "Coming soon" row on More) was removed.

One menu definition (`menu.ts`, new) feeds the sidebar, More and the bottom nav. Other files: `Sidebar.tsx`, `MoreScreen.tsx`, `BottomNav.tsx`, `NavIcons.tsx`, `MonthScreen.tsx`, `ShopWordScreen.tsx` (new).

## Phase 3, API: what was found and what changed

Everything marked "ran" was run locally in Production mode.

| # | Found | Changed |
|---|---|---|
| 1 Errors | No developer exception page outside Development (correct). But an unhandled error returned an empty 500, and bad input returned 400s naming C# types. | Unhandled errors return "Salio could not do that. Nothing was saved. Try again; if it keeps happening, tell support this code: XXXXXXXX." The same code is in the log with the full detail. Bad input now says "The input was not valid." (ran) |
| 2 Secrets | See "Committed secrets" below. | Nothing rewritten. |
| 3 Cookies | Already correct. `salio.auth`: HttpOnly, Secure outside Development, SameSite Lax, path /, 12 hours. `salio_device`: HttpOnly, Secure outside Development, SameSite Lax, path /api/auth, 90 days. | Path made explicit; a stale comment fixed. |
| 4 HTTPS | Forwarded headers were ignored. `UseHttpsRedirection` ran everywhere and did nothing in the container. | The API now reads X-Forwarded-For and X-Forwarded-Proto from private-range proxies. HTTPS redirect is Development-only; Caddy owns the redirect and HSTS. (ran, from loopback only) |
| 5 CORS | `http://localhost:5173` was allowed with credentials in every environment. | Config key `Cors:AllowedOrigins`. Empty in production means no cross-origin access at all. (ran) |
| 6 Rate limits | Sign-in, PIN unlock and change-password shared 5/min + 20/hour per address. Setup codes 10/hour. Behind Caddy every caller was one address. | PIN unlock: its own limit, 10/min per address and device. Sign-in: 10/min + 100/hour per address, plus 10 wrong passwords in 10 minutes per email. Setup codes: 30/hour. Admin unchanged at 60 per 10 minutes. (ran) |
| 7 Health | None. | `GET /api/health`: 200 `{"status":"ok"}`, or 503 when the database does not answer. (ran) |
| 8 Confirmations | Demo seeder and startup migrations: Development only (correct, now pinned by tests). `POST /api/users`: gone. DateTimeOffset: JSON bodies are normalised; no endpoint takes one from a query string. Setup links were silently built from the `salio.example.com` placeholder. | An unset `PublicBaseUrl` logs an error at startup and the CLI prints no link. |
| 9 Logging | No log line carried a password, PIN, code or cookie. EF logged every SQL statement at Information. | EF set to Warning. Added the error-code log and rate-limit rejection logs. |
| 10 Test shops | 12 shops in the dev database. | `delete-test-shops.sql` and `delete-test-shops.ps1`. Dry run by default; `-Commit` asks you to type DELETE. **Only the dry run was run.** It would remove the two named shops: 28 journal entries, 35 parts, 4 sales, 2 users. |

Also added: `GET /api/supplier-invoices/reader-status`, Owner only, `{"real": false}` today.

Files created: `HealthController.cs`, `UnhandledErrorHandler.cs`, `Security/CorsOrigins.cs`, `Security/SignInAttempts.cs`, `Salio.Tests/ProductionHardeningTests.cs`, the two deletion scripts. Changed: `Program.cs`, both `appsettings` files, `SetupLink.cs`, `SetupCodeCommand.cs`, `AuthController.cs`, `AdminController.cs`, `SupplierInvoicesController.cs`, `SignInService.cs`, `ConfiguredInvoiceReader.cs`, `InvoiceViews.cs`.

## Phase 3, web: what was found and what changed

| # | Found | Changed |
|---|---|---|
| 1 API URL | A production build without `VITE_API_URL` would call `localhost:5077` on the tablet. | Unset now means same origin in a production build. Set by the Docker build-arg `VITE_API_URL` (compose passes it empty) or `web/.env.production`. A build with it unset had no "localhost" anywhere in `dist/`. |
| 2 Build | — | Clean, no warnings. About 130 kB gzipped on first load. |
| 3 PWA | Manifest had three icons and no `id` or `scope`. The service worker cached nothing. The Install button showed on browsers that can never install. | Eight icons (48 to 512, maskable 192 and 512). The worker caches the shell and never touches `/api`. With the network off, a reload rendered the sign-in screen. Chrome reported no installability errors in a normal profile. The Install button shows only when Chrome offers. |
| 4 Screen sizes | In portrait the cart showed 2 of 7 lines. | Fixed. 19 screens had no sideways scroll and nothing outside the viewport at 1024x768 and 800x1280, by automated measurement; only three screenshots were looked at by eye. |
| 5 Messages | Several screens showed the server's raw title ("One or more validation errors occurred.", "Bad Request") or "error 4xx". | One helper, `problemText` in `api.ts`, used by every screen. Setup and Admin screens were not audited. |
| 6 Connection lost | A failed start showed sign-in as if signed out. Sign out with no connection threw an error. | Banner: "Can't reach Salio. Check the internet on this tablet. What's on screen is kept." A start-up screen with "Try again". An error screen with "Reload Salio" in place of a white page. The cart stayed at 2 lines through every failure tested. |

Files created: `api.ts`, `ConnectionBanner.tsx`, `ErrorBoundary.tsx`, `vite-env.d.ts`, `web/.env.example`, six icons. Changed: `auth.ts`, `Root.tsx`, `main.tsx`, `install.ts`, `InstallButton.tsx`, `InstallPill.tsx`, `LoginScreen.tsx`, `PinUnlockScreen.tsx`, `importApi.ts`, `index.html`, `public/manifest.webmanifest`, `public/sw.js`, `nginx.conf`, `README.md`, plus a mechanical `fetch` to `apiFetch` swap in 23 other screens.

## Phase 4: deploy files

Exercised end to end on the laptop in a separate throwaway stack (`salioqa`). **Not run on the VPS.**

| File | What it is |
|---|---|
| `deploy/DEPLOY.md` | The runbook, 18 sections. |
| `docker-compose.yml` | Postgres, API, web, Caddy (behind a profile), migrate. Memory, CPU and process limits on each. |
| `.env.example` | Empty secrets, each commented. |
| `Salio.Api/Dockerfile` | Existing; gained the invoices folder. |
| `deploy/caddy/` | `Caddyfile` (bundled Caddy), `salio-host.caddy` (host Caddy), `salio-rules.caddy` (shared rules). |
| `deploy/backup.sh` | Dated dump, 14-day retention, also archives uploaded invoices. |
| `deploy/restore.sh` | `restore.sh <dump> <database>`. Refuses the live name without `--replace-live`. |
| `deploy/smoke-test.sh` | Five PASS/FAIL steps. |
| `deploy/wait-for-api.sh` | Polls health. Not in your brief. |

Removed and folded in: `deploy/CHECKLIST.md`, `backup-salio-db.sh`, `restore-test-salio-db.sh`, `salio.caddy`.

**Memory at the limits:** db 1024 MB, API 512 MB, web 64 MB, Caddy 128 MB. Total 1,728 MB with the bundled Caddy, 1,600 MB without.

**Smoke test output seen by the deploy agent** (local stack, over HTTPS through Caddy and over plain HTTP): five PASS lines, `SMOKE TEST PASSED`, exit code 0, receipt S-000001, debits = credits = 10000. Against a shop not named "Smoke…" it printed `REFUSED … Nothing was written.` with exit code 1, and the database confirmed nothing was written. The full output was not kept, so it is not pasted here.

**Restore row counts seen by the deploy agent:** 23 tables side by side, all equal, both shops balanced, `RESTORE TEST PASSED`. The per-table figures were not kept.

## Committed secrets

- **A real database password is in git history, pushed to GitHub.** `salio-net/Salio.Api/appsettings.json`, the `ConnectionStrings:Default` line, from commit `c8a3b60` (4 Sept) until it was removed in `837c52c` (21 Sept). It still equals the dev container's password, and the dev Postgres listens on `0.0.0.0:5432`. Generate a new one on the server and never reuse this one. Change the dev one too.
- **Not secrets, but in files that may be committed:** a test account's PIN and an unused dev setup code (expires 16 Nov) in `claude/BOOKS_AND_LOCK_FIX_REPORT.md`; older dev setup codes in two other reports and `BUILD_LOG.md`; your personal email in `claude/SETUP_REBUILD_REPORT.md`.
- **Working tree otherwise:** no password, key or token found.

## QA (Phase 5): not run

You told me to skip it. None of the 18 checks was performed by a QA agent. What stands in for them:

| # | Check | Status |
|---|---|---|
| 1–8 | Home screen, count, import wording, Cashier | Seen by the web agent against a stubbed API only. Check 6 (import 20, count says 20) was never run end to end. |
| 9–11 | Sidebar at 1024x768, Cashier, group opens | Seen by the web agent, stubbed API. Figures above. |
| 12 | 800x1280 portrait | 19 screens measured, stubbed API. |
| 13 | API stopped | Seen with stubbed failures, not with the real API stopped. |
| 14 | Production error, no stack trace | Seen by the API agent with curl. Not seen on screen in the app. |
| 15 | Smoke test | Run by the deploy agent on images built 12 Nov, before later code changes. |
| 16 | Backup and restore counts | Run by the deploy agent; counts equal, figures not kept. |
| 17 | Build and tests | Run by me: both clean, 291 passed, guard files 2/2, 3/3, 2/2, 5/5 per the API agent. |
| 18 | PWA installable | Seen by the PWA agent in headless Chromium on a stand-in server. |

## Decisions I made that you had not

| | Decision | Why |
|---|---|---|
| D1 | Caddy is in the compose file behind a profile, with the host-Caddy snippet kept as a second path. | I don't know whether something on the VPS already owns ports 80/443. The runbook's first check tells you which path you are on. |
| D2 | The smoke test refuses unless the shop's name starts with "Smoke". | It writes a real part and sale into append-only books. |
| D3 | The test-shop script was only dry-run. | Deleting is your call. |
| D4 | A demo invoice reading is refused in production (503, plain message). | A real shop could otherwise receive invented parts into its books. |
| D5 | Ten wrong passwords in ten minutes blocks that email's password sign-in. | Per-address limits alone are weak behind shared mobile addresses. Someone who knows an owner's email can use this to block them; PIN unlock is unaffected. |
| D6 | Sign-in limits loosened; PIN unlock given its own. | A shop behind one address would have been locked out of its own till. |
| D7 | Manifest orientation is `any`, not `landscape`. | A landscape lock turns the app sideways on staff phones. |
| D8 | A 5xx on page load shows the saved till. | Better than a gateway error page. |
| D9 | "Add a part" from the empty shelf adds to the shelf, not the cart. | Stocking the shelf should not fill a customer's cart. |
| D10 | Top three are tiles side by side; Lock and Sign out helper lines hide below 720px height. | To fit without scrolling. |
| D11 | "Money out" is in Books. | It had to go somewhere. |
| D12 | "Tax to file" opens the current month. | Your brief's wording. Month end opens last month, which is the one due; one line to change. |
| D13 | A sale with no answer shows "Check Today before trying again", not "not saved". | A retry can double-post (see below). |
| D14 | Content-Security-Policy: only safe directives enforced, the rest report-only. | It was not tested in a real browser. |
| D15 | If nginx or Apache owns 80/443, the runbook says stop. | No untested commands for that case. |
| D16 | A live restore keeps the old database under a dated name. | Safer than dropping it. |
| D17 | Backups also archive uploaded invoices. | The dump does not hold them. |
| D18 | The error handler runs in Development too. | One behaviour everywhere; detail is in the console. |

## Could not do

- **QA**, by your instruction.
- **Anything on the real VPS:** DNS, the certificate, the image transfer, cron, the scripts on Linux itself.
- **The host-Caddy path** beyond config validation.
- **A real Android tablet:** install dialog, camera, the service worker over HTTPS.
- **Shop details screen:** does not exist.
- **Signed-in API checks** such as reader-status for an Owner: unit tests only.

## What I would not ship

Ranked by how badly it would hurt the shopkeeper on day one.

1. **A sale can be posted twice.** `POST /api/sales` has no client id, so if the answer is lost on a bad connection and the cashier presses Pay again, the books hold two sales and stock drops twice. The app now says "Check Today before trying again", but that relies on a busy cashier reading it. The fix is in the API: accept a client-made id and reject a repeat. On a mobile connection in Kenya this will happen.
2. **Nobody has used the finished app against the finished API.** Every web check used a stubbed API; every API check used curl. The images tested were built before the last code changes. Do one sign-in, one quick-add, one import and one sale yourself on the tablet before the client does.
3. **The database password in git history.** If the production password is the old one, anyone with the repository has it.
4. **Sales between midnight and 03:00 Kenyan time are dated the previous day.** `saleDate` is taken in UTC. A shop that closes late will see them on yesterday's day sheet.
5. **The shop can be locked out of password sign-in by anyone who knows the owner's email** (D5). PIN unlock still works, so it bites only on a new device or after sign-out.
6. **Rate limits live in memory.** A restart clears them. Fine for one API container; wrong the day there are two.
7. **A tablet on a bad connection can run last week's screens against this week's API** until its next successful load. That is the price of the offline shell.
8. **Below 720px height, Sign out no longer warns that it clears the cart.**
9. **No screen to change your own password.** Known from the last run; still true.
10. **A 401 while paying uses a browser alert box**, then drops to sign-in. The cart survives.
11. **"Your shelf is empty / Put your services in…"** reads wrongly for a Services shop.
12. **Whole quantities only at the till.** A decimal quantity cannot be typed.
13. **Two shops are named "Jovis Motorcycle Spares"** in the dev database. Know which is real before any cleanup.
14. **`GET /api/organizations` returns whole shop records to any member**, cashiers included.
15. **`GET /api/auth/device` is anonymous and unlimited** and does a database lookup; a cheap way to load the server.

## Left behind

- **Local QA stack `salioqa`:** still running. Till at `https://localhost:8443`; its Caddy listens on all interfaces (8080 and 8443). Remove with `cd /c/Users/charl/Desktop/salio/salio-net && docker compose --profile caddy down -v && rm .env`.
- **`salio-net/.env`:** local QA only, gitignored. Do not copy it to the server.
- **A Vite dev server on port 5173** (pid 22700 when last seen); not started by this run's last agent.
- **Dev database:** unchanged. Eight other test shops remain besides the two named.
- **`Salio.Api/App_Data/invoices/`:** nine test files, untouched.
