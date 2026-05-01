ALTER TABLE sales_case
    ADD COLUMN shipping_instruction_date DATE,
    ADD COLUMN shipping_completed_date   DATE;

ALTER TABLE contract
    ADD COLUMN payment_deferral_condition TEXT,
    ADD COLUMN payment_deferral_amount    INTEGER,
    ADD COLUMN usage_                     TEXT;
