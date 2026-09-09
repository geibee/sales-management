package com.example.salesmanagement.api;

import static org.assertj.core.api.Assertions.assertThat;

import com.example.salesmanagement.api.HttpPropertyFixture.CaseSnapshot;
import com.example.salesmanagement.api.HttpPropertyFixture.JsonResponse;
import com.example.salesmanagement.api.HttpPropertyFixture.LotSnapshot;
import com.example.salesmanagement.api.HttpPropertyFixture.SeededCase;
import com.fasterxml.jackson.databind.JsonNode;
import java.util.List;
import java.util.Optional;
import net.jqwik.api.Arbitraries;
import net.jqwik.api.Arbitrary;
import net.jqwik.api.Combinators;
import net.jqwik.api.ForAll;
import net.jqwik.api.Property;
import net.jqwik.api.Provide;
import net.jqwik.api.lifecycle.AfterContainer;
import net.jqwik.api.lifecycle.BeforeContainer;

/** 純粋な状態モデルと実 HTTP/DB の振る舞いを、生成した操作列で照合する。 */
final class HttpStateMachineProperties {
    private static final HttpPropertyFixture API = new HttpPropertyFixture();

    @BeforeContainer
    static void startApi() {
        API.start();
    }

    @AfterContainer
    static void stopApi() {
        API.close();
    }

    @Property(tries = 30)
    void xrPbtHttp008LotSequencesMatchModelAndRejectedRequestsAreAtomic(
            @ForAll("lotAttempts") List<LotAttempt> attempts) throws Exception {
        var seeded = API.seedLotAtPositiveVersion();
        String lotId = seeded.id();
        LotState state = LotState.MANUFACTURING;
        int version = seeded.version();

        for (LotAttempt attempt : attempts) {
            JsonNode before = API.get("/lots/" + lotId);
            LotSnapshot databaseBefore = API.lotSnapshot(lotId);
            Optional<LotState> next = nextLotState(state, attempt.command());
            JsonResponse response = executeLot(lotId, attempt.command(), attempt.stale() ? version - 1 : version);
            JsonNode after = API.get("/lots/" + lotId);

            if (next.isEmpty()) {
                assertProblem(response, 400, "invalid-state-transition");
                assertThat(after).isEqualTo(before);
                assertThat(API.lotSnapshot(lotId)).isEqualTo(databaseBefore);
            } else if (attempt.stale()) {
                assertProblem(response, 409, "optimistic-lock-conflict");
                assertThat(after).isEqualTo(before);
                assertThat(API.lotSnapshot(lotId)).isEqualTo(databaseBefore);
            } else {
                assertThat(response.status()).isEqualTo(200);
                state = next.orElseThrow();
                version++;
                assertThat(response.body().path("status").asText()).isEqualTo(state.value);
                assertThat(response.body().path("version").asInt()).isEqualTo(version);
                assertThat(after.path("status").asText()).isEqualTo(state.value);
                assertThat(after.path("version").asInt()).isEqualTo(version);
                assertLotImmutable(before, after);
            }
        }
    }

    @Property(tries = 20)
    void xrPbtHttp009DirectSequencesMatchModelAndRejectedRequestsAreAtomic(
            @ForAll("directCommands") List<DirectCommand> commands) throws Exception {
        SeededCase seeded = API.seedCase("direct");
        runCase(
                seeded,
                DirectState.BEFORE_APPRAISAL,
                commands,
                HttpStateMachineProperties::nextDirectState,
                (command, version) -> executeDirect(seeded, command, version));
    }

    @Property(tries = 20)
    void xrPbtHttp010ReservationSequencesMatchModelAndRejectedRequestsAreAtomic(
            @ForAll("reservationCommands") List<ReservationCommand> commands) throws Exception {
        SeededCase seeded = API.seedCase("reservation");
        runCase(
                seeded,
                ReservationState.BEFORE_RESERVATION,
                commands,
                HttpStateMachineProperties::nextReservationState,
                (command, version) -> executeReservation(seeded, command, version));
    }

