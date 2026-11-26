-- Unknown legacy allocations remain NULL. Reads only recover counts from complete roll history;
-- no stat-value estimate or blanket backfill is safe for pre-enhanced GM equipment.
-- Reentrant if the process stops after ALTER TABLE but before recording the migration.
SET @equipment_count_column_exists = (
    SELECT COUNT(*) FROM information_schema.columns
    WHERE table_schema = DATABASE() AND table_name = 'inventory_equipment_substat' AND column_name = 'enhancement_count'
);
SET @equipment_count_ddl = IF(@equipment_count_column_exists = 0,
    'ALTER TABLE inventory_equipment_substat ADD COLUMN enhancement_count TINYINT UNSIGNED NULL, ADD CONSTRAINT equipment_substat_count_bounds CHECK (enhancement_count <= 5)',
    'DO 0');
PREPARE equipment_count_statement FROM @equipment_count_ddl;
EXECUTE equipment_count_statement;
DEALLOCATE PREPARE equipment_count_statement;
