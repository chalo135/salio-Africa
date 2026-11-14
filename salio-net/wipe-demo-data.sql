-- Wipe the demo shop's transactions. DEVELOPMENT DATABASE ONLY.
-- Keeps products, accounts, organisations, users and memberships.
--
-- Run with ON_ERROR_STOP so a failure stops before COMMIT:
--   docker exec -i salio-db psql -U salio -d salio -v ON_ERROR_STOP=1 < wipe-demo-data.sql

BEGIN;

-- The one place the scope is named.
CREATE TEMP TABLE wipe_scope ON COMMIT DROP AS
SELECT '00000000-0000-0000-0000-000000000001'::uuid AS org_id;

-- Refuse unless that id is the demo shop, and no return has been filed for it.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM "Organizations"
                   WHERE "Id" = (SELECT org_id FROM wipe_scope)
                     AND "Name" = 'Salio Demo Shop') THEN
        RAISE EXCEPTION 'Scope is not the Salio Demo Shop. Nothing deleted.';
    END IF;

    IF EXISTS (SELECT 1 FROM "FiledReturns"
               WHERE "OrganizationId" = (SELECT org_id FROM wipe_scope)) THEN
        RAISE EXCEPTION 'This shop has a filed return. Nothing deleted.';
    END IF;
END $$;

-- Children before parents. Every foreign key here is RESTRICT.
-- Expenses, drawings, stock receipts and customer payments have no table of
-- their own: they are journal entries, so these two deletes remove them.
DELETE FROM "JournalLines"   WHERE "OrganizationId" = (SELECT org_id FROM wipe_scope);
DELETE FROM "JournalEntries" WHERE "OrganizationId" = (SELECT org_id FROM wipe_scope);
DELETE FROM "StockMovements" WHERE "OrganizationId" = (SELECT org_id FROM wipe_scope);
DELETE FROM "SaleLines"      WHERE "OrganizationId" = (SELECT org_id FROM wipe_scope);
DELETE FROM "Payments"       WHERE "OrganizationId" = (SELECT org_id FROM wipe_scope);
DELETE FROM "Sales"          WHERE "OrganizationId" = (SELECT org_id FROM wipe_scope);

-- Customers exists only once migration CustomersAndCustomerIds is applied.
-- After JournalLines and Sales, which both point at it.
DO $$
BEGIN
    IF to_regclass('"Customers"') IS NOT NULL THEN
        EXECUTE 'DELETE FROM "Customers" WHERE "OrganizationId" = (SELECT org_id FROM wipe_scope)';
    END IF;
END $$;

-- What is left for this shop. Every count must be 0 except Products.
SELECT 'JournalLines' AS table_name, count(*) FROM "JournalLines"   WHERE "OrganizationId" = (SELECT org_id FROM wipe_scope)
UNION ALL SELECT 'JournalEntries', count(*) FROM "JournalEntries" WHERE "OrganizationId" = (SELECT org_id FROM wipe_scope)
UNION ALL SELECT 'StockMovements', count(*) FROM "StockMovements" WHERE "OrganizationId" = (SELECT org_id FROM wipe_scope)
UNION ALL SELECT 'SaleLines',      count(*) FROM "SaleLines"      WHERE "OrganizationId" = (SELECT org_id FROM wipe_scope)
UNION ALL SELECT 'Payments',       count(*) FROM "Payments"       WHERE "OrganizationId" = (SELECT org_id FROM wipe_scope)
UNION ALL SELECT 'Sales',          count(*) FROM "Sales"          WHERE "OrganizationId" = (SELECT org_id FROM wipe_scope)
UNION ALL SELECT 'Products (kept)', count(*) FROM "Products"      WHERE "OrganizationId" = (SELECT org_id FROM wipe_scope);

COMMIT;
