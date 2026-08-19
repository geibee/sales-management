package com.example.salesmanagement.api;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertThrows;

import org.junit.jupiter.api.Test;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.mock.web.MockHttpServletResponse;

final class QueryGuardInterceptorTest {
    private final QueryGuardInterceptor interceptor = new QueryGuardInterceptor();

    @Test
    void rejectsUnknownAndEmptyQueryParameters() {
        var unknown = new MockHttpServletRequest("GET", "/lots");
        unknown.addParameter("statuz", "manufactured");
        assertThrows(
                ApiProblemException.class,
                () -> interceptor.preHandle(unknown, new MockHttpServletResponse(), new Object()));

        var empty = new MockHttpServletRequest("GET", "/lots");
        empty.addParameter("status", "");
        assertThrows(
                ApiProblemException.class,
                () -> interceptor.preHandle(empty, new MockHttpServletResponse(), new Object()));

        var duplicate = new MockHttpServletRequest("GET", "/lots");
        duplicate.addParameter("limit", "10", "20");
        assertThrows(
                ApiProblemException.class,
                () -> interceptor.preHandle(duplicate, new MockHttpServletResponse(), new Object()));

        var emptyName = new MockHttpServletRequest("GET", "/lots/export");
        emptyName.setQueryString("=false");
        assertThrows(
                ApiProblemException.class,
                () -> interceptor.preHandle(emptyName, new MockHttpServletResponse(), new Object()));

        var invalidLotStatus = new MockHttpServletRequest("GET", "/lots");
        invalidLotStatus.addParameter("status", "AAA");
        assertThrows(
                ApiProblemException.class,
                () -> interceptor.preHandle(invalidLotStatus, new MockHttpServletResponse(), new Object()));

        var invalidCsvFormat = new MockHttpServletRequest("GET", "/lots/export");
        invalidCsvFormat.addParameter("format", "json");
        assertThrows(
                ApiProblemException.class,
                () -> interceptor.preHandle(invalidCsvFormat, new MockHttpServletResponse(), new Object()));

        var invalidCaseType = new MockHttpServletRequest("GET", "/sales-cases");
        invalidCaseType.addParameter("caseType", "AAA");
        assertThrows(
                ApiProblemException.class,
                () -> interceptor.preHandle(invalidCaseType, new MockHttpServletResponse(), new Object()));
    }

    @Test
    void allowsContractedQueryParameters() {
        var request = new MockHttpServletRequest("GET", "/lots");
        request.addParameter("status", "manufactured");
        assertDoesNotThrow(() -> interceptor.preHandle(request, new MockHttpServletResponse(), new Object()));

        var priceCheck = new MockHttpServletRequest("GET", "/api/external/price-check");
        priceCheck.addParameter("lotId", "2098-PARITY-009");
        assertDoesNotThrow(() -> interceptor.preHandle(priceCheck, new MockHttpServletResponse(), new Object()));

        var available = new MockHttpServletRequest("GET", "/lots/available");
        available.addParameter("excludeCase", "0-0-0");
        assertDoesNotThrow(() -> interceptor.preHandle(available, new MockHttpServletResponse(), new Object()));
    }
}
