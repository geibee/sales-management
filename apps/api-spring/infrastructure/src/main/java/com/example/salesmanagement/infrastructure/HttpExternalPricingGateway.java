package com.example.salesmanagement.infrastructure;

import com.example.salesmanagement.application.ExternalPricingGateway;
import com.example.salesmanagement.domain.Result;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.IOException;
import java.math.BigDecimal;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;

public final class HttpExternalPricingGateway implements ExternalPricingGateway {
    private final HttpClient client;
    private final ObjectMapper objectMapper;
    private final URI baseUri;
    private final Duration timeout;
    private final Clock clock;
    private int consecutiveFailures;
    private Instant circuitOpenedAt;

    public HttpExternalPricingGateway(
            HttpClient client, ObjectMapper objectMapper, URI baseUri, Duration timeout, Clock clock) {
        this.client = client;
        this.objectMapper = objectMapper;
        this.baseUri = baseUri;
        this.timeout = timeout;
        this.clock = clock;
        this.circuitOpenedAt = Instant.EPOCH;
    }

    @Override
    public synchronized Result<PriceQuote, ExternalPricingError> fetch(String lotId) {
        Instant now = clock.instant();
        if (consecutiveFailures >= 3 && now.isBefore(circuitOpenedAt.plusSeconds(30))) {
            return Result.failure(new ExternalPricingError.CircuitOpen());
        }
        if (consecutiveFailures >= 3) {
            consecutiveFailures = 0;
        }

        Result<PriceQuote, ExternalPricingError> last =
                Result.failure(new ExternalPricingError.Malformed("upstream request was not attempted"));
        for (int attempt = 0; attempt < 3; attempt++) {
            last = requestOnce(lotId);
            if (last.isSuccess()) {
                consecutiveFailures = 0;
                return last;
            }
        }
        consecutiveFailures++;
        if (consecutiveFailures >= 3) {
            circuitOpenedAt = now;
        }
        return last;
    }

    private Result<PriceQuote, ExternalPricingError> requestOnce(String lotId) {
        try {
            var request = HttpRequest.newBuilder(baseUri.resolve("/api/pricing/" + encodePathSegment(lotId)))
                    .timeout(timeout)
                    .GET()
                    .build();
            var response = client.send(request, HttpResponse.BodyHandlers.ofString());
            if (response.statusCode() < 200 || response.statusCode() >= 300) {
                return Result.failure(new ExternalPricingError.UpstreamStatus(response.statusCode()));
            }
            var tree = objectMapper.readTree(response.body());
            if (!tree.has("basePrice") || !tree.path("basePrice").isNumber()) {
                return Result.failure(new ExternalPricingError.Malformed("missing basePrice"));
            }
            BigDecimal adjustment =
                    tree.has("adjustmentRate") && tree.path("adjustmentRate").isNumber()
                            ? tree.path("adjustmentRate").decimalValue()
                            : null;
            String source = tree.has("source") ? tree.path("source").asText() : "";
            return Result.success(new PriceQuote(tree.path("basePrice").decimalValue(), adjustment, source));
        } catch (java.net.http.HttpTimeoutException exception) {
            return Result.failure(new ExternalPricingError.Timeout((int) timeout.toMillis()));
        } catch (IOException exception) {
            return Result.failure(new ExternalPricingError.Malformed(exception.getMessage()));
        } catch (IllegalArgumentException exception) {
            return Result.failure(new ExternalPricingError.Malformed(exception.getMessage()));
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            return Result.failure(new ExternalPricingError.Malformed("request interrupted"));
        }
    }

    static String encodePathSegment(String value) {
        return URLEncoder.encode(value, StandardCharsets.UTF_8).replace("+", "%20");
    }
}
