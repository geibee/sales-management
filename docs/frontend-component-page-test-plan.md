# DD-03 フロントエンド Component/Page Test 計画 (rev2)

## Summary

component/page test は `Vitest + Testing Library + MSW` を主戦場にし、E2E は代表導線だけに絞る。純粋関数は `fast-check` で property-based test (PBT) を本格運用する。

本書はテスト実装・レビュー時の oracle として使えるように、テスト ID、関連設計 ID、MSW scenario、validation matrix、状態別操作可否、PBT 戦略、**現状ステータス**、**既知制約**、**Phase ロードマップ**を一冊にまとめる。新しい画面 / 振る舞いを追加するときは「拡充手順」§ に沿って ID を発行し、状態列を更新する。

関連 BD が未整備の場合は、本書の `関連ID` を BD-01 作成時の traceability entry に転記する。

## 現状ステータス (2026-05-31 時点)

| 集計 | 値 |
|---|---|
| テストファイル数 | 25 |
| passing tests | 156 |
| todo | 3 |
| 既知 Red (未実装 ID) | 後述「Phase ロードマップ」参照 |

| Phase | 状態 | 主な未消化 ID |
|---|---|---|
| 0 MSW infra | ✅ | — |
| 1 Test infra | ✅ | — |
| 2a Guard | ✅ | — |
| 2b LotActionForm | ✅ | `FE-COMP-LOT-ACTION-007` (todo: 未 touched 検査) |
| 2c RichActionForms | ⚠️ | `FE-COMP-RICH-SC-002 / SC-003` (契約調整率 89 / 100 → 1.0) |
| 2d a11y form | ✅ | — |
| 2e LotSelectDialog | ✅ | — |
| 2f SalesCaseCreateDialog | ✅ | — |
| 2g Validation policy | ✅ | — |
| 3a LotCreatePage | ✅ | cascade の段階遷移詳細は §拡充手順 参照 |
| 3b LotDetailPage | ⚠️ | `FE-MATRIX-LOT-*` (全状態 × 6 action)、`FE-VERSION-LOT-002..006` (cancel-mfg / instruct-ship / complete-ship / instruct-conv / cancel-conv の version body) |
| 3c CSV | ✅ | — |
| 3d LotListPage | ✅ | — |
| 4a SalesCaseCreatePage | ✅ | — |
| 4b SalesCaseDetailPage | ⚠️ | `FE-REQ-SALES-ACTION-001 / 003` (POST appraisals / contracts rate ÷100)、`FE-VERSION-SALES-002 / 003` (delete contract / cancel shipping instruction) |
| 4c Reservation / Consignment Detail | ⚠️ | `FE-REQ-RESERVATION-001` / `FE-REQ-CONSIGNMENT-001` (POST rich body の rate ÷100) |
| 4d ロット修正 | ✅ | — |
| 4e 査定合計 / 上長承認 | ⚠️ | page 層では未。RichActionForms (organism) で `FE-COMP-RICH-DA-004..006` のみカバー。`FE-TOTAL-001..005` / `FE-APPROVER-001..003` の page wire-up 未 |
| 5 PriceCheckPage | ✅ | — |
| 6 Router integration | ❌ | `FE-NAV-LOT-001` / `FE-NAV-SALES-001..003` (本物 `routeTree.gen` で navigate 解決) / `FE-NAV-AUTH-001` (実 route 上の Guard fallback) |
| 7 describeApiError unit | ⚠️ | `tests/unit/api-client.test.ts` に 4 件あるのみ。`FE-ERR-001..010` の全 variant 未網羅 |
| 8 Evidence / CI | ❌ | JUnit reporter / coverage artifact / MSW request log |
| 9 PBT | ❌ | 本書 §PBT で計画化。`fast-check` 未導入 |

凡例: ✅ 全 ID green / ⚠️ 一部 ID 未消化 / ❌ 未着手

## ID 体系

| Prefix | 用途 |
|---|---|
| `FE-COMP-*` | 再利用 component の単体振る舞い (organism / molecule 層) |
| `FE-PAGE-*` | page component の表示・操作・API response handling |
| `FE-REQ-*` | MSW request assertion (URL / method / body / query) |
| `FE-VERSION-*` | optimistic-lock 対象 action の version 必須検査 |
| `FE-REFETCH-*` | mutation 後の SWR 再取得 (GET count) |
| `FE-MATRIX-*` | 状態 × action の表 (Lot status × action) |
| `FE-NAV-*` | 本物 Router を使う navigation integration |
| `FE-A11Y-*` | DOM/ARIA/keyboard の機械検査 |
| `FE-RATE-*` / `FE-TOTAL-*` / `FE-APPROVER-*` | 調整率スケール変換 / 査定合計 / 上長承認 oracle |
| `FE-ERR-*` | `describeApiError` の unit oracle |
| `FE-CSV-*` | CSV / blob download oracle |
| `FE-VAL-POLICY-*` | form 共通 validation 表示ポリシー |
| `FE-STAB-*` | テスト stability の指針 |
| `FE-PBT-*` | fast-check property |
| `FE-EVID-*` | evidence / CI artifact 方針 |

## 関連設計 ID

| 関連ID | 種別 | 内容 |
|---|---|---|
| `AUTH-ROLE-HIERARCHY` | BD-03 | `admin > operator > viewer` のロール階層 |
| `AUTH-GUARD-FALLBACK` | BD-03 / BD-05 | 権限不足時は対象 action area を表示せず fallback UI を表示する |
| `BR-LOT-STATE-ACTION` | BD-04 | ロット状態ごとの状態遷移 action 可否 |
| `BR-VERSION-REQUIRED` | BD-04 / BD-06 | 競合制御対象 action は request body に `version` を含める |
| `BR-LOT-MANUFACTURED-REQUIRED` | BD-04 | 販売案件に組み込めるロットは `manufactured` 状態に限定する |
| `BR-LOT-ASSIGNMENT-UNIQUE` | BD-04 | 1ロットは高々1案件に割り当てられる |
| `BR-ADJUSTMENT-RATE-RANGE` | BD-04 / BD-06 | 調整率は画面 90〜110%、API `0.9〜1.1` (÷100) の範囲 |
| `BR-APPRAISAL-TOTAL-FORMULA` | BD-04 | 税抜査定合計 = Σ (基準単価 × 各調整率 ÷ 100) |
| `UI-LOT-LIST` | BD-05 | `/lots` 在庫ロット一覧 |
| `UI-LOT-CREATE` | BD-05 | `/lots/new` 在庫ロット作成 |
| `UI-LOT-DETAIL` | BD-05 | `/lots/:id` 在庫ロット詳細・状態遷移 |
| `UI-SALES-CREATE` | BD-05 | `/sales-cases/new` 販売案件作成 |
| `UI-SALES-CREATE-DIALOG` | BD-05 | ロット一覧からの販売案件新規登録モーダル |
| `UI-LOT-SELECT-DIALOG` | BD-05 | 製造完了かつ未割当ロットの選択モーダル |
| `UI-SALES-DETAIL` | BD-05 | `/sales-cases/:id` 直接販売案件詳細 |
| `UI-RESERVATION-DETAIL` | BD-05 | `/reservation-cases/:id` 予約販売案件詳細 |
| `UI-CONSIGNMENT-DETAIL` | BD-05 | `/consignment-cases/:id` 委託販売案件詳細 |
| `UI-PRICE-CHECK` | BD-05 | `/external/price-check` 外部価格チェック |
| `UI-APPRAISAL-APPROVAL` | BD-05 | 査定合計の直接入力切替には上長承認モーダルを介する |
| `API-CODE-MASTERS` | DD-01 | `GET /code-masters` コード名称マスタ (階層・フラット) |
| `API-LOT-AVAILABLE` | DD-01 | `GET /lots/available?excludeCase=` 製造完了かつ未割当ロット |
| `API-SALES-CASE-LOTS` | DD-01 | `PUT /sales-cases/{id}/lots` 案件ロット差し替え |
| `API-PROBLEM-DETAILS` | DD-01 | problem detail / validation error の表示方針 |

## 対象画面一覧

