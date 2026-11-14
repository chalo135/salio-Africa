-- =====================================================================
-- reset-shop-data.sql
--
-- Empties ONE shop's trading data so it can start again:
--   deleted: journal lines, journal entries (this includes expenses,
--            drawings, stock receipts, stock counts, opening stock and
--            customer payments, which are all journal entries), stock
--            movements, payments, sale lines, sales, customers, products.
--   kept:    the organisation, its chart of accounts, its tax rates, its
--            shifts, its filed returns (there can be none: see step 2),
--            setup codes, every user, membership and trusted phone.
--
-- Written for PostgreSQL 16. It is NOT run by anyone but you.
--
-- HOW TO RUN (stop the API first, so nothing writes while this runs):
--   1. Put the shop's exact name, or its id, in step 1 below.
--   2. docker exec -i salio-db psql -U salio -d salio -v ON_ERROR_STOP=1 < reset-shop-data.sql
--
-- ON_ERROR_STOP=1 matters: without it psql carries on after an error.
-- With it, any error stops the script before COMMIT and nothing is deleted.
--
-- This breaks locked rule 5 (stock movements are never deleted) on purpose,
-- for one shop, by hand. It is a reset, not something the app ever does.
-- =====================================================================

-- Everything between BEGIN and COMMIT happens together or not at all.
-- If any step fails, PostgreSQL throws the whole lot away (a rollback).
BEGIN;


-- ---------------------------------------------------------------------
-- Step 1. THE ONE SHOP. Change only the text between the quotes.
--
-- Type the shop's exact name (as in "Organizations"."Name") or its id.
-- The placeholder below matches no shop, so running the file unchanged
-- deletes nothing: step 2 refuses.
-- ---------------------------------------------------------------------
DROP VIEW  IF EXISTS pg_temp.reset_counts;
DROP TABLE IF EXISTS pg_temp.reset_scope;

CREATE TEMP TABLE reset_scope (shop text NOT NULL, org_id uuid);

INSERT INTO reset_scope (shop) VALUES ('PUT THE SHOP NAME OR ID HERE');


-- ---------------------------------------------------------------------
-- Step 2. Safety checks. Each RAISE EXCEPTION stops the script, and the
-- BEGIN above means nothing has been or will be deleted.
--
-- a) The text must match exactly ONE shop. Zero means a typo; two or
--    more means two shops share that name, so use the id instead.
--    A blank or missing value can never match "all shops", because every
--    delete below is limited to the single id found here.
-- b) The shop must have no filed tax return. Once a return is filed with
--    KRA, the books behind it must never disappear.
-- ---------------------------------------------------------------------
DO $$
DECLARE
    wanted  text := (SELECT shop FROM reset_scope);
    matches int;
    found   uuid;
BEGIN
    SELECT count(*) INTO matches
    FROM "Organizations"
    WHERE "Name" = wanted OR "Id"::text = lower(wanted);

    IF matches = 0 THEN
        RAISE EXCEPTION 'No shop is called or has the id "%". Nothing deleted.', wanted;
    END IF;

    IF matches > 1 THEN
        RAISE EXCEPTION '% shops are called "%". Use the shop''s id instead. Nothing deleted.', matches, wanted;
    END IF;

    SELECT "Id" INTO found
    FROM "Organizations"
    WHERE "Name" = wanted OR "Id"::text = lower(wanted);

    IF EXISTS (SELECT 1 FROM "FiledReturns" WHERE "OrganizationId" = found) THEN
        RAISE EXCEPTION 'This shop has a filed tax return, so its books must be kept. Nothing deleted.';
    END IF;

    -- Remember the id. Every delete below uses it, and only it.
    UPDATE reset_scope SET org_id = found;

    RAISE NOTICE 'Resetting shop % (%)', wanted, found;
END $$;


