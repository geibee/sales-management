package com.example.salesmanagement.testsupport;

import java.util.SplittableRandom;

public final class Generators {
    private final SplittableRandom random;

    public Generators(long seed) {
        random = new SplittableRandom(seed);
    }

    public String lotNumber() {
        return "2026-T%02d-%03d".formatted(random.nextInt(100), random.nextInt(1, 1000));
    }
}
