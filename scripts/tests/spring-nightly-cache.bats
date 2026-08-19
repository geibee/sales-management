#!/usr/bin/env bats
# Trivy は Java 依存の POM を標準の ~/.m2/repository から探索する。
# Maven だけを別ディレクトリへ向けると、取得済み POM を再利用できず Maven Central の
# レート制限を受けるため、workflow・Maven・Trivy のキャッシュ契約を固定する。

setup() {
  SPRING_CI_SCRIPT="$BATS_TEST_DIRNAME/../../apps/api-spring/ci.sh"
  SPRING_NIGHTLY_WORKFLOW="$BATS_TEST_DIRNAME/../../.github/workflows/spring-nightly.yml"
}

@test "Spring nightly は Maven と Trivy で標準ローカルリポジトリを共有する" {
  ci_script=$(<"$SPRING_CI_SCRIPT")
  workflow=$(<"$SPRING_NIGHTLY_WORKFLOW")

  [[ "$ci_script" == *'MAVEN_REPOSITORY="${HOME:?HOME が設定されていません}/.m2/repository"'* ]]
  [[ "$ci_script" != *'MAVEN_USER_HOME='* ]]
  [[ "$workflow" != *'MAVEN_USER_HOME:'* ]]
}

@test "setup-java のキャッシュキーは Spring の全 pom.xml を追跡する" {
  workflow=$(<"$SPRING_NIGHTLY_WORKFLOW")

  [[ "$workflow" == *'cache-dependency-path: apps/api-spring/**/pom.xml'* ]]
}
