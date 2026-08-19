package com.example.salesmanagement.api;

import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.Collection;
import java.util.List;
import java.util.Map;
import javax.crypto.spec.SecretKeySpec;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.core.convert.converter.Converter;
import org.springframework.http.HttpMethod;
import org.springframework.http.MediaType;
import org.springframework.security.authentication.AbstractAuthenticationToken;
import org.springframework.security.config.Customizer;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.oauth2.core.DelegatingOAuth2TokenValidator;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.security.oauth2.jwt.JwtClaimNames;
import org.springframework.security.oauth2.jwt.JwtClaimValidator;
import org.springframework.security.oauth2.jwt.JwtDecoder;
import org.springframework.security.oauth2.jwt.JwtTimestampValidator;
import org.springframework.security.oauth2.jwt.NimbusJwtDecoder;
import org.springframework.security.oauth2.server.resource.authentication.JwtAuthenticationConverter;
import org.springframework.security.oauth2.server.resource.authentication.JwtGrantedAuthoritiesConverter;
import org.springframework.security.web.SecurityFilterChain;

@Configuration
public class SecurityConfiguration {
    private static final String VIEWER = "ROLE_viewer";
    private static final String OPERATOR = "ROLE_operator";
    private static final String ADMIN = "ROLE_admin";

    @Bean
    SecurityFilterChain securityFilterChain(
            HttpSecurity http, @Value("${sales-management.authentication.enabled:false}") boolean authenticationEnabled)
            throws Exception {
        http.csrf(csrf -> csrf.disable())
                .headers(headers -> headers.defaultsDisabled()
                        .contentTypeOptions(Customizer.withDefaults())
                        .addHeaderWriter((request, response) -> {
                            response.setHeader("Cross-Origin-Resource-Policy", "same-origin");
                            if (isBusinessPath(request.getRequestURI())) {
                                response.setHeader("X-Frame-Options", "DENY");
                                response.setHeader("X-XSS-Protection", "1; mode=block");
                                response.setHeader(
                                        "Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
                                response.setHeader("Referrer-Policy", "no-referrer");
                            }
                        }))
                .cors(Customizer.withDefaults());
        if (authenticationEnabled) {
            http.authorizeHttpRequests(authorize -> authorize
                            .requestMatchers("/health", "/auth/config", "/openapi.yaml", "/swagger", "/swagger/")
                            .permitAll()
                            .requestMatchers(HttpMethod.GET, "/**")
                            .hasAnyAuthority(VIEWER, OPERATOR, ADMIN)
                            .anyRequest()
                            .hasAnyAuthority(OPERATOR, ADMIN))
                    .oauth2ResourceServer(
                            oauth -> oauth.jwt(jwt -> jwt.jwtAuthenticationConverter(jwtAuthenticationConverter()))
                                    .authenticationEntryPoint((request, response, exception) -> {
                                        response.setStatus(401);
                                        response.setHeader("WWW-Authenticate", "Bearer");
                                        response.setContentType(MediaType.APPLICATION_PROBLEM_JSON_VALUE);
                                        response.getWriter()
                                                .write("{\"type\":\"unauthorized\",\"title\":\"Unauthorized\","
                                                        + "\"status\":401,\"detail\":\"Authentication required.\"}");
                                    }))
                    .exceptionHandling(errors -> errors.accessDeniedHandler((request, response, exception) -> {
                        response.setStatus(403);
                        response.setContentType(MediaType.APPLICATION_PROBLEM_JSON_VALUE);
                        response.getWriter()
                                .write("{\"type\":\"forbidden\",\"title\":\"Forbidden\","
                                        + "\"status\":403,\"detail\":\"Insufficient role.\"}");
                    }));
        } else {
            http.authorizeHttpRequests(authorize -> authorize.anyRequest().permitAll());
        }
        return http.build();
    }

    private static boolean isBusinessPath(String path) {
        return path.equals("/lots")
                || path.startsWith("/lots/")
                || path.equals("/code-masters")
                || path.equals("/sales-cases")
                || path.startsWith("/sales-cases/")
                || path.startsWith("/api/external/");
    }

    @Bean
    @ConditionalOnProperty(prefix = "sales-management.authentication", name = "enabled", havingValue = "true")
    JwtDecoder jwtDecoder(
            @Value("${sales-management.authentication.signing-key:}") String signingKey,
            @Value("${sales-management.authentication.audience:sales-api}") String audience) {
        if (signingKey.isBlank()) {
            throw new IllegalStateException("authentication signing key must be configured");
        }
        var key = new SecretKeySpec(signingKey.getBytes(StandardCharsets.UTF_8), "HmacSHA256");
        var decoder = NimbusJwtDecoder.withSecretKey(key).build();
        var timestamp = new JwtTimestampValidator(Duration.ZERO);
        var expectedAudience = new JwtClaimValidator<List<String>>(
                JwtClaimNames.AUD, audiences -> audiences != null && audiences.contains(audience));
        decoder.setJwtValidator(new DelegatingOAuth2TokenValidator<>(timestamp, expectedAudience));
        return decoder;
    }

    private static Converter<Jwt, AbstractAuthenticationToken> jwtAuthenticationConverter() {
        var converter = new JwtAuthenticationConverter();
        converter.setPrincipalClaimName("preferred_username");
        converter.setJwtGrantedAuthoritiesConverter(SecurityConfiguration::realmAuthorities);
        return converter;
    }

    private static Collection<org.springframework.security.core.GrantedAuthority> realmAuthorities(Jwt jwt) {
        Object realmClaim = jwt.getClaim("realm_access");
        if (!(realmClaim instanceof Map<?, ?> realm) || !(realm.get("roles") instanceof Collection<?> roles)) {
            return List.of();
        }
        var converter = new JwtGrantedAuthoritiesConverter();
        converter.setAuthorityPrefix("ROLE_");
        converter.setAuthoritiesClaimName("roles");
        return converter.convert(Jwt.withTokenValue(jwt.getTokenValue())
                .headers(headers -> headers.putAll(jwt.getHeaders()))
                .claims(claims -> claims.put(
                        "roles",
                        roles.stream()
                                .filter(String.class::isInstance)
                                .map(String.class::cast)
                                .toList()))
                .issuedAt(jwt.getIssuedAt())
                .expiresAt(jwt.getExpiresAt())
                .build());
    }
}
