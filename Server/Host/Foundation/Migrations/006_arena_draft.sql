CREATE TABLE IF NOT EXISTS arena_room_draft_settings (
    room_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    preban_count TINYINT UNSIGNED NOT NULL,
    first_pick VARCHAR(8) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    ban_protection BOOLEAN NOT NULL,
    CONSTRAINT arena_draft_settings_room_fk FOREIGN KEY (room_id) REFERENCES arena_room(id) ON DELETE CASCADE,
    CONSTRAINT arena_draft_settings_preban CHECK (preban_count <= 2),
    CONSTRAINT arena_draft_settings_first_pick CHECK (first_pick IN ('host', 'guest'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS arena_room_draft (
    room_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    draft_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL UNIQUE,
    state JSON NOT NULL,
    CONSTRAINT arena_room_draft_room_fk FOREIGN KEY (room_id) REFERENCES arena_room(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS arena_battle_command (
    draft_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    sequence BIGINT UNSIGNED NOT NULL,
    side TINYINT UNSIGNED NOT NULL,
    payload JSON NOT NULL,
    PRIMARY KEY (draft_id, sequence),
    CONSTRAINT arena_battle_command_draft_fk FOREIGN KEY (draft_id) REFERENCES arena_room_draft(draft_id) ON DELETE CASCADE,
    CONSTRAINT arena_battle_command_side CHECK (side <= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
