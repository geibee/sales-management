package com.example.salesmanagement.infrastructure;

import com.example.salesmanagement.application.SalesCaseStore;
import com.example.salesmanagement.domain.LotNumber;
import com.example.salesmanagement.domain.SalesCaseNumber;
import java.time.LocalDate;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.support.TransactionTemplate;

public final class JdbcSalesCaseStore implements SalesCaseStore {
    private final JdbcTemplate jdbc;
    private final TransactionTemplate transaction;

    public JdbcSalesCaseStore(JdbcTemplate jdbc, PlatformTransactionManager transactionManager) {
        this.jdbc = jdbc;
        this.transaction = new TransactionTemplate(transactionManager);
    }

    @Override
    public Header create(String caseType, int divisionCode, LocalDate salesDate, List<LotNumber> lots) {
        return transaction.execute(status -> {
            int sequence = jdbc.queryForObject(
                    """
                    SELECT COALESCE(MAX(sales_case_number_seq), 0) + 1 FROM sales_case
                     WHERE sales_case_number_year = ? AND sales_case_number_month = ?
                    """,
                    Integer.class,
                    salesDate.getYear(),
                    salesDate.getMonthValue());
            var number = new SalesCaseNumber(salesDate.getYear(), salesDate.getMonthValue(), sequence);
            String initialStatus =
                    switch (caseType) {
                        case "direct" -> "before_appraisal";
                        case "reservation" -> "before_reservation";
                        case "consignment" -> "before_consignment";
                        default -> throw new IllegalArgumentException("Unknown caseType: " + caseType);
                    };
            jdbc.update(
                    """
                    INSERT INTO sales_case (
                      sales_case_number_year, sales_case_number_month, sales_case_number_seq,
                      division_code, sales_date, case_type, status)
                    VALUES (?, ?, ?, ?, ?, ?, ?)
                    """,
                    number.year(),
                    number.month(),
                    number.sequence(),
                    divisionCode,
                    salesDate,
                    caseType,
                    initialStatus);
            replaceLots(number, lots);
            return new Header(
                    number, caseType, initialStatus, divisionCode, salesDate, 1, lots, emptyDetails(caseType));
        });
    }

    @Override
    public Optional<Header> find(SalesCaseNumber number) {
        var rows = jdbc.query(
                """
                SELECT case_type, status, division_code, sales_date, version,
                       shipping_instruction_date, shipping_completed_date
                  FROM sales_case
                 WHERE sales_case_number_year = ? AND sales_case_number_month = ? AND sales_case_number_seq = ?
                """,
                (row, index) -> new Header(
                        number,
                        row.getString("case_type"),
                        row.getString("status"),
                        row.getInt("division_code"),
                        row.getObject("sales_date", LocalDate.class),
                        row.getInt("version"),
                        listLots(number),
                        loadDetails(
                                number,
                                row.getString("case_type"),
                                Optional.ofNullable(row.getObject("shipping_instruction_date", LocalDate.class)),
                                Optional.ofNullable(row.getObject("shipping_completed_date", LocalDate.class)))),
                number.year(),
                number.month(),
                number.sequence());
        return rows.stream().findFirst();
    }

    @Override
    public Page list(Optional<String> status, Optional<String> caseType, int limit, int offset) {
        var predicates = new ArrayList<String>();
        var filters = new ArrayList<Object>();
        status.ifPresent(value -> {
            predicates.add("status = ?");
            filters.add(value);
        });
        caseType.ifPresent(value -> {
            predicates.add("case_type = ?");
            filters.add(value);
        });
        String where = predicates.isEmpty() ? "" : " WHERE " + String.join(" AND ", predicates);
        int total = jdbc.queryForObject("SELECT COUNT(*) FROM sales_case" + where, Integer.class, filters.toArray());
        filters.add(limit);
        filters.add(offset);
        var items = jdbc
                .query(
                        """
                SELECT sales_case_number_year, sales_case_number_month, sales_case_number_seq
                  FROM sales_case
                """
                                + where
                                + """
                                 ORDER BY sales_case_number_year, sales_case_number_month,
                                          sales_case_number_seq LIMIT ? OFFSET ?
                                """,
                        (row, index) -> new SalesCaseNumber(row.getInt(1), row.getInt(2), row.getInt(3)),
                        filters.toArray())
                .stream()
                .map(this::find)
                .flatMap(Optional::stream)
                .toList();
        return new Page(items, total, limit, offset);
    }

