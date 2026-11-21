-- The acquisition number already uniquely identifies a hero. Keep the existing inventory ordering
-- index and make its uniqueness explicit so MySQL 8.4 can enforce ownership on equipped slots.
ALTER TABLE inventory_hero
    DROP INDEX inventory_hero_owner,
    ADD UNIQUE INDEX inventory_hero_owner (player_id, realm_id, acquisition_order);

CREATE TABLE IF NOT EXISTS inventory_equipment_capacity (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    capacity SMALLINT UNSIGNED NOT NULL DEFAULT 500,
    revision BIGINT NOT NULL DEFAULT 0,
    training_granted BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (player_id, realm_id),
    CONSTRAINT equipment_header_owner_fk FOREIGN KEY (player_id, realm_id)
        REFERENCES inventory_capacity(player_id, realm_id) ON DELETE CASCADE,
    CONSTRAINT equipment_header_bounds CHECK (capacity > 0 AND revision >= 0)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS inventory_equipment (
    instance_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    template_id VARCHAR(48) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    slot VARCHAR(12) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    set_id VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    item_level TINYINT UNSIGNED NOT NULL,
    enhance_level TINYINT UNSIGNED NOT NULL,
    grade VARCHAR(12) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    main_kind VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    main_value INT NOT NULL,
    source_ref VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    source_index SMALLINT UNSIGNED NOT NULL,
    acquisition_order BIGINT UNSIGNED NOT NULL AUTO_INCREMENT UNIQUE,
    UNIQUE INDEX equipment_source_once (player_id, realm_id, source_ref, source_index),
    UNIQUE INDEX equipment_owner_slot (player_id, realm_id, instance_id, slot),
    INDEX equipment_owner_order (player_id, realm_id, acquisition_order),
    CONSTRAINT equipment_valid_slot CHECK (slot IN ('weapon','helmet','armor','necklace','ring','boots')),
    CONSTRAINT equipment_valid_level CHECK (item_level > 0 AND enhance_level <= 15 AND main_value >= 0),
    CONSTRAINT equipment_item_owner_fk FOREIGN KEY (player_id, realm_id)
        REFERENCES inventory_equipment_capacity(player_id, realm_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS inventory_equipment_substat (
    instance_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    position TINYINT UNSIGNED NOT NULL,
    kind VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    stat_value INT NOT NULL,
    PRIMARY KEY (instance_id, position),
    UNIQUE INDEX equipment_distinct_substats (instance_id, kind),
    CONSTRAINT equipment_substat_bounds CHECK (position < 4 AND stat_value >= 0),
    CONSTRAINT equipment_substat_item_fk FOREIGN KEY (instance_id)
        REFERENCES inventory_equipment(instance_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS inventory_equipment_loadout (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    hero_order BIGINT UNSIGNED NOT NULL,
    slot VARCHAR(12) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    instance_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    PRIMARY KEY (player_id, realm_id, hero_order, slot),
    UNIQUE INDEX equipment_single_wearer (instance_id),
    CONSTRAINT equipment_slot_hero_fk FOREIGN KEY (player_id, realm_id, hero_order)
        REFERENCES inventory_hero(player_id, realm_id, acquisition_order) ON DELETE CASCADE,
    CONSTRAINT equipment_slot_item_fk FOREIGN KEY (player_id, realm_id, instance_id, slot)
        REFERENCES inventory_equipment(player_id, realm_id, instance_id, slot) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS inventory_equipment_operation (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    request_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    fingerprint BINARY(32) NOT NULL,
    receipt JSON NOT NULL,
    created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (player_id, realm_id, request_id),
    CONSTRAINT equipment_operation_owner_fk FOREIGN KEY (player_id, realm_id)
        REFERENCES inventory_equipment_capacity(player_id, realm_id) ON DELETE CASCADE
) ENGINE=InnoDB;
