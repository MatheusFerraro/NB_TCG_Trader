\pset pager off
\echo ============ 1. BASE COUNT ============
EXPLAIN (ANALYZE, BUFFERS)
SELECT count(*) FROM "CollectionItems" i
WHERE i."IsForSale" AND NOT i."IsPrivate";

\echo ============ 2. BASE FETCH (page 1) ============
EXPLAIN (ANALYZE, BUFFERS)
SELECT i."Id", c."Name", cs."Name", g."Name", u."DisplayName", u."City", u."Country"
FROM "CollectionItems" i
JOIN "Cards" c ON c."Id" = i."CardId"
LEFT JOIN "CardSets" cs ON cs."Id" = c."CardSetId"
JOIN "Games" g ON g."Id" = c."GameId"
JOIN "AspNetUsers" u ON u."Id" = i."UserId"
WHERE i."IsForSale" AND NOT i."IsPrivate"
ORDER BY i."CreatedAt" DESC, i."Id" DESC
LIMIT 25 OFFSET 0;

\echo ============ 3. NAME FILTER (ILIKE %char% via Cards) ============
EXPLAIN (ANALYZE, BUFFERS)
SELECT i."Id", c."Name", g."Name", u."City"
FROM "CollectionItems" i
JOIN "Cards" c ON c."Id" = i."CardId"
LEFT JOIN "CardSets" cs ON cs."Id" = c."CardSetId"
JOIN "Games" g ON g."Id" = c."GameId"
JOIN "AspNetUsers" u ON u."Id" = i."UserId"
WHERE i."IsForSale" AND NOT i."IsPrivate"
  AND c."Name" ILIKE '%char%'
ORDER BY i."CreatedAt" DESC, i."Id" DESC
LIMIT 25 OFFSET 0;

\echo ============ 4. SET FILTER (ILIKE %base% on CardSet Name/Code) ============
EXPLAIN (ANALYZE, BUFFERS)
SELECT i."Id", c."Name", cs."Name", g."Name"
FROM "CollectionItems" i
JOIN "Cards" c ON c."Id" = i."CardId"
LEFT JOIN "CardSets" cs ON cs."Id" = c."CardSetId"
JOIN "Games" g ON g."Id" = c."GameId"
JOIN "AspNetUsers" u ON u."Id" = i."UserId"
WHERE i."IsForSale" AND NOT i."IsPrivate"
  AND (cs."Name" ILIKE '%base%' OR cs."Code" ILIKE '%base%')
ORDER BY i."CreatedAt" DESC, i."Id" DESC
LIMIT 25 OFFSET 0;

\echo ============ 5. LOCATION FILTER (lower(City)=moncton) ============
EXPLAIN (ANALYZE, BUFFERS)
SELECT i."Id", c."Name", u."City"
FROM "CollectionItems" i
JOIN "Cards" c ON c."Id" = i."CardId"
JOIN "Games" g ON g."Id" = c."GameId"
JOIN "AspNetUsers" u ON u."Id" = i."UserId"
WHERE i."IsForSale" AND NOT i."IsPrivate"
  AND u."City" IS NOT NULL AND lower(u."City") = lower('Moncton')
ORDER BY i."CreatedAt" DESC, i."Id" DESC
LIMIT 25 OFFSET 0;

\echo ============ 6. PRICE + CURRENCY FILTER ============
EXPLAIN (ANALYZE, BUFFERS)
SELECT i."Id", c."Name", i."Price", i."Currency"
FROM "CollectionItems" i
JOIN "Cards" c ON c."Id" = i."CardId"
JOIN "Games" g ON g."Id" = c."GameId"
JOIN "AspNetUsers" u ON u."Id" = i."UserId"
WHERE i."IsForSale" AND NOT i."IsPrivate"
  AND i."Price" >= 10 AND i."Price" <= 100 AND i."Currency" = 'CAD'
ORDER BY i."CreatedAt" DESC, i."Id" DESC
LIMIT 25 OFFSET 0;

\echo ============ 7. DEEP PAGE (offset 4000) ============
EXPLAIN (ANALYZE, BUFFERS)
SELECT i."Id", c."Name"
FROM "CollectionItems" i
JOIN "Cards" c ON c."Id" = i."CardId"
JOIN "Games" g ON g."Id" = c."GameId"
JOIN "AspNetUsers" u ON u."Id" = i."UserId"
WHERE i."IsForSale" AND NOT i."IsPrivate"
ORDER BY i."CreatedAt" DESC, i."Id" DESC
LIMIT 25 OFFSET 4000;