    @Override
    public Optional<Header> changeStatus(
            SalesCaseNumber number, int expectedVersion, String expectedStatus, String newStatus) {
        int changed = jdbc.update(
                """
                UPDATE sales_case SET status = ?, version = version + 1
                 WHERE sales_case_number_year = ? AND sales_case_number_month = ? AND sales_case_number_seq = ?
                   AND status = ? AND version = ?
                """,
                newStatus,
                number.year(),
                number.month(),
                number.sequence(),
                expectedStatus,
                expectedVersion);
        return changed == 0 ? Optional.empty() : find(number);
    }

    @Override
    public Optional<Header> changeStatusWithDate(
            SalesCaseNumber number,
            int expectedVersion,
            String expectedStatus,
            String newStatus,
            DateField field,
            Optional<LocalDate> date) {
        String column =
                switch (field) {
                    case SHIPPING_INSTRUCTION -> "shipping_instruction_date";
                    case SHIPPING_COMPLETION -> "shipping_completed_date";
                };
        String sql =
                """
                UPDATE sales_case SET status = ?, %s = ?, version = version + 1
                 WHERE sales_case_number_year = ? AND sales_case_number_month = ?
                   AND sales_case_number_seq = ? AND status = ? AND version = ?
                """
                        .replace("%s", column);
        int changed = jdbc.update(
                sql,
                newStatus,
                date.orElse(null),
                number.year(),
                number.month(),
                number.sequence(),
                expectedStatus,
                expectedVersion);
        return changed == 0 ? Optional.empty() : find(number);
    }

