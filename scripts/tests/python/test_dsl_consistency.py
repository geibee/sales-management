"""dsl-consistency.py (DSL ↔ コード整合ゲート) の単体テスト。

「behavior 追加 → 注釈なし/実装なしで赤になる」ことと fail-closed
(behavior が 1 件も列挙できない = 失敗) を固定する (issue #9 §3)。
"""

import pytest

SCRIPT = "apps/api-fsharp/scripts/dsl-consistency.py"


@pytest.fixture
def gate(load_script, tmp_path, monkeypatch):
    mod = load_script(SCRIPT)
    dsl = tmp_path / "dsl" / "domain-model.md"
    src = tmp_path / "src" / "SalesManagement"
    dsl.parent.mkdir(parents=True)
    src.mkdir(parents=True)
    monkeypatch.setattr(mod, "dsl_path", dsl)
    monkeypatch.setattr(mod, "src_root", src)

    def _run(dsl_text: str, modules: dict[str, str]) -> int:
        dsl.write_text(dsl_text, encoding="utf-8")
        for name, body in modules.items():
            (src / f"{name}.fs").write_text(body, encoding="utf-8")
        return mod.main()

    return _run


DSL_OK = "behavior 製造完了を指示する = A -> B OR E  // fn: LotWorkflows.completeManufacturing\n"


def test_annotated_behavior_with_existing_fn_passes(gate):
    assert gate(DSL_OK, {"LotWorkflows": "let completeManufacturing x = x\n"}) == 0


def test_private_and_rec_definitions_count_as_existing(gate):
    dsl = (
        "behavior A案件 = X -> Y  // fn: M.privFn\n"
        "behavior B案件 = X -> Y  // fn: M.recFn\n"
    )
    assert gate(dsl, {"M": "let private privFn x = x\nlet rec recFn x = recFn x\n"}) == 0


def test_behavior_without_annotation_fails(gate):
    assert gate("behavior 注釈なし = A -> B\n", {"LotWorkflows": "let f x = x\n"}) == 1


def test_missing_function_fails(gate):
    assert gate(DSL_OK, {"LotWorkflows": "let somethingElse x = x\n"}) == 1


def test_missing_module_file_fails(gate):
    assert gate(DSL_OK, {"OtherModule": "let completeManufacturing x = x\n"}) == 1


def test_substring_function_name_does_not_match(gate):
    # completeManufacturingCompletion は completeManufacturing の定義にならない
    assert gate(DSL_OK, {"LotWorkflows": "let completeManufacturingX x = x\n"}) == 1


def test_no_behaviors_at_all_is_fail_closed(gate):
    assert gate("# behaviors はまだ無い\n", {"LotWorkflows": "let f x = x\n"}) == 1


def test_missing_dsl_file_is_fail_closed(load_script, tmp_path, monkeypatch):
    mod = load_script(SCRIPT)
    monkeypatch.setattr(mod, "dsl_path", tmp_path / "nope.md")
    monkeypatch.setattr(mod, "src_root", tmp_path)
    assert mod.main() == 1