| 画面ID | route | 対象 role | 主要状態 | 主要テスト ID | 状態 |
|---|---|---|---|---|---|
| `UI-LOT-LIST` | `/lots` | viewer, operator | loading, success, error, 行選択, 案件新規登録導線 | `FE-PAGE-LOT-LIST-*`, `FE-COMP-SALES-CREATE-DIALOG-*` | ✅ |
| `UI-LOT-CREATE` | `/lots/new` | operator | fallback, ready, pending, success, API error, code-master cascade | `FE-PAGE-LOT-CREATE-*`, `FE-REQ-LOT-CREATE-*`, `FE-NAV-LOT-001` | ⚠️ NAV 未 |
| `UI-LOT-DETAIL` | `/lots/:id` | viewer, operator | loading, success, error, action matrix, conflict, csv, 明細, 名称 | `FE-PAGE-LOT-DETAIL-*`, `FE-REQ-LOT-ACTION-*`, `FE-MATRIX-LOT-*`, `FE-VERSION-LOT-*` | ⚠️ MATRIX/VERSION 未 |
| `UI-SALES-CREATE` | `/sales-cases/new` | operator | fallback, validation, pending, success, API error, ロット選択モーダル | `FE-PAGE-SALES-CREATE-*`, `FE-REQ-SALES-CREATE-*`, `FE-NAV-SALES-*` | ⚠️ NAV-002/003 (real router) 未 |
| `UI-SALES-DETAIL` | `/sales-cases/:id` | viewer, operator, admin | loading, success, error, rich actions, ロット修正, 査定合計, 上長承認 | `FE-PAGE-SALES-DETAIL-*`, `FE-REQ-SALES-ACTION-*`, `FE-REQ-SALES-LOTS-*`, `FE-VERSION-SALES-*`, `FE-TOTAL-*`, `FE-APPROVER-*` | ⚠️ ACTION-001/003, VERSION-002/003, TOTAL/APPROVER 未 |
| `UI-RESERVATION-DETAIL` | `/reservation-cases/:id` | viewer, operator | loading, success, error, rich actions, cancel | `FE-PAGE-RESERVATION-*`, `FE-REQ-RESERVATION-*`, `FE-VERSION-RES-*` | ⚠️ REQ-RESERVATION-001 未 |
| `UI-CONSIGNMENT-DETAIL` | `/consignment-cases/:id` | viewer, operator | loading, success, error, rich actions, cancel, ロット修正 | `FE-PAGE-CONSIGNMENT-*`, `FE-REQ-CONSIGNMENT-*`, `FE-REQ-CONSIGNMENT-LOTS-*`, `FE-VERSION-CON-*` | ⚠️ REQ-CONSIGNMENT-001 未 |
| `UI-PRICE-CHECK` | `/external/price-check` | viewer | idle, loading, success, 400, 502, 503, unknown error | `FE-PAGE-PRICE-*`, `FE-REQ-PRICE-*` | ✅ |

## Test Infrastructure

| ファイル | 役割 | 必須仕様 | 状態 |
|---|---|---|---|
| `tests/support/render.tsx` | render helper | `renderWithApp(...)` と `renderWithRouter(...)` を提供する。後者は catch-all root route で即席起動する。本物 `routeTree.gen` 起動用の `renderWithRealRouter(initialPath)` は Phase 6 で追加する。 | ⚠️ realRouter 未 |
| `tests/support/server.ts` | MSW server | `setupServer()` インスタンス、`request:start` で全リクエストを `capturedRequests` に蓄積、`resetCapturedRequests()` / `requestsFor(pathname)` / `requestCount(pathname)` を提供する。 | ✅ |
| `tests/support/fixtures.ts` | API fixture | `lot`, `salesCase`, `problem`, `priceQuote`, `codeMasters`, `availableLot` の factory を提供する。 | ✅ |
| `tests/support/deferred.ts` | loading 固定 | `deferred<T>()` を提供し、loading を pending promise で固定できる。 | ✅ |
| `tests/setup.ts` | 共通 setup | MSW lifecycle、`toast` no-op、`window.confirm` 既定 true、`URL.createObjectURL` / `revokeObjectURL` / `HTMLAnchorElement.click` mock、`useAuth.clear()` を初期化する。 | ✅ |

### Atomic Design 構成 (テスト配置とレイヤごとの責務)

`src/components/` と `tests/unit/` は atoms / molecules / organisms / templates / pages の 5 層で並行する。テストはそのコンポーネントの責務がある層で書き、上位層では「下位層が満たすべき契約」を再検査しない (wire-up 検証だけにする)。

| layer | src 配置 | tests 配置 | 主な責務 / 検査対象 |
|---|---|---|---|
| atoms | `src/components/atoms/` | `tests/unit/atoms/` | shadcn 由来 primitive (Button / Input / Form / Dialog / Select / Card 等)。テストは原則不要 (smoke のみ) |
| molecules | `src/components/molecules/` | `tests/unit/molecules/` | `FieldError` / `TextField` / `NumberField` / `SelectField` 等の form field wrapper。`FE-VAL-POLICY-*` と `FE-A11Y-FORM-*` の契約をここで満たす |
| organisms | `src/components/organisms/` | `tests/unit/organisms/` | `auth/Guard` / `forms/LotActionForm` / `forms/rich-actions/*` / `dialogs/{LotSelectDialog,SalesCaseCreateDialog}`。`FE-COMP-*` の振る舞いはここで検査 |
| templates | `src/components/templates/` | `tests/unit/templates/` | `PageHeader` 等の薄い page shell。タイトル / 説明 / back link / action slot の DOM 落ちを検査 |
| pages | `src/pages/` | `tests/unit/pages/` | route component。`FE-PAGE-*` / `FE-REQ-*` の wire-up と固有ロジックだけを検査し、molecule / organism 単体の契約は再検査しない |
| pbt | `src/lib/` 等の純粋関数 | `tests/unit/pbt/` | fast-check の property を 1 ファイル = 1 関数で配置 |

`renderWithApp(...)` の SWR 設定は以下で固定する。

```ts
{
  provider: () => new Map(),
  dedupingInterval: 0,
  revalidateOnFocus: false,
  revalidateOnReconnect: false,
  shouldRetryOnError: false,
}
```

## Test Stability Policy

| ID | 方針 | assertion | 状態 |
|---|---|---|---|
| `FE-STAB-001` | loading は MSW `delay` または `deferred promise` で固定する | 一瞬だけ出る表示に依存しない | ✅ |
| `FE-STAB-002` | `sleep` は使わない | `findByRole`, `waitFor`, `waitForElementToBeRemoved` を使う | ✅ |
| `FE-STAB-003` | mutation の二重実行防止は request count で見る | double click / Enter 連打後も request count = 1 | ✅ |
| `FE-STAB-004` | SWR retry/revalidate/dedupe は無効化する | 意図しない再取得や request count の揺れを防ぐ | ✅ |
| `FE-STAB-005` | テスト間の auth-store / MSW handler は `tests/setup.ts` で確定リセットする | 直前テストの token が漏れない | ✅ |

## 既知制約 (テスト infra 起因)

| ID | 制約 | 影響範囲 | 回避策 |
|---|---|---|---|
| `FE-CONSTRAINT-001` | `renderWithApp` / `renderWithRouter` の SWRConfig `provider: () => new Map()` は per-test cache を作るため、`use-lot.ts` 等が呼ぶ `globalMutate` (swr の named export) は cache に届かない | `FE-REFETCH-001 / 002 / 003 / 005` (page 内 mutation 後の SWR 再取得) | (a) `useSWRConfig().mutate` を使うようにプロダクトコードを書き換える、または (b) `tests/support/render.tsx` に「global cache 利用 + 手動 cleanup」モードを追加する。Phase 8 までに方針決定 |
| `FE-CONSTRAINT-002` | shadcn (radix-ui) `Select` は jsdom で `fireEvent.change` / `click` による値変更が安定しない | `FE-NAV-SALES-002 / 003` (案件種別ごとの navigate 検査) | (a) 純粋関数 `caseDetailRoute()` を直接検査する (現状)、(b) `userEvent.selectOptions` を使う、(c) Storybook + Playwright Component Testing に移す。Phase 6 で再評価 |
| `FE-CONSTRAINT-003` | `useExternalPriceCheck` の SWR key は `apiGet` の `BASE` 前置と衝突しないこと (`/external/price-check?lotId=...` を渡し、`/api/external/price-check?lotId=...` を fetch する) | `FE-REQ-PRICE-*` | 仕様化済み (rev2 で修正済)。新しい hook を作るときは「key にスキーマ識別 prefix を入れない」を §拡充手順 に明記 |

## Role Matrix

`AUTH-ROLE-HIERARCHY` の frontend oracle は以下。

