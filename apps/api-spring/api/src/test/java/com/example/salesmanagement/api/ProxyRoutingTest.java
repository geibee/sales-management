package com.example.salesmanagement.api;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import java.time.Clock;
import org.junit.jupiter.api.Test;
import org.springframework.mock.env.MockEnvironment;
import org.springframework.test.web.servlet.setup.MockMvcBuilders;

final class ProxyRoutingTest {
    @Test
    void exposesRoutesFromOpenApiInterfaceAnnotations() throws Exception {
        var handler = new ApiInvocationHandler(
                null,
                null,
                null,
                null,
                null,
                null,
                Clock.systemUTC(),
                new MockEnvironment().withProperty("sales-management.authentication.enabled", "false"),
                null);
        var api = new SalesManagementController(handler);
        var mvc = MockMvcBuilders.standaloneSetup(api)
                .setControllerAdvice(new ProblemDetailsAdvice())
                .build();

        mvc.perform(get("/health")).andExpect(status().isOk());
        mvc.perform(get("/auth/config"))
                .andExpect(status().isOk())
                .andExpect(content().contentTypeCompatibleWith("application/json"))
                .andExpect(jsonPath("$.enabled").value(false));
    }
}
