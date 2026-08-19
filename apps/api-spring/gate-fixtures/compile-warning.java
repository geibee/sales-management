// Red fixture: javac -Xlint:all -Werror は raw type を検出しなければならない。
package fixture.compile;

import java.util.List;

final class CompileWarningViolation {
    List copy(List values) {
        return values;
    }
}