| ID | backend auth | user role | required role | 期待結果 | assertion | 状態 |
|---|---|---|---|---|---|---|
| `FE-COMP-GUARD-001` | disabled | anonymous | admin | children 表示 | fallback not in document | ✅ |
| `FE-COMP-GUARD-002` | enabled | anonymous | viewer | fallback 表示 | children not in document | ✅ |
| `FE-COMP-GUARD-003` | enabled | viewer | viewer | children 表示 | visible | ✅ |
| `FE-COMP-GUARD-004` | enabled | viewer | operator | fallback 表示 | visible | ✅ |
| `FE-COMP-GUARD-005` | enabled | operator | viewer | children 表示 | visible | ✅ |
| `FE-COMP-GUARD-006` | enabled | operator | operator | children 表示 | visible | ✅ |
| `FE-COMP-GUARD-007` | enabled | operator | admin | fallback 表示 | visible | ✅ |
| `FE-COMP-GUARD-008` | enabled | admin | viewer/operator/admin | children 表示 | matrix 全許可 | ✅ |

page test ではこの matrix を重複網羅しない。各 page は「該当 action area に Guard が適用されていること」を代表 case で確認する。

## Lot Status Action Matrix

`BR-LOT-STATE-ACTION` の frontend oracle。button は disabled property と `aria-disabled` 相当の見た目ではなく DOM state を検査する。

| status | complete-mfg | cancel-mfg | instruct-ship | complete-ship | instruct-conv | cancel-conv | テスト ID | 状態 |
|---|---:|---:|---:|---:|---:|---:|---|---|
| `manufacturing` | true | false | false | false | false | false | `FE-MATRIX-LOT-001` | ❌ |
| `manufactured` | false | true | true | false | true | false | `FE-MATRIX-LOT-002` | ❌ |
| `shipping_instructed` | false | false | false | true | false | false | `FE-MATRIX-LOT-003` | ❌ |
| `shipped` | false | false | false | false | false | false | `FE-MATRIX-LOT-004` | ❌ |
| `conversion_instructed` | false | false | false | false | false | true | `FE-MATRIX-LOT-005` | ❌ |
| unknown / null | false | false | false | false | false | false | `FE-MATRIX-LOT-006` | ❌ |

実装方針: `tests/unit/pages/LotDetailPage.matrix.test.tsx` を新設し、`it.each` で 6 status × 6 action ボタンの disabled 状態を 1 行 oracle にする。純粋関数 `lotActionEnabled(action, status)` の網羅は `FE-PBT-STATUS-001` に委譲。

## Version Required Action Policy

| ID | page | action | method/path | version 位置 | `version == null` の期待結果 | 状態 |
|---|---|---|---|---|---|---|
| `FE-VERSION-LOT-001` | LotDetail | complete manufacturing | `POST /lots/{id}/complete-manufacturing` | body.version | API を呼ばず共通 toast | ✅ (page 経由で body.version=N の経路のみ。null 経路は LotDetailPage が `if (!lot) return null` で守るため reachable でなく、`FE-PBT-VERSION-001` 純粋関数に委譲) |
| `FE-VERSION-LOT-002` | LotDetail | cancel manufacturing | `POST /lots/{id}/cancel-manufacturing-completion` | body.version | 共通 toast | ❌ |
| `FE-VERSION-LOT-003` | LotDetail | instruct shipping | `POST /lots/{id}/instruct-shipping` | body.version | 共通 toast | ❌ |
| `FE-VERSION-LOT-004` | LotDetail | complete shipping | `POST /lots/{id}/complete-shipping` | body.version | 共通 toast | ❌ |
| `FE-VERSION-LOT-005` | LotDetail | instruct conversion | `POST /lots/{id}/instruct-item-conversion` | body.version | 共通 toast | ❌ |
| `FE-VERSION-LOT-006` | LotDetail | cancel conversion | `DELETE /lots/{id}/instruct-item-conversion` | body.version | 共通 toast | ❌ |
| `FE-VERSION-SALES-001` | SalesCaseDetail | delete appraisal | `DELETE /sales-cases/{id}/appraisals` | body.version | 共通 toast | ✅ |
| `FE-VERSION-SALES-002` | SalesCaseDetail | delete contract | `DELETE /sales-cases/{id}/contracts` | body.version | 共通 toast | ❌ |
| `FE-VERSION-SALES-003` | SalesCaseDetail | cancel shipping instruction | `DELETE /sales-cases/{id}/shipping-instruction` | body.version | 共通 toast | ❌ |
| `FE-VERSION-RES-001` | ReservationCaseDetail | cancel determination | `DELETE /sales-cases/{id}/reservation/determination` | body.version | 共通 toast | ✅ |
| `FE-VERSION-CON-001` | ConsignmentCaseDetail | cancel designation | `DELETE /sales-cases/{id}/consignment/designation` | body.version | 共通 toast | ✅ |

共通 toast は `最新の状態を読み込めませんでした。再読み込みしてください。` とする。

実装方針: 残 ID は `tests/unit/pages/LotDetailPage.version.test.tsx` と `SalesCaseDetailPage.version.test.tsx` を新設して `it.each` で網羅する。

## Validation 表示ポリシー

| ID | 方針 | assertion | 状態 |
|---|---|---|---|
| `FE-VAL-POLICY-001` | ブラウザ標準 validation popup は使わない | form に `noValidate` | ✅ |
| `FE-VAL-POLICY-002` | error は該当 field 直下に赤字で表示する | field wrapper 内に error node | ✅ |
| `FE-VAL-POLICY-003` | 違反している全項目を同時表示する | invalid field 全てに error node | ✅ |
| `FE-VAL-POLICY-004` | 未操作 field は赤くしない (`mode:"onTouched"` / RichAction の touched 集合) | blur 前の field に error が無い | ✅ |
| `FE-VAL-POLICY-005` | 修正後は該当 field の error を即消す | typing 中に `aria-invalid=false` | ✅ |
| `FE-VAL-POLICY-006` | submit 時に全 invalid を同時に赤くし、API は呼ばない | request count 0、全 invalid に `aria-invalid=true` | ✅ |
| `FE-VAL-POLICY-007` | ロットID 入力は最初の1件だけでなく不正値を全件列挙する | error に全 invalid lotId を含む | ✅ |

## Validation Matrix

### LotActionForm

| ID | 操作 | 入力 | 期待結果 | assertion | 状態 |
|---|---|---|---|---|---|
| `FE-COMP-LOT-ACTION-001` | date form submit | date 空 | API を呼ばず `日付を入力してください` を toast | toast.error、request count 0 | ✅ |
| `FE-COMP-LOT-ACTION-002` | text form submit | text 空 | API を呼ばず `{label}を入力してください` を toast | toast.error、request count 0 | ✅ |
| `FE-COMP-LOT-ACTION-003` | valid submit | date `2026-04-28` | `onSubmit(date, undefined)` | success toast | ✅ |
| `FE-COMP-LOT-ACTION-004` | valid submit | text `2026-T-902` | `onSubmit(undefined, text)` | success toast | ✅ |
| `FE-COMP-LOT-ACTION-005` | double submit | valid input | API 呼び出し 1 回 | request count 1, pending label | ✅ |
| `FE-COMP-LOT-ACTION-006` | disabled submit | valid input | API を呼ばない | button disabled, request count 0 | ✅ |
| `FE-COMP-LOT-ACTION-007` | 未 touched 表示 | mount 後 blur なし | field-level error 非表示 | UI が field-level error を持つよう改修した後に Red→Green | ⏳ todo (UI 改修待ち) |

### RichActionForms

`src/components/organisms/forms/rich-actions/RichActionForms.tsx` の 7 form を対象とする。validation は field 下表示 / touched / 全項目同時表示の共通ポリシーに従う。

