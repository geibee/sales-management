package com.example.salesmanagement.api;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.example.salesmanagement.contracts.api.DefaultApi;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

final class BrokerlessPactContractTest {
    @Test
    void committedPactReferencesProviderStatesAndActualRoutes() throws Exception {
        var pactPath = Path.of(System.getProperty("repository.root")).resolve("pacts/frontend-sales-management.json");
        var pact = new ObjectMapper().readTree(Files.readString(pactPath));

        assertValidPact(pact);
    }

    @Test
    void invalidPactFixtureIsRejected() throws Exception {
        var fixture = Path.of(System.getProperty("repository.root"))
                .resolve("apps/api-spring/gate-fixtures/invalid-pact.json");
        var invalidPact = new ObjectMapper().readTree(Files.readString(fixture));

        assertThrows(AssertionError.class, () -> assertValidPact(invalidPact));
    }

    private static void assertValidPact(JsonNode pact) {
        assertEquals("sales-management", pact.path("provider").path("name").asText());
        assertEquals(2, pact.path("interactions").size());
        pact.path("interactions").forEach(interaction -> {
            assertFalse(interaction.path("providerStates").isEmpty());
            String path = interaction.path("request").path("path").asText();
            boolean knownRoute =
                    path.equals("/lots/2026-PACT-001") || path.equals("/lots/2026-PACT-001/complete-manufacturing");
            assertTrue(knownRoute, "未知の Pact route: " + path);
        });
        assertEquals("/lots/{id}", DefaultApi.PATH_GET_LOT);
        assertEquals("/lots/{id}/complete-manufacturing", DefaultApi.PATH_COMPLETE_MANUFACTURING);
    }
}
