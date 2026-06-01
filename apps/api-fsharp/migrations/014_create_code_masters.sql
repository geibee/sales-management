-- コード値マスタ（事業部→部→課の階層、工程/検査/製造のフラット）。
-- lot.* との外部キーは張らない（既存ロットが未登録コードを持ち得るため。未登録は名称 null で返す）。

CREATE TABLE master_division (
    code INTEGER NOT NULL,
    name TEXT    NOT NULL,
    PRIMARY KEY (code)
);

CREATE TABLE master_department (
    code          INTEGER NOT NULL,
    name          TEXT    NOT NULL,
    division_code INTEGER NOT NULL,
    PRIMARY KEY (code),
    FOREIGN KEY (division_code) REFERENCES master_division (code)
);

CREATE TABLE master_section (
    code            INTEGER NOT NULL,
    name            TEXT    NOT NULL,
    department_code INTEGER NOT NULL,
    PRIMARY KEY (code),
    FOREIGN KEY (department_code) REFERENCES master_department (code)
);

CREATE TABLE master_process_category (
    code INTEGER NOT NULL,
    name TEXT    NOT NULL,
    PRIMARY KEY (code)
);

CREATE TABLE master_inspection_category (
    code INTEGER NOT NULL,
    name TEXT    NOT NULL,
    PRIMARY KEY (code)
);

CREATE TABLE master_manufacturing_category (
    code INTEGER NOT NULL,
    name TEXT    NOT NULL,
    PRIMARY KEY (code)
);

-- seed: 既存の開発データ（division=1, department=10, section=100, 工程/検査/製造=1）を必ず含める。
INSERT INTO master_division (code, name) VALUES
    (1, '第一事業部'),
    (2, '第二事業部');

INSERT INTO master_department (code, name, division_code) VALUES
    (10, '営業部', 1),
    (11, '製造部', 1),
    (20, '管理部', 2);

INSERT INTO master_section (code, name, department_code) VALUES
    (100, '第一営業課', 10),
    (101, '第二営業課', 10),
    (110, '製造一課', 11),
    (200, '総務課', 20);

INSERT INTO master_process_category (code, name) VALUES
    (1, '通常工程'),
    (2, '特急工程');

INSERT INTO master_inspection_category (code, name) VALUES
    (1, '標準検査'),
    (2, '全数検査');

INSERT INTO master_manufacturing_category (code, name) VALUES
    (1, '量産'),
    (2, '試作');
