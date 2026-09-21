# Salio — Claude Code context

Read this before writing any code in this repo.

## What this is

Salio is a Kenyan point-of-sale system with real double-entry accounting
underneath. Tagline: *"Sell like a till. Book like an accountant. File on your
own terms."*

A sale is simultaneously a receipt, a stock movement, and a balanced journal
entry — written in one database transaction. There is no POS-to-books sync,
because there is only one set of books. That is the entire product.

Target user: a 1–20 person Kenyan business (retail, restaurant, automotive,
services) on modest hardware and unreliable internet.

## Who you are working with

The owner is a **beginner in C# and .NET**. They are rewriting from scratch
because a previous codebase became unreadable to them. Therefore:

- **Small changes only.** One file or one concept per response.
- **Explain what you wrote**, briefly, in plain language.
- **No cleverness.** Simple, obvious code beats concise code. Every time.
- **Never write more than fits on one screen** without being asked.
- **Don't scaffold ahead.** Do not create files that were not requested.
- If a request is too large, say so and propose the first slice.

## Stack

| Layer | Choice |
|---|---|
| Runtime | .NET 10 (LTS) |
| Language | C# 14 |
| API | ASP.NET Core Web API, **controllers** (not minimal APIs) |
| ORM | EF Core 10 + Npgsql |
| Database | PostgreSQL 16 (Docker container `salio-db`) |
| Tests | xUnit |
| Frontend | React 19 + TypeScript + Vite 8 + Tailwind v4, in `web/`. No shadcn/ui |

## Project layout

```
salio-net/
├── Salio.Api/            controllers, Program.cs, appsettings.json
├── Salio.Domain/         entities + business rules. NO database code, NO EF
├── Salio.Infrastructure/ SalioDbContext, EF configuration, migrations
└── Salio.Tests/          xUnit
```

**Salio.Domain must not reference EF Core or ASP.NET.** Ledger rules have to be
testable without a database running. Do not add `using Microsoft.EntityFrameworkCore`
to anything in Domain.

## Locked rules — never violate these

1. **Money is `long`, in minor units.** KES 45.50 is stored as `4550`.
   Never `decimal`, never `double`, never `float`, anywhere, for any reason.

2. **Every table has `OrganizationId` (Guid).** Every query filters on it.
   No exceptions, including lookup tables.

3. **Only `JournalPoster.PostAsync()` writes to `JournalEntries` and
   `JournalLines`**, and it always calls `LedgerService.PostJournal()` first.
   No controller, no other service, no direct `DbContext` write. Ever.

4. **`PostJournal` throws if debits ≠ credits.** This is checked in Domain,
   before any database call.

5. **Stock movements are append-only.** Never update, never delete a
   `StockMovement` row. Corrections are new opposing movements.

6. **Reports are derived on read.** Never store a computed balance, total,
   or trial balance. Compute from journal lines every time.

7. **Guid primary keys**, generated in code, not by the database. The till must
   be able to create records offline before syncing.

8. **Two-column amounts**: `DebitMinor` and `CreditMinor`, both `long`.
   Exactly one is non-zero on any line. Never a single signed column.

## Conventions

- Entity files: one class per file, in `Salio.Domain/Entities/`
- EF configuration goes in `Salio.Infrastructure/Configurations/`, using
  `IEntityTypeConfiguration<T>` — not attributes on the entity, and not
  inline in `OnModelCreating`
- Migrations: `dotnet ef migrations add <Name> --project Salio.Infrastructure --startup-project Salio.Api`
- Timestamps are `DateTimeOffset` (UTC). Business dates are `DateOnly`.
- Async everywhere for database calls, with `CancellationToken`
- Nullable reference types are on. Respect them; don't suppress with `!`

## Compliance schema — build the columns now, use them later

eTIMS integration is deferred, but the columns ship from day one as **nullable
and unused**. Retrofitting them later would make every existing customer's
back catalogue unfiscalisable.

- Invoices/sales: `EtimsControlNumber`, `EtimsSignature`, `EtimsQr`, `EtimsStatus`
- Products: `EtimsItemClsCd`, `EtimsPkgUnitCd`, `EtimsQtyUnitCd`, `EtimsOriginCountry`
- Customers: `EtimsBranchCd`, `KraPin`
- Every sale line stores its tax band (`A`–`E`) from day one

`EtimsStatus` enum: `NotRequired, Queued, Transmitting, Transmitted, Rejected`.
Default is `NotRequired`.

## Tax rates

Never hardcode a tax rate. Rates live in a `TaxRates` table with
`EffectiveFrom` / `EffectiveTo` / `Source`. Kenyan rates are genuinely
contested between sources and change with each Finance Act.

## What not to do

- Don't add packages without being asked
- Don't add logging frameworks, MediatR, AutoMapper, or any
  architectural pattern that wasn't requested
- Authentication is Stage 7 and gets its own rules when that stage starts.
  Until then, add no authentication code, packages or configuration.
- Don't write repository interfaces over EF Core. `DbContext` is the repository
- Don't create `Class1.cs` leftovers; delete them
- Don't refactor code that wasn't part of the request
- Don't write the frontend until the API for that feature works in Scalar
  (http://localhost:5077/scalar/v1)

## Current stage

Stage 7 — login, users and roles. Building.

The state of the work is in BUILD_LOG.md. Read it before any task.

### Stage 7 locked rules

9.  The server decides which organisation a request may touch, from the
    logged-in user. An OrganizationId sent by the client is never trusted
    on its own. If the client names an organisation, the server must
    confirm the user is a member of it before anything else happens.
10. A user may belong to more than one organisation. Membership is its
    own table: user, organisation, role. A user with no membership row
    for an organisation can see nothing of it.
11. Passwords are never stored, only hashes produced by ASP.NET Core
    Identity's password hasher. No hand-written hashing, no hand-written
    token signing, no cryptography written in this repo by anyone.
12. A login lasts a limited time, never forever. Staff use their own
    phones, so an owner must be able to sign out every device in their
    organisation without changing other people's passwords.
13. Two roles to start: Owner and Cashier. A Cashier may sell and see
    their own sales. An Owner may do everything a Cashier can, plus see
    money, profit, stock value and tax. New roles need a decision, not a
    guess.
14. No self-signup. Organisations and their first Owner are created by
    the developer. An Owner creates their own Cashiers.

### Owner-written code — do not edit
- LedgerService.PostJournal
- StockService weighted average cost
- Any code that resolves which OrganizationId a request may touch

If a task seems to need a change to any of these, stop and say so.