    @Override
    public Optional<Header> saveAppraisal(
            SalesCaseNumber number, int expectedVersion, String expectedStatus, DirectAppraisal appraisal) {
        return transaction.execute(status -> {
            if (!advance(number, expectedVersion, expectedStatus, "appraised")) {
                status.setRollbackOnly();
                return Optional.empty();
            }
            if ("appraised".equals(expectedStatus)) {
                deleteAppraisalRows(number);
            }
            int sequence = nextSequence("appraisal", "appraisal_number", number);
            jdbc.update(
                    """
                    INSERT INTO appraisal (
                      appraisal_number_year, appraisal_number_month, appraisal_number_seq,
                      sales_case_number_year, sales_case_number_month, sales_case_number_seq,
                      appraisal_type, appraisal_date, delivery_date, sales_market,
                      base_unit_price_date, period_adjustment_rate_date,
                      counterparty_adjustment_rate_date, tax_excluded_estimated_total,
                      customer_contract_number, contract_adjustment_rate)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    number.year(),
                    number.month(),
                    sequence,
                    number.year(),
                    number.month(),
                    number.sequence(),
                    appraisal.type(),
                    appraisal.appraisalDate(),
                    appraisal.deliveryDate(),
                    appraisal.salesMarket(),
                    appraisal.baseUnitPriceDate().toString(),
                    appraisal.periodAdjustmentRateDate().toString(),
                    appraisal.counterpartyAdjustmentRateDate().toString(),
                    appraisal.taxExcludedEstimatedTotal(),
                    appraisal.customerContractNumber().orElse(null),
                    appraisal.contractAdjustmentRate().orElse(null));
            for (var lot : appraisal.lotAppraisals()) {
                var lotNumber = lot.lotNumber();
                jdbc.update(
                        """
                        INSERT INTO lot_appraisal (
                          appraisal_number_year, appraisal_number_month, appraisal_number_seq,
                          lot_number_year, lot_number_location, lot_number_seq)
                        VALUES (?, ?, ?, ?, ?, ?)
                        """,
                        number.year(),
                        number.month(),
                        sequence,
                        lotNumber.year(),
                        lotNumber.location(),
                        lotNumber.sequence());
                for (var detail : lot.detailAppraisals()) {
                    jdbc.update(
                            """
                            INSERT INTO lot_detail_appraisal (
                              appraisal_number_year, appraisal_number_month, appraisal_number_seq,
                              lot_number_year, lot_number_location, lot_number_seq, detail_seq_no,
                              base_unit_price, period_adjustment_rate, counterparty_adjustment_rate,
                              exceptional_period_adjustment_rate)
                            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                            """,
                            number.year(),
                            number.month(),
                            sequence,
                            lotNumber.year(),
                            lotNumber.location(),
                            lotNumber.sequence(),
                            detail.detailIndex(),
                            detail.baseUnitPrice(),
                            detail.periodAdjustmentRate(),
                            detail.counterpartyAdjustmentRate(),
                            detail.exceptionalPeriodAdjustmentRate().orElse(null));
                }
            }
            return find(number);
        });
    }

    @Override
    public Optional<Header> deleteAppraisal(SalesCaseNumber number, int expectedVersion) {
        return transaction.execute(status -> {
            if (!advance(number, expectedVersion, "appraised", "before_appraisal")) {
                status.setRollbackOnly();
                return Optional.empty();
            }
            deleteAppraisalRows(number);
            return find(number);
        });
    }

    @Override
    public Optional<Header> saveContract(SalesCaseNumber number, int expectedVersion, DirectContract contract) {
        return transaction.execute(status -> {
            if (!advance(number, expectedVersion, "appraised", "contracted")) {
                status.setRollbackOnly();
                return Optional.empty();
            }
            var appraisalKey = appraisalKey(number).orElseThrow();
            int sequence = nextSequence("contract", "contract_number", number);
            jdbc.update(
                    """
                    INSERT INTO contract (
                      contract_number_year, contract_number_month, contract_number_seq,
                      appraisal_number_year, appraisal_number_month, appraisal_number_seq,
                      contract_date, person, customer_number, agent_name, sales_type, item,
                      delivery_method, sales_method, payment_deferral_condition,
                      payment_deferral_amount, usage_, tax_excluded_contract_amount,
                      consumption_tax, tax_excluded_payment_amount, payment_consumption_tax)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    number.year(),
                    number.month(),
                    sequence,
                    appraisalKey.year(),
                    appraisalKey.month(),
                    appraisalKey.sequence(),
                    contract.contractDate(),
                    contract.person(),
                    contract.customerNumber(),
                    contract.agentName().orElse(null),
                    contract.salesType(),
                    contract.item(),
                    contract.deliveryMethod(),
                    contract.salesMethod(),
                    contract.paymentDeferralCondition().orElse(null),
                    contract.paymentDeferralAmount().orElse(null),
                    contract.usage().orElse(null),
                    contract.taxExcludedContractAmount(),
                    contract.consumptionTax(),
                    contract.taxExcludedPaymentAmount(),
                    contract.paymentConsumptionTax());
            return find(number);
        });
    }

    @Override
    public Optional<Header> deleteContract(SalesCaseNumber number, int expectedVersion) {
        return transaction.execute(status -> {
            if (!advance(number, expectedVersion, "contracted", "appraised")) {
                status.setRollbackOnly();
                return Optional.empty();
            }
            jdbc.update(
                    """
                    DELETE FROM contract WHERE (appraisal_number_year, appraisal_number_month,
                      appraisal_number_seq) IN (
                        SELECT appraisal_number_year, appraisal_number_month, appraisal_number_seq
                          FROM appraisal WHERE sales_case_number_year = ?
                           AND sales_case_number_month = ? AND sales_case_number_seq = ?)
                    """,
                    number.year(),
                    number.month(),
                    number.sequence());
            return find(number);
        });
    }

