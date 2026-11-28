CREATE TABLE IF NOT EXISTS arena_tower_battle (
    id CHAR(38) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    request_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    stage SMALLINT UNSIGNED NOT NULL,
    seed BIGINT UNSIGNED NOT NULL,
    rules_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    allies JSON NOT NULL,
    defense JSON NOT NULL,
    outcome TINYINT UNSIGNED NOT NULL DEFAULT 0,
    revision BIGINT UNSIGNED NOT NULL DEFAULT 0,
    created_at DATETIME(6) NOT NULL,
    UNIQUE KEY tower_challenge_request (player_id, realm_id, request_id),
    CONSTRAINT tower_battle_owner_fk FOREIGN KEY (player_id) REFERENCES arena_player(id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS arena_tower_command (
    battle_id CHAR(38) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    sequence BIGINT UNSIGNED NOT NULL,
    payload JSON NOT NULL,
    PRIMARY KEY (battle_id, sequence),
    CONSTRAINT tower_command_battle_fk FOREIGN KEY (battle_id) REFERENCES arena_tower_battle(id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS arena_tower_clear (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    stage SMALLINT UNSIGNED NOT NULL,
    first_battle_id CHAR(38) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    credited_points INT UNSIGNED NOT NULL,
    cleared_at DATETIME(6) NOT NULL,
    PRIMARY KEY (player_id, realm_id, stage),
    CONSTRAINT tower_clear_battle_fk FOREIGN KEY (first_battle_id) REFERENCES arena_tower_battle(id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS arena_tower_wallet (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    currency_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    balance INT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (player_id, realm_id, currency_id),
    CONSTRAINT tower_wallet_owner_fk FOREIGN KEY (player_id) REFERENCES arena_player(id) ON DELETE CASCADE
) ENGINE=InnoDB;
