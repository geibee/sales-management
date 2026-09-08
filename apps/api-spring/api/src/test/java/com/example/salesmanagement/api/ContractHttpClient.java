package com.example.salesmanagement.api;

import com.atlassian.oai.validator.OpenApiInteractionValidator;
import com.atlassian.oai.validator.model.Request;
import com.atlassian.oai.validator.model.SimpleResponse;
import com.example.salesmanagement.contracts.api.DefaultApi;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.IOException;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.Arrays;
import java.util.Comparator;
import java.util.List;
import java.util.TreeSet;
import java.util.regex.Pattern;
import org.springframework.web.bind.annotation.RequestMapping;

/** 実レスポンスの契約照合が成功した後だけ operation 到達を記録する。 */
final class ContractHttpClient {
    private static final Path ROOT = Path.of(System.getProperty("repository.root"));
    private static final OpenApiInteractionValidator VALIDATOR = OpenApiInteractionValidator.createForSpecificationUrl(
                    ROOT.resolve("apps/api-fsharp/openapi.yaml").toUri().toString())
            .build();
    private static final TreeSet<String> HITS = new TreeSet<>();
    private static final List<Operation> OPERATIONS = Arrays.stream(DefaultApi.class.getDeclaredMethods())
            .filter(method -> method.getAnnotation(RequestMapping.class) != null)
            .map(method -> {
                var mapping = method.getAnnotation(RequestMapping.class);
                String path = mapping.value()[0];
                return new Operation(
                        method.getName(),
                        mapping.method()[0].name(),
                        path,
                        Pattern.compile("^" + path.replace("{id}", "[^/]+") + "$"));
            })
            .sorted(Comparator.comparing(operation -> operation.path().contains("{")))
            .toList();
    private final HttpClient client = HttpClient.newHttpClient();

    HttpResponse<String> send(HttpRequest request) throws IOException, InterruptedException {
        HttpResponse<String> response = client.send(request, HttpResponse.BodyHandlers.ofString());
        var operation = OPERATIONS.stream()
                .filter(item -> item.method().equals(request.method())
                        && item.pattern().matcher(request.uri().getPath()).matches())
                .findFirst();
        if (operation.isPresent()) {
            var builder = SimpleResponse.Builder.status(response.statusCode()).withBody(response.body());
            response.headers().map().forEach((name, values) -> builder.withHeader(name, values.toArray(String[]::new)));
            var report = ResponseContract.validate(
                    VALIDATOR, request.uri().getPath(), Request.Method.valueOf(request.method()), builder.build());
            if (report.hasErrors()) {
                throw new AssertionError("[openapi-validation] " + request.method() + " "
                        + request.uri().getPath() + ": " + report);
            }
            if (response.statusCode() >= 200 && response.statusCode() < 300) {
                recordHit(operation.orElseThrow().id());
            }
        }
        return response;
    }

    private static synchronized void recordHit(String id) throws IOException {
        if (HITS.add(id)) {
            String override = System.getenv("OPERATION_COVERAGE_FILE");
            Path output = override == null
                    ? ROOT.resolve("apps/api-spring/api/target/operation-coverage.json")
                    : Path.of(override);
            Files.createDirectories(output.getParent());
            Path temporary = Files.createTempFile(output.getParent(), "operations-", ".json");
            new ObjectMapper().writeValue(temporary.toFile(), HITS);
            Files.move(temporary, output, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
        }
    }

    private record Operation(String id, String method, String path, Pattern pattern) {}
}