    @Override
    public Optional<Header> saveReservationPrice(
            SalesCaseNumber number, int expectedVersion, ReservationPrice reservationPrice) {
        return transaction.execute(status -> {
            if (!advance(number, expectedVersion, "before_reservation", "reserved")) {
                status.setRollbackOnly();
                return Optional.empty();
            }
            int sequence = nextSequence("reservation_price", "appraisal_number", number);
            jdbc.update(
                    """
                    INSERT INTO reservation_price (
                      appraisal_number_year, appraisal_number_month, appraisal_number_seq,
                      sales_case_number_year, sales_case_number_month, sales_case_number_seq,
                      appraisal_date, reserved_lot_info, reserved_amount, status)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'provisional')
                    """,
                    number.year(),
                    number.month(),
                    sequence,
                    number.year(),
                    number.month(),
                    number.sequence(),
                    reservationPrice.appraisalDate(),
                    reservationPrice.reservedLotInfo(),
                    reservationPrice.reservedAmount());
            return find(number);
        });
    }

    @Override
    public Optional<Header> confirmReservation(
            SalesCaseNumber number, int expectedVersion, LocalDate determinedDate, int determinedAmount) {
        return updateReservation(
                number,
                expectedVersion,
                "reserved",
                "reservation_confirmed",
                "status = 'determined', determined_date = ?, determined_amount = ?",
                determinedDate,
                determinedAmount);
    }

    @Override
    public Optional<Header> cancelReservation(SalesCaseNumber number, int expectedVersion) {
        return updateReservation(
                number,
                expectedVersion,
                "reservation_confirmed",
                "reserved",
                "status = 'provisional', determined_date = NULL, determined_amount = NULL");
    }

    @Override
    public Optional<Header> deliverReservation(SalesCaseNumber number, int expectedVersion, LocalDate deliveredDate) {
        return updateReservation(
                number,
                expectedVersion,
                "reservation_confirmed",
                "reservation_delivered",
                "delivered_date = ?",
                deliveredDate);
    }

    @Override
    public Optional<Header> designateConsignment(SalesCaseNumber number, int expectedVersion, Consignor consignor) {
        return transaction.execute(status -> {
            if (!advance(number, expectedVersion, "before_consignment", "consignment_designated")) {
                status.setRollbackOnly();
                return Optional.empty();
            }
            jdbc.update(
                    """
                    INSERT INTO consignment_info (
                      sales_case_number_year, sales_case_number_month, sales_case_number_seq,
                      consignor_name, consignor_code, designated_date)
                    VALUES (?, ?, ?, ?, ?, ?)
                    """,
                    number.year(),
                    number.month(),
                    number.sequence(),
                    consignor.name(),
                    consignor.code(),
                    consignor.designatedDate());
            return find(number);
        });
    }

    @Override
    public Optional<Header> cancelConsignment(SalesCaseNumber number, int expectedVersion) {
        return transaction.execute(status -> {
            if (!advance(number, expectedVersion, "consignment_designated", "before_consignment")) {
                status.setRollbackOnly();
                return Optional.empty();
            }
            jdbc.update(
                    """
                    DELETE FROM consignment_info WHERE sales_case_number_year = ?
                      AND sales_case_number_month = ? AND sales_case_number_seq = ?
                    """,
                    number.year(),
                    number.month(),
                    number.sequence());
            return find(number);
        });
    }

    @Override
    public Optional<Header> saveConsignmentResult(
            SalesCaseNumber number, int expectedVersion, ConsignmentResult result) {
        return transaction.execute(status -> {
            if (!advance(number, expectedVersion, "consignment_designated", "consignment_result_entered")) {
                status.setRollbackOnly();
                return Optional.empty();
            }
            jdbc.update(
                    """
                    INSERT INTO consignment_result (
                      sales_case_number_year, sales_case_number_month, sales_case_number_seq,
                      result_date, result_amount) VALUES (?, ?, ?, ?, ?)
                    """,
                    number.year(),
                    number.month(),
                    number.sequence(),
                    result.resultDate(),
                    result.resultAmount());
            return find(number);
        });
    }

