package com.example.salesmanagement.infrastructure;

import com.example.salesmanagement.application.CodeMasterQueries;
import java.util.List;
import org.springframework.jdbc.core.JdbcTemplate;

public final class JdbcCodeMasterQueries implements CodeMasterQueries {
    private final JdbcTemplate jdbc;

    public JdbcCodeMasterQueries(JdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    @Override
    public CodeMasters loadAll() {
        return new CodeMasters(
                codeNames("master_division"),
                jdbc.query(
                        "SELECT code, name, division_code FROM master_department ORDER BY code",
                        (result, row) -> new Department(
                                result.getInt("code"), result.getString("name"), result.getInt("division_code"))),
                jdbc.query(
                        "SELECT code, name, department_code FROM master_section ORDER BY code",
                        (result, row) -> new Section(
                                result.getInt("code"), result.getString("name"), result.getInt("department_code"))),
                codeNames("master_process_category"),
                codeNames("master_inspection_category"),
                codeNames("master_manufacturing_category"));
    }

    private List<CodeName> codeNames(String table) {
        String sql =
                switch (table) {
                    case "master_division" -> "SELECT code, name FROM master_division ORDER BY code";
                    case "master_process_category" -> "SELECT code, name FROM master_process_category ORDER BY code";
                    case "master_inspection_category" ->
                        "SELECT code, name FROM master_inspection_category ORDER BY code";
                    case "master_manufacturing_category" ->
                        "SELECT code, name FROM master_manufacturing_category ORDER BY code";
                    default -> throw new IllegalArgumentException("Unsupported code master table: " + table);
                };
        return jdbc.query(sql, (result, row) -> new CodeName(result.getInt("code"), result.getString("name")));
    }
}
