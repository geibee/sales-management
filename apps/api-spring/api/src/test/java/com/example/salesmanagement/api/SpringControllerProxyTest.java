package com.example.salesmanagement.api;

import static org.junit.jupiter.api.Assertions.assertInstanceOf;

import com.example.salesmanagement.contracts.api.DefaultApi;
import java.time.Clock;
import org.junit.jupiter.api.Test;
import org.springframework.aop.framework.ProxyFactory;
import org.springframework.mock.env.MockEnvironment;

final class SpringControllerProxyTest {
    @Test
    void controllerSupportsClassBasedMethodValidationProxy() {
        var handler = new ApiInvocationHandler(
                null, null, null, null, null, null, Clock.systemUTC(), new MockEnvironment(), null);
        var factory = new ProxyFactory(new SalesManagementController(handler));
        factory.setProxyTargetClass(true);

        assertInstanceOf(DefaultApi.class, factory.getProxy());
    }
}