| ID | form | 操作 | 入力 | 期待結果 | assertion | 状態 |
|---|---|---|---|---|---|---|
| `FE-COMP-RICH-DA-001` | DirectAppraisalForm | submit | 全項目空 | 全 invalid field に error、API 未呼出 | field 直下 error 複数、request count 0 | ✅ |
| `FE-COMP-RICH-DA-002` | DirectAppraisalForm | submit | 調整率 89 / 111 | 範囲外 error `90〜110%` | field 内 error、request count 0 | ✅ |
| `FE-COMP-RICH-DA-003` | DirectAppraisalForm | submit | 調整率 90 / 110 (境界) | 受理 | request body の各 rate が `0.9` / `1.1` | ✅ |
| `FE-COMP-RICH-DA-004` | DirectAppraisalForm | 入力 | 各単価 + 調整率 | 査定合計表示が `Σ 単価 × rate ÷ 100` で再計算 | DOM 表示が即更新 | ✅ |
| `FE-COMP-RICH-DA-005` | DirectAppraisalForm | 「変更する」→ チェック → 承認者確認 | 上長承認モーダル | total を直接入力可能になる | input enabled、approver は read-only `営業部長（システム既定）` | ✅ |
| `FE-COMP-RICH-DA-006` | DirectAppraisalForm | 「変更する」モーダルで cancel | - | total は自動計算のまま | input disabled | ✅ |
| `FE-COMP-RICH-SC-001` | SalesContractForm | submit | 必須空 | 全 invalid に error、API 未呼出 | request count 0 | ✅ |
| `FE-COMP-RICH-SC-002` | SalesContractForm | submit | 契約調整率 89 | 範囲外 error | field 内 error | ❌ |
| `FE-COMP-RICH-SC-003` | SalesContractForm | submit | 契約調整率 100 | 受理 | request body `1.0` | ❌ |
| `FE-COMP-RICH-DV-001` | DateVersionActionForm | submit | 必須空 | field 直下 error、API 未呼出 | request count 0 | ✅ |
| `FE-COMP-RICH-DV-002` | DateVersionActionForm | submit | valid + version | API 呼出 | body に `date` と `version` | ✅ |
| `FE-COMP-RICH-RP-001` | ReservationPriceForm | submit | 単価/調整率いずれか不正 | 全 invalid に error | field 直下 error | ✅ |
| `FE-COMP-RICH-RC-001` | ReservationConfirmationForm | submit | 必須空 | error | request count 0 | ✅ |
| `FE-COMP-RICH-CD-001` | ConsignmentDesignationForm | submit | 必須空 | error | request count 0 | ✅ |
| `FE-COMP-RICH-CR-001` | ConsignmentResultForm | submit | 必須空 | error | request count 0 | ✅ |
| `FE-COMP-RICH-COMMON-001` | 全 form | 二重 submit | valid 入力 | API 1 回 | request count 1 | ✅ |
| `FE-COMP-RICH-COMMON-002` | 全 form | 未 touched | mount 後 blur 無 | error 非表示 | DOM に error なし | ✅ |

### LotSelectDialog

| ID | 操作 | MSW | 期待結果 | assertion | 状態 |
|---|---|---|---|---|---|
| `FE-COMP-LOT-SELECT-001` | open | `GET /lots/available` `200` | 製造完了かつ未割当ロットがチェックボックス一覧に出る | row count、checkbox unchecked | ✅ |
| `FE-COMP-LOT-SELECT-002` | excludeCase 付き open | `GET /lots/available?excludeCase={id}` `200` | 自案件分も残る | query `excludeCase` が含まれる | ✅ |
| `FE-COMP-LOT-SELECT-003` | 選択 → 確定 | - | `onConfirm(selectedIds)` | callback args = 選択 lotIds | ✅ |
| `FE-COMP-LOT-SELECT-004` | 選択 0 件で確定 | - | confirm disabled or 警告 | 確定 button disabled | ✅ |
| `FE-COMP-LOT-SELECT-005` | API error | `500` | error 表示 + 再試行可能 | error text | ✅ |
| `FE-COMP-LOT-SELECT-006` | loading | pending | spinner / 読み込み中 | deferred response 中 visible | ✅ |

### SalesCaseCreateDialog

| ID | 操作 | MSW | 期待結果 | assertion | 状態 |
|---|---|---|---|---|---|
| `FE-COMP-SALES-CREATE-DIALOG-001` | LotListPage で行選択 → 「販売案件新規登録」 | - | dialog open、選択 lotIds が `lots` 初期値 | initial form state | ✅ |
| `FE-COMP-SALES-CREATE-DIALOG-002` | 事業部 dropdown | `GET /code-masters` `200` | 事業部 option が name 表示、value は整数 code | option 数、selected value 型 | ✅ |
| `FE-COMP-SALES-CREATE-DIALOG-003` | submit | `POST /sales-cases` `201` | request body の `lots` が string[]、`divisionCode` が integer | body 型 | ✅ |
| `FE-COMP-SALES-CREATE-DIALOG-004` | submit validation | 必須空 | 全 invalid に error、API 未呼出 | request count 0 | ✅ |
| `FE-COMP-SALES-CREATE-DIALOG-005` | API error | `400 problem` | toast error、dialog は閉じない | toast、dialog visible | ✅ |

### Page validation

| ID | page | 操作 | 入力 | 期待結果 | assertion | 状態 |
|---|---|---|---|---|---|---|
| `FE-PAGE-SALES-CREATE-001` | SalesCaseCreatePage | submit | lots 空 | field 直下に `ロットを1つ以上選択してください`、API 未呼出 | field 内 error、request count 0 | ✅ |
| `FE-PAGE-SALES-CREATE-002` | SalesCaseCreatePage | LotSelectDialog で 2 件選択 → 確定 → submit | `2026-A-1, 2026-A-2` | request body `lots:["2026-A-1","2026-A-2"]` | body 型 string[] | ✅ |
| `FE-PAGE-SALES-CREATE-003` | SalesCaseCreatePage | 事業部 dropdown | `GET /code-masters` | option が name 表示・value 整数 | option 数 | ✅ |
| `FE-PAGE-SALES-CREATE-004` | SalesCaseCreatePage | 調整率 step | (該当無し: page には調整率入力が無い) | — | `FE-RATE-*` に委譲 | n/a |
| `FE-PAGE-LOT-LIST-001` | LotListPage | 製造完了行 | - | 行 checkbox enabled | DOM disabled false | ✅ |
| `FE-PAGE-LOT-LIST-002` | LotListPage | 非製造完了行 | - | 行 checkbox disabled | DOM disabled true | ✅ |
| `FE-PAGE-LOT-LIST-003` | LotListPage | 選択 0 件 | - | `販売案件新規登録` 非表示 | button not in document | ✅ |
| `FE-PAGE-LOT-LIST-004` | LotListPage | 選択 1 件以上で押下 | - | SalesCaseCreateDialog open | dialog visible | ✅ |
| `FE-PAGE-PRICE-001` | PriceCheckPage | 初期表示 | lotId 空 | `取得` disabled | button disabled | ✅ |
| `FE-PAGE-PRICE-002` | PriceCheckPage | 入力 | lotId 空白のみ | `取得` disabled | button disabled | ✅ |

## Adjustment Rate / Appraisal Total Policy

`BR-ADJUSTMENT-RATE-RANGE` と `BR-APPRAISAL-TOTAL-FORMULA` の oracle。

| ID | 場面 | 入力 (画面) | 期待 request body | DOM 期待 | 状態 |
|---|---|---|---|---|---|
| `FE-RATE-001` | DirectAppraisalForm | 90 | `0.9` | input value `90`, step `1` | ✅ (RichAction 単体で検査) |
| `FE-RATE-002` | DirectAppraisalForm | 110 | `1.1` | input value `110` | ✅ |
| `FE-RATE-003` | DirectAppraisalForm | 89 | API 未呼出、`90〜110%` field error | request count 0 | ✅ |
| `FE-RATE-004` | DirectAppraisalForm | 111 | API 未呼出、範囲外 error | request count 0 | ✅ |
| `FE-RATE-005` | SalesContractForm 契約調整率 | 100 | `1.0` | input value `100` | ❌ (SC-003 と一体) |
| `FE-TOTAL-001` | DirectAppraisalForm | 単価 a, b と各 rate% | total = `(a × rateA + b × rateB) ÷ 100` | DOM total 表示 | ⚠️ (RichAction 単体で `DA-004`、page wire-up 未) |
| `FE-TOTAL-002` | DirectAppraisalForm | 入力 1 個変更 | total 即更新 | DOM 表示変化 | ✅ (DA-004) |
| `FE-TOTAL-003` | DirectAppraisalForm 既定モード | - | total input は read-only、ラベル表示 | input disabled | ✅ |
| `FE-TOTAL-004` | DirectAppraisalForm 「変更する」モーダル承認後 | - | total input 編集可能 | input enabled | ✅ (DA-005) |
| `FE-TOTAL-005` | DirectAppraisalForm 「変更する」cancel | - | total 自動計算のまま | input disabled | ✅ (DA-006) |
| `FE-APPROVER-001` | 上長承認モーダル | 表示 | 承認者は固定 `営業部長（システム既定）` を read-only 表示 | input/select disabled、value 一致 | ✅ (DA-005) |
| `FE-APPROVER-002` | 上長承認モーダル | チェック未付与 | 確定 disabled | button disabled | ✅ (DA-005) |
| `FE-APPROVER-003` | 上長承認モーダル | チェック付与 → 確定 | direct 入力モードへ遷移 | total input enabled | ✅ (DA-005) |

