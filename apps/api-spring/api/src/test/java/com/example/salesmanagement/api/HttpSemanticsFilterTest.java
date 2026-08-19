package com.example.salesmanagement.api;

import static org.assertj.core.api.Assertions.assertThat;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletRequest;
import java.nio.charset.StandardCharsets;
import java.util.Set;
import java.util.concurrent.atomic.AtomicReference;
import org.junit.jupiter.api.Test;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.mock.web.MockHttpServletResponse;

final class HttpSemanticsFilterTest {
    private static final ObjectMapper JSON = new ObjectMapper();

    @Test
    void specificationProducesACompleteTransportMatrix() {
        var filter = new HttpSemanticsFilter(JSON, 1024);

        assertThat(filter.pathCount()).isPositive();
        assertThat(filter.undefinedMethodCount(Set.of("GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS")))
                .isGreaterThanOrEqualTo(100);
        assertThat(filter.requestBodyOperationCount()).isGreaterThanOrEqualTo(20);
    }

    @Test
    void rejectsUndefinedMethodsAndUnsupportedMediaTypes() throws Exception {
        var filter = new HttpSemanticsFilter(JSON, 1024);

        var undefined = request("POST", "/health");
        var undefinedResponse = new MockHttpServletResponse();
        filter.doFilter(undefined, undefinedResponse, failIfForwarded());
        assertThat(undefinedResponse.getStatus()).isEqualTo(405);
        assertThat(undefinedResponse.getHeader("Allow")).isEqualTo("GET");
        assertThat(problemType(undefinedResponse)).isEqualTo("method-not-allowed");

        var unsupported = request("POST", "/lots");
        unsupported.setContentType("text/plain");
        unsupported.setContent("{}".getBytes(StandardCharsets.UTF_8));
        var unsupportedResponse = new MockHttpServletResponse();
        filter.doFilter(unsupported, unsupportedResponse, failIfForwarded());
        assertThat(unsupportedResponse.getStatus()).isEqualTo(415);
        assertThat(problemType(unsupportedResponse)).isEqualTo("unsupported-media-type");
    }

    @Test
    void rejectsDeclaredAndChunkedBodiesOverTheLimitAndReplaysSmallChunkedBodies() throws Exception {
        var filter = new HttpSemanticsFilter(JSON, 8);

        var declared = request("POST", "/lots");
        declared.setContentType("application/json");
        declared.setContent("123456789".getBytes(StandardCharsets.UTF_8));
        var declaredResponse = new MockHttpServletResponse();
        filter.doFilter(declared, declaredResponse, failIfForwarded());
        assertThat(declaredResponse.getStatus()).isEqualTo(413);
        assertThat(problemType(declaredResponse)).isEqualTo("payload-too-large");

        var chunked = new ChunkedRequest("123456789".getBytes(StandardCharsets.UTF_8));
        var chunkedResponse = new MockHttpServletResponse();
        filter.doFilter(chunked, chunkedResponse, failIfForwarded());
        assertThat(chunkedResponse.getStatus()).isEqualTo(413);
        assertThat(problemType(chunkedResponse)).isEqualTo("payload-too-large");

        var forwardedRequest = new AtomicReference<ServletRequest>();
        FilterChain capture = (request, response) -> forwardedRequest.set(request);
        byte[] smallBody = "12345678".getBytes(StandardCharsets.UTF_8);
        filter.doFilter(new ChunkedRequest(smallBody), new MockHttpServletResponse(), capture);
        assertThat(forwardedRequest.get().getInputStream().readAllBytes()).isEqualTo(smallBody);
    }

    @Test
    void passesUnknownPathsThrough() throws Exception {
        var filter = new HttpSemanticsFilter(JSON, 1024);
        var forwarded = new AtomicReference<ServletRequest>();

        filter.doFilter(
                request("POST", "/not-in-openapi"),
                new MockHttpServletResponse(),
                (request, response) -> forwarded.set(request));

        assertThat(forwarded).hasValueSatisfying(request -> assertThat(request).isNotNull());
    }

    private static MockHttpServletRequest request(String method, String path) {
        var request = new MockHttpServletRequest(method, path);
        request.setRequestURI(path);
        return request;
    }

    private static String problemType(MockHttpServletResponse response) throws Exception {
        JsonNode body = JSON.readTree(response.getContentAsByteArray());
        assertThat(response.getContentType()).startsWith("application/problem+json");
        return body.get("type").asText();
    }

    private static FilterChain failIfForwarded() {
        return (request, response) -> {
            throw new AssertionError("request must not be forwarded");
        };
    }

    private static final class ChunkedRequest extends MockHttpServletRequest {
        ChunkedRequest(byte[] body) {
            super("POST", "/lots");
            setRequestURI("/lots");
            setContentType("application/json");
            setContent(body);
        }

        @Override
        public long getContentLengthLong() {
            return -1;
        }

        @Override
        public int getContentLength() {
            return -1;
        }

        @Override
        public String getHeader(String name) {
            return "Transfer-Encoding".equalsIgnoreCase(name) ? "chunked" : super.getHeader(name);
        }
    }
}
