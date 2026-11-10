CREATE TABLE IF NOT EXISTS foundation_echo (
    sequence BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    request_id VARBINARY(64) NOT NULL,
    protocol_version INT UNSIGNED NOT NULL,
    message BLOB NOT NULL,
    PRIMARY KEY (sequence),
    UNIQUE KEY uq_foundation_echo_request_id (request_id)
) ENGINE=InnoDB;