    @Property(tries = 20)
    void xrPbtHttp011ConsignmentSequencesMatchModelAndRejectedRequestsAreAtomic(
            @ForAll("consignmentCommands") List<ConsignmentCommand> commands) throws Exception {
        SeededCase seeded = API.seedCase("consignment");
        runCase(
                seeded,
                ConsignmentState.BEFORE_CONSIGNMENT,
                commands,
                HttpStateMachineProperties::nextConsignmentState,
                (command, version) -> executeConsignment(seeded, command, version));
    }

    private static <S extends ModelState, C> void runCase(
            SeededCase seeded, S initialState, List<C> commands, Transition<S, C> transition, Executor<C> executor)
            throws Exception {
        S state = initialState;
        int version = 1;
        for (C command : commands) {
            JsonNode before = API.get("/sales-cases/" + seeded.id());
            CaseSnapshot databaseBefore = API.caseSnapshot(seeded.id());
            Optional<S> next = transition.next(state, command);
            JsonResponse response = executor.execute(command, version);
            JsonNode after = API.get("/sales-cases/" + seeded.id());

            if (next.isPresent()) {
                assertThat(response.status()).isIn(200, 204);
                state = next.orElseThrow();
                version++;
                assertThat(after.path("status").asText()).isEqualTo(state.value());
                assertThat(after.path("version").asInt()).isEqualTo(version);
                assertCaseImmutable(before, after);
            } else {
                assertThat(response.status()).isEqualTo(409);
                assertThat(response.contentType()).startsWith("application/problem+json");
                assertThat(response.body().path("type").asText()).isIn("optimistic-lock-conflict", "conflict");
                assertThat(after).isEqualTo(before);
                assertThat(API.caseSnapshot(seeded.id())).isEqualTo(databaseBefore);
            }
        }
    }

    private static JsonResponse executeLot(String lotId, LotCommand command, int version) throws Exception {
        String path = "/lots/" + lotId + "/" + command.path;
        String body =
                switch (command) {
                    case COMPLETE_MANUFACTURING -> "{\"date\":\"2026-04-22\",\"version\":" + version + "}";
                    case INSTRUCT_SHIPPING -> "{\"deadline\":\"2026-04-25\",\"version\":" + version + "}";
                    case COMPLETE_SHIPPING -> "{\"date\":\"2026-04-24\",\"version\":" + version + "}";
                    case CANCEL_MANUFACTURING, CANCEL_CONVERSION -> "{\"version\":" + version + "}";
                    case INSTRUCT_CONVERSION -> "{\"destinationItem\":\"変換先品目\",\"version\":" + version + "}";
                };
        return API.request(command.method, path, body);
    }

    private static JsonResponse executeDirect(SeededCase seeded, DirectCommand command, int version) throws Exception {
        String base = "/sales-cases/" + seeded.id();
        return switch (command) {
            case CREATE_APPRAISAL -> API.request("POST", base + "/appraisals", appraisalBody(seeded.lotId(), version));
            case UPDATE_APPRAISAL -> API.request("PUT", base + "/appraisals", appraisalBody(seeded.lotId(), version));
            case DELETE_APPRAISAL -> API.request("DELETE", base + "/appraisals", versionBody(version));
            case CREATE_CONTRACT -> API.request("POST", base + "/contracts", contractBody(version));
            case DELETE_CONTRACT -> API.request("DELETE", base + "/contracts", versionBody(version));
            case INSTRUCT_SHIPPING ->
                API.request("POST", base + "/shipping-instruction", dateVersionBody("date", "2026-02-10", version));
            case CANCEL_SHIPPING -> API.request("DELETE", base + "/shipping-instruction", versionBody(version));
            case COMPLETE_SHIPPING ->
                API.request("POST", base + "/shipping-completion", dateVersionBody("date", "2026-02-20", version));
        };
    }

