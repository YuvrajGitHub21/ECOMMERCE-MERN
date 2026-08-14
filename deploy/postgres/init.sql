-- Runs once, on an empty data volume, before anything else touches the database.
-- Extensions must exist before EF Core's first migration, because the catalogue
-- schema declares a generated tsvector column and a trigram index on top of them.
--
-- To re-run: docker compose down -v && docker compose up -d

-- Full-text search ranking helpers and trigram similarity, used together:
-- tsvector/GIN handles the normal query path, pg_trgm provides the typo-tolerant
-- fallback so "amool" still finds "Amul". See ADR-0009.
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- Accent-insensitive matching for product names.
CREATE EXTENSION IF NOT EXISTS unaccent;

-- Case-insensitive text, used for email addresses so that Foo@x.com and
-- foo@x.com cannot become two accounts (a defect in the legacy schema).
CREATE EXTENSION IF NOT EXISTS citext;

DO $$
BEGIN
    RAISE NOTICE 'GroceryEasy: extensions pg_trgm, unaccent, citext installed.';
END
$$;
