\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT count(*)::text || ':' ||
  encode(sha256(convert_to(coalesce(string_agg(h, '' ORDER BY h COLLATE "C"), ''), 'UTF8')), 'hex')
FROM (
  SELECT encode(sha256(convert_to((to_jsonb(c)-'abreviatura')::text, 'UTF8')), 'hex') AS h
  FROM catalogo.categoria c
) rows;
ROLLBACK;
