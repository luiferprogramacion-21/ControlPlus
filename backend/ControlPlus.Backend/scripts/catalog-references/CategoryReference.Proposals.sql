\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
WITH proposed AS (
  SELECT id, nombre,
         upper(left(regexp_replace(nombre, '[^A-Za-z]', '', 'g'), 3)) AS abbreviation
  FROM catalogo.categoria
), checked AS (
  SELECT *, count(*) OVER (PARTITION BY abbreviation) AS proposal_count
  FROM proposed
)
SELECT json_build_object('id', id, 'name', nombre, 'proposal', abbreviation,
  'status', CASE WHEN nombre !~ '^[ -~]+$' OR length(abbreviation) < 2 THEN 'REVIEW'
                 WHEN proposal_count > 1 THEN 'COLLISION' ELSE 'PROPOSED' END)
FROM checked ORDER BY nombre, id;
ROLLBACK;
