package com.example.salesmanagement.api;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;

import com.example.salesmanagement.contracts.api.DefaultApi;
import java.lang.reflect.Proxy;
import java.time.Clock;
import org.junit.jupiter.api.Test;
import org.springframework.mock.env.MockEnvironment;

final class ApiInvocationHandlerTest {
    @Test
    void healthAndAuthenticationConfigurationAreAvailableWithoutDatabase() {
        var environment = new MockEnvironment().withProperty("sales-management.authentication.enabled", "false");
        var handler =
                new ApiInvocationHandler(null, null, null, null, null, null, Clock.systemUTC(), environment, null);
        var api = (DefaultApi)
                Proxy.newProxyInstance(DefaultApi.class.getClassLoader(), new Class<?>[] {DefaultApi.class}, handler);

        assertEquals(200, api.healthCheck().getStatusCode().value());
        assertFalse(api.getAuthConfig().getBody().getEnabled());
    }
}
