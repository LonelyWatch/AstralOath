CREATE TABLE IF NOT EXISTS entry_account (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    user_key VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL UNIQUE,
    email VARCHAR(254) CHARACTER SET ascii COLLATE ascii_bin NOT NULL UNIQUE,
    password_salt BINARY(16) NOT NULL,
    password_hash BINARY(32) NOT NULL,
    password_iterations INT NOT NULL,
    entry_hash BINARY(32) NOT NULL UNIQUE,
    entry_expires_at DATETIME(6) NOT NULL,
    arena_expires_at DATETIME(6) NULL,
    created_at DATETIME(6) NOT NULL,
    CONSTRAINT entry_account_player_fk FOREIGN KEY (player_id) REFERENCES arena_player(id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS entry_email_challenge (
    email VARCHAR(254) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    salt BINARY(16) NOT NULL,
    digest BINARY(32) NOT NULL,
    ready BOOLEAN NOT NULL,
    attempts_left TINYINT UNSIGNED NOT NULL,
    resend_at DATETIME(6) NOT NULL,
    expires_at DATETIME(6) NOT NULL,
    INDEX entry_email_challenge_expiry (expires_at)
) ENGINE=InnoDB;
