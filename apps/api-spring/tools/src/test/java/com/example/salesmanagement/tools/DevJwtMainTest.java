package com.example.salesmanagement.tools;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import com.nimbusds.jwt.SignedJWT;
import java.io.ByteArrayOutputStream;
import java.io.PrintStream;
import java.nio.charset.StandardCharsets;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

final class DevJwtMainTest {
    private static final String SIGNING_KEY = "stepf16-test-signing-key-please-do-not-use-in-production";
    private static final Clock CLOCK = Clock.fixed(Instant.parse("2026-08-09T00:00:00Z"), ZoneOffset.UTC);

    @Test
    void fixedClockProducesApiCompatibleClaims() throws Exception {
        var token = DevJwtMain.mint(
                SIGNING_KEY, "sales-api", "test-issuer", "operator-1", "operator", CLOCK, Duration.ofHours(1));
        var claims = SignedJWT.parse(token).getJWTClaimsSet();

        assertEquals("operator-1", claims.getSubject());
        assertEquals("test-issuer", claims.getIssuer());
        assertEquals(List.of("sales-api"), claims.getAudience());
        assertEquals(Map.of("roles", List.of("operator")), claims.getJSONObjectClaim("realm_access"));
        assertEquals(
                Instant.parse("2026-08-09T01:00:00Z"),
                claims.getExpirationTime().toInstant());
    }

    @Test
    void rejectsUnknownRolesAndParsesSupportedTtlUnits() {
        assertThrows(
                IllegalArgumentException.class,
                () -> DevJwtMain.mint(
                        SIGNING_KEY, "sales-api", "issuer", "u1", "superuser", CLOCK, Duration.ofHours(1)));
        assertEquals(Duration.ofSeconds(30), DevJwtMain.parseTtl("30s"));
        assertEquals(Duration.ofMinutes(5), DevJwtMain.parseTtl("5m"));
        assertEquals(Duration.ofHours(2), DevJwtMain.parseTtl("2h"));
        assertEquals(Duration.ofDays(7), DevJwtMain.parseTtl("7d"));
        assertThrows(IllegalArgumentException.class, () -> DevJwtMain.parseTtl("abc"));
        assertThrows(IllegalArgumentException.class, () -> DevJwtMain.parseTtl("0s"));
        assertThrows(IllegalArgumentException.class, () -> DevJwtMain.parseTtl("10x"));
    }

    @Test
    void commandDefaultsToViewerAndEmitsJwt() throws Exception {
        var stdout = new ByteArrayOutputStream();
        var stderr = new ByteArrayOutputStream();
        int exit = DevJwtMain.run(
                new String[] {"--user", "u1", "--signing-key", SIGNING_KEY, "--issuer", "test-issuer"},
                new PrintStream(stdout, true, StandardCharsets.UTF_8),
                new PrintStream(stderr, true, StandardCharsets.UTF_8),
                CLOCK);

        assertEquals(0, exit);
        assertEquals("", stderr.toString(StandardCharsets.UTF_8));
        var claims =
                SignedJWT.parse(stdout.toString(StandardCharsets.UTF_8).strip()).getJWTClaimsSet();
        assertEquals("u1", claims.getSubject());
        assertEquals(Map.of("roles", List.of("viewer")), claims.getJSONObjectClaim("realm_access"));
    }
}
