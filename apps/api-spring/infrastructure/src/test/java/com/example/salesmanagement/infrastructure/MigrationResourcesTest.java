package com.example.salesmanagement.infrastructure;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.List;
import org.junit.jupiter.api.Test;
import org.springframework.core.io.ClassPathResource;

class MigrationResourcesTest {
    @Test
    void importsFsharpMigrationsWithMatchingNamesAndChecksums() throws Exception {
        var sourceDirectory = Path.of(System.getProperty("repository.root"), "apps/api-fsharp/migrations");
        List<Path> expected;
        try (var files = Files.list(sourceDirectory)) {
            expected = files.filter(path -> path.getFileName().toString().endsWith(".sql"))
                    .sorted()
                    .toList();
        }
        var order = readOrder();

        assertMigrationOrder(
                expected.stream().map(path -> path.getFileName().toString()).toList(), order);
        for (var source : expected) {
            var resource = new ClassPathResource("db/migrations/" + source.getFileName());
            try (var input = resource.getInputStream()) {
                assertArrayEquals(checksum(Files.readAllBytes(source)), checksum(input.readAllBytes()));
            }
        }
    }

    @Test
    void invalidMigrationOrderFixtureIsRejected() throws Exception {
        var fixture = Path.of(System.getProperty("repository.root"))
                .resolve("apps/api-spring/gate-fixtures/migration-order.txt");
        var invalidOrder = Files.readAllLines(fixture).stream()
                .filter(line -> !line.isBlank())
                .toList();

        assertThrows(AssertionError.class, () -> assertMigrationOrder(readOrder(), invalidOrder));
    }

    private static void assertMigrationOrder(List<String> expected, List<String> actual) {
        assertEquals(expected, actual);
    }

    private static List<String> readOrder() throws IOException {
        try (var lines = new ClassPathResource("db/migration-order.txt").getInputStream()) {
            return new String(lines.readAllBytes(), java.nio.charset.StandardCharsets.UTF_8)
                    .lines()
                    .filter(line -> !line.isBlank())
                    .toList();
        }
    }

    private static byte[] checksum(byte[] bytes) throws NoSuchAlgorithmException {
        return MessageDigest.getInstance("SHA-256").digest(bytes);
    }
}
