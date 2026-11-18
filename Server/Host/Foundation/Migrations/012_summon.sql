CREATE TABLE IF NOT EXISTS summon_wallet (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    bookmarks INT UNSIGNED NOT NULL,
    epic_bookmarks INT UNSIGNED NOT NULL DEFAULT 0,
    free_period DATE NULL,
    wishlist JSON NULL,
    PRIMARY KEY (player_id, realm_id),
    CONSTRAINT summon_wallet_owner_fk FOREIGN KEY (player_id) REFERENCES arena_player(id) ON DELETE CASCADE,
    CONSTRAINT summon_wallet_balance_range CHECK (bookmarks <= 1000000000 AND epic_bookmarks <= 1000000000)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS summon_pity (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    banner_id VARCHAR(48) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    misses INT UNSIGNED NOT NULL,
    PRIMARY KEY (player_id, realm_id, banner_id),
    CONSTRAINT summon_pity_owner_fk FOREIGN KEY (player_id, realm_id) REFERENCES summon_wallet(player_id, realm_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS summon_receipt (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    request_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    issue_order BIGINT UNSIGNED NOT NULL AUTO_INCREMENT UNIQUE,
    receipt JSON NOT NULL,
    PRIMARY KEY (player_id, realm_id, request_id),
    INDEX summon_receipt_latest (player_id, realm_id, issue_order),
    CONSTRAINT summon_receipt_owner_fk FOREIGN KEY (player_id, realm_id) REFERENCES summon_wallet(player_id, realm_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS summon_wallet_entry (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    operation_id VARCHAR(48) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    currency VARCHAR(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    amount INT NOT NULL,
    balance_after INT UNSIGNED NOT NULL,
    occurred_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (player_id, realm_id, operation_id, currency),
    CONSTRAINT summon_wallet_entry_owner_fk FOREIGN KEY (player_id, realm_id) REFERENCES summon_wallet(player_id, realm_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS summon_collection (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    kind VARCHAR(12) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    template_id VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    PRIMARY KEY (player_id, realm_id, kind, template_id),
    CONSTRAINT summon_collection_owner_fk FOREIGN KEY (player_id, realm_id) REFERENCES summon_wallet(player_id, realm_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS summon_storage (
    instance_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    request_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    draw_position TINYINT UNSIGNED NOT NULL,
    added_order BIGINT UNSIGNED NOT NULL AUTO_INCREMENT UNIQUE,
    kind VARCHAR(12) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    template_id VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    stars TINYINT UNSIGNED NOT NULL,
    INDEX summon_storage_owner (player_id, realm_id, added_order),
    CONSTRAINT summon_storage_receipt_fk FOREIGN KEY (player_id, realm_id, request_id)
        REFERENCES summon_receipt(player_id, realm_id, request_id) ON DELETE CASCADE,
    CONSTRAINT summon_storage_draw UNIQUE (player_id, realm_id, request_id, draw_position),
    CONSTRAINT summon_storage_kind CHECK (kind IN ('hero', 'artifact')),
    CONSTRAINT summon_storage_stars CHECK (stars BETWEEN 3 AND 5)
) ENGINE=InnoDB;
