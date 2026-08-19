package com.example.salesmanagement.api;

import com.example.salesmanagement.contracts.model.ProblemDetails;
import com.fasterxml.jackson.databind.ObjectMapper;
import jakarta.servlet.FilterChain;
import jakarta.servlet.ReadListener;
import jakarta.servlet.ServletException;
import jakarta.servlet.ServletInputStream;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletRequestWrapper;
import jakarta.servlet.http.HttpServletResponse;
import java.io.BufferedReader;
import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.core.Ordered;
import org.springframework.core.annotation.Order;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.stereotype.Component;
import org.springframework.web.filter.OncePerRequestFilter;
import org.yaml.snakeyaml.Yaml;

/** OpenAPI から機械導出した 405・415・413 の転送レベル契約を強制する。 */
@Component
@Order(Ordered.HIGHEST_PRECEDENCE)
public final class HttpSemanticsFilter extends OncePerRequestFilter {
    private static final String SPEC_RESOURCE = "/META-INF/sales-management/openapi.yaml";
    private static final Set<String> HTTP_METHODS =
            Set.of("GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS", "TRACE");

    private final ObjectMapper objectMapper;
    private final int maxRequestBodyBytes;
    private final List<PathSpec> paths;

    public HttpSemanticsFilter(
            ObjectMapper objectMapper,
            @Value("${sales-management.http.max-request-body-bytes:1048576}") int maxRequestBodyBytes) {
        if (maxRequestBodyBytes < 1) {
            throw new IllegalArgumentException("maxRequestBodyBytes must be positive");
        }
        this.objectMapper = objectMapper;
        this.maxRequestBodyBytes = maxRequestBodyBytes;
        this.paths = loadSpecification();
    }

