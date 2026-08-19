package com.example.salesmanagement.tools;

import com.nimbusds.jose.JOSEException;
import com.nimbusds.jose.JWSAlgorithm;
import com.nimbusds.jose.JWSHeader;
import com.nimbusds.jose.crypto.MACSigner;
import com.nimbusds.jwt.JWTClaimsSet;
import com.nimbusds.jwt.SignedJWT;
import java.io.PrintStream;
import java.nio.charset.StandardCharsets;
import java.time.Clock;
import java.time.Duration;
import java.util.Date;
import java.util.HashMap;
import java.util.Map;
import java.util.Set;
import java.util.regex.Pattern;

public final class DevJwtMain {
    private static final Set<String> ALLOWED_ROLES = Set.of("viewer", "operator", "admin");
    private static final Pattern TTL = Pattern.compile("^([1-9][0-9]*)([smhd])$");

    private DevJwtMain() {}

    public static void main(String[] args) {
        int exit = run(args, System.out, System.err, Clock.systemUTC());
        if (exit != 0) {
            throw new IllegalArgumentException("JWT の発行に失敗しました");
        }
    }

    static int run(String[] args, PrintStream stdout, PrintStream stderr, Clock clock) {
        try {
            Map<String, String> options = parseArguments(args);
            if (options.containsKey("help")) {
                stdout.println(usage());
                return 0;
            }
            String signingKey = requiredSigningKey(options);
            String audience = options.getOrDefault("audience", "sales-api");
            String issuer = options.getOrDefault("issuer", "dev-token-mint");
            String role = options.getOrDefault("role", "viewer");
            String user = options.getOrDefault("user", "dev-user");
            Duration lifetime = parseTtl(options.getOrDefault("ttl", "1h"));
            stdout.println(mint(signingKey, audience, issuer, user, role, clock, lifetime));
            return 0;
        } catch (RuntimeException | JOSEException exception) {
            stderr.println("Error: " + exception.getMessage());
            return 1;
        }
    }

    public static String mint(
            String signingKey,
            String audience,
            String issuer,
            String subject,
            String role,
            Clock clock,
            Duration lifetime)
            throws JOSEException {
        if (!ALLOWED_ROLES.contains(role)) {
            throw new IllegalArgumentException("role must be viewer, operator, or admin");
        }
        if (lifetime.isZero() || lifetime.isNegative()) {
            throw new IllegalArgumentException("ttl must be positive");
        }
        var issuedAt = clock.instant();
        var claims = new JWTClaimsSet.Builder()
                .subject(subject)
                .issuer(issuer)
                .audience(audience)
                .issueTime(Date.from(issuedAt))
                .expirationTime(Date.from(issuedAt.plus(lifetime)))
                .claim("preferred_username", subject)
                .claim("realm_access", Map.of("roles", java.util.List.of(role)))
                .build();
        var jwt = new SignedJWT(new JWSHeader(JWSAlgorithm.HS256), claims);
        jwt.sign(new MACSigner(signingKey.getBytes(StandardCharsets.UTF_8)));
        return jwt.serialize();
    }

    static Duration parseTtl(String value) {
        var matcher = TTL.matcher(value);
        if (!matcher.matches()) {
            throw new IllegalArgumentException("ttl must use a positive s, m, h, or d duration");
        }
        long amount = Long.parseLong(matcher.group(1));
        return switch (matcher.group(2)) {
            case "s" -> Duration.ofSeconds(amount);
            case "m" -> Duration.ofMinutes(amount);
            case "h" -> Duration.ofHours(amount);
            case "d" -> Duration.ofDays(amount);
            default -> throw new IllegalStateException("unreachable ttl unit");
        };
    }

    private static Map<String, String> parseArguments(String[] args) {
        var options = new HashMap<String, String>();
        for (int index = 0; index < args.length; index++) {
            String flag = args[index];
            if (flag.equals("-h") || flag.equals("--help")) {
                options.put("help", "true");
                continue;
            }
            if (!flag.startsWith("--") || index + 1 >= args.length) {
                throw new IllegalArgumentException("Unexpected argument: " + flag);
            }
            options.put(flag.substring(2), args[++index]);
        }
        return options;
    }

    private static String requiredSigningKey(Map<String, String> options) {
        String value = options.get("signing-key");
        if (value == null || value.isBlank()) {
            value = System.getenv("AUTHENTICATION_SIGNING_KEY");
        }
        if (value == null || value.isBlank()) {
            value = System.getenv("Authentication__SigningKey");
        }
        if (value == null || value.isBlank()) {
            throw new IllegalArgumentException("--signing-key または AUTHENTICATION_SIGNING_KEY は必須です");
        }
        return value;
    }

    private static String usage() {
        return """
                Usage: DevJwtMain [options]
                  --role <viewer|operator|admin>  Default: viewer
                  --user <username>               Default: dev-user
                  --ttl <30s|5m|1h|7d>            Default: 1h
                  --signing-key <key>             HMAC signing key
                  --audience <audience>            Default: sales-api
                  --issuer <issuer>                Default: dev-token-mint
                """;
    }
}
