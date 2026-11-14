-- =====================================================================
-- delete-shop.sql
--
-- Deletes ONE named shop, whole. On the live server its only job is to
-- remove the Smoke Test shop after step 11 of DEPLOY.md, so the client's
-- shop is the only one that has ever existed.
--
-- Originally: deletes the TEST shops agents left in the DEVELOPMENT database,
-- whole: the shop, its books, stock, sales, customers, products, imports,
-- supplier invoices, tax rates, accounts, memberships and setup codes,
-- and the people who are left belonging to no shop at all.
--
-- For the dev database only (the Docker container salio-db). Never run it
-- against a real shop's database: books are append-only, and this is the
-- one thing that deletes them.
--
-- IT IS A DRY RUN UNLESS YOU SAY OTHERWISE. It does every delete, prints
-- how many rows went from each table, then ROLLS BACK: nothing changes.
-- Only "commit=true" really deletes.
--
-- HOW TO RUN (PowerShell, from salio-net/; stop the API first):
--   .\delete-test-shops.ps1            dry run: prints the counts, deletes nothing
--   .\delete-test-shops.ps1 -Commit    really deletes
--
-- Or by hand:
--   docker exec -i salio-db psql -U salio -d salio -v ON_ERROR_STOP=1 -v commit=false < delete-test-shops.sql
--
-- Written for PostgreSQL 16 and psql (it uses psql's \if).
-- =====================================================================

\set ON_ERROR_STOP on

-- No "commit" given means a dry run.
\if :{?commit}
\else
  \set commit false
\endif

-- Everything between BEGIN and the COMMIT / ROLLBACK at the bottom happens
-- together or not at all. Any error stops the script and nothing is deleted.
BEGIN;



-- ---------------------------------------------------------------------
-- Step 1. THE SHOPS TO DELETE, by exact name, one per line.
--
-- To add another test shop, add ONE line like the two below, e.g.
--     ('QA4 Books Shop'),
-- Every line but the last ends with a comma.
-- ---------------------------------------------------------------------
CREATE TEMP TABLE shops_to_delete (shop_name text NOT NULL);

INSERT INTO shops_to_delete (shop_name) VALUES
    ('Smoke Test');


-- ---------------------------------------------------------------------
-- Step 2. Shops this script will NEVER delete, whatever step 1 says.
-- ---------------------------------------------------------------------
CREATE TEMP TABLE protected_shops (shop_name text NOT NULL);

-- Nothing is protected by name here: on the live server the only shop that
-- should ever be in the list above is the Smoke Test shop. The check in
-- step 3b still refuses to run unless the name matches exactly one shop,
-- so a typo deletes nothing.
INSERT INTO protected_shops (shop_name) VALUES
    ('__none__');


-- ---------------------------------------------------------------------
-- Step 3. Safety checks. Each RAISE EXCEPTION stops the script before
-- anything is deleted.
--
-- a) A protected name must not be in the list (compared without case or
--    stray spaces, so "jovis motorcycle spares " is caught too).
-- b) Each name must match exactly ONE shop. None means a typo, or it has
--    already been deleted. Two or more means two shops share the name,
--    and this script will not guess which one is meant.
-- c) Every table that has an OrganizationId must be one this script
--    knows how to empty. A table added later stops it here, rather than
--    being left behind or blocking the delete half way.
-- ---------------------------------------------------------------------
DO $$
DECLARE
    wanted  record;
    matches int;
    unknown text;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM shops_to_delete) THEN
        RAISE EXCEPTION 'The list of shops in step 1 is empty. Nothing deleted.';
    END IF;

    FOR wanted IN SELECT shop_name FROM shops_to_delete LOOP
        IF EXISTS (
            SELECT 1 FROM protected_shops p
            WHERE lower(btrim(p.shop_name)) = lower(btrim(wanted.shop_name)))
        THEN
            RAISE EXCEPTION '"%" is protected and is never deleted by this script. Take it out of step 1. Nothing deleted.',
                wanted.shop_name;
        END IF;

        SELECT count(*) INTO matches FROM "Organizations" WHERE "Name" = wanted.shop_name;

        IF matches = 0 THEN
            RAISE EXCEPTION 'No shop is called "%". Check the spelling, or take it out of step 1 if it is already gone. Nothing deleted.',
                wanted.shop_name;
        END IF;

        IF matches > 1 THEN
            RAISE EXCEPTION '% shops are called "%". This script will not guess which. Nothing deleted.',
                matches, wanted.shop_name;
        END IF;
    END LOOP;

    IF (SELECT count(*) FROM shops_to_delete) <> (SELECT count(DISTINCT shop_name) FROM shops_to_delete) THEN
        RAISE EXCEPTION 'A shop is named twice in step 1. Nothing deleted.';
    END IF;

    SELECT string_agg(c.table_name, ', ') INTO unknown
    FROM information_schema.columns c
    WHERE c.table_schema = 'public'
      AND c.column_name ILIKE '%OrganizationId'
      AND c.table_name NOT IN (
          'Accounts', 'AdminAuditLogs', 'Customers', 'FiledReturns', 'ImportBatchChanges',
          'ImportBatches', 'JournalEntries', 'JournalLines', 'Memberships', 'Payments',
          'Products', 'SaleLines', 'Sales', 'SetupCodes', 'Shifts', 'StockMovements',
          'SupplierInvoiceLines', 'SupplierInvoices', 'TaxRates');

    IF unknown IS NOT NULL THEN
        RAISE EXCEPTION 'This script does not know these tables, which also hold shop data: %. Add them to it first. Nothing deleted.',
            unknown;
    END IF;