page test での再検証は **重複ポリシー** とする。RichActionForms 単体テストで oracle 済みのため、Phase 4e では「SalesCaseDetailPage 上でも `変更する` button が露出し、承認後の total を含めて `POST /sales-cases/{id}/appraisals` body に届く」ことのみ 1 ケース確認すれば足りる (`FE-PAGE-SALES-DETAIL-APPRAISAL-001` を新設予定 — ❌)。

## Accessibility / Keyboard Matrix

| ID | 対象 | 操作 | 期待結果 | assertion | 状態 |
|---|---|---|---|---|---|
| `FE-A11Y-RICH-001` | RichActionForms 各 form | 全 input を取得 | 全 label と input が `htmlFor` で紐付く | `getByLabelText(fieldLabel)` が全項目で hit | ❌ (実装方針: 7 form を `it.each` で回す) |
| `FE-A11Y-LOT-ACTION-001` | LotActionForm | date input を取得 | label と input が紐付く | `getByLabelText(dateLabel)` | ✅ |
| `FE-A11Y-LOT-SELECT-001` | LotSelectDialog | 行 checkbox | label (lotNumber) と checkbox が紐付く、列ヘッダは `role="columnheader"` | `getByRole("checkbox", { name: lotNumber })` | ✅ (LotSelectDialog-001 内) |
| `FE-A11Y-APPROVER-001` | 上長承認モーダル | 承認者表示 | 固定値を read-only として `aria-readonly` または disabled | DOM 属性 | ✅ (DA-005) |
| `FE-A11Y-FORM-001` | validation error | invalid submit | error が該当 field 近傍に出る | field wrapper 内の error | ✅ |
| `FE-A11Y-FORM-002` | validation error | invalid submit | invalid input は `aria-invalid=true` | 導入時に必須 assertion | ✅ |
| `FE-A11Y-FORM-003` | validation error | invalid submit | input と error は `aria-describedby` で紐付く | 導入時に必須 assertion | ✅ |
| `FE-A11Y-FORM-004` | form | Enter submit | click と同じ request | request count / body | ✅ |
| `FE-A11Y-FORM-005` | dialog 移行後 | Escape | dialog が閉じる | future Storybook/RTL 対象 | ❌ (Phase 6 / Storybook) |

`FE-A11Y-FORM-001` 〜 `FE-A11Y-FORM-003` は molecule 層 (`tests/unit/molecules/{TextField,NumberField,SelectField}.test.tsx`) で恒常的に検査する。`molecules/*` は shadcn `atoms/form.tsx` (`FormField` / `FormControl` / `FormMessage`) の上に組まれているため、`aria-invalid` と `aria-describedby` (formMessageId) は自動付与される。page test では同契約を再検査せず、wire-up だけを検査する。

## MSW Scenario / Request Assertion

API 呼び出しの正しさは、原則 endpoint helper mock ではなく MSW request assertion で検査する。URL builder のような純粋関数だけ unit test で検査してよい。

| ID | page | request | MSW response | assertion | 状態 |
|---|---|---|---|---|---|
| `FE-REQ-LOT-CREATE-001` | LotCreatePage | `POST /lots` body に number fields と code-master 整数値 | `201 { lotNumber }` | body 型 (integer)、toast success、navigate mock | ✅ |
| `FE-REQ-LOT-CREATE-002` | LotCreatePage | double submit `POST /lots` | pending promise | request count 1 | ✅ |
| `FE-REQ-LOT-CREATE-003` | LotCreatePage | `POST /lots` | `400 problem` | toast error、navigation なし | ✅ |
| `FE-REQ-LOT-CREATE-004` | LotCreatePage | `GET /code-masters` | `200 階層 + フラット` | 事業部→部→課 cascade、工程/検査/製造 flat、option name 表示 | ⚠️ (option 表示は ✅、cascade の段階遷移は未) |
| `FE-REQ-LOT-ACTION-001` | LotDetailPage | `GET /lots/{id}` | delayed success | loading 固定後 success 表示、明細テーブル、名称 `(コード)` 表示 | ✅ |
| `FE-REQ-LOT-ACTION-002` | LotDetailPage | `POST /lots/{id}/complete-manufacturing` body `{ date, version }` | `200 updated lot` | request body、success toast | ✅ |
| `FE-REQ-LOT-ACTION-003` | LotDetailPage | `POST /lots/{id}/complete-manufacturing` body `{ date, version }` | `409 problem` | toast error、page 残存 | ✅ |
| `FE-REQ-LOT-LIST-001` | LotListPage | `GET /lots` | `200 list` | 製造完了行のみ row checkbox enabled | ✅ |
| `FE-REQ-SALES-CREATE-001` | SalesCaseCreatePage | `POST /sales-cases` body `{ lots:string[], divisionCode:integer, salesDate, caseType }` | `201 { salesCaseNumber }` | body 型・正規化、navigate mock | ✅ |
| `FE-REQ-SALES-CREATE-002` | SalesCaseCreatePage / Dialog | `GET /lots/available` | `200` | dialog open 時に呼ばれる | ✅ |
| `FE-REQ-SALES-CREATE-003` | SalesCaseCreatePage | `GET /code-masters` | `200` | 事業部 dropdown option | ✅ |
| `FE-REQ-SALES-CREATE-004` | SalesCaseCreatePage | `POST /sales-cases` | `400 problem` | toast error、navigation なし | ✅ |
| `FE-REQ-SALES-ACTION-001` | SalesCaseDetailPage | `POST /sales-cases/{id}/appraisals` body rich form 出力 (各 rate は ÷100) | `204` | request body の各 rate `0.9〜1.1` | ❌ |
| `FE-REQ-SALES-ACTION-002` | SalesCaseDetailPage | `DELETE /sales-cases/{id}/appraisals` body `{ version }` | `204` | confirm true、version body | ✅ |
| `FE-REQ-SALES-ACTION-003` | SalesCaseDetailPage | `POST /sales-cases/{id}/contracts` body 契約 rich form (契約調整率 ÷100) | `204` | body rate `0.9〜1.1` | ❌ |
| `FE-REQ-SALES-LOTS-001` | SalesCaseDetailPage | direct/before_appraisal で「ロットを修正」 → LotSelectDialog → `PUT /sales-cases/{id}/lots` body `{ lots:string[], version }` | `204` | body 型、version | ✅ |
| `FE-REQ-SALES-LOTS-002` | SalesCaseDetailPage | 価格登録後 | - | 「ロットを修正」 button 非表示 | button not in document | ✅ |
| `FE-REQ-SALES-LOTS-003` | SalesCaseDetailPage | LotSelectDialog open 時 | `GET /lots/available?excludeCase={id}` | query に excludeCase が入る | ✅ |
| `FE-REQ-SALES-LOTS-004` | SalesCaseDetailPage | `PUT /sales-cases/{id}/lots` | `409 problem` | toast error | ❌ (refetch は constraint-001) |
| `FE-REQ-RESERVATION-001` | ReservationCaseDetailPage | `POST /sales-cases/{id}/reservation/appraisals` body rich form | `204` | rate `0.9〜1.1` | ❌ |
| `FE-REQ-RESERVATION-002` | ReservationCaseDetailPage | `DELETE /sales-cases/{id}/reservation/determination` body `{ version }` | `204` | confirm true、version body | ✅ |
| `FE-REQ-CONSIGNMENT-001` | ConsignmentCaseDetailPage | `POST /sales-cases/{id}/consignment/designate` body rich form | `204` | rate `0.9〜1.1` | ❌ |
| `FE-REQ-CONSIGNMENT-002` | ConsignmentCaseDetailPage | `DELETE /sales-cases/{id}/consignment/designation` body `{ version }` | `204` | confirm true、version body | ✅ |
| `FE-REQ-CONSIGNMENT-LOTS-001` | ConsignmentCaseDetailPage | consignment/before_consignment で「ロットを修正」→ `PUT /sales-cases/{id}/lots` | `204` | body 型、version | ✅ |
| `FE-REQ-CONSIGNMENT-LOTS-002` | ConsignmentCaseDetailPage | 価格登録後 | - | 「ロットを修正」非表示 | button not in document | ✅ |
| `FE-REQ-PRICE-001` | PriceCheckPage | `GET /external/price-check?lotId=2026-A-001` | `200 priceQuote` | query、result 表示 | ✅ |
| `FE-REQ-PRICE-002` | PriceCheckPage | same lotId retry | `200 priceQuote` | request count が増える (dedupe しない) | ✅ |
| `FE-REQ-PRICE-003` | PriceCheckPage | different lotId | `200 priceQuote` | 新 query で request | ✅ |

