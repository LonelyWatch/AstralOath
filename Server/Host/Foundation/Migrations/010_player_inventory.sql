CREATE TABLE IF NOT EXISTS inventory_capacity (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    capacity SMALLINT UNSIGNED NOT NULL DEFAULT 1000,
    starter_granted BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (player_id, realm_id),
    CONSTRAINT inventory_capacity_range CHECK (capacity BETWEEN 3 AND 1000),
    CONSTRAINT inventory_capacity_player_fk FOREIGN KEY (player_id) REFERENCES arena_player(id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS inventory_hero (
    instance_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    template_id CHAR(5) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    level TINYINT UNSIGNED NOT NULL,
    stars TINYINT UNSIGNED NOT NULL,
    acquisition_order BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    UNIQUE INDEX inventory_hero_acquisition (acquisition_order),
    INDEX inventory_hero_owner (player_id, realm_id, acquisition_order),
    CONSTRAINT inventory_hero_level_range CHECK (level BETWEEN 1 AND 60),
    CONSTRAINT inventory_hero_star_range CHECK (stars BETWEEN 1 AND 6),
    CONSTRAINT inventory_hero_owner_fk FOREIGN KEY (player_id, realm_id)
        REFERENCES inventory_capacity(player_id, realm_id) ON DELETE CASCADE
) ENGINE=InnoDB;
