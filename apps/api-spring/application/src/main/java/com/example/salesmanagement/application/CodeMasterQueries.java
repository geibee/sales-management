package com.example.salesmanagement.application;

import java.util.List;
import java.util.Optional;

/** コードマスターを参照する読み取り専用 port。 */
public interface CodeMasterQueries {
    CodeMasters loadAll();

    record CodeName(int code, String name) {}

    record Department(int code, String name, int divisionCode) {}

    record Section(int code, String name, int departmentCode) {}

    record CodeMasters(
            List<CodeName> divisions,
            List<Department> departments,
            List<Section> sections,
            List<CodeName> processCategories,
            List<CodeName> inspectionCategories,
            List<CodeName> manufacturingCategories) {
        public CodeMasters {
            divisions = List.copyOf(divisions);
            departments = List.copyOf(departments);
            sections = List.copyOf(sections);
            processCategories = List.copyOf(processCategories);
            inspectionCategories = List.copyOf(inspectionCategories);
            manufacturingCategories = List.copyOf(manufacturingCategories);
        }

        public Optional<String> divisionName(int code) {
            return find(divisions, code);
        }

        public Optional<String> departmentName(int code) {
            return departments.stream()
                    .filter(item -> item.code() == code)
                    .map(Department::name)
                    .findFirst();
        }

        public Optional<String> sectionName(int code) {
            return sections.stream()
                    .filter(item -> item.code() == code)
                    .map(Section::name)
                    .findFirst();
        }

        public Optional<String> processCategoryName(int code) {
            return find(processCategories, code);
        }

        public Optional<String> inspectionCategoryName(int code) {
            return find(inspectionCategories, code);
        }

        public Optional<String> manufacturingCategoryName(int code) {
            return find(manufacturingCategories, code);
        }

        private static Optional<String> find(List<CodeName> items, int code) {
            return items.stream()
                    .filter(item -> item.code() == code)
                    .map(CodeName::name)
                    .findFirst();
        }
    }
}
