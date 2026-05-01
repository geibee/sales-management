-- 品目変換先情報を保存するための列追加（Converted 状態のロットでのみ NOT NULL 値が入る）
ALTER TABLE lot ADD COLUMN destination_item TEXT;
