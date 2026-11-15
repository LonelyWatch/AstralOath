CREATE TABLE IF NOT EXISTS arena_room_draft_ready (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    CONSTRAINT arena_draft_ready_member_fk FOREIGN KEY (player_id) REFERENCES arena_room_member (player_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS arena_room_message_notice (
    message_id BIGINT NOT NULL PRIMARY KEY,
    notice_code VARCHAR(48) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    CONSTRAINT arena_message_notice_message_fk FOREIGN KEY (message_id) REFERENCES arena_room_message (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
