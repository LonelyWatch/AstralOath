CREATE TABLE IF NOT EXISTS player_realm_membership (
 realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 first_entered_at DATETIME(6) NOT NULL, last_entered_at DATETIME(6) NOT NULL,
 registered BOOLEAN NOT NULL, PRIMARY KEY(realm_id,player_id),
 FOREIGN KEY(player_id) REFERENCES arena_player(id) ON DELETE CASCADE
) ENGINE=InnoDB;
INSERT INTO player_realm_membership(realm_id,player_id,first_entered_at,last_entered_at,registered)
 SELECT 'undying-memory',player_id,created_at,created_at,TRUE FROM entry_account WHERE arena_expires_at IS NOT NULL
 ON DUPLICATE KEY UPDATE registered=TRUE;
CREATE TABLE IF NOT EXISTS mailbox (
 realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 version BIGINT NOT NULL DEFAULT 0, PRIMARY KEY(realm_id,player_id),
 FOREIGN KEY(player_id) REFERENCES arena_player(id) ON DELETE CASCADE
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS mail_preview (
 preview_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 operator_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 scope VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 frozen JSON NOT NULL, recipient_count INT NOT NULL, duplicate_count INT NOT NULL, excluded_count INT NOT NULL,
 created_at DATETIME(6) NOT NULL, expires_at DATETIME(6) NOT NULL
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS mail_preview_recipient (
 preview_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 PRIMARY KEY(preview_id,player_id), FOREIGN KEY(preview_id) REFERENCES mail_preview(preview_id)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS mail_batch (
 batch_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 preview_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL UNIQUE,
 expires_at DATETIME(6) NOT NULL, created_at DATETIME(6) NOT NULL,
 FOREIGN KEY(preview_id) REFERENCES mail_preview(preview_id)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS mail_batch_target (
 batch_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 outcome VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'pending',
 error_code VARCHAR(48) CHARACTER SET ascii COLLATE ascii_bin NULL,
 delivery_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NULL,
 PRIMARY KEY(batch_id,player_id), KEY mail_target_work(outcome,batch_id),
 FOREIGN KEY(batch_id) REFERENCES mail_batch(batch_id)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS mail_delivery (
 delivery_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
 realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 batch_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 delivered_at DATETIME(6) NOT NULL, expires_at DATETIME(6) NOT NULL,
 read_at DATETIME(6) NULL, history_at DATETIME(6) NULL,
 outcome VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'active',
 frozen JSON NOT NULL, claim_operation CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NULL,
 UNIQUE KEY mail_delivery_once(batch_id,realm_id,player_id),
 KEY mail_inbox(realm_id,player_id,outcome,delivered_at,delivery_id),
 KEY mail_history(realm_id,player_id,history_at,delivery_id),KEY mail_expiry(outcome,expires_at),
 FOREIGN KEY(player_id) REFERENCES arena_player(id) ON DELETE CASCADE
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS mail_operation (
 realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 request_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 fingerprint BINARY(32) NOT NULL, receipt JSON NOT NULL,
 PRIMARY KEY(realm_id,player_id,request_id),FOREIGN KEY(player_id) REFERENCES arena_player(id) ON DELETE CASCADE
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS mail_outbox (
 id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
 realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 version BIGINT NOT NULL, created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 KEY mail_outbox_owner(realm_id,player_id,id)
) ENGINE=InnoDB;
ALTER TABLE inventory_artifact MODIFY source VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL;
