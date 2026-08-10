package com.example.salesmanagement.api;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Configuration;
import org.springframework.web.cors.CorsConfiguration;
import org.springframework.web.servlet.config.annotation.CorsRegistry;
import org.springframework.web.servlet.config.annotation.WebMvcConfigurer;

@Configuration
public class WebConfiguration implements WebMvcConfigurer {
    private final String[] allowedOrigins;

    public WebConfiguration(
            @Value("${sales-management.cors.allowed-origins:http://localhost:5173}") String allowedOrigins) {
        this.allowedOrigins = allowedOrigins.split(",");
    }

    @Override
    public void addCorsMappings(CorsRegistry registry) {
        var configuration = corsConfiguration();
        registry.addMapping("/**")
                .allowedOrigins(configuration.getAllowedOrigins().toArray(String[]::new))
                .allowedMethods(configuration.getAllowedMethods().toArray(String[]::new))
                .allowedHeaders(configuration.getAllowedHeaders().toArray(String[]::new))
                .exposedHeaders(configuration.getExposedHeaders().toArray(String[]::new))
                .allowCredentials(Boolean.TRUE.equals(configuration.getAllowCredentials()))
                .maxAge(configuration.getMaxAge());
    }

    CorsConfiguration corsConfiguration() {
        var configuration = new CorsConfiguration();
        configuration.setAllowedOrigins(java.util.List.of(allowedOrigins));
        configuration.setAllowedMethods(java.util.List.of("GET", "POST", "PUT", "DELETE", "OPTIONS"));
        configuration.setAllowedHeaders(java.util.List.of("Authorization", "Content-Type", "Accept"));
        configuration.setExposedHeaders(java.util.List.of("Content-Disposition", "X-Trace-Id"));
        configuration.setAllowCredentials(true);
        configuration.setMaxAge(600L);
        return configuration;
    }
}
