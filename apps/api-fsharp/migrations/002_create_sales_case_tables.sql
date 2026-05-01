CREATE TABLE sales_case (
    sales_case_number_year  INTEGER NOT NULL,
    sales_case_number_month INTEGER NOT NULL,
    sales_case_number_seq   INTEGER NOT NULL,
    division_code           INTEGER NOT NULL,
    sales_date              DATE    NOT NULL,
    case_type               TEXT    NOT NULL DEFAULT 'direct',
    status                  TEXT    NOT NULL DEFAULT 'before_appraisal',
    PRIMARY KEY (sales_case_number_year, sales_case_number_month, sales_case_number_seq)
);

CREATE TABLE sales_case_lot (
    sales_case_number_year  INTEGER NOT NULL,
    sales_case_number_month INTEGER NOT NULL,
    sales_case_number_seq   INTEGER NOT NULL,
    lot_number_year         INTEGER NOT NULL,
    lot_number_location     TEXT    NOT NULL,
    lot_number_seq          INTEGER NOT NULL,
    PRIMARY KEY (sales_case_number_year, sales_case_number_month, sales_case_number_seq,
                 lot_number_year, lot_number_location, lot_number_seq),
    FOREIGN KEY (sales_case_number_year, sales_case_number_month, sales_case_number_seq)
        REFERENCES sales_case (sales_case_number_year, sales_case_number_month, sales_case_number_seq),
    FOREIGN KEY (lot_number_year, lot_number_location, lot_number_seq)
        REFERENCES lot (lot_number_year, lot_number_location, lot_number_seq)
);

CREATE TABLE appraisal (
    appraisal_number_year   INTEGER NOT NULL,
    appraisal_number_month  INTEGER NOT NULL,
    appraisal_number_seq    INTEGER NOT NULL,
    sales_case_number_year  INTEGER NOT NULL,
    sales_case_number_month INTEGER NOT NULL,
    sales_case_number_seq   INTEGER NOT NULL,
    appraisal_type          TEXT    NOT NULL DEFAULT 'normal',
    appraisal_date          DATE    NOT NULL,
    delivery_date           DATE    NOT NULL,
    sales_market            TEXT    NOT NULL,
    base_unit_price_date    TEXT    NOT NULL,
    period_adjustment_rate_date TEXT NOT NULL,
    counterparty_adjustment_rate_date TEXT NOT NULL,
    tax_excluded_estimated_total INTEGER NOT NULL,
    customer_contract_number  TEXT,
    contract_adjustment_rate NUMERIC,
    PRIMARY KEY (appraisal_number_year, appraisal_number_month, appraisal_number_seq),
    FOREIGN KEY (sales_case_number_year, sales_case_number_month, sales_case_number_seq)
        REFERENCES sales_case (sales_case_number_year, sales_case_number_month, sales_case_number_seq)
);

CREATE TABLE contract (
    contract_number_year    INTEGER NOT NULL,
    contract_number_month   INTEGER NOT NULL,
    contract_number_seq     INTEGER NOT NULL,
    appraisal_number_year   INTEGER NOT NULL,
    appraisal_number_month  INTEGER NOT NULL,
    appraisal_number_seq    INTEGER NOT NULL,
    contract_date           DATE    NOT NULL,
    person         TEXT    NOT NULL,
    customer_number         TEXT    NOT NULL,
    agent_name              TEXT,
    sales_type              INTEGER NOT NULL,
    item                    TEXT    NOT NULL,
    delivery_method         TEXT    NOT NULL,
    sales_method            INTEGER NOT NULL,
    tax_excluded_contract_amount INTEGER NOT NULL,
    consumption_tax         INTEGER NOT NULL,
    tax_excluded_payment_amount INTEGER NOT NULL,
    payment_consumption_tax INTEGER NOT NULL,
    PRIMARY KEY (contract_number_year, contract_number_month, contract_number_seq),
    FOREIGN KEY (appraisal_number_year, appraisal_number_month, appraisal_number_seq)
        REFERENCES appraisal (appraisal_number_year, appraisal_number_month, appraisal_number_seq)
);
