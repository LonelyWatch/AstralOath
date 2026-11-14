CREATE TABLE IF NOT EXISTS arena_room_member_lease (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    expires_at DATETIME(6) NOT NULL,
    INDEX arena_member_lease_expiration (expires_at),
    CONSTRAINT arena_member_lease_player_fk FOREIGN KEY (player_id) REFERENCES arena_room_member (player_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO arena_room_member_lease (player_id, expires_at)
SELECT m.player_id, TIMESTAMPADD(SECOND, 60, UTC_TIMESTAMP(6))
FROM arena_room_member m LEFT JOIN arena_room_member_lease l ON l.player_id = m.player_id
WHERE l.player_id IS NULL;