    @Override
    public Optional<Header> editLots(SalesCaseNumber number, int expectedVersion, List<LotNumber> lots) {
        return transaction.execute(status -> {
            int changed = jdbc.update(
                    """
                    UPDATE sales_case SET version = version + 1
                     WHERE sales_case_number_year = ? AND sales_case_number_month = ? AND sales_case_number_seq = ?
                       AND version = ? AND ((case_type = 'direct' AND status = 'before_appraisal')
                                        OR (case_type = 'consignment' AND status = 'before_consignment'))
                    """,
                    number.year(),
                    number.month(),
                    number.sequence(),
                    expectedVersion);
            if (changed == 0) {
                status.setRollbackOnly();
                return Optional.empty();
            }
            jdbc.update(
                    """
                    DELETE FROM sales_case_lot
                     WHERE sales_case_number_year = ? AND sales_case_number_month = ?
                       AND sales_case_number_seq = ?
                    """,
                    number.year(),
                    number.month(),
                    number.sequence());
            replaceLots(number, lots);
            return find(number);
        });
    }

    @Override
    public boolean delete(SalesCaseNumber number, List<String> allowedStatuses) {
        return Boolean.TRUE.equals(transaction.execute(status -> {
            var header = find(number);
            if (header.isEmpty()
                    || !allowedStatuses.contains(header.orElseThrow().status())) {
                status.setRollbackOnly();
                return false;
            }
            jdbc.update(
                    """
                    DELETE FROM sales_case_lot
                     WHERE sales_case_number_year = ? AND sales_case_number_month = ?
                       AND sales_case_number_seq = ?
                    """,
                    number.year(),
                    number.month(),
                    number.sequence());
            return jdbc.update(
                            """
                            DELETE FROM sales_case
                             WHERE sales_case_number_year = ? AND sales_case_number_month = ?
                               AND sales_case_number_seq = ?
                            """,
                            number.year(),
                            number.month(),
                            number.sequence())
                    == 1;
        }));
    }

    private List<LotNumber> listLots(SalesCaseNumber number) {
        return jdbc.query(
                """
                SELECT lot_number_year, lot_number_location, lot_number_seq FROM sales_case_lot
                 WHERE sales_case_number_year = ? AND sales_case_number_month = ? AND sales_case_number_seq = ?
                 ORDER BY lot_number_year, lot_number_location, lot_number_seq
                """,
                (row, index) -> new LotNumber(row.getInt(1), row.getString(2), row.getInt(3)),
                number.year(),
                number.month(),
                number.sequence());
    }

    private Details loadDetails(
            SalesCaseNumber number,
            String caseType,
            Optional<LocalDate> shippingInstructionDate,
            Optional<LocalDate> shippingCompletionDate) {
        return switch (caseType) {
            case "direct" ->
                new DirectDetails(
                        loadDirectAppraisal(number),
                        loadDirectContract(number),
                        shippingInstructionDate,
                        shippingCompletionDate);
            case "reservation" -> new ReservationDetails(loadReservationPrice(number));
            case "consignment" -> new ConsignmentDetails(loadConsignor(number), loadConsignmentResult(number));
            default -> throw new IllegalStateException("Unknown caseType: " + caseType);
        };
    }

    private static Details emptyDetails(String caseType) {
        return switch (caseType) {
            case "direct" -> new DirectDetails(Optional.empty(), Optional.empty(), Optional.empty(), Optional.empty());
            case "reservation" -> new ReservationDetails(Optional.empty());
            case "consignment" -> new ConsignmentDetails(Optional.empty(), Optional.empty());
            default -> throw new IllegalStateException("Unknown caseType: " + caseType);
        };
    }

    private Optional<DirectAppraisal> loadDirectAppraisal(SalesCaseNumber number) {
        return jdbc
                .query(
                        """
                        SELECT appraisal_type, appraisal_date, delivery_date, sales_market,
                               base_unit_price_date, period_adjustment_rate_date,
                               counterparty_adjustment_rate_date, tax_excluded_estimated_total,
                               customer_contract_number, contract_adjustment_rate
                          FROM appraisal WHERE sales_case_number_year = ?
                           AND sales_case_number_month = ? AND sales_case_number_seq = ?
                         ORDER BY appraisal_number_year, appraisal_number_month, appraisal_number_seq
                        """,
                        (row, index) -> new DirectAppraisal(
                                row.getString("appraisal_type"),
                                row.getObject("appraisal_date", LocalDate.class),
                                row.getObject("delivery_date", LocalDate.class),
                                row.getString("sales_market"),
                                LocalDate.parse(row.getString("base_unit_price_date")),
                                LocalDate.parse(row.getString("period_adjustment_rate_date")),
                                LocalDate.parse(row.getString("counterparty_adjustment_rate_date")),
                                row.getInt("tax_excluded_estimated_total"),
                                Optional.ofNullable(row.getString("customer_contract_number")),
                                Optional.ofNullable(row.getBigDecimal("contract_adjustment_rate")),
                                List.of()),
                        number.year(),
                        number.month(),
                        number.sequence())
                .stream()
                .findFirst();
    }