END $$;


-- ---------------------------------------------------------------------
-- Step 4. Work out exactly what goes, before anything is deleted.
-- ---------------------------------------------------------------------

-- The shops, by id. Every delete below uses these ids and nothing else.
CREATE TEMP TABLE doomed_orgs AS
SELECT o."Id" AS org_id, o."Name" AS shop_name
FROM "Organizations" o
JOIN shops_to_delete s ON s.shop_name = o."Name";

-- The setup codes that made those shops.
CREATE TEMP TABLE doomed_codes AS
SELECT "Id" AS code_id FROM "SetupCodes" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs);

-- The people who belong to those shops today. Only these can be deleted,
-- and only if step 6 finds they belong nowhere else.
CREATE TEMP TABLE their_people AS
SELECT DISTINCT "AppUserId" AS user_id
FROM "Memberships"
WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs);

CREATE TEMP TABLE deleted_counts (step serial, table_name text, rows_deleted bigint);

SELECT 'will delete' AS what, shop_name, org_id FROM doomed_orgs ORDER BY shop_name;


-- ---------------------------------------------------------------------
-- Step 5. The deletes, children before parents. Every foreign key in
-- Salio is RESTRICT, so the row that points at another goes first.
-- Each statement also writes its row count into deleted_counts.
-- ---------------------------------------------------------------------

