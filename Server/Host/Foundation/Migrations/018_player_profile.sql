CREATE TABLE IF NOT EXISTS player_profile (
 player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 greeting VARCHAR(50) NOT NULL DEFAULT '', season_visible BOOLEAN NOT NULL DEFAULT TRUE,
 best_visible BOOLEAN NOT NULL DEFAULT TRUE, revision BIGINT UNSIGNED NOT NULL DEFAULT 0,
 updated_at DATETIME(6) NOT NULL, PRIMARY KEY(player_id,realm_id),
 FOREIGN KEY(player_id) REFERENCES arena_player(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Existing duplicate display names are retained. An unclaimed key serializes legacy
-- identity creation against a new nickname claim without making arena_player unique.
CREATE TABLE IF NOT EXISTS entry_nickname_key (
 realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 name_key VARCHAR(24) NOT NULL, claimed BOOLEAN NOT NULL DEFAULT FALSE,
 owner_player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NULL,
 PRIMARY KEY(realm_id,name_key), UNIQUE KEY nickname_owner(realm_id,owner_player_id),
 FOREIGN KEY(owner_player_id) REFERENCES arena_player(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS player_profile_operation (
 player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 request_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 fingerprint BINARY(32) NOT NULL, receipt JSON NOT NULL,
 PRIMARY KEY(player_id,realm_id,request_id),
 FOREIGN KEY(player_id) REFERENCES arena_player(id) ON DELETE CASCADE
) ENGINE=InnoDB;
