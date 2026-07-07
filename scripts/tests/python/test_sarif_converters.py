"""SARIF 変換スクリプト (junit/lint/zap-to-sarif, sarif-merge) の単体テスト。

merged.sarif は LESSONS.md 自動更新とエージェント参照の入力なので、
変換の levels / 構造 / 不正入力時の挙動を固定する (issue #9 Tier2-17)。
"""

import json

JUNIT = "apps/api-fsharp/scripts/junit-to-sarif.py"
LINT = "apps/api-fsharp/scripts/lint-to-sarif.py"
ZAP = "apps/api-fsharp/scripts/zap-to-sarif.py"
MERGE = "apps/api-fsharp/scripts/sarif-merge.py"


def read_sarif(path) -> dict:
    data = json.loads(path.read_text())
    assert data["version"] == "2.1.0"
    return data


# ---------------------------------------------------------------- junit-to-sarif


def test_junit_failure_は_warning_で記録し_passed_は記録しない(load_script, tmp_path, set_argv):
    mod = load_script(JUNIT)
    src = tmp_path / "junit.xml"
    dst = tmp_path / "out.sarif"
    src.write_text(
        """<testsuites>
  <testsuite name="schemathesis">
    <testcase classname="schemathesis" name="GET /lots" time="1.0"/>
    <testcase classname="schemathesis" name="POST /lots" time="1.0">
      <failure message="server_error" type="failure.http.server_error">500</failure>
    </testcase>
    <testcase classname="schemathesis" name="GET /health" time="0.1">
      <skipped message="excluded"/>
    </testcase>
  </testsuite>
</testsuites>
"""
    )
    set_argv(str(src), str(dst))
    assert mod.main() == 0

    results = read_sarif(dst)["runs"][0]["results"]
    assert [(r["ruleId"], r["level"]) for r in results] == [
        ("failure.http.server_error", "warning"),
        ("schemathesis.skipped", "note"),
    ]
    assert "POST /lots" in results[0]["message"]["text"]


def test_junit_不正な_XML_は失敗(load_script, tmp_path, set_argv):
    mod = load_script(JUNIT)
    src = tmp_path / "junit.xml"
    src.write_text("not xml at all <")
    set_argv(str(src), str(tmp_path / "out.sarif"))
    assert mod.main() == 1


def test_junit_入力欠如は失敗(load_script, tmp_path, set_argv):
    mod = load_script(JUNIT)
    set_argv(str(tmp_path / "missing.xml"), str(tmp_path / "out.sarif"))
    assert mod.main() == 1


# ---------------------------------------------------------------- lint-to-sarif


def test_lint_警告行を位置情報つきで変換する(load_script, tmp_path, set_argv):
    mod = load_script(LINT)
    src = tmp_path / "lint.txt"
    dst = tmp_path / "out.sarif"
    src.write_text(
        "src/SalesManagement/Domain/Types.fs(42,5): warning FL0015: ネストが深すぎます\n"
        "src/SalesManagement/Api/Routes.fs(7,1): error FL0029: ファイルが長すぎます\n"
        "========== Finished: 2 warnings ==========\n"
    )
    set_argv(str(src), str(dst))
    assert mod.main() == 0

    results = read_sarif(dst)["runs"][0]["results"]
    assert [(r["ruleId"], r["level"]) for r in results] == [
        ("FL0015", "warning"),
        ("FL0029", "error"),
    ]
    loc = results[0]["locations"][0]["physicalLocation"]
    assert loc["artifactLocation"]["uri"] == "src/SalesManagement/Domain/Types.fs"
    assert loc["region"] == {"startLine": 42, "startColumn": 5}


# ---------------------------------------------------------------- zap-to-sarif


def test_zap_riskcode_を_SARIF_level_にマップする(load_script):
    mod = load_script(ZAP)
    assert mod.risk_to_level(3) == "error"
    assert mod.risk_to_level("2") == "warning"
    assert mod.risk_to_level(1) == "warning"
    assert mod.risk_to_level(0) == "note"
    assert mod.risk_to_level("garbage") == "note"


def test_zap_アラートを_instance_ごとの_result_に展開する(load_script, tmp_path, set_argv):
    mod = load_script(ZAP)
    src = tmp_path / "zap.json"
    dst = tmp_path / "out.sarif"
    src.write_text(
        json.dumps(
            {
                "@version": "2.15.0",
                "site": [
                    {
                        "@name": "http://localhost:5000",
                        "alerts": [
                            {
                                "pluginid": "10038",
                                "name": "CSP Header Not Set",
                                "riskcode": "2",
                                "desc": "desc",
                                "instances": [{"uri": "http://localhost:5000/lots"}, {"uri": "http://localhost:5000/health"}],
                            }
                        ],
                    }
                ],
            }
        )
    )
    set_argv(str(src), str(dst))
    assert mod.main() == 0

    run = read_sarif(dst)["runs"][0]
    assert len(run["results"]) == 2
    assert all(r["level"] == "warning" for r in run["results"])
    assert run["tool"]["driver"]["rules"][0]["id"] == "10038"


def test_zap_不正な_JSON_は失敗(load_script, tmp_path, set_argv):
    mod = load_script(ZAP)
    src = tmp_path / "zap.json"
    src.write_text("{broken")
    set_argv(str(src), str(tmp_path / "out.sarif"))
    assert mod.main() == 1


# ---------------------------------------------------------------- sarif-merge


def sarif_with(results_count: int) -> str:
    return json.dumps(
        {
            "version": "2.1.0",
            "runs": [{"tool": {"driver": {"name": "t"}}, "results": [{"ruleId": f"r{i}"} for i in range(results_count)]}],
        }
    )


def test_merge_は_runs_を素朴に連結し_欠損と不正入力をスキップする(load_script, tmp_path, set_argv):
    mod = load_script(MERGE)
    a = tmp_path / "a.sarif"
    b = tmp_path / "b.sarif"
    broken = tmp_path / "broken.sarif"
    a.write_text(sarif_with(2))
    b.write_text(sarif_with(1))
    broken.write_text("{oops")
    dst = tmp_path / "merged.sarif"

    set_argv(str(dst), str(a), str(tmp_path / "missing.sarif"), str(broken), str(b))
    assert mod.main() == 0

    merged = read_sarif(dst)
    assert len(merged["runs"]) == 2
    assert sum(len(r["results"]) for r in merged["runs"]) == 3


def test_merge_引数不足は_usage_エラー(load_script, tmp_path, set_argv):
    mod = load_script(MERGE)
    set_argv(str(tmp_path / "merged.sarif"))
    assert mod.main() == 2
