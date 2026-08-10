package com.example.salesmanagement.infrastructure;

import com.example.salesmanagement.application.LotQueries;
import com.example.salesmanagement.application.VersionedLot;
import com.example.salesmanagement.domain.LotNumber;
import java.util.List;
import java.util.Optional;
import org.springframework.jdbc.core.JdbcTemplate;

public final class JdbcLotQueries implements LotQueries {
    private final JdbcTemplate jdbc;
    private final JdbcLotRepository repository;

    public JdbcLotQueries(JdbcTemplate jdbc, JdbcLotRepository repository) {
        this.jdbc = jdbc;
        this.repository = repository;
    }

    @Override
    public LotPage list(Optional<String> status, int limit, int offset) {
        var where = status.isPresent() ? " WHERE status = ?" : "";
        Object[] filter = status.<Object[]>map(value -> new Object[] {value}).orElseGet(() -> new Object[0]);
        int total = jdbc.queryForObject("SELECT COUNT(*) FROM lot" + where, Integer.class, filter);
        Object[] parameters = status.<Object[]>map(value -> new Object[] {value, limit, offset})
                .orElseGet(() -> new Object[] {limit, offset});
        List<VersionedLot> items = jdbc
                .query(
                        "SELECT lot_number_year, lot_number_location, lot_number_seq FROM lot"
                                + where
                                + " ORDER BY lot_number_year, lot_number_location, lot_number_seq LIMIT ? OFFSET ?",
                        (row, index) -> new LotNumber(row.getInt(1), row.getString(2), row.getInt(3)),
                        parameters)
                .stream()
                .map(repository::find)
                .flatMap(Optional::stream)
                .toList();
        return new LotPage(items, total, limit, offset);
    }

    @Override
    public List<VersionedLot> listAvailable(Optional<String> excludedSalesCase) {
        String exclusion = excludedSalesCase.isPresent()
                ? """
                   AND NOT EXISTS (
                     SELECT 1 FROM sales_case_lot scl
                      WHERE scl.lot_number_year = l.lot_number_year
                        AND scl.lot_number_location = l.lot_number_location
                        AND scl.lot_number_seq = l.lot_number_seq
                        AND concat(
                              scl.sales_case_number_year, '-',
                              scl.sales_case_number_month, '-',
                              scl.sales_case_number_seq) <> ?)
                   """
                : """
                   AND NOT EXISTS (
                     SELECT 1 FROM sales_case_lot scl
                      WHERE scl.lot_number_year = l.lot_number_year
                        AND scl.lot_number_location = l.lot_number_location
                        AND scl.lot_number_seq = l.lot_number_seq)
                   """;
        Object[] parameters =
                excludedSalesCase.<Object[]>map(value -> new Object[] {value}).orElseGet(() -> new Object[0]);
        return jdbc
                .query(
                        """
                        SELECT l.lot_number_year, l.lot_number_location, l.lot_number_seq
                          FROM lot l WHERE l.status = 'manufactured'
                        """
                                + exclusion
                                + " ORDER BY l.lot_number_year, l.lot_number_location, l.lot_number_seq",
                        (row, index) -> new LotNumber(row.getInt(1), row.getString(2), row.getInt(3)),
                        parameters)
                .stream()
                .map(repository::find)
                .flatMap(Optional::stream)
                .toList();
    }
}
