CREATE TABLE IF NOT EXISTS arena_room_message_sticker (
    message_id BIGINT NOT NULL PRIMARY KEY,
    sticker_id VARCHAR(48) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    CONSTRAINT arena_message_sticker_message_fk FOREIGN KEY (message_id) REFERENCES arena_room_message (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
