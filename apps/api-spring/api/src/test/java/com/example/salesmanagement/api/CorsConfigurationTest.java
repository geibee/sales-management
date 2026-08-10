package com.example.salesmanagement.api;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

final class CorsConfigurationTest {
    @Test
    void configuresExplicitOriginsAndEveryPublishedMutationMethod() {
        var configuration = new WebConfiguration("http://localhost:5173,https://admin.example.com").corsConfiguration();

        assertThat(configuration.getAllowedOrigins())
                .containsExactly("http://localhost:5173", "https://admin.example.com");
        assertThat(configuration.getAllowedMethods()).containsExactly("GET", "POST", "PUT", "DELETE", "OPTIONS");
        assertThat(configuration.getAllowCredentials()).isTrue();
        assertThat(configuration.getMaxAge()).isEqualTo(600L);
    }
}