-- ---------------------------------------------------------------------
-- Step 3. A saved question: "how many rows does this shop have in each
-- table?" Asked once before the deletes and once after.
-- ---------------------------------------------------------------------
CREATE TEMP VIEW reset_counts AS
SELECT 'JournalLines' AS table_name, count(*) AS row_count FROM "JournalLines" WHERE "OrganizationId" = (SELECT org_id FROM reset_scope)
UNION ALL SELECT 'JournalEntries',         count(*) FROM "JournalEntries" WHERE "OrganizationId" = (SELECT org_id FROM reset_scope)
UNION ALL SELECT 'StockMovements',         count(*) FROM "StockMovements" WHERE "OrganizationId" = (SELECT org_id FROM reset_scope)
UNION ALL SELECT 'Payments',               count(*) FROM "Payments"       WHERE "OrganizationId" = (SELECT org_id FROM reset_scope)
UNION ALL SELECT 'SaleLines',              count(*) FROM "SaleLines"      WHERE "OrganizationId" = (SELECT org_id FROM reset_scope)
UNION ALL SELECT 'Sales',                  count(*) FROM "Sales"          WHERE "OrganizationId" = (SELECT org_id FROM reset_scope)
UNION ALL SELECT 'Customers',              count(*) FROM "Customers"      WHERE "OrganizationId" = (SELECT org_id FROM reset_scope)
UNION ALL SELECT 'Products',               count(*) FROM "Products"       WHERE "OrganizationId" = (SELECT org_id FROM reset_scope)
UNION ALL SELECT 'Accounts (kept)',        count(*) FROM "Accounts"       WHERE "OrganizationId" = (SELECT org_id FROM reset_scope)
UNION ALL SELECT 'TaxRates (kept)',        count(*) FROM "TaxRates"       WHERE "OrganizationId" = (SELECT org_id FROM reset_scope)
UNION ALL SELECT 'Shifts (kept)',          count(*) FROM "Shifts"         WHERE "OrganizationId" = (SELECT org_id FROM reset_scope)
UNION ALL SELECT 'Memberships (kept)',     count(*) FROM "Memberships"    WHERE "OrganizationId" = (SELECT org_id FROM reset_scope);

-- BEFORE
SELECT 'before' AS moment, * FROM reset_counts;


-- ---------------------------------------------------------------------
-- Step 4. The deletes, children before parents.
--
-- Every foreign key in Salio is RESTRICT: the database refuses to delete
-- a row that another row still points at. So the row that points goes
-- first:
--   JournalLines   point at JournalEntries, Accounts and Customers
--   StockMovements point at Products
--   Payments       point at Sales
--   SaleLines      point at Sales and Products
--   Sales          point at Customers (and at AppUsers, which we keep)
-- Customers and Products point at nothing we delete, so they go last.
--
-- Expenses have no table of their own: an expense is a journal entry
-- with its lines. So do drawings, stock receipts, stock counts, opening
-- stock and customer payments. The first two deletes remove them all.
-- ---------------------------------------------------------------------
DELETE FROM "JournalLines"   WHERE "OrganizationId" = (SELECT org_id FROM reset_scope);
DELETE FROM "JournalEntries" WHERE "OrganizationId" = (SELECT org_id FROM reset_scope);
DELETE FROM "StockMovements" WHERE "OrganizationId" = (SELECT org_id FROM reset_scope);
DELETE FROM "Payments"       WHERE "OrganizationId" = (SELECT org_id FROM reset_scope);
DELETE FROM "SaleLines"      WHERE "OrganizationId" = (SELECT org_id FROM reset_scope);
DELETE FROM "Sales"          WHERE "OrganizationId" = (SELECT org_id FROM reset_scope);
DELETE FROM "Customers"      WHERE "OrganizationId" = (SELECT org_id FROM reset_scope);
DELETE FROM "Products"       WHERE "OrganizationId" = (SELECT org_id FROM reset_scope);


-- ---------------------------------------------------------------------
-- Step 5. AFTER. Every row must show 0 except the four marked (kept).
-- Compare with BEFORE: the kept rows must be unchanged.
-- ---------------------------------------------------------------------
SELECT 'after' AS moment, * FROM reset_counts;


-- Make it permanent.
COMMIT;
