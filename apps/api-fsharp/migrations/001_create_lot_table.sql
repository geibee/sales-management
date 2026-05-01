CREATE TABLE lot (
    lot_number_year     INTEGER NOT NULL,
    lot_number_location TEXT    NOT NULL,
    lot_number_seq      INTEGER NOT NULL,
    division_code       INTEGER NOT NULL,
    department_code     INTEGER NOT NULL,
    section_code        INTEGER NOT NULL,
    process_category    INTEGER NOT NULL,
    inspection_category INTEGER NOT NULL,
    manufacturing_category INTEGER NOT NULL,
    status              TEXT    NOT NULL DEFAULT 'manufacturing',
    manufacturing_completed_date DATE,
    shipping_deadline_date       DATE,
    shipped_date                 DATE,
    PRIMARY KEY (lot_number_year, lot_number_location, lot_number_seq)
);

CREATE TABLE lot_detail (
    lot_number_year     INTEGER NOT NULL,
    lot_number_location TEXT    NOT NULL,
    lot_number_seq      INTEGER NOT NULL,
    seq_no              INTEGER NOT NULL,
    item_category       TEXT    NOT NULL,
    premium_category    TEXT,
    product_category_code             TEXT    NOT NULL,
    length_spec_lower   NUMERIC NOT NULL,
    thickness_spec_lower NUMERIC NOT NULL,
    thickness_spec_upper NUMERIC NOT NULL,
    quality_grade       TEXT    NOT NULL,
    quantity_count      INTEGER NOT NULL,
    quantity_amount     NUMERIC NOT NULL,
    inspection_result_category  TEXT,
    PRIMARY KEY (lot_number_year, lot_number_location, lot_number_seq, seq_no),
    FOREIGN KEY (lot_number_year, lot_number_location, lot_number_seq)
        REFERENCES lot (lot_number_year, lot_number_location, lot_number_seq)
);
