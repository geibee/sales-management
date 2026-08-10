package com.example.salesmanagement.infrastructure;

import java.time.Clock;
import java.time.Duration;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.List;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.support.TransactionTemplate;

/** SKIP LOCKED で排他的に claim し、障害で残った claim を再投入できる outbox processor。 */
public final class JdbcOutboxProcessor {
    private final JdbcTemplate jdbc;
    private final TransactionTemplate transaction;
    private final Clock clock;
    private final Duration abandonedAfter;
    private final int batchSize;

    public JdbcOutboxProcessor(
            JdbcTemplate jdbc,
            PlatformTransactionManager transactionManager,
            Clock clock,
            Duration abandonedAfter,
            int batchSize) {
        if (abandonedAfter.isNegative() || abandonedAfter.isZero() || batchSize < 1) {
            throw new IllegalArgumentException("outbox processor limits must be positive");
        }
        this.jdbc = jdbc;
        this.transaction = new TransactionTemplate(transactionManager);
        this.clock = clock;
        this.abandonedAfter = abandonedAfter;
        this.batchSize = batchSize;
    }

    public int resetAbandonedClaims() {
        OffsetDateTime cutoff = OffsetDateTime.ofInstant(clock.instant().minus(abandonedAfter), ZoneOffset.UTC);
        return jdbc.update(
                """
                UPDATE outbox_events
                   SET status = 'pending', processed_at = NULL,
                       error_detail = 'processing claim timed out'
                 WHERE status = 'processing' AND processed_at < ?
                """,
                cutoff);
    }

    public int processPending() {
        List<Long> claimed = transaction.execute(status -> jdbc.queryForList(
                """
                WITH candidates AS (
                    SELECT id FROM outbox_events
                     WHERE status = 'pending'
                     ORDER BY id
                     FOR UPDATE SKIP LOCKED
                     LIMIT ?
                )
                UPDATE outbox_events event
                   SET status = 'processing', processed_at = ?
                  FROM candidates
                 WHERE event.id = candidates.id
                RETURNING event.id
                """,
                Long.class,
                batchSize,
                OffsetDateTime.ofInstant(clock.instant(), ZoneOffset.UTC)));
        if (claimed == null || claimed.isEmpty()) {
            return 0;
        }

        int processed = 0;
        OffsetDateTime completedAt = OffsetDateTime.ofInstant(clock.instant(), ZoneOffset.UTC);
        for (Long id : claimed) {
            processed += jdbc.update(
                    """
                    UPDATE outbox_events
                       SET status = 'processed', processed_at = ?, error_detail = NULL
                     WHERE id = ? AND status = 'processing'
                    """,
                    completedAt,
                    id);
        }
        return processed;
    }
}
