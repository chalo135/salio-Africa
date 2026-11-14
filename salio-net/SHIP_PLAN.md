# Salio — ship plan

Written 6 November 2026. Updated 9 November 2026.

## What "shipped" means

One definition, so it can be judged:

**The client uses Salio at their counter for a full week, on their own tablet,
without Chalo present, and the books at the end of that week are right.**

Not: every feature built. Not: no bugs. Those lines never arrive.

---

## The blocking list

Eight items. Most are small. One is not, and it has been deferred for three
weeks.

### 1. Hosting — THE blocker
Salio runs on one laptop. Nothing else on this list matters until it doesn't.
Contabo VPS, Docker, Caddy with HTTPS, memory limits on every Salio container
(Jade Wears shares the box), a subdomain, and a backup script that has been
restored from at least once.
**Roughly a day. The prompt is written.**

### 2. Rotate the database password
It was pasted into a chat. On a laptop that was survivable; on a public box it
is not. Do this before step 1 finishes.
**Twenty minutes.**

### 3. Wipe the demo data, gate the seeder
The client starts with their own products or none. The seeder must stop
recreating the twelve demo parts.
**An evening. Prompted; status unconfirmed.**

### 4. Setup flow
Designed on the canvas: four steps plus the opening screen. Setup code arrives
in a link, the client chooses their own password, and it ends with parts on the
shelf rather than an empty till.
**A day. Rebuild prompted 8 November — code generation, trade-derived wording,
real progress bar, vendor admin area.**

### 5. Opening stock
A shop already owns what is on its shelves. Debit Inventory, credit Owner's
Equity — not cash. Refuse per product once that product has any movement.
**API built in Wave 1. Screen outstanding. Half a day.**

### 6. People
Owner adds staff; each sets their own password; the developer never knows one.
**A day. Migration was failing on a file lock; state unknown.**

### 7. PIN sign-in
**Done, 8 November.** `PinUnlockRules.Decide` was an unimplemented placeholder —
that one line was why every unlock returned a 500. Written and in; 102 tests
pass. Signing out and back in works. One cosmetic bug left: signing in with
email always demands a new PIN even where one exists. Prompt written.

### 8. Backups, proven
A nightly dump, kept off the box, and one restore actually performed. A backup
never restored is not a backup.
**Half a day.**

Total: roughly a focused weekend plus two days.

---

## Getting the catalogue in — decided 9 November

A till with no parts in it is useless, and typing a spares catalogue by hand is
the thing most likely to make the client give up in week one.

**What the research found.** No POS company makes the merchant type their
catalogue in. Toast has the restaurant fill a Google Sheets template and emails
it to an *Onboarding Consultant* who builds the menu and reviews it before
go-live. Lightspeed takes the file and runs the import itself. Revel and FTx
sell it as "concierge onboarding". Woflow is an entire company whose only
product is doing catalogue onboarding on behalf of POS vendors — their pitch is
that inertia kills the deal.

So the vendor does the data entry. That is the model.

**The decision, in four parts, in this order:**

1. **Don't load the whole catalogue.** The locked rule already lets the till
   sell an item it has never seen. So the catalogue builds itself at the
   counter: the first time someone asks for a part that isn't there, the
   cashier adds a name and price in ten seconds and sells it. Cost is
   backfilled at the next stock receipt. A shop stocking 1,500 lines probably
   sells a few hundred in a month — entering the rest up front is wasted work.
   This needs a companion screen, **"Parts needing a cost"**, or revenue gets
   booked with no cost of sales and profit is overstated.

2. **Chalo imports the fast movers himself** for this client, from whatever
   list exists — notebook, supplier invoice, photo. This week. It is the Toast
   model and it is billable as setup.

3. **A CSV import, built the Loyverse way.** This is the feature that serves
   client #2 onwards. Export-with-real-samples rather than a blank template;
   a preview that says how many rows will be created and how many edited;
   row/column/cell-level errors that block, warnings that don't; and — unlike
   Odoo, whose docs say imports "cannot be undone" — a one-click undo per
   import batch.

4. **Invoice OCR last, and aimed at restocking, not onboarding.** Initial load
   happens once; buying from a wholesaler happens every week forever. That is
   where the repeated typing is, and it maps onto the stock receipt and
   weighted-average cost code that already exists. Mandatory human review
   before anything is committed.

**On the scanning blocker.** The image-compression problem was self-inflicted:
a document was being treated as a photo. Deskewed, perspective-corrected,
grayscale or bitonal at ~300 DPI is both *smaller* than a compressed colour
JPEG and far more readable. The real limit is different — Kenyan motorcycle
parts wholesalers often write invoices by hand, and OCR on handwriting is poor.
Printed distributor invoices are fine. The feature will work for some suppliers
and not others, and should be built knowing that.

**Barcodes are a dead end for this shop.** Spare parts mostly don't carry retail
barcodes, and where they do there is no Kenyan catalogue to look them up in.
Square's barcode auto-fill works because it is backed by a US/EU consumer-goods
database. Useful later at the till for oils and branded bulbs; useless for
building a catalogue.

**The accounting rule that must not be lost:** an imported row carrying a
quantity must also carry a unit cost. Stock accepted at zero cost makes cost of
sales zero and overstates profit. And neither the import nor the invoice reader
may write its own journal or stock logic — both call the existing stock receipt
service, which owns weighted-average cost, and `PostJournal`.

---

## Deliberately not in v1

Each has a workaround for one week.

| Not built | Workaround |
|---|---|
| Returns | Note on paper; record as a correcting entry later |
| Supplier payments | Do not use Credit on stock receipts |
| Password reset | Chalo is reachable if someone is locked out |
| Counting stock | Data is new; it will not drift in a week |
| Offline selling | Find out how often the connection actually drops first |
| Service businesses | Now supported in setup — Sales or Services, wording derived from trade |

---

## Only Chalo can do these

No agent, no prompt.

- **Confirm eTIMS with KRA.** Whether a non-VAT shop must issue eTIMS
  invoices. If it must, shipping to a paying client without it exposes *them*,
  not just Salio. This is the single unresolved item with legal weight and it
  has been open for weeks.
- Rotate the database password.
- Deploy, run migrations, hold the DNS.
- Decide the price and what is promised.
- Import the client's fast-moving parts before day one.
- Sit with the client on day one and watch.

---

## Risks being carried into week one

- **Nobody has used Salio in a shop.** Every design decision so far came from
  reasoning, not watching. Expect the first day to surface things no amount of
  planning would have.
- **The books have never run a full month on real data.** The tax endpoints
  have never been called with anything real; the period close has never been
  tried.
- **Customer credit is built and unverified.** Sell on credit, take a payment,
  check the balance and the trial balance — before the client does.
- **One shared account, if People is not finished.** Acceptable for a
  supervised week; not beyond it.
- **Quick-add at the counter can book revenue with no cost** if the "parts
  needing a cost" list is ignored. Check it at the end of week one before
  reading any profit figure.

---

## Sequence

**Before the client sees it**
1. Rotate the password
2. Host it, with backups, and restore one
3. Wipe demo data, gate the seeder
4. Verify credit, the tax endpoints, and the period close on an empty month

**Before they use it alone**
5. Setup flow
6. Opening stock screen
7. Quick-add at the counter, and the parts-needing-a-cost list
8. CSV import — then Chalo imports the client's fast movers
9. People
10. PIN — done; the email-sign-in bug outstanding

**Week one**
Chalo present day one. Write down every hesitation and every question. That
list, not this one, decides what gets built next.