    private static JsonResponse executeReservation(SeededCase seeded, ReservationCommand command, int version)
            throws Exception {
        String base = "/sales-cases/" + seeded.id() + "/reservation";
        return switch (command) {
            case CREATE_APPRAISAL ->
                API.request(
                        "POST",
                        base + "/appraisals",
                        """
                    {"appraisalDate":"2026-01-20","reservedLotInfo":"reserved-info",
                     "reservedAmount":500000,"version":%d}
                    """
                                .formatted(version));
            case DETERMINE ->
                API.request(
                        "POST",
                        base + "/determine",
                        "{\"determinedDate\":\"2026-01-22\",\"determinedAmount\":480000,\"version\":" + version + "}");
            case CANCEL_DETERMINATION -> API.request("DELETE", base + "/determination", versionBody(version));
            case DELIVER ->
                API.request("POST", base + "/delivery", dateVersionBody("deliveryDate", "2026-01-30", version));
        };
    }

    private static JsonResponse executeConsignment(SeededCase seeded, ConsignmentCommand command, int version)
            throws Exception {
        String base = "/sales-cases/" + seeded.id() + "/consignment";
        return switch (command) {
            case DESIGNATE ->
                API.request(
                        "POST",
                        base + "/designate",
                        """
                    {"consignorName":"Acme","consignorCode":"C001",
                     "designatedDate":"2026-01-25","version":%d}
                    """
                                .formatted(version));
            case CANCEL_DESIGNATION -> API.request("DELETE", base + "/designation", versionBody(version));
            case ENTER_RESULT ->
                API.request(
                        "POST",
                        base + "/result",
                        "{\"resultDate\":\"2026-01-30\",\"resultAmount\":480000,\"version\":" + version + "}");
        };
    }

    private static Optional<LotState> nextLotState(LotState state, LotCommand command) {
        return switch (state) {
            case MANUFACTURING ->
                command == LotCommand.COMPLETE_MANUFACTURING ? Optional.of(LotState.MANUFACTURED) : Optional.empty();
            case MANUFACTURED ->
                switch (command) {
                    case INSTRUCT_SHIPPING -> Optional.of(LotState.SHIPPING_INSTRUCTED);
                    case CANCEL_MANUFACTURING -> Optional.of(LotState.MANUFACTURING);
                    case INSTRUCT_CONVERSION -> Optional.of(LotState.CONVERSION_INSTRUCTED);
                    default -> Optional.empty();
                };
            case SHIPPING_INSTRUCTED ->
                command == LotCommand.COMPLETE_SHIPPING ? Optional.of(LotState.SHIPPED) : Optional.empty();
            case CONVERSION_INSTRUCTED ->
                command == LotCommand.CANCEL_CONVERSION ? Optional.of(LotState.MANUFACTURED) : Optional.empty();
            case SHIPPED -> Optional.empty();
        };
    }

    private static Optional<DirectState> nextDirectState(DirectState state, DirectCommand command) {
        return switch (state) {
            case BEFORE_APPRAISAL ->
                command == DirectCommand.CREATE_APPRAISAL ? Optional.of(DirectState.APPRAISED) : Optional.empty();
            case APPRAISED ->
                switch (command) {
                    case UPDATE_APPRAISAL -> Optional.of(DirectState.APPRAISED);
                    case DELETE_APPRAISAL -> Optional.of(DirectState.BEFORE_APPRAISAL);
                    case CREATE_CONTRACT -> Optional.of(DirectState.CONTRACTED);
                    default -> Optional.empty();
                };
            case CONTRACTED ->
                switch (command) {
                    case DELETE_CONTRACT -> Optional.of(DirectState.APPRAISED);
                    case INSTRUCT_SHIPPING -> Optional.of(DirectState.SHIPPING_INSTRUCTED);
                    default -> Optional.empty();
                };
            case SHIPPING_INSTRUCTED ->
                switch (command) {
                    case CANCEL_SHIPPING -> Optional.of(DirectState.CONTRACTED);
                    case COMPLETE_SHIPPING -> Optional.of(DirectState.SHIPPING_COMPLETED);
                    default -> Optional.empty();
                };
            case SHIPPING_COMPLETED -> Optional.empty();
        };
    }