## Page State Matrix

| ID | page | MSW response | 期待表示 | assertion | 状態 |
|---|---|---|---|---|---|
| `FE-PAGE-LOT-LIST-LOAD-001` | LotListPage | `GET /lots` pending | `読み込み中…` | visible | ✅ |
| `FE-PAGE-LOT-LIST-LOAD-002` | LotListPage | `200 list` | 行表示、製造完了のみ checkbox enabled | DOM 行 | ✅ |
| `FE-PAGE-LOT-LIST-LOAD-003` | LotListPage | `500 problem` | error 表示 | error text | ✅ |
| `FE-PAGE-LOT-DETAIL-001` | LotDetailPage | `GET /lots/{id}` pending | `読み込み中…` | delayed response 中 | ✅ |
| `FE-PAGE-LOT-DETAIL-002` | LotDetailPage | `200 lot` | status label, version, 明細, 名称 `(コード)` | DOM text | ✅ |
| `FE-PAGE-LOT-DETAIL-003` | LotDetailPage | `500 problem` | `エラー: ...` | error text | ✅ |
| `FE-PAGE-LOT-DETAIL-004` | LotDetailPage | コード未登録 | `name=null` を fallback 表示 | コードのみ | ✅ |
| `FE-PAGE-SALES-DETAIL-001` | SalesCaseDetailPage | pending | `読み込み中…` | visible | ✅ |
| `FE-PAGE-SALES-DETAIL-002` | SalesCaseDetailPage | `200 salesCase` | heading, badge, action area | DOM text | ✅ |
| `FE-PAGE-SALES-DETAIL-003` | SalesCaseDetailPage | `500 problem` | `エラー: ...` | error text | ✅ |
| `FE-PAGE-RESERVATION-001` | ReservationCaseDetailPage | `200 reservation case` | status label, JSON pre | DOM text | ✅ |
| `FE-PAGE-CONSIGNMENT-001` | ConsignmentCaseDetailPage | `200 consignment case` | status label, JSON pre | DOM text | ✅ |
| `FE-PAGE-PRICE-003` | PriceCheckPage | `200 { basePrice, adjustmentRate, source }` | 取得結果 | result values | ✅ |
| `FE-PAGE-PRICE-004` | PriceCheckPage | `200 { adjustmentRate: null }` | `(未設定)` | DOM text | ✅ |
| `FE-PAGE-PRICE-005` | PriceCheckPage | `400 problem` | `ロット番号の形式が不正です。` | DOM text | ✅ |
| `FE-PAGE-PRICE-006` | PriceCheckPage | `502 problem` | `上流の価格 API がエラーを返しました。` | DOM text | ✅ |
| `FE-PAGE-PRICE-007` | PriceCheckPage | `503 problem` | `サーキットが OPEN しています...` | DOM text | ✅ |
| `FE-PAGE-PRICE-008` | PriceCheckPage | network error / unknown | `取得に失敗しました。` | DOM text | ✅ |

## Router Integration Tests

通常 page behavior test では `useNavigate` mock を許容する。代表 create success は本物 TanStack Router (`routeTree.gen`) で route 解決まで確認する。Phase 6 で `tests/support/render.tsx` に `renderWithRealRouter(initialPath)` を追加し、以下を検査する。

| ID | 操作 | 期待 route | assertion | 状態 |
|---|---|---|---|---|
| `FE-NAV-LOT-001` | LotCreatePage 作成成功 | `/lots/{lotNumber}` | detail route が解決され、detail page heading が出る | ❌ |
| `FE-NAV-SALES-001` | SalesCaseCreatePage direct 作成成功 | `/sales-cases/{id}` | direct detail route が解決される | ❌ |
| `FE-NAV-SALES-002` | SalesCaseCreatePage reservation 作成成功 | `/reservation-cases/{id}` | reservation route が解決される | ❌ (`FE-CONSTRAINT-002` 影響あり) |
| `FE-NAV-SALES-003` | SalesCaseCreatePage consignment 作成成功 | `/consignment-cases/{id}` | consignment route が解決される | ❌ |
| `FE-NAV-AUTH-001` | role 不足で protected route 表示 | current route | fallback UI が実 route 上で出る | ❌ |

## Error Mapping Policy

`describeApiError` は共通 unit test で厚くし、page test はページ固有挙動だけ確認する。

| ID | 入力 | 期待結果 | layer | 状態 |
|---|---|---|---|---|
| `FE-ERR-001` | 400 validation problem | status/detail を表示 | unit | ❌ |
| `FE-ERR-002` | 401 problem | auth clear と error 表示 | api-client unit | ✅ |
| `FE-ERR-003` | 403 problem | 権限不足として表示 | unit | ❌ |
| `FE-ERR-004` | 404 problem | not found detail 表示 | unit | ❌ |
| `FE-ERR-005` | 409 optimistic-lock conflict | 再表示を促す文言 | unit | ✅ |
| `FE-ERR-006` | 422 problem | detail 表示 | unit | ❌ |
| `FE-ERR-007` | 500 problem | detail 表示 | unit | ❌ |
| `FE-ERR-008` | 502 problem | detail 表示 | unit | ❌ |
| `FE-ERR-009` | network error | fallback 文言 | unit | ❌ |
| `FE-ERR-010` | malformed problem response | fallback 文言 | unit | ❌ |
| `FE-ERR-PAGE-001` | Lot mutation 409 | refetch し navigation しない | page | ⚠️ (toast 確認のみ。refetch は `FE-CONSTRAINT-001`) |
| `FE-ERR-PAGE-002` | create API error | toast error し navigation しない | page | ✅ |
| `FE-ERR-PAGE-003` | PriceCheck 400/502/503 | ページ固有文言 | page | ✅ |

## CSV / Blob Download Policy

| ID | 対象 | 期待仕様 | assertion | 状態 |
|---|---|---|---|---|
| `FE-CSV-001` | LotDetail CSV success | `GET /lots/export` を呼ぶ | MSW request assertion | ✅ |
| `FE-CSV-002` | blob download | object URL を作り、`download="lots_YYYYMMDD.csv"` の anchor click を実行する | `URL.createObjectURL`, `HTMLAnchorElement.click`, `URL.revokeObjectURL` mock | ✅ |
| `FE-CSV-003` | success UI | success toast、pending 解除 | toast.success, button enabled | ✅ |
| `FE-CSV-004` | failure UI | error toast、pending 解除 | toast.error, button enabled | ✅ |

## SWR Mutate / Refetch Policy

refetch は implementation mock ではなく、原則 MSW GET count で検査する。**`FE-CONSTRAINT-001`** を解消するまで以下は todo 扱い。

| ID | 対象 | 操作 | 期待結果 | assertion | 状態 |
|---|---|---|---|---|---|
| `FE-REFETCH-001` | LotDetail mutation success | mutation success 後 | 対象 lot key を再取得 | `GET /lots/{id}` count が初回 + refetch | 🔒 blocked |
| `FE-REFETCH-002` | LotDetail mutation 409 | conflict 後 | 最新 lot を再取得 | `GET /lots/{id}` count | 🔒 blocked |
| `FE-REFETCH-003` | SalesCase mutation success | action success 後 | 対象 sales case key を再取得 | `GET /sales-cases/{id}` count | 🔒 blocked |
| `FE-REFETCH-004` | PriceCheck same lot retry | 同じ lotId で再押下 | 同一 query を再取得 | `GET /external/price-check?...` count increment | ✅ (page 側で `mutate()` を直接呼ぶ pattern のため通る) |
| `FE-REFETCH-005` | SalesCase / Consignment PUT lots success | 「ロットを修正」成功後 | sales case 再取得 | `GET /sales-cases/{id}` count | 🔒 blocked |
| `FE-REFETCH-006` | LotList → SalesCaseCreateDialog 成功後 | dialog success → list 復帰 | lots 一覧再取得 | `GET /lots` count | ⚠️ (count 増加 oracle はあるが mutation 成功と紐付かない部分は blocked) |

解消ロードマップ:

