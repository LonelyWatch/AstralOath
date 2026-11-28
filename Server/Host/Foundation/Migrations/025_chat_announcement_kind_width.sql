-- The existing chat message kinds fit into eight ASCII characters. The typed
-- five-star artifact announcement also uses the persisted name "artifact5".
ALTER TABLE channel_chat_message
    MODIFY COLUMN kind VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL;
