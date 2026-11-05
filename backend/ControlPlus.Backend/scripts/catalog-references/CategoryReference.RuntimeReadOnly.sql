\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT json_build_object('kind','context','database',current_database(),
 'username',current_user,'readOnly',current_setting('transaction_read_only'),
 'targetCount',(SELECT count(*) FROM public."__EFMigrationsHistory"
   WHERE "MigrationId"='20261102020000_CategoryProductReferencesV1'));
SELECT json_build_object('kind','category','id',id,'abbreviation',abreviatura)
FROM catalogo.categoria WHERE activo ORDER BY id;
SELECT json_build_object('kind','alias','code',c.codigo,'productId',p.id,
 'reference',p.codigo_interno,'barcode',p.codigo_barras,'active',p.activo)
FROM catalogo.producto_codigo c JOIN catalogo.producto p ON p.id=c.producto_id
WHERE c.codigo<>p.codigo_interno ORDER BY c.codigo COLLATE "C";
ROLLBACK;
