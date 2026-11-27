-- A per-channel transaction mutex makes AUTO_INCREMENT cursor order also commit
-- order within that channel. Different channels do not share this lock.
CREATE TABLE IF NOT EXISTS channel_chat_order (
 realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 channel_no SMALLINT UNSIGNED NOT NULL,
 PRIMARY KEY(realm_id,channel_no),
 CONSTRAINT channel_order_range CHECK(channel_no BETWEEN 1 AND 9999)
) ENGINE=InnoDB;