1. **短期 (Phase 8 前)**: `tests/support/render.tsx` に `renderWithAppGlobalCache(ui)` を追加し、global cache を使うが test 終了時に `globalMutate(() => true, undefined, { revalidate: false })` で in-flight key を破棄する helper を導入する。
2. **中期**: プロダクトコード側 (`use-lot.ts` 等) を `useSWRConfig().mutate` ベースに書き換えてテスト infra と独立させる。

## Frontend PBT Scope (本格運用計画)

`fast-check` で純粋関数を property-based に検査する。example test と並走させ、新規純粋関数を追加するときは PBT も同時に書く。

### 抽出対象と property

| ID | 対象関数 / 場所 | property | generator | 状態 |
|---|---|---|---|---|
| `FE-PBT-SALES-LOTS-001` | `parseLotNumbers` (`src/pages/sales-cases/sales-case-create-validation.ts`) と LotSelectDialog 出力 | 選択 lotIds はユニーク、空文字を含まない | `fc.uniqueArray(fc.stringMatching(/^\d+-[^-\s]+-\d+$/))` | ❌ |
| `FE-PBT-RATE-001` | `displayRate ⇄ apiRate` (要抽出: RichActionForms 内 `RATE_DISPLAY_SCALE`) | 画面値 r∈[90,110] を `÷100` した API 値は `[0.9, 1.1]`、逆変換で誤差 < `1e-9` で一致 | `fc.integer({ min: 90, max: 110 })` | ❌ |
| `FE-PBT-RATE-002` | `requiredRate` の range guard | r ∈ [90,110] のみ valid、外は `error` を立てる | `fc.integer({ min: -1000, max: 1000 })` | ❌ |
| `FE-PBT-TOTAL-001` | `computeEstimatedTotal` (要 export 化) | `Math.round(Σ base × period × counterparty × exceptional)` の結果が手計算値 ±1 で一致 | `fc.array(fc.record({ base: fc.integer({min:0, max:1_000_000}), period: fc.integer({min:90,max:110}), counterparty: fc.integer({min:90,max:110}), exceptional: fc.oneof(fc.constant(null), fc.integer({min:90,max:110})) }))` | ❌ |
| `FE-PBT-STATUS-001` | `lotActionEnabled` (`src/lib/format.ts`) | matrix に無い status × action は `false` | `fc.tuple(fc.constantFrom(...LOT_ACTIONS), fc.oneof(fc.constantFrom(...KNOWN_STATUSES), fc.string()))` | ❌ |
| `FE-PBT-FORMAT-001` | `lotStatusLabel` / `caseStatusLabel` | 未知 status は入力値を fallback 表示 | `fc.string().filter(s => !KNOWN.includes(s))` | ❌ |
| `FE-PBT-NAMEMAP-001` | `codeName` | `name == null` のとき `String(code)`、`name` 有のとき `${name} (${code})` | `fc.record({ code: fc.integer(), name: fc.oneof(fc.constant(null), fc.constant(undefined), fc.string()) })` | ❌ |
| `FE-PBT-VERSION-001` (新) | `withConflictRefresh` の純粋部分 (要抽出): `version` が null/undefined のとき API call は skip | `fc.oneof(fc.constant(null), fc.constant(undefined), fc.integer())` | ❌ |
| `FE-PBT-LOTID-001` (新) | `lotsSchema.superRefine` (`/^\d+-[^-\s]+-\d+$/`) | invalid string array に対し issue には全 invalid を列挙する | `fc.array(fc.oneof(fc.stringMatching(/^\d+-[^-\s]+-\d+$/), fc.string()))` | ❌ |

### 実装規約

- 配置: `tests/unit/pbt/{function-name}.test.ts`。1 関数 1 ファイル。
- shrinking が時間を食うので `fc.assert(..., { numRuns: 100 })` を既定値とし、CI nightly では `{ numRuns: 1000 }` に切り替える環境変数 `FE_PBT_RUNS` を用意する。
- `vitest` の `testTimeout` を PBT ファイルだけ `15000` に引き上げる (`describe.concurrent` は使わない)。
- 純粋関数として export されていない場合は **抽出を先に行う** (`src/lib/rate.ts` / `src/lib/appraisal.ts` 等の新規ファイル)。組み込みコンポーネントから直接 import せず unit テストが回せる形にする。

### PBT 導入の Phase

PBT は **Phase 9** の単発ではなく、Phase 3〜5 と並走させる:

| サブフェーズ | 対象 | 依存する Phase |
|---|---|---|
| 9a `fast-check` 導入 & 設定 | `devDependencies` 追加、`vitest.config` 更新、`pbt/sample.test.ts` で動作確認 | infra |
| 9b 純粋関数抽出 (rate / appraisal) | `RATE_DISPLAY_SCALE` と `computeEstimatedTotal` を `src/lib/rate.ts` に move | Phase 2c |
| 9c 純粋関数抽出 (lot / case status / format) | `lotActionEnabled`, `lotStatusLabel`, `caseStatusLabel`, `codeName` は既に `src/lib/format.ts` にある | — |
| 9d 純粋関数抽出 (validation refine) | `parseLotNumbers`, `lotsSchema` の判定ロジックを純粋関数化 | Phase 4a |
| 9e property 実装 | `FE-PBT-RATE-001..002`, `FE-PBT-TOTAL-001`, `FE-PBT-STATUS-001`, `FE-PBT-FORMAT-001`, `FE-PBT-NAMEMAP-001` | 9b/9c/9d |
| 9f version / lotid property | `FE-PBT-VERSION-001`, `FE-PBT-LOTID-001` | 9d |

## Phase ロードマップ

`docs/frontend-test-implementation-plan.md` を本書に統合し、状況を反映する。各 Phase の完了条件と未消化 ID を以下に列挙する。

| Phase | スコープ | 完了条件 | 未消化 ID |
|---|---|---|---|
| 0 | MSW 導入と tests/support 雛形 | `pnpm test` 空 suite green | — |
| 1 | Test Infrastructure | dummy test で infra 動作確認 | `renderWithRealRouter` (Phase 6 で追加) |
| 2a | Guard Role Matrix | `FE-COMP-GUARD-001..008` green | — |
| 2b | LotActionForm | `FE-COMP-LOT-ACTION-001..006` green、007 は UI 改修後 | `007` (UI 改修起票) |
| 2c | RichActionForms | 7 form の必須空 / 範囲外 / 境界 / 二重 submit / 未 touched | `FE-COMP-RICH-SC-002 / SC-003`、`FE-A11Y-RICH-001` |
| 2d | 共通 a11y / form | `FE-A11Y-FORM-001..004` | `FE-A11Y-FORM-005` (Storybook) |
| 2e | LotSelectDialog | `FE-COMP-LOT-SELECT-001..006` | — |
| 2f | SalesCaseCreateDialog | `FE-COMP-SALES-CREATE-DIALOG-001..005` | — |
| 2g | 共通 Validation 表示ポリシー | `FE-VAL-POLICY-001..007` | — |
| 3a | LotCreatePage | `FE-REQ-LOT-CREATE-001..003`、code-master option 表示 | `FE-REQ-LOT-CREATE-004` cascade 段階遷移 |
| 3b | LotDetailPage | `FE-PAGE-LOT-DETAIL-001..004`、`FE-REQ-LOT-ACTION-001..003` | `FE-MATRIX-LOT-001..006`、`FE-VERSION-LOT-002..006` |
| 3c | CSV / Blob | `FE-CSV-001..004` | — |
| 3d | LotListPage | `FE-PAGE-LOT-LIST-001..004` + LOAD-001..003 + `FE-REQ-LOT-LIST-001` | `FE-REFETCH-006` の完全形 (constraint-001) |
| 4a | SalesCaseCreatePage | `FE-PAGE-SALES-CREATE-001..003`、`FE-REQ-SALES-CREATE-001..004` | — |
| 4b | SalesCaseDetailPage (rich/version) | `FE-PAGE-SALES-DETAIL-001..003`、`FE-REQ-SALES-ACTION-002`、`FE-VERSION-SALES-001` | `FE-REQ-SALES-ACTION-001 / 003`、`FE-VERSION-SALES-002 / 003`、`FE-REQ-SALES-LOTS-004` (toast 部分のみ) |
| 4c | Reservation / Consignment Detail | `FE-PAGE-RESERVATION-001`、`FE-PAGE-CONSIGNMENT-001`、`FE-REQ-RESERVATION-002`、`FE-REQ-CONSIGNMENT-002`、`FE-VERSION-RES-001`、`FE-VERSION-CON-001` | `FE-REQ-RESERVATION-001`、`FE-REQ-CONSIGNMENT-001` |
| 4d | 「ロットを修正」 | `FE-REQ-SALES-LOTS-001..003`、`FE-REQ-CONSIGNMENT-LOTS-001..002` | `FE-REFETCH-005` (constraint-001) |
| 4e | 査定合計 / 上長承認 | `FE-TOTAL-001..005`、`FE-APPROVER-001..003` (RichAction 単体で達成) | page wire-up 1 ケース (`FE-PAGE-SALES-DETAIL-APPRAISAL-001`) を新設 |
| 5 | PriceCheckPage | `FE-PAGE-PRICE-001..008`、`FE-REQ-PRICE-001..003` | — |
| 6 | Router Integration | `FE-NAV-LOT-001`、`FE-NAV-SALES-001..003`、`FE-NAV-AUTH-001` | 全件 (要 `renderWithRealRouter`) |
| 7 | describeApiError unit | `FE-ERR-001..010` 全 variant、ページ重複削除 | `FE-ERR-001/003/004/006..010` |
| 8 | Evidence / CI | JUnit reporter、coverage artifact、MSW request log 失敗時出力 | 全件 |
| 9a-9f | PBT | 上節「PBT 導入の Phase」参照 | 全件 |

