-- Public lookup identifiers are independent of internal player IDs and credentials.
-- DatabaseMigrator initializes existing players before recording this migration.
CREATE TABLE IF NOT EXISTS player_public_identity (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    uid BIGINT UNSIGNED NOT NULL,
    UNIQUE KEY player_public_uid (uid),
    CONSTRAINT player_public_identity_player_fk FOREIGN KEY (player_id) REFERENCES arena_player(id) ON DELETE CASCADE,
    CONSTRAINT player_public_uid_bounds CHECK (uid BETWEEN 1000000000 AND 9999999999)
) ENGINE=InnoDB;
