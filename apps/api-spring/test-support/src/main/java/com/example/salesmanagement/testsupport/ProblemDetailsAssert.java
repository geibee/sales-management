package com.example.salesmanagement.testsupport;

import com.example.salesmanagement.contracts.model.ProblemDetails;

public final class ProblemDetailsAssert {
    private ProblemDetailsAssert() {}

    public static void hasStatusAndInstance(ProblemDetails problem, int status, String instance) {
        if (!Integer.valueOf(status).equals(problem.getStatus())) {
            throw new AssertionError("ProblemDetails status: " + problem.getStatus());
        }
        if (!instance.equals(problem.getInstance())) {
            throw new AssertionError("ProblemDetails instance: " + problem.getInstance());
        }
    }
}
