CREATE INDEX IF NOT EXISTS idx_lot_status ON lot(status);
CREATE INDEX IF NOT EXISTS idx_sales_case_status ON sales_case(status);
CREATE INDEX IF NOT EXISTS idx_sales_case_case_type ON sales_case(case_type);