    @Override
    protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain filterChain)
            throws ServletException, IOException {
        PathSpec path = findPath(request.getRequestURI());
        if (path == null) {
            filterChain.doFilter(request, response);
            return;
        }

        String method = request.getMethod().toUpperCase(Locale.ROOT);
        OperationSpec operation = path.operations().get(method);
        if (operation == null) {
            String allow = String.join(
                    ", ", path.operations().keySet().stream().sorted().toList());
            response.setHeader("Allow", allow);
            writeProblem(
                    response,
                    HttpStatus.METHOD_NOT_ALLOWED,
                    "method-not-allowed",
                    "Method " + method + " is not allowed for this path. Allowed: " + allow,
                    !"HEAD".equals(method));
            return;
        }

        boolean hasBody = request.getContentLengthLong() > 0 || request.getHeader("Transfer-Encoding") != null;
        if (hasBody && !operation.contentTypes().isEmpty()) {
            String contentType = mediaType(request.getContentType());
            if (operation.contentTypes().stream().noneMatch(type -> type.equalsIgnoreCase(contentType))) {
                writeProblem(
                        response,
                        HttpStatus.UNSUPPORTED_MEDIA_TYPE,
                        "unsupported-media-type",
                        "Content-Type '" + contentType + "' is not supported. Supported: "
                                + String.join(", ", operation.contentTypes()),
                        true);
                return;
            }
        }

        if (request.getContentLengthLong() > maxRequestBodyBytes) {
            writePayloadTooLarge(response);
            return;
        }
        if (request.getContentLengthLong() < 0 && hasBody) {
            byte[] body = request.getInputStream().readNBytes(maxRequestBodyBytes + 1);
            if (body.length > maxRequestBodyBytes) {
                writePayloadTooLarge(response);
                return;
            }
            filterChain.doFilter(new ReplayableRequest(request, body), response);
            return;
        }
        filterChain.doFilter(request, response);
    }

    int pathCount() {
        return paths.size();
    }

    int undefinedMethodCount(Set<String> probeMethods) {
        return paths.stream()
                .mapToInt(path -> (int) probeMethods.stream()
                        .filter(method -> !path.operations().containsKey(method))
                        .count())
                .sum();
    }

    int requestBodyOperationCount() {
        return paths.stream()
                .mapToInt(path -> (int) path.operations().values().stream()
                        .filter(operation -> !operation.contentTypes().isEmpty())
                        .count())
                .sum();
    }

    private void writePayloadTooLarge(HttpServletResponse response) throws IOException {
        writeProblem(
                response,
                HttpStatus.CONTENT_TOO_LARGE,
                "payload-too-large",
                "Request body exceeds the limit of " + maxRequestBodyBytes + " bytes",
                true);
    }

    private void writeProblem(
            HttpServletResponse response, HttpStatus status, String type, String detail, boolean includeBody)
            throws IOException {
        response.setStatus(status.value());
        response.setContentType(MediaType.APPLICATION_PROBLEM_JSON_VALUE);
        if (!includeBody) {
            return;
        }
        var problem = new ProblemDetails()
                .type(type)
                .title(status.getReasonPhrase())
                .status(status.value())
                .detail(detail)
                .errors(null);
        objectMapper.writeValue(response.getOutputStream(), problem);
    }

    private PathSpec findPath(String requestPath) {
        List<String> actual = segments(requestPath);
        return paths.stream()
                .filter(path -> path.matches(actual))
                .max(Comparator.comparingInt(PathSpec::literalCount))
                .orElse(null);
    }

    private static String mediaType(String contentType) {
        if (contentType == null) {
            return "";
        }
        int separator = contentType.indexOf(';');
        return (separator < 0 ? contentType : contentType.substring(0, separator)).trim();
    }

    private static List<String> segments(String path) {
        return List.of(path.replaceAll("^/+|/+$", "").split("/"));
    }

    private static List<PathSpec> loadSpecification() {
        try (InputStream stream = HttpSemanticsFilter.class.getResourceAsStream(SPEC_RESOURCE)) {
            if (stream == null) {
                throw new IllegalStateException("OpenAPI resource is missing: " + SPEC_RESOURCE);
            }
            Map<String, Object> document = stringMap(new Yaml().load(stream));
            Map<String, Object> pathItems = stringMap(document.get("paths"));
            var result = new ArrayList<PathSpec>();
            pathItems.forEach((template, itemValue) -> {
                Map<String, Object> item = stringMap(itemValue);
                var operations = new LinkedHashMap<String, OperationSpec>();
                item.forEach((methodName, operationValue) -> {
                    String method = methodName.toUpperCase(Locale.ROOT);
                    if (!HTTP_METHODS.contains(method)) {
                        return;
                    }
                    Map<String, Object> operation = stringMap(operationValue);
                    Map<String, Object> requestBody = stringMap(operation.get("requestBody"));
                    Set<String> contentTypes = new LinkedHashSet<>(
                            stringMap(requestBody.get("content")).keySet());
                    operations.put(method, new OperationSpec(Set.copyOf(contentTypes)));
                });
                result.add(new PathSpec(segments(template), Map.copyOf(operations)));
            });
            if (result.isEmpty()) {
                throw new IllegalStateException("OpenAPI paths are empty");
            }
            return List.copyOf(result);
        } catch (IOException exception) {
            throw new IllegalStateException("Failed to load OpenAPI resource", exception);
        }
    }

    private static Map<String, Object> stringMap(Object value) {
        var result = new LinkedHashMap<String, Object>();
        if (value instanceof Map<?, ?> map) {
            map.forEach((key, item) -> {
                if (key instanceof String name) {
                    result.put(name, item);
                }
            });
        }
        return result;
    }

    private record OperationSpec(Set<String> contentTypes) {}

    private record PathSpec(List<String> segments, Map<String, OperationSpec> operations) {
        boolean matches(List<String> actual) {
            if (segments.size() != actual.size()) {
                return false;
            }
            for (int index = 0; index < segments.size(); index++) {
                String expected = segments.get(index);
                if (!(expected.startsWith("{") && expected.endsWith("}")) && !expected.equals(actual.get(index))) {
                    return false;
                }
            }
            return true;
        }

        int literalCount() {
            return (int) segments.stream()
                    .filter(segment -> !(segment.startsWith("{") && segment.endsWith("}")))
                    .count();
        }
    }

    private static final class ReplayableRequest extends HttpServletRequestWrapper {
        private final byte[] body;

        ReplayableRequest(HttpServletRequest request, byte[] body) {
            super(request);
            this.body = body.clone();
        }

        @Override
        public ServletInputStream getInputStream() {
            var input = new ByteArrayInputStream(body);
            return new ServletInputStream() {
                @Override
                public int read() {
                    return input.read();
                }

                @Override
                public int read(byte[] target, int offset, int length) {
                    return input.read(target, offset, length);
                }

                @Override
                public boolean isFinished() {
                    return input.available() == 0;
                }

                @Override
                public boolean isReady() {
                    return true;
                }

                @Override
                public void setReadListener(ReadListener listener) {
                    // MVC の同期読み取りだけを対象とする。
                }
            };
        }

        @Override
        public BufferedReader getReader() {
            return new BufferedReader(new InputStreamReader(getInputStream(), StandardCharsets.UTF_8));
        }
    }
}
