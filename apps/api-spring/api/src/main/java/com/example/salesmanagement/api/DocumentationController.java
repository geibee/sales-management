package com.example.salesmanagement.api;

import org.springframework.core.io.ClassPathResource;
import org.springframework.core.io.Resource;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
public final class DocumentationController {
    private static final String SWAGGER_HTML =
            """
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8">
              <title>Sales Management API</title>
              <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui.css">
            </head>
            <body>
              <div id="swagger-ui"></div>
              <script src="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
              <script>
                window.onload = () => SwaggerUIBundle({url: '/openapi.yaml', dom_id: '#swagger-ui'});
              </script>
            </body>
            </html>
            """;

    @GetMapping(value = "/openapi.yaml", produces = "application/yaml;charset=UTF-8")
    ResponseEntity<Resource> openapi() {
        return ResponseEntity.ok(new ClassPathResource("META-INF/sales-management/openapi.yaml"));
    }

    @GetMapping(
            value = {"/swagger", "/swagger/"},
            produces = MediaType.TEXT_HTML_VALUE)
    ResponseEntity<String> swagger() {
        return ResponseEntity.ok(SWAGGER_HTML);
    }
}
