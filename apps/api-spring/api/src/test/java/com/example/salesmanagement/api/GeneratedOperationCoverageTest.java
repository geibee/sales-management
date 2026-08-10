package com.example.salesmanagement.api;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import com.example.salesmanagement.contracts.api.DefaultApi;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.Set;
import java.util.regex.Pattern;
import java.util.stream.Collectors;
import org.junit.jupiter.api.Test;

final class GeneratedOperationCoverageTest {
    @Test
    void dispatcherExplicitlyHandlesAllThirtyFiveOpenApiOperations() throws Exception {
        Set<String> contractMethods = contractMethods();
        Set<String> dispatched = dispatchedOperations();

        assertCompleteCoverage(contractMethods, dispatched);
    }

    @Test
    void missingOperationFixtureIsRejected() throws Exception {
        var fixturePath = Path.of(System.getProperty("repository.root"))
                .resolve("apps/api-spring/gate-fixtures/missing-operation.json");
        String missingOperation = new ObjectMapper()
                .readTree(Files.readString(fixturePath))
                .path("removeOperationId")
                .asText();
        var incomplete = new java.util.HashSet<>(dispatchedOperations());
        incomplete.remove(missingOperation);

        assertThrows(AssertionError.class, () -> assertCompleteCoverage(contractMethods(), incomplete));
    }

    private static Set<String> contractMethods() {
        return Arrays.stream(DefaultApi.class.getDeclaredMethods())
                .map(method -> method.getName())
                .collect(Collectors.toSet());
    }

    private static Set<String> dispatchedOperations() throws Exception {
        var source = Files.readString(Path.of(System.getProperty("repository.root"))
                .resolve(
                        "apps/api-spring/api/src/main/java/com/example/salesmanagement/api/ApiInvocationHandler.java"));
        var dispatchSwitch = source.substring(
                source.indexOf("return switch (operation)"), source.indexOf("private Object objectMethod"));
        var matcher = Pattern.compile("case \\\"([A-Za-z][A-Za-z0-9]*)\\\"").matcher(dispatchSwitch);
        var dispatched = new java.util.HashSet<String>();
        while (matcher.find()) {
            dispatched.add(matcher.group(1));
        }
        return dispatched;
    }

    private static void assertCompleteCoverage(Set<String> contractMethods, Set<String> dispatched) {
        assertEquals(35, contractMethods.size());
        assertEquals(contractMethods, dispatched);
    }
}
