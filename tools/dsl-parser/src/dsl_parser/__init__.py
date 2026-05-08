"""Sales Management DSL parser.

This package implements a minimal parser for the DSL defined in
``dsl/grammar.ebnf``. The current scope is the [CORE] subset, P1-1:
only ``data <identifier> = <identifier>`` declarations are accepted.
"""

from dsl_parser.cli import main
from dsl_parser.parser import parse
from dsl_parser.spec import to_spec_dict

__all__ = ["parse", "main", "to_spec_dict"]
