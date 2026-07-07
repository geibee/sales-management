-- schema-snapshot.sql — 正規化スキーマダンプ (issue #9 Tier2-13)
--
-- マイグレーション適用済み DB の「観測可能なスキーマ」を 1 行 1 事実の
-- 決定的テキストとして列挙する。結果は apps/api-fsharp/schema-snapshot.txt に
-- コミットし、IntegrationTests/MigrationTests.fs が空 DB → 全マイグレーション
-- 適用後の実スキーマと diff ゲートする。マイグレーション追加の影響が
-- PR diff で見えるようにするのが目的。
--
-- 決定性の担保:
--   - DbUp のジャーナル (schemaversions) は対象外
--   - NOT NULL 由来の自動 CHECK 制約は名前が不安定なため対象外
--   - ORDER BY は COLLATE "C" 固定 (実行環境のロケール差を排除)
SELECT line
FROM (
  SELECT 'column|' || table_name || '|' || column_name || '|' || data_type
           || '|' || is_nullable || '|' || coalesce(column_default, '') AS line,
         1 AS section,
         table_name AS k1,
         lpad(ordinal_position::text, 3, '0') AS k2
    FROM information_schema.columns
   WHERE table_schema = 'public' AND table_name <> 'schemaversions'
  UNION ALL
  SELECT 'constraint|' || tc.table_name || '|' || tc.constraint_type || '|' || tc.constraint_name
           || '|' || coalesce(string_agg(kcu.column_name, ',' ORDER BY kcu.ordinal_position), ''),
         2,
         tc.table_name,
         tc.constraint_name
    FROM information_schema.table_constraints tc
    LEFT JOIN information_schema.key_column_usage kcu
      ON kcu.constraint_name = tc.constraint_name
     AND kcu.table_schema = tc.table_schema
   WHERE tc.table_schema = 'public'
     AND tc.table_name <> 'schemaversions'
     AND tc.constraint_type <> 'CHECK'
   GROUP BY tc.table_name, tc.constraint_type, tc.constraint_name
  UNION ALL
  SELECT 'index|' || tablename || '|' || indexname || '|' || indexdef,
         3,
         tablename,
         indexname
    FROM pg_indexes
   WHERE schemaname = 'public' AND tablename <> 'schemaversions'
) t
ORDER BY section, k1 COLLATE "C", k2 COLLATE "C", line COLLATE "C";
