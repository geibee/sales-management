# API 仕様 — 値引き申請（記入済みサンプル・架空）

> 承認・取り下げの API は同型のため本サンプルでは申請のみ示す（実運用では API-02 / API-03 として同様に起こす）。

## メタ情報

| 項目 | 記入 |
|---|---|
| 依頼 ID / 仕様 ID | CR-20260706-discount-approval / API-01 |
| 種別 | 操作（状態遷移の実行） |

## 1. 対応する業務操作

| 項目 | 記入 |
|---|---|
| 実行するドメイン操作 | DA-01「値引きを申請する」（DA-ST-01 / 02 / 03 / 06） |
| 呼び出し元の想定 | 販売案件詳細画面（PG-01）のみ。外部システムからの利用は想定しない |
| 権限 | DA-01 §7 権限マトリクスに従う（API 独自の差異なし） |

## 2. 入力項目

| 項目 | 意味 | 必須 | 制約（DA 参照） |
|---|---|---|---|
| 対象案件 | 値引きする販売案件 | ○ | 存在し「査定済み」であること（DA-ST-01〜03） |
| 値引き率 | | ○ | DA-01 §5（0% 超〜10%、小数第 1 位まで） |
| 申請理由 | 上長が見るメモ | ○ | DA-01 §5（1〜500 文字） |

## 3. 出力項目

| 項目 | 意味 |
|---|---|
| 更新後の案件状態 | 「値引き承認待ち」 |
| 承認された場合の想定契約額 | 参考表示用（DA-01 §5 の計算式） |

## 4. 既定からの逸脱

なし（競合 = DEF-API-02 楽観ロック、エラー形式 = DEF-API-01、認可 = DEF-API-03）。

## 5. 互換性

| 観点 | 記入 |
|---|---|
| 既存 API 利用箇所への影響 | なし（新規エンドポイント。既存 `getSalesCase` 応答への状態値追加あり → 下記承認欄参照） |
| 破壊的変更か | いいえ（enum への値追加は本リポジトリでは互換扱い。フロントは生成 zod で追随） |

## 6. 契約差分の承認（エンジニア記入・承認の記録）

```yaml
# openapi.yaml 差分の要点（エンジニア起案。全文は契約差分コミット参照）
paths:
  /sales-cases/{salesCaseId}/discount-requests:
    post:
      operationId: requestDiscount        # 既存の resource/sub-resource 命名に合わせる
      requestBody: { discountRate, reason, version }   # rate は % 実数（10.0 = 10%）— 既存 API の
                                                       # 「rate ÷ 100 事故」(LESSONS 参照) を避けるため単位を description に明記
      responses:
        "201": DiscountRequestResult      # 更新後状態 + 想定契約額
        "400" / "403" / "404" / "409": ProblemDetails  # DEF-API-01〜03
components:
  schemas:
    SalesCaseStatus:
      enum: [..., discountPending]        # 既存 enum に追加（互換）
```

| 項目 | 記入 |
|---|---|
| openapi.yaml 差分（コミット / PR リンク） | （サンプルのため省略。実運用では契約差分コミットの URL） |
| 差分の種類 | 新規エンドポイント + 互換な enum 追加 |
| エンジニア承認（承認者・日付） | geibee・2026-07-08 — 命名を `discounts` → `discount-requests` に修正の上承認（申請という業務語に合わせる） |

---

## 品質ゲート化対応表（③で AI が記入済みの想定サンプル）

| 仕様欄 | 追加するゲート | テスト ID / 検査 |
|---|---|---|
| 入出力項目 | openapi 差分 + Spectral + 生成コードドリフト + レスポンススキーマ検証 | （契約ゲート） |
| 対応する業務操作 | 統合テスト（DA-EX-01〜06 を HTTP 経由で再検証） | `DiscountRequestTests` |
| 権限 | 認可テスト（未認証 401 / 上長ロール 403） | `DiscountAuthTests` |
| 競合制御 | 並列申請で片方 409 | `DiscountConcurrencyTests` |
| 互換性 | oasdiff（破壊的変更なしを機械確認） | （契約ゲート） |
| フロント連携 | Pact interaction `requestDiscount` + MSW zod 検証 | `FE-REQ-DISCOUNT-001` |