    private static Optional<ReservationState> nextReservationState(ReservationState state, ReservationCommand command) {
        return switch (state) {
            case BEFORE_RESERVATION ->
                command == ReservationCommand.CREATE_APPRAISAL
                        ? Optional.of(ReservationState.RESERVED)
                        : Optional.empty();
            case RESERVED ->
                command == ReservationCommand.DETERMINE ? Optional.of(ReservationState.CONFIRMED) : Optional.empty();
            case CONFIRMED ->
                switch (command) {
                    case CANCEL_DETERMINATION -> Optional.of(ReservationState.RESERVED);
                    case DELIVER -> Optional.of(ReservationState.DELIVERED);
                    default -> Optional.empty();
                };
            case DELIVERED -> Optional.empty();
        };
    }

    private static Optional<ConsignmentState> nextConsignmentState(ConsignmentState state, ConsignmentCommand command) {
        return switch (state) {
            case BEFORE_CONSIGNMENT ->
                command == ConsignmentCommand.DESIGNATE ? Optional.of(ConsignmentState.DESIGNATED) : Optional.empty();
            case DESIGNATED ->
                switch (command) {
                    case CANCEL_DESIGNATION -> Optional.of(ConsignmentState.BEFORE_CONSIGNMENT);
                    case ENTER_RESULT -> Optional.of(ConsignmentState.RESULT_ENTERED);
                    default -> Optional.empty();
                };
            case RESULT_ENTERED -> Optional.empty();
        };
    }

    private static void assertProblem(JsonResponse response, int status, String type) {
        assertThat(response.status()).isEqualTo(status);
        assertThat(response.contentType()).startsWith("application/problem+json");
        assertThat(response.body().path("type").asText()).isEqualTo(type);
    }

    private static void assertLotImmutable(JsonNode before, JsonNode after) {
        for (String field : List.of("lotNumber", "division", "department", "section", "details")) {
            assertThat(after.path(field)).as(field).isEqualTo(before.path(field));
        }
    }

    private static void assertCaseImmutable(JsonNode before, JsonNode after) {
        for (String field : List.of("salesCaseNumber", "caseType", "lots", "divisionCode", "salesDate")) {
            assertThat(after.path(field)).as(field).isEqualTo(before.path(field));
        }
    }

    private static String appraisalBody(String lot, int version) {
        return """
                {"type":"normal","appraisalDate":"2026-03-03","deliveryDate":"2026-03-15",
                 "salesMarket":"market","baseUnitPriceDate":"2026-03-01",
                 "periodAdjustmentRateDate":"2026-03-01","counterpartyAdjustmentRateDate":"2026-03-01",
                 "taxExcludedEstimatedTotal":100000,
                 "lotAppraisals":[{"lotNumber":"%s","detailAppraisals":[{"detailIndex":1,
                 "baseUnitPrice":1000,"periodAdjustmentRate":1.0,"counterpartyAdjustmentRate":1.0}]}],
                 "version":%d}
                """
                .formatted(lot, version);
    }

    private static String contractBody(int version) {
        return """
                {"contractDate":"2026-03-08","person":"person","buyer":{"customerNumber":"C98001"},
                 "salesType":1,"item":"item","deliveryMethod":"method",
                 "paymentDeferralCondition":"","salesMethod":1,"usage":"",
                 "taxExcludedContractAmount":100000,"consumptionTax":10000,
                 "taxExcludedPaymentAmount":100000,"paymentConsumptionTax":10000,"version":%d}
                """
                .formatted(version);
    }

    private static String versionBody(int version) {
        return "{\"version\":" + version + "}";
    }

    private static String dateVersionBody(String field, String date, int version) {
        return "{\"" + field + "\":\"" + date + "\",\"version\":" + version + "}";
    }

