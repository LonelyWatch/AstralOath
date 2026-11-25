ALTER TABLE inventory_equipment
    ADD COLUMN experience BIGINT NOT NULL DEFAULT 0,
    ADD COLUMN enhancement_profile_id VARCHAR(120) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ADD CONSTRAINT equipment_experience_bounds CHECK (experience >= 0);

-- Existing development approximations intentionally retain a NULL profile. They are never
-- silently converted, reduced to a new main stat, or treated as source-authenticated gear.
CREATE TABLE IF NOT EXISTS inventory_equipment_enhancement_roll (
    player_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    realm_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    request_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    instance_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    milestone TINYINT UNSIGNED NOT NULL,
    kind VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    before_value INT NOT NULL,
    after_value INT NOT NULL,
    added BOOLEAN NOT NULL,
    PRIMARY KEY (player_id, realm_id, request_id, milestone),
    CONSTRAINT equipment_roll_milestone CHECK (milestone IN (3,6,9,12,15)),
    CONSTRAINT equipment_roll_values CHECK (before_value >= 0 AND after_value >= before_value),
    CONSTRAINT equipment_roll_operation_fk FOREIGN KEY (player_id, realm_id, request_id)
        REFERENCES inventory_equipment_operation(player_id, realm_id, request_id) ON DELETE CASCADE,
    CONSTRAINT equipment_roll_item_fk FOREIGN KEY (instance_id)
        REFERENCES inventory_equipment(instance_id) ON DELETE CASCADE
) ENGINE=InnoDB;
