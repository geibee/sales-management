CREATE TABLE lot_appraisal (
    appraisal_number_year         INTEGER NOT NULL,
    appraisal_number_month        INTEGER NOT NULL,
    appraisal_number_seq          INTEGER NOT NULL,
    lot_number_year               INTEGER NOT NULL,
    lot_number_location           TEXT    NOT NULL,
    lot_number_seq                INTEGER NOT NULL,
    processing_cost                INTEGER,
    individual_order_premium                 NUMERIC,
    grade_premium             NUMERIC,
    reservation_addon           NUMERIC,
    adjustment_rate               NUMERIC,
    quality_adjustment_rate       NUMERIC,
    manufacturing_cost_unit_price INTEGER,
    expected_sales_period    INTEGER,
    target_profit_rate                   NUMERIC,
    PRIMARY KEY (appraisal_number_year, appraisal_number_month, appraisal_number_seq,
                 lot_number_year, lot_number_location, lot_number_seq),
    FOREIGN KEY (appraisal_number_year, appraisal_number_month, appraisal_number_seq)
        REFERENCES appraisal (appraisal_number_year, appraisal_number_month, appraisal_number_seq)
);

CREATE TABLE lot_detail_appraisal (
    appraisal_number_year          INTEGER NOT NULL,
    appraisal_number_month         INTEGER NOT NULL,
    appraisal_number_seq           INTEGER NOT NULL,
    lot_number_year                INTEGER NOT NULL,
    lot_number_location            TEXT    NOT NULL,
    lot_number_seq                 INTEGER NOT NULL,
    detail_seq_no                  INTEGER NOT NULL,
    base_unit_price                INTEGER NOT NULL,
    period_adjustment_rate         NUMERIC NOT NULL,
    counterparty_adjustment_rate         NUMERIC NOT NULL,
    exceptional_period_adjustment_rate NUMERIC,
    PRIMARY KEY (appraisal_number_year, appraisal_number_month, appraisal_number_seq,
                 lot_number_year, lot_number_location, lot_number_seq, detail_seq_no),
    FOREIGN KEY (appraisal_number_year, appraisal_number_month, appraisal_number_seq,
                 lot_number_year, lot_number_location, lot_number_seq)
        REFERENCES lot_appraisal (appraisal_number_year, appraisal_number_month, appraisal_number_seq,
                                  lot_number_year, lot_number_location, lot_number_seq)
);
