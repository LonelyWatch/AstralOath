-- This single atomic ALTER is restartable if the migration receipt was interrupted.
SET @announcement_exists = (SELECT COUNT(*) FROM information_schema.columns
    WHERE table_schema=DATABASE() AND table_name='channel_chat_message' AND column_name='template_id');
SET @announcement_ddl = IF(@announcement_exists>0,'SELECT 1',
    'ALTER TABLE channel_chat_message DROP CHECK channel_message_shape,
     ADD COLUMN template_id VARCHAR(48) CHARACTER SET ascii COLLATE ascii_bin NULL,
     ADD CONSTRAINT channel_message_shape_v2 CHECK (
         (kind=''text'' AND body IS NOT NULL AND sticker_id IS NULL AND template_id IS NULL)
         OR (kind=''sticker'' AND body IS NULL AND sticker_id IS NOT NULL AND template_id IS NULL)
         OR (kind IN (''hero5'',''artifact5'') AND body IS NULL AND sticker_id IS NULL AND template_id IS NOT NULL))');
PREPARE announcement_change FROM @announcement_ddl;
EXECUTE announcement_change;
DEALLOCATE PREPARE announcement_change;
