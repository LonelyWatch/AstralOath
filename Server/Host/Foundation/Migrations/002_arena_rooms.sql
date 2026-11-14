CREATE TABLE IF NOT EXISTS arena_player (
    id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    token_hash BINARY(32) NOT NULL UNIQUE,
    display_name VARCHAR(24) NOT NULL,
    avatar_id CHAR(5) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at DATETIME(6) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS arena_room (
    id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    host_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    name VARCHAR(28) NOT NULL,
    password_salt BINARY(16) NULL,
    password_hash BINARY(32) NULL,
    allow_chat BOOLEAN NOT NULL,
    rule VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at DATETIME(6) NOT NULL,
    INDEX arena_room_recent (created_at, id),
    CONSTRAINT arena_room_host_fk FOREIGN KEY (host_id) REFERENCES arena_player(id),
    CONSTRAINT arena_room_password_pair CHECK ((password_hash IS NULL) = (password_salt IS NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS arena_room_member (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    room_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    joined_at DATETIME(6) NOT NULL,
    INDEX arena_room_members (room_id, joined_at, player_id),
    CONSTRAINT arena_member_room_fk FOREIGN KEY (room_id) REFERENCES arena_room(id) ON DELETE CASCADE,
    CONSTRAINT arena_member_player_fk FOREIGN KEY (player_id) REFERENCES arena_player(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS arena_room_message (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    room_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    body VARCHAR(240) NOT NULL,
    created_at DATETIME(6) NOT NULL,
    INDEX arena_message_cursor (room_id, id),
    CONSTRAINT arena_message_room_fk FOREIGN KEY (room_id) REFERENCES arena_room(id) ON DELETE CASCADE,
    CONSTRAINT arena_message_player_fk FOREIGN KEY (player_id) REFERENCES arena_player(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
