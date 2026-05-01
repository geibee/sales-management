-- 予約価格
CREATE TABLE reservation_price (
    appraisal_number_year   INTEGER NOT NULL,
    appraisal_number_month  INTEGER NOT NULL,
    appraisal_number_seq    INTEGER NOT NULL,
    sales_case_number_year  INTEGER NOT NULL,
    sales_case_number_month INTEGER NOT NULL,
    sales_case_number_seq   INTEGER NOT NULL,
    appraisal_date          DATE    NOT NULL,
    reserved_lot_info       TEXT    NOT NULL,
    reserved_amount         INTEGER NOT NULL,
    status                  TEXT    NOT NULL DEFAULT 'provisional',
    determined_date         DATE,
    determined_amount       INTEGER,
    PRIMARY KEY (appraisal_number_year, appraisal_number_month, appraisal_number_seq),
    FOREIGN KEY (sales_case_number_year, sales_case_number_month, sales_case_number_seq)
        REFERENCES sales_case (sales_case_number_year, sales_case_number_month, sales_case_number_seq)
);

-- 委託業者情報
CREATE TABLE consignment_info (
    sales_case_number_year  INTEGER NOT NULL,
    sales_case_number_month INTEGER NOT NULL,
    sales_case_number_seq   INTEGER NOT NULL,
    consignor_name          TEXT    NOT NULL,
    consignor_code          TEXT    NOT NULL,
    designated_date         DATE    NOT NULL,
    PRIMARY KEY (sales_case_number_year, sales_case_number_month, sales_case_number_seq),
    FOREIGN KEY (sales_case_number_year, sales_case_number_month, sales_case_number_seq)
        REFERENCES sales_case (sales_case_number_year, sales_case_number_month, sales_case_number_seq)
);

-- 委託販売結果
CREATE TABLE consignment_result (
    sales_case_number_year  INTEGER NOT NULL,
    sales_case_number_month INTEGER NOT NULL,
    sales_case_number_seq   INTEGER NOT NULL,
    result_date             DATE    NOT NULL,
    result_amount           INTEGER NOT NULL,
    PRIMARY KEY (sales_case_number_year, sales_case_number_month, sales_case_number_seq),
    FOREIGN KEY (sales_case_number_year, sales_case_number_month, sales_case_number_seq)
        REFERENCES sales_case (sales_case_number_year, sales_case_number_month, sales_case_number_seq)
);

-- sales_case.case_type の値域を 'direct' / 'reservation' / 'consignment' に制限
ALTER TABLE sales_case ADD CONSTRAINT chk_case_type
    CHECK (case_type IN ('direct', 'reservation', 'consignment'));
