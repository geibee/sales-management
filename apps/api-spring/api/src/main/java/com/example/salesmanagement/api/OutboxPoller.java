package com.example.salesmanagement.api;

import com.example.salesmanagement.infrastructure.JdbcOutboxProcessor;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.boot.context.event.ApplicationReadyEvent;
import org.springframework.context.ApplicationListener;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;

@Component
@ConditionalOnProperty(name = "sales-management.outbox.enabled", havingValue = "true", matchIfMissing = true)
public final class OutboxPoller implements ApplicationListener<ApplicationReadyEvent> {
    private final JdbcOutboxProcessor processor;
    private volatile boolean applicationReady;

    public OutboxPoller(JdbcOutboxProcessor processor) {
        this.processor = processor;
    }

    @Override
    public void onApplicationEvent(ApplicationReadyEvent event) {
        applicationReady = true;
    }

    @Scheduled(
            initialDelayString = "${sales-management.outbox.poll-interval-milliseconds:5000}",
            fixedDelayString = "${sales-management.outbox.poll-interval-milliseconds:5000}")
    void poll() {
        if (!applicationReady) {
            return;
        }
        processor.resetAbandonedClaims();
        processor.processPending();
    }
}
