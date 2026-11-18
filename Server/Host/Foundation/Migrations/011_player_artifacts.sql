CREATE TABLE IF NOT EXISTS inventory_artifact (
    instance_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    template_id VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    experience INT NOT NULL DEFAULT 0,
    limit_breaks TINYINT UNSIGNED NOT NULL DEFAULT 0,
    locked BOOLEAN NOT NULL DEFAULT FALSE,
    source VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    acquisition_order BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    UNIQUE KEY inventory_artifact_order (acquisition_order),
    UNIQUE KEY inventory_artifact_owned_id (player_id, realm_id, instance_id),
    KEY inventory_artifact_owner_order (player_id, realm_id, acquisition_order),
    CONSTRAINT inventory_artifact_breaks CHECK (limit_breaks BETWEEN 0 AND 5),
    CONSTRAINT inventory_artifact_exp CHECK (experience >= 0),
    CONSTRAINT inventory_artifact_owner_fk FOREIGN KEY (player_id, realm_id)
        REFERENCES inventory_capacity(player_id, realm_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS inventory_artifact_loadout (
    hero_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    artifact_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    UNIQUE KEY inventory_artifact_equipped_once (artifact_id),
    CONSTRAINT inventory_artifact_loadout_hero_fk FOREIGN KEY (hero_id)
        REFERENCES inventory_hero(instance_id) ON DELETE CASCADE,
    CONSTRAINT inventory_artifact_loadout_owner_fk FOREIGN KEY (player_id, realm_id, artifact_id)
        REFERENCES inventory_artifact(player_id, realm_id, instance_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS inventory_artifact_material (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    material_id VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    balance INT NOT NULL DEFAULT 0,
    PRIMARY KEY (player_id, realm_id, material_id),
    CONSTRAINT inventory_artifact_material_balance CHECK (balance >= 0),
    CONSTRAINT inventory_artifact_material_owner_fk FOREIGN KEY (player_id, realm_id)
        REFERENCES inventory_capacity(player_id, realm_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS inventory_artifact_operation (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    request_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    fingerprint BINARY(32) NOT NULL,
    created_at DATETIME(6) NOT NULL,
    PRIMARY KEY (player_id, realm_id, request_id),
    CONSTRAINT inventory_artifact_operation_owner_fk FOREIGN KEY (player_id, realm_id)
        REFERENCES inventory_capacity(player_id, realm_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS inventory_artifact_material_entry (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    request_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    material_id VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    delta INT NOT NULL,
    PRIMARY KEY (player_id, realm_id, request_id, material_id),
    CONSTRAINT inventory_artifact_material_entry_operation_fk FOREIGN KEY (player_id, realm_id, request_id)
        REFERENCES inventory_artifact_operation(player_id, realm_id, request_id) ON DELETE CASCADE
) ENGINE=InnoDB;
