CREATE TABLE IF NOT EXISTS game_operation_audit (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    operation VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    actor_type VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    actor_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    subject_type VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NULL,
    subject_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
    client_event_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NULL,
    occurred_at DATETIME(6) NOT NULL,
    UNIQUE KEY game_operation_client_event (actor_type, actor_id, client_event_id),
    INDEX game_operation_subject (subject_type, subject_id, id),
    INDEX game_operation_time (occurred_at, id),
    CONSTRAINT game_operation_subject_pair CHECK ((subject_type IS NULL) = (subject_id IS NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
