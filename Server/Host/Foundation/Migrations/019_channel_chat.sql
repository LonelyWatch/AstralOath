CREATE TABLE IF NOT EXISTS channel_chat_preferences (
 player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 favorite_channel SMALLINT UNSIGNED NULL,
 hero5 BOOLEAN NOT NULL DEFAULT TRUE, artifact5 BOOLEAN NOT NULL DEFAULT TRUE,
 guild_promotion BOOLEAN NOT NULL DEFAULT TRUE, general_chat BOOLEAN NOT NULL DEFAULT TRUE,
 keywords JSON NOT NULL, transparent_background BOOLEAN NOT NULL DEFAULT FALSE,
 revision BIGINT UNSIGNED NOT NULL DEFAULT 0, last_sent_at DATETIME(6) NULL,
 updated_at DATETIME(6) NOT NULL, PRIMARY KEY(player_id,realm_id),
 CONSTRAINT chat_favorite_range CHECK(favorite_channel IS NULL OR favorite_channel BETWEEN 1 AND 9999),
 FOREIGN KEY(player_id) REFERENCES arena_player(id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS channel_chat_message (
 id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
 realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 channel_no SMALLINT UNSIGNED NOT NULL,
 player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 kind VARCHAR(8) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 body VARCHAR(240) NULL, sticker_id VARCHAR(48) CHARACTER SET ascii COLLATE ascii_bin NULL,
 created_at DATETIME(6) NOT NULL,
 INDEX channel_message_cursor(realm_id,channel_no,id),
 INDEX channel_message_sender(player_id,created_at),
 CONSTRAINT channel_range CHECK(channel_no BETWEEN 1 AND 9999),
 CONSTRAINT channel_message_shape CHECK((kind='text' AND body IS NOT NULL AND sticker_id IS NULL)
   OR (kind='sticker' AND body IS NULL AND sticker_id IS NOT NULL)),
 FOREIGN KEY(player_id) REFERENCES arena_player(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS channel_chat_operation (
 player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 request_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 fingerprint BINARY(32) NOT NULL, receipt JSON NOT NULL,
 PRIMARY KEY(player_id,realm_id,request_id),
 FOREIGN KEY(player_id) REFERENCES arena_player(id) ON DELETE CASCADE
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS channel_preferences_operation (
 player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 request_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 fingerprint BINARY(32) NOT NULL, receipt JSON NOT NULL,
 PRIMARY KEY(player_id,realm_id,request_id),
 FOREIGN KEY(player_id) REFERENCES arena_player(id) ON DELETE CASCADE
) ENGINE=InnoDB;