    private Optional<DirectContract> loadDirectContract(SalesCaseNumber number) {
        return jdbc
                .query(
                        """
                        SELECT c.contract_date, c.person, c.customer_number, c.agent_name,
                               c.sales_type, c.item, c.delivery_method, c.payment_deferral_condition,
                               c.sales_method, c.usage_, c.payment_deferral_amount,
                               c.tax_excluded_contract_amount, c.consumption_tax,
                               c.tax_excluded_payment_amount, c.payment_consumption_tax
                          FROM contract c JOIN appraisal a
                            ON (a.appraisal_number_year, a.appraisal_number_month, a.appraisal_number_seq) =
                               (c.appraisal_number_year, c.appraisal_number_month, c.appraisal_number_seq)
                         WHERE a.sales_case_number_year = ? AND a.sales_case_number_month = ?
                           AND a.sales_case_number_seq = ?
                        """,
                        (row, index) -> new DirectContract(
                                row.getObject("contract_date", LocalDate.class),
                                row.getString("person"),
                                row.getString("customer_number"),
                                Optional.ofNullable(row.getString("agent_name")),
                                row.getInt("sales_type"),
                                row.getString("item"),
                                row.getString("delivery_method"),
                                Optional.ofNullable(row.getString("payment_deferral_condition")),
                                row.getInt("sales_method"),
                                Optional.ofNullable(row.getString("usage_")),
                                Optional.ofNullable(row.getObject("payment_deferral_amount", Integer.class)),
                                row.getInt("tax_excluded_contract_amount"),
                                row.getInt("consumption_tax"),
                                row.getInt("tax_excluded_payment_amount"),
                                row.getInt("payment_consumption_tax")),
                        number.year(),
                        number.month(),
                        number.sequence())
                .stream()
                .findFirst();
    }

    private Optional<ReservationPrice> loadReservationPrice(SalesCaseNumber number) {
        return jdbc
                .query(
                        """
                        SELECT appraisal_date, reserved_lot_info, reserved_amount,
                               determined_date, determined_amount, delivered_date
                          FROM reservation_price WHERE sales_case_number_year = ?
                           AND sales_case_number_month = ? AND sales_case_number_seq = ?
                        """,
                        (row, index) -> new ReservationPrice(
                                row.getObject("appraisal_date", LocalDate.class),
                                row.getString("reserved_lot_info"),
                                row.getInt("reserved_amount"),
                                Optional.ofNullable(row.getObject("determined_date", LocalDate.class)),
                                Optional.ofNullable(row.getObject("determined_amount", Integer.class)),
                                Optional.ofNullable(row.getObject("delivered_date", LocalDate.class))),
                        number.year(),
                        number.month(),
                        number.sequence())
                .stream()
                .findFirst();
    }

    private Optional<Consignor> loadConsignor(SalesCaseNumber number) {
        return jdbc
                .query(
                        """
                        SELECT consignor_name, consignor_code, designated_date FROM consignment_info
                         WHERE sales_case_number_year = ? AND sales_case_number_month = ?
                           AND sales_case_number_seq = ?
                        """,
                        (row, index) -> new Consignor(
                                row.getString("consignor_name"),
                                row.getString("consignor_code"),
                                row.getObject("designated_date", LocalDate.class)),
                        number.year(),
                        number.month(),
                        number.sequence())
                .stream()
                .findFirst();
    }

