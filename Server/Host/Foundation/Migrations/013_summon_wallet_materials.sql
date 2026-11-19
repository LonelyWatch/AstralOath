-- A wallet without a material row owns zero of each item until its first credit.
CREATE TABLE IF NOT EXISTS summon_wallet_materials (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    gold_transmit_stones INT UNSIGNED NOT NULL DEFAULT 0,
    galaxy_bookmarks INT UNSIGNED NOT NULL DEFAULT 0,
    mystic_medals INT UNSIGNED NOT NULL DEFAULT 0,
    skystones INT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (player_id, realm_id),
    CONSTRAINT summon_wallet_materials_owner_fk FOREIGN KEY (player_id, realm_id)
        REFERENCES summon_wallet (player_id, realm_id) ON DELETE CASCADE
) ENGINE=InnoDB;