-- The admin's log lines about these shops and their codes. They point at
-- the shop and the code, so they must go before either can.
WITH gone AS (DELETE FROM "AdminAuditLogs"
              WHERE "TargetOrganizationId" IN (SELECT org_id FROM doomed_orgs)
                 OR "TargetSetupCodeId" IN (SELECT code_id FROM doomed_codes) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'AdminAuditLogs', count(*) FROM gone;

WITH gone AS (DELETE FROM "JournalLines" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'JournalLines', count(*) FROM gone;

WITH gone AS (DELETE FROM "JournalEntries" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'JournalEntries', count(*) FROM gone;

WITH gone AS (DELETE FROM "StockMovements" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'StockMovements', count(*) FROM gone;

WITH gone AS (DELETE FROM "Payments" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'Payments', count(*) FROM gone;

WITH gone AS (DELETE FROM "SaleLines" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'SaleLines', count(*) FROM gone;

WITH gone AS (DELETE FROM "Sales" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'Sales', count(*) FROM gone;

WITH gone AS (DELETE FROM "SupplierInvoiceLines" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'SupplierInvoiceLines', count(*) FROM gone;

WITH gone AS (DELETE FROM "SupplierInvoices" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'SupplierInvoices', count(*) FROM gone;

WITH gone AS (DELETE FROM "ImportBatchChanges" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'ImportBatchChanges', count(*) FROM gone;

WITH gone AS (DELETE FROM "ImportBatches" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'ImportBatches', count(*) FROM gone;

WITH gone AS (DELETE FROM "Customers" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'Customers', count(*) FROM gone;

WITH gone AS (DELETE FROM "Products" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'Products', count(*) FROM gone;

WITH gone AS (DELETE FROM "FiledReturns" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'FiledReturns', count(*) FROM gone;

WITH gone AS (DELETE FROM "Shifts" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'Shifts', count(*) FROM gone;

WITH gone AS (DELETE FROM "TaxRates" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'TaxRates', count(*) FROM gone;

-- An account can point at its parent account. Unhook them first, so the
-- order the accounts are deleted in does not matter.
UPDATE "Accounts" SET "ParentId" = NULL
WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) AND "ParentId" IS NOT NULL;

WITH gone AS (DELETE FROM "Accounts" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'Accounts', count(*) FROM gone;

WITH gone AS (DELETE FROM "Memberships" WHERE "OrganizationId" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'Memberships', count(*) FROM gone;

WITH gone AS (DELETE FROM "SetupCodes" WHERE "Id" IN (SELECT code_id FROM doomed_codes) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'SetupCodes', count(*) FROM gone;

WITH gone AS (DELETE FROM "Organizations" WHERE "Id" IN (SELECT org_id FROM doomed_orgs) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'Organizations', count(*) FROM gone;


-- ---------------------------------------------------------------------
-- Step 6. The people. A person is deleted only if ALL of this is true:
--   - they belonged to one of the deleted shops (their_people), and
--   - they now belong to no shop at all: not one membership row is left,
--     active or switched off, so anyone in a shop that is staying is kept,
--   - they are not a platform admin,
--   - nothing that is staying still points at them (a sale, an import, a
--     supplier invoice or an admin log line in another shop).
-- Their trusted phones go with them.
-- ---------------------------------------------------------------------
CREATE TEMP TABLE doomed_users AS
SELECT u."Id" AS user_id
FROM "AppUsers" u
WHERE u."Id" IN (SELECT user_id FROM their_people)
  AND NOT u."IsPlatformAdmin"
  AND NOT EXISTS (SELECT 1 FROM "Memberships" m      WHERE m."AppUserId" = u."Id")
  AND NOT EXISTS (SELECT 1 FROM "Sales" s            WHERE s."AppUserId" = u."Id")
  AND NOT EXISTS (SELECT 1 FROM "ImportBatches" b    WHERE b."UploadedByUserId" = u."Id" OR b."UndoneByUserId" = u."Id")
  AND NOT EXISTS (SELECT 1 FROM "SupplierInvoices" i WHERE i."UploadedByUserId" = u."Id")
  AND NOT EXISTS (SELECT 1 FROM "AdminAuditLogs" a   WHERE a."AdminUserId" = u."Id");

WITH gone AS (DELETE FROM "TrustedDevices" WHERE "AppUserId" IN (SELECT user_id FROM doomed_users) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'TrustedDevices', count(*) FROM gone;

WITH gone AS (DELETE FROM "AppUsers" WHERE "Id" IN (SELECT user_id FROM doomed_users) RETURNING 1)
INSERT INTO deleted_counts (table_name, rows_deleted) SELECT 'AppUsers', count(*) FROM gone;


-- ---------------------------------------------------------------------
-- Step 7. Check, then show the counts.
-- ---------------------------------------------------------------------
DO $$
DECLARE
    kept_people int;
BEGIN
    IF EXISTS (SELECT 1 FROM "Organizations" WHERE "Id" IN (SELECT org_id FROM doomed_orgs)) THEN
        RAISE EXCEPTION 'A shop that should be gone is still there. Nothing deleted.';
    END IF;

    IF EXISTS (SELECT 1 FROM "Organizations" o JOIN protected_shops p ON p.shop_name = o."Name"
               WHERE o."Id" IN (SELECT org_id FROM doomed_orgs)) THEN
        RAISE EXCEPTION 'A protected shop was about to be deleted. Nothing deleted.';
    END IF;

    SELECT count(*) INTO kept_people
    FROM their_people WHERE user_id NOT IN (SELECT user_id FROM doomed_users);

    RAISE NOTICE '% people from these shops were KEPT (they belong to another shop, are a platform admin, or something still points at them).',
        kept_people;
END $$;

SELECT table_name, rows_deleted FROM deleted_counts ORDER BY step;

SELECT 'shops left after this' AS what, "Name", "CreatedAt"::date AS created
FROM "Organizations" ORDER BY "CreatedAt";


-- ---------------------------------------------------------------------
-- Step 8. Keep it, or put everything back.
-- ---------------------------------------------------------------------
\if :commit
  COMMIT;
  \echo 'COMMITTED: the rows counted above are deleted.'
\else
  ROLLBACK;
  \echo 'DRY RUN: rolled back. Nothing was deleted. Run with commit=true (or .\\delete-test-shops.ps1 -Commit) to really delete.'
\endif
