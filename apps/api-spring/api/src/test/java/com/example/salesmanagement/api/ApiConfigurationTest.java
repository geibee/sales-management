package com.example.salesmanagement.api;

import static org.assertj.core.api.Assertions.assertThat;

import com.example.salesmanagement.contracts.model.ProblemDetails;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.security.authentication.AnonymousAuthenticationToken;
import org.springframework.security.core.authority.AuthorityUtils;
import org.springframework.security.core.context.SecurityContextHolder;

final class ApiConfigurationTest {
    @AfterEach
    void clearSecurityContext() {
        SecurityContextHolder.clearContext();
    }

    @Test
    void mapsSpringAnonymousAuthenticationToSystemActor() {
        var anonymous = new AnonymousAuthenticationToken(
                "test-key", "anonymousUser", AuthorityUtils.createAuthorityList("ROLE_ANONYMOUS"));
        SecurityContextHolder.getContext().setAuthentication(anonymous);

        assertThat(new ApiConfiguration().currentActor().userId()).isEqualTo("system");
    }

    @Test
    void omitsUnsetOptionalProblemDetailsPropertiesOnly() throws Exception {
        var configuration = new ApiConfiguration();
        var problem = new ProblemDetails()
                .type("not-found")
                .title("Not Found")
                .status(404)
                .detail("missing");

        String body = configuration.infrastructureObjectMapper().writeValueAsString(problem);
        var httpMapperBuilder = tools.jackson.databind.json.JsonMapper.builder();
        configuration.problemDetailsJsonCustomizer().customize(httpMapperBuilder);
        String httpBody = httpMapperBuilder.build().writeValueAsString(problem);

        assertThat(body)
                .isEqualTo("{\"type\":\"not-found\",\"title\":\"Not Found\",\"status\":404,\"detail\":\"missing\"}");
        assertThat(configuration.infrastructureObjectMapper().readTree(httpBody))
                .isEqualTo(configuration.infrastructureObjectMapper().readTree(body));
    }
}
