package com.example.salesmanagement.domain;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.puppycrawl.tools.checkstyle.Checker;
import com.puppycrawl.tools.checkstyle.ConfigurationLoader;
import com.puppycrawl.tools.checkstyle.ConfigurationLoader.IgnoredModulesOptions;
import com.puppycrawl.tools.checkstyle.PropertiesExpander;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Properties;
import java.util.regex.Pattern;
import java.util.stream.Stream;
import javax.tools.ToolProvider;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

final class SourceRuleTests {
    private static final Pattern DOMAIN_FORBIDDEN = Pattern.compile(
            "System\\.(currentTimeMillis|nanoTime)|java\\.time\\.Clock|"
                    + "java\\.util\\.(Random|UUID)|org\\.springframework|"
                    + "\\b(?:SELECT|INSERT|UPDATE|DELETE)\\b",
            Pattern.CASE_INSENSITIVE);
    private static final Pattern API_SQL = Pattern.compile(
            "\\b(?:SELECT\\s+.+?\\s+FROM|INSERT\\s+INTO|"
                    + "UPDATE\\s+[A-Za-z_]\\w*\\s+SET|DELETE\\s+FROM|"
                    + "(?:CREATE|ALTER|DROP)\\s+TABLE)\\b",
            Pattern.CASE_INSENSITIVE | Pattern.DOTALL);

    @Test
    void domainDoesNotDependOnWallClockRandomSpringOrSql() throws IOException {
        assertNoMatch(sourceRoot("domain"), DOMAIN_FORBIDDEN);
    }

    @Test
    void apiDoesNotContainSql() throws IOException {
        assertNoMatch(sourceRoot("api"), API_SQL);
    }

    @Test
    void intentionalFixturesAreDetectedByEachRule() throws IOException {
        var fixtureRoot = springRoot().resolve("gate-fixtures");
        assertTrue(matches(fixtureRoot.resolve("domain-wall-clock.java"), DOMAIN_FORBIDDEN));
        assertTrue(matches(fixtureRoot.resolve("api-sql.java"), API_SQL));
    }

    @Test
    void compilerWarningsFixtureIsRejected(@TempDir Path outputDirectory) {
        var compiler = ToolProvider.getSystemJavaCompiler();
        assertTrue(compiler != null, "JDK compiler が必要です");
        int exitCode = compiler.run(
                null,
                null,
                null,
                "-Xlint:all",
                "-Werror",
                "-d",
                outputDirectory.toString(),
                springRoot().resolve("gate-fixtures/compile-warning.java").toString());

        assertNotEquals(0, exitCode, "警告 fixture を javac が拒否する必要があります");
    }

    @Test
    void checkstyleFixtureIsRejected() throws Exception {
        var configuration = ConfigurationLoader.loadConfiguration(
                springRoot().resolve("config/checkstyle.xml").toString(),
                new PropertiesExpander(new Properties()),
                IgnoredModulesOptions.OMIT);
        var checker = new Checker();
        checker.setModuleClassLoader(Checker.class.getClassLoader());
        try {
            checker.configure(configuration);
            int violations = checker.process(List.of(
                    springRoot().resolve("gate-fixtures/style-violation.java").toFile()));
            assertTrue(violations > 0, "style fixture を Checkstyle が拒否する必要があります");
        } finally {
            checker.destroy();
        }
    }

    private static Path sourceRoot(String module) {
        return springRoot().resolve(module).resolve("src/main/java");
    }

    private static Path springRoot() {
        return Path.of(System.getProperty("repository.root")).resolve("apps/api-spring");
    }

    private static void assertNoMatch(Path root, Pattern pattern) throws IOException {
        if (!Files.exists(root)) {
            return;
        }
        try (Stream<Path> files = Files.walk(root)) {
            List<Path> javaFiles =
                    files.filter(path -> path.toString().endsWith(".java")).toList();
            assertFalse(javaFiles.isEmpty(), "test setup must include Java sources in " + root);
            List<Path> violations =
                    javaFiles.stream().filter(path -> matches(path, pattern)).toList();
            assertTrue(violations.isEmpty(), "禁止 source pattern: " + violations);
        }
    }

    private static boolean matches(Path path, Pattern pattern) {
        try {
            return pattern.matcher(Files.readString(path)).find();
        } catch (IOException exception) {
            throw new IllegalStateException(path + " を読めません", exception);
        }
    }
}
