-- DSL の状態名「変換指示済みロット」 (domain-model-section3.md) に揃えるため、
-- 既存ロットの status='converted' を 'conversion_instructed' に書き換える。
-- 旧 'converted' は 'instruct/cancel' 一往復しかしないライフサイクルにも関わらず
-- 「Converted = 完了済」と誤訳されていた (dsl-conversion-rules.md の英訳ルールに反する)。
UPDATE lot
SET status = 'conversion_instructed'
WHERE status = 'converted';