    private Optional<ConsignmentResult> loadConsignmentResult(SalesCaseNumber number) {
        return jdbc
                .query(
                        """
                        SELECT result_date, result_amount FROM consignment_result
                         WHERE sales_case_number_year = ? AND sales_case_number_month = ?
                           AND sales_case_number_seq = ?
                        """,
                        (row, index) -> new ConsignmentResult(
                                row.getObject("result_date", LocalDate.class), row.getInt("result_amount")),
                        number.year(),
                        number.month(),
                        number.sequence())
                .stream()
                .findFirst();
    }

    private Optional<SequenceKey> appraisalKey(SalesCaseNumber number) {
        return jdbc
                .query(
                        """
                        SELECT appraisal_number_year, appraisal_number_month, appraisal_number_seq
                          FROM appraisal WHERE sales_case_number_year = ?
                           AND sales_case_number_month = ? AND sales_case_number_seq = ?
                        """,
                        (row, index) -> new SequenceKey(row.getInt(1), row.getInt(2), row.getInt(3)),
                        number.year(),
                        number.month(),
                        number.sequence())
                .stream()
                .findFirst();
    }

    private int nextSequence(String table, String prefix, SalesCaseNumber number) {
        String sql = "SELECT COALESCE(MAX(" + prefix + "_seq), 0) + 1 FROM " + table + " WHERE " + prefix
                + "_year = ? AND " + prefix + "_month = ?";
        return jdbc.queryForObject(sql, Integer.class, number.year(), number.month());
    }

    private boolean advance(SalesCaseNumber number, int expectedVersion, String expectedStatus, String nextStatus) {
        return jdbc.update(
                        """
                        UPDATE sales_case SET status = ?, version = version + 1
                         WHERE sales_case_number_year = ? AND sales_case_number_month = ?
                           AND sales_case_number_seq = ? AND status = ? AND version = ?
                        """,
                        nextStatus,
                        number.year(),
                        number.month(),
                        number.sequence(),
                        expectedStatus,
                        expectedVersion)
                == 1;
    }

    private Optional<Header> updateReservation(
            SalesCaseNumber number,
            int expectedVersion,
            String expectedStatus,
            String nextStatus,
            String assignment,
            Object... values) {
        return transaction.execute(status -> {
            if (!advance(number, expectedVersion, expectedStatus, nextStatus)) {
                status.setRollbackOnly();
                return Optional.empty();
            }
            var arguments = new ArrayList<Object>(List.of(values));
            arguments.add(number.year());
            arguments.add(number.month());
            arguments.add(number.sequence());
            jdbc.update(
                    "UPDATE reservation_price SET " + assignment
                            + " WHERE sales_case_number_year = ? AND sales_case_number_month = ?"
                            + " AND sales_case_number_seq = ?",
                    arguments.toArray());
            return find(number);
        });
    }

    private void deleteAppraisalRows(SalesCaseNumber number) {
        var key = appraisalKey(number);
        if (key.isEmpty()) {
            return;
        }
        var value = key.orElseThrow();
        jdbc.update(
                """
                DELETE FROM lot_detail_appraisal WHERE appraisal_number_year = ?
                  AND appraisal_number_month = ? AND appraisal_number_seq = ?
                """,
                value.year(),
                value.month(),
                value.sequence());
        jdbc.update(
                """
                DELETE FROM lot_appraisal WHERE appraisal_number_year = ?
                  AND appraisal_number_month = ? AND appraisal_number_seq = ?
                """,
                value.year(),
                value.month(),
                value.sequence());
        jdbc.update(
                """
                DELETE FROM appraisal WHERE appraisal_number_year = ?
                  AND appraisal_number_month = ? AND appraisal_number_seq = ?
                """,
                value.year(),
                value.month(),
                value.sequence());
    }

    private record SequenceKey(int year, int month, int sequence) {}

    private void replaceLots(SalesCaseNumber number, List<LotNumber> lots) {
        for (var lot : lots) {
            jdbc.update(
                    """
                    INSERT INTO sales_case_lot (
                      sales_case_number_year, sales_case_number_month, sales_case_number_seq,
                      lot_number_year, lot_number_location, lot_number_seq)
                    VALUES (?, ?, ?, ?, ?, ?)
                    """,
                    number.year(),
                    number.month(),
                    number.sequence(),
                    lot.year(),
                    lot.location(),
                    lot.sequence());
        }
    }
}
