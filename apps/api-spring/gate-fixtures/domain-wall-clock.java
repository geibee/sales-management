// Red fixture: domain source rule は壁時計を検出しなければならない。
package fixture.domain;

final class DomainWallClockViolation {
    long now() {
        return System.currentTimeMillis();
    }
}