### 次の優先順 (推奨)

1. **Phase 2c 残り** (`SC-002 / SC-003`) — RichActionForms 単体に 2 ケース追加するだけ
2. **Phase 3b matrix / version** — `FE-MATRIX-LOT-*` と `FE-VERSION-LOT-*` を `it.each` で網羅
3. **Phase 4b/4c rate** — `FE-REQ-SALES-ACTION-001 / 003`, `FE-REQ-RESERVATION-001`, `FE-REQ-CONSIGNMENT-001`
4. **Phase 9a-9c** — fast-check 導入、純粋関数抽出 (Phase 4e の wire-up より先)
5. **Phase 7** — `describeApiError` 全 variant
6. **Phase 6** — `renderWithRealRouter` 追加と FE-NAV-*
7. **Phase 8** — CI artifact

## 拡充手順 (新しいテストを追加するとき)

### 新しい画面 / 振る舞いを追加するとき

1. **ID 発行**: `FE-{prefix}-{area}-{seq}` の規約で新規 ID を作る。`prefix` は §ID 体系の表から選ぶ。
2. **oracle を本書に追記**: 該当 matrix (Validation Matrix / MSW Scenario / Page State Matrix / Version Required 等) に行を追加し、**Status 列は ❌ から始める**。
3. **テスト実装**: `tests/unit/{layer}/{Component}.test.tsx` か `tests/unit/pages/{Page}.test.tsx` に追加。oracle ID をテスト名先頭に必ず入れる (`it("FE-XXX-001: …", …)`)。
4. **Status 更新**: 緑になったら本書の Status 列を ✅ に変える。部分緑なら ⚠️ にする。
5. **constraint に当たったら**: §既知制約 に `FE-CONSTRAINT-NNN` を追記し、影響 ID と回避策を書く。Status は 🔒 (blocked) にする。

### 新しい純粋関数を追加するとき (PBT 同時導入)

1. `src/lib/` に export 関数として置く (コンポーネントから直接書かない)。
2. example test を `tests/unit/lib/{name}.test.ts` に書く。
3. **同じ commit で** `tests/unit/pbt/{name}.test.ts` に fast-check property を 1 つ以上書く。
4. 本書 §Frontend PBT Scope の表に `FE-PBT-{AREA}-NNN` を新規行追加。

### Status 列を更新するルール

- ✅ 全 ID のテストが green、かつ rev2 で意図された oracle を満たす
- ⚠️ 一部の ID が未消化 / 部分的にしか検査していない (理由を 1 行コメントで残す)
- ❌ テスト未実装
- 🔒 既知制約により実装不能 / 仕様変更待ち (`FE-CONSTRAINT-NNN` への参照を付ける)
- ⏳ UI / プロダクトコード改修待ち (Red から Green への切替予定が立っている)

## Evidence / CI

| ID | 対象 | artifact | 実行タイミング | 状態 |
|---|---|---|---|---|
| `FE-EVID-UNIT-001` | Vitest component/page/unit | JUnit XML または Vitest report | PR | ❌ |
| `FE-EVID-MSW-001` | MSW request assertion | 失敗時の request log | PR | ❌ |
| `FE-EVID-COVERAGE-001` | coverage | coverage report | PR または nightly | ❌ |
| `FE-EVID-PBT-001` (新) | fast-check property | seed / counterexample を失敗時に出力 | PR、nightly で `FE_PBT_RUNS=1000` | ❌ |
| `FE-EVID-E2E-001` | Playwright smoke | HTML report, trace, screenshot | PR | ⚠️ (既存 smoke 1 本) |
| `FE-EVID-BACKEND-E2E-001` | `E2E_BACKEND=1` | Playwright trace/video/screenshot | manual / nightly | ⚠️ |
| `FE-EVID-MANUAL-001` | UAT / manual | screenshot, reviewer, date, build SHA | release 前 | n/a |

## Storybook 後続方針

Storybook は初期スコープ外。後続で visual / interaction / a11y test に移しやすいよう、状態を外から注入できる component 境界を維持する。

| 後続対象 | Story |
|---|---|
| `LotActionForm` | default, disabled, pending, validation error |
| `RichActionForms` (各 7 form) | default, validation errors, 調整率範囲外, pending |
| `LotSelectDialog` | loading, success, excludeCase 適用, 空, error |
| `SalesCaseCreateDialog` | default, validation errors, pending, API error |
| 上長承認モーダル | default, チェック付与, 確定後 (直接入力可) |
| lot action area | status 別 action matrix |
| `PriceCheckPage` | idle, success, null adjustment, 400, 502, 503 |
| Guard fallback | viewer/operator/admin/fallback |

## 手動でテストすべき部分

| 観点 | 理由 | evidence |
|---|---|---|
| 業務フロー、状態名、ボタン文言、権限設計の妥当性 | 業務担当者の判断が必要 | review comment / UAT log |
| 「変更する」承認モーダルが業務統制として十分か (固定承認者・チェックのみゲート) | 上長承認の運用妥当性は業務判断 | reviewer approval / UAT log |
| 調整率の画面 % / API ÷100 の二重表現が利用者に伝わるか | 単位誤認は数値リスクが大きい | UAT / 探索的テスト |
| ロット選択モーダルの操作感 (大量ロット時、絞込・並び替え) | 操作量が多くなる業務 | UX レビュー |
| 実機での操作感、スクロール、タップ、見切れ | 自動 DOM assertion では判断できない | screenshot / device note |
| スクリーンリーダーでの読み上げ順とエラー理解 | axe/ARIA だけでは体験を保証できない | accessibility review note |
| 探索的テスト | 仕様化されていないリスク発見 | exploratory test memo |

## Assumptions

- MSW を採用済み。
- `JsonActionForm` は廃止済み。後継 `src/components/organisms/forms/rich-actions/RichActionForms.tsx` の 7 form (DirectAppraisal / SalesContract / DateVersionAction / ReservationPrice / ReservationConfirmation / ConsignmentDesignation / ConsignmentResult) を扱う。
- 共通 component `LotSelectDialog` / `SalesCaseCreateDialog` および hook `use-available-lots` / `use-code-masters` を前提とする。
- 調整率は画面 90〜110 (%), API ÷100 (0.9〜1.1) のスケール変換を前提とする。
- 査定合計は `Σ 単価 × rate ÷ 100` のリアルタイム計算を既定とし、直接入力は上長承認モーダル経由でのみ許可する。承認者は固定値 `営業部長（システム既定）`。
- ロットは「製造完了」かつ「未割当」のみ案件に組み込める (`BR-LOT-MANUFACTURED-REQUIRED` / `BR-LOT-ASSIGNMENT-UNIQUE`)。
- 案件ロット差し替えは direct/before_appraisal・consignment/before_consignment のみ可、`PUT /sales-cases/{id}/lots` で楽観ロック。
- Pact は初期スコープ外。
- Storybook は初期スコープ外だが、後続導入しやすい component 境界を維持する。
- Playwright は起動、routing、代表業務導線のみ担当する。
- **PBT (`fast-check`) を採用する**。純粋関数の網羅は example test と PBT の両建てとする。
- `docs/frontend-test-implementation-plan.md` は本書 §Phase ロードマップに統合し、削除可とする (重複防止)。