    @Provide
    Arbitrary<List<LotAttempt>> lotAttempts() {
        return Combinators.combine(Arbitraries.of(LotCommand.values()), Arbitraries.of(false, true))
                .as(LotAttempt::new)
                .list()
                .ofMinSize(0)
                .ofMaxSize(20);
    }

    @Provide
    Arbitrary<List<DirectCommand>> directCommands() {
        return commandLists(DirectCommand.values());
    }

    @Provide
    Arbitrary<List<ReservationCommand>> reservationCommands() {
        return commandLists(ReservationCommand.values());
    }

    @Provide
    Arbitrary<List<ConsignmentCommand>> consignmentCommands() {
        return commandLists(ConsignmentCommand.values());
    }

    private static <C> Arbitrary<List<C>> commandLists(C[] commands) {
        return Arbitraries.of(commands).list().ofMinSize(0).ofMaxSize(12);
    }

    private interface ModelState {
        String value();
    }

    @FunctionalInterface
    private interface Transition<S, C> {
        Optional<S> next(S state, C command);
    }

    @FunctionalInterface
    private interface Executor<C> {
        JsonResponse execute(C command, int version) throws Exception;
    }

    private enum LotState {
        MANUFACTURING("manufacturing"),
        MANUFACTURED("manufactured"),
        SHIPPING_INSTRUCTED("shipping_instructed"),
        SHIPPED("shipped"),
        CONVERSION_INSTRUCTED("conversion_instructed");

        private final String value;

        LotState(String value) {
            this.value = value;
        }
    }

    private enum LotCommand {
        COMPLETE_MANUFACTURING("POST", "complete-manufacturing"),
        INSTRUCT_SHIPPING("POST", "instruct-shipping"),
        COMPLETE_SHIPPING("POST", "complete-shipping"),
        CANCEL_MANUFACTURING("POST", "cancel-manufacturing-completion"),
        INSTRUCT_CONVERSION("POST", "instruct-item-conversion"),
        CANCEL_CONVERSION("DELETE", "instruct-item-conversion");

        private final String method;
        private final String path;

        LotCommand(String method, String path) {
            this.method = method;
            this.path = path;
        }
    }

    private record LotAttempt(LotCommand command, boolean stale) {}

    private enum DirectState implements ModelState {
        BEFORE_APPRAISAL("before_appraisal"),
        APPRAISED("appraised"),
        CONTRACTED("contracted"),
        SHIPPING_INSTRUCTED("shipping_instructed"),
        SHIPPING_COMPLETED("shipping_completed");

        private final String value;

        DirectState(String value) {
            this.value = value;
        }

        @Override
        public String value() {
            return value;
        }
    }

    private enum DirectCommand {
        CREATE_APPRAISAL,
        UPDATE_APPRAISAL,
        DELETE_APPRAISAL,
        CREATE_CONTRACT,
        DELETE_CONTRACT,
        INSTRUCT_SHIPPING,
        CANCEL_SHIPPING,
        COMPLETE_SHIPPING
    }

    private enum ReservationState implements ModelState {
        BEFORE_RESERVATION("before_reservation"),
        RESERVED("reserved"),
        CONFIRMED("reservation_confirmed"),
        DELIVERED("reservation_delivered");

        private final String value;

        ReservationState(String value) {
            this.value = value;
        }

        @Override
        public String value() {
            return value;
        }
    }

    private enum ReservationCommand {
        CREATE_APPRAISAL,
        DETERMINE,
        CANCEL_DETERMINATION,
        DELIVER
    }

    private enum ConsignmentState implements ModelState {
        BEFORE_CONSIGNMENT("before_consignment"),
        DESIGNATED("consignment_designated"),
        RESULT_ENTERED("consignment_result_entered");

        private final String value;

        ConsignmentState(String value) {
            this.value = value;
        }

        @Override
        public String value() {
            return value;
        }
    }

    private enum ConsignmentCommand {
        DESIGNATE,
        CANCEL_DESIGNATION,
        ENTER_RESULT
    }
}
