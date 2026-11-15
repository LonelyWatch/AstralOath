-- Keep the existing constraint name so retrying the atomic DDL is safe after an interruption.
ALTER TABLE arena_room_draft_settings
    DROP CHECK arena_draft_settings_preban,
    ADD CONSTRAINT arena_draft_settings_preban CHECK (preban_count <= 3);
