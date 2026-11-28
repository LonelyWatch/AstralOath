CREATE TABLE IF NOT EXISTS shop_exchange (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    request_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    shop_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    offer_id VARCHAR(48) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    period_key VARCHAR(12) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    price INT UNSIGNED NOT NULL,
    material_id VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    quantity INT UNSIGNED NOT NULL,
    created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (player_id,realm_id,request_id),
    INDEX shop_exchange_quota(player_id,realm_id,shop_id,offer_id,period_key),
    FOREIGN KEY (player_id) REFERENCES arena_player(id) ON DELETE CASCADE
) ENGINE=InnoDB;
