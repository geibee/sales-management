package com.example.salesmanagement.api;

import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.noClasses;
import static com.tngtech.archunit.library.Architectures.layeredArchitecture;

import com.tngtech.archunit.core.importer.ClassFileImporter;
import com.tngtech.archunit.core.importer.ImportOption;
import org.junit.jupiter.api.Test;

final class ArchitectureRulesTest {
    private static final String ROOT = "com.example.salesmanagement";

    @Test
    void dependenciesPointFromApiTowardDomain() {
        var classes = new ClassFileImporter()
                .withImportOption(ImportOption.Predefined.DO_NOT_INCLUDE_TESTS)
                .importPackages(ROOT);
        layeredArchitecture()
                .consideringOnlyDependenciesInLayers()
                .layer("Domain")
                .definedBy(ROOT + ".domain..")
                .layer("Application")
                .definedBy(ROOT + ".application..")
                .layer("Infrastructure")
                .definedBy(ROOT + ".infrastructure..")
                .layer("Api")
                .definedBy(ROOT + ".api..")
                .whereLayer("Domain")
                .mayOnlyBeAccessedByLayers("Application", "Infrastructure", "Api")
                .whereLayer("Application")
                .mayOnlyBeAccessedByLayers("Infrastructure", "Api")
                .whereLayer("Infrastructure")
                .mayOnlyBeAccessedByLayers("Api")
                .check(classes);
    }

    @Test
    void domainDoesNotDependOnFrameworkOrSql() {
        var classes = new ClassFileImporter().importPackages(ROOT + ".domain");
        noClasses()
                .that()
                .resideInAPackage(ROOT + ".domain..")
                .should()
                .dependOnClassesThat()
                .resideInAnyPackage("org.springframework..", "java.sql..", "javax.sql..")
                .check(classes);
    }
}
