-- Perf seed for marketplace browse tuning (#65). Run in a THROWAWAY database copied
-- from a seeded dev DB (see docs/perf-marketplace-browse.md), never against real data.
-- Assumes the local catalog is seeded (#72) with contiguous Card ids (1..count) and
-- at least a handful of AspNetUsers rows. Card count is read dynamically so the script
-- survives a differently sized catalog.

-- Assign cities/countries to the existing users so the location filter is exercisable.
WITH u AS (
    SELECT "Id", row_number() OVER (ORDER BY "Id") AS rn FROM "AspNetUsers"
)
UPDATE "AspNetUsers" a
SET "City" = c.city, "Country" = c.country
FROM u
JOIN (VALUES
    (0, 'Moncton',   'Canada'),
    (1, 'Ipaussu',   'Brazil'),
    (2, 'Toronto',   'Canada'),
    (3, 'Sao Paulo', 'Brazil'),
    (4, 'Halifax',   'Canada'),
    (5, 'Vancouver', 'Canada')
) AS c(idx, city, country) ON (u.rn - 1) % 6 = c.idx
WHERE a."Id" = u."Id";

-- Id-addressable seller list (0-based row_number over the users).
CREATE TEMP TABLE sellers AS
SELECT "Id" AS uid, row_number() OVER (ORDER BY "Id") - 1 AS sn FROM "AspNetUsers";

-- 6000 public listings (IsForSale AND NOT IsPrivate) spread across cards, sellers,
-- currencies, conditions, prices, and a year of CreatedAt values so ORDER BY
-- CreatedAt DESC has real work to do.
INSERT INTO "CollectionItems"
    ("UserId","CardId","Quantity","Condition","IsForSale","Price","Currency","IsPrivate","Notes","CreatedAt","UpdatedAt")
SELECT
    s.uid,
    1 + (g * 7919) % (SELECT count(*) FROM "Cards")::int,
    1 + (g % 4),
    (ARRAY['NM','LP','MP','HP','DMG'])[1 + (g % 5)],
    true,
    round((1 + (g * 13 % 500))::numeric, 2),
    (ARRAY['CAD','BRL'])[1 + (g % 2)],
    false,
    NULL,
    now() - ((g * 97 % 525600) || ' minutes')::interval,
    now()
FROM generate_series(1, 6000) g
JOIN sellers s ON s.sn = g % (SELECT count(*) FROM sellers);

-- 2000 non-public rows (private or not-for-sale) so the base predicate is selective
-- and the privacy filter has something to exclude.
INSERT INTO "CollectionItems"
    ("UserId","CardId","Quantity","Condition","IsForSale","Price","Currency","IsPrivate","Notes","CreatedAt","UpdatedAt")
SELECT
    s.uid,
    1 + (g * 6151) % (SELECT count(*) FROM "Cards")::int,
    1,
    'NM',
    (g % 2 = 0),                 -- half for-sale-but-private, half not-for-sale
    round((1 + (g * 11 % 500))::numeric, 2),
    (ARRAY['CAD','BRL'])[1 + (g % 2)],
    (g % 2 = 0),                 -- IsPrivate true when for-sale (so never public)
    NULL,
    now() - ((g * 61 % 525600) || ' minutes')::interval,
    now()
FROM generate_series(1, 2000) g
JOIN sellers s ON s.sn = g % (SELECT count(*) FROM sellers);

ANALYZE "CollectionItems";
ANALYZE "AspNetUsers";

SELECT
    count(*) FILTER (WHERE "IsForSale" AND NOT "IsPrivate") AS public_listings,
    count(*) AS total_items
FROM "CollectionItems";
