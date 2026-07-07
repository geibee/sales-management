/**
 * Phase 3b — `LotDetailPage` の状態×action マトリクス (FE-MATRIX-LOT-*) と
 * version 必須検査 (FE-VERSION-LOT-002..006)。
 *
 * - FE-MATRIX-LOT-001..005: BR-LOT-STATE-ACTION の frontend oracle。
 *   各 status で 6 つの遷移 button の disabled 状態を DOM で検査する。
 * - FE-MATRIX-LOT-006 (unknown / null) は page 層では検査しない:
 *   契約 (zod enum) と contract-guard が未知 status のモック自体を禁止しており
 *   page に到達する経路が存在しない。純粋関数 `lotActionEnabled` の
 *   未知/null 網羅は `FE-PBT-STATUS-001` (tests/unit/pbt/lot-action-enabled.test.ts)
 *   が担保する。
 * - FE-VERSION-LOT-002..006: 各遷移 action の request body に現在の
 *   `version` が含まれる (BR-VERSION-REQUIRED)。
 */
import type { LotResponse } from "@/contracts";
import { LotDetailPage } from "@/pages/lots/LotDetailPage";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { makeLot } from "../../support/fixtures";
import { renderWithRouter } from "../../support/render";
import { requestsFor, server } from "../../support/server";

const ID = "2026-A-1";

function authDisabled(): void {
  server.use(http.get("/api/auth/config", () => HttpResponse.json({ enabled: false })));
}

async function renderWithStatus(status: LotResponse["status"], version = 3): Promise<void> {
  authDisabled();
  server.use(
    http.get(`/api/lots/${ID}`, () =>
      HttpResponse.json(makeLot({ lotNumber: ID, status, version })),
    ),
  );
  renderWithRouter(<LotDetailPage id={ID} />);
  await screen.findByRole("heading", { name: new RegExp(ID) });
}

/** DOM 上の 6 遷移 button。「取消を実行」は製造完了取消 / 品目変換取消の 2 つが DOM 順で並ぶ。 */
function actionButtons() {
  const cancels = screen.getAllByRole("button", { name: "取消を実行" });
  return {
    completeMfg: screen.getByRole("button", { name: "製造完了を登録" }),
    cancelMfg: cancels[0],
    instructShip: screen.getByRole("button", { name: "出荷指示を登録" }),
    completeShip: screen.getByRole("button", { name: "出荷完了を登録" }),
    instructConv: screen.getByRole("button", { name: "品目変換を指示" }),
    cancelConv: cancels[1],
  };
}

describe("<LotDetailPage> 状態×action マトリクス (FE-MATRIX-LOT-*)", () => {
  it.each([
    ["FE-MATRIX-LOT-001", "manufacturing", [true, false, false, false, false, false]],
    ["FE-MATRIX-LOT-002", "manufactured", [false, true, true, false, true, false]],
    ["FE-MATRIX-LOT-003", "shipping_instructed", [false, false, false, true, false, false]],
    ["FE-MATRIX-LOT-004", "shipped", [false, false, false, false, false, false]],
    ["FE-MATRIX-LOT-005", "conversion_instructed", [false, false, false, false, false, true]],
  ] as const)("%s: status=%s の action 可否", async (_id, status, enabled) => {
    await renderWithStatus(status);
    const b = actionButtons();
    const [completeMfg, cancelMfg, instructShip, completeShip, instructConv, cancelConv] = enabled;
    expect(!b.completeMfg.hasAttribute("disabled")).toBe(completeMfg);
    expect(!b.cancelMfg!.hasAttribute("disabled")).toBe(cancelMfg);
    expect(!b.instructShip.hasAttribute("disabled")).toBe(instructShip);
    expect(!b.completeShip.hasAttribute("disabled")).toBe(completeShip);
    expect(!b.instructConv.hasAttribute("disabled")).toBe(instructConv);
    expect(!b.cancelConv!.hasAttribute("disabled")).toBe(cancelConv);
  });

  // FE-MATRIX-LOT-006 (unknown / null) — 契約 enum + contract-guard により
  // 未知 status は page に到達不能。FE-PBT-STATUS-001 が oracle を網羅する。
});

describe("<LotDetailPage> version 必須 (FE-VERSION-LOT-002..006)", () => {
  it("FE-VERSION-LOT-002: 製造完了取消 → POST body.version", async () => {
    await renderWithStatus("manufactured", 5);
    server.use(
      http.post(`/api/lots/${ID}/cancel-manufacturing-completion`, () =>
        HttpResponse.json(makeLot({ lotNumber: ID, status: "manufacturing", version: 6 })),
      ),
    );
    fireEvent.click(actionButtons().cancelMfg!);
    await waitFor(() =>
      expect(requestsFor(`/api/lots/${ID}/cancel-manufacturing-completion`)).toHaveLength(1),
    );
    const [req] = requestsFor(`/api/lots/${ID}/cancel-manufacturing-completion`);
    expect(req!.body).toMatchObject({ version: 5 });
  });

  it("FE-VERSION-LOT-003: 出荷指示 → POST body.deadline + version", async () => {
    await renderWithStatus("manufactured", 5);
    server.use(
      http.post(`/api/lots/${ID}/instruct-shipping`, () =>
        HttpResponse.json(makeLot({ lotNumber: ID, status: "shipping_instructed", version: 6 })),
      ),
    );
    fireEvent.change(screen.getByLabelText("出荷期限"), { target: { value: "2026-05-10" } });
    fireEvent.click(actionButtons().instructShip);
    await waitFor(() => expect(requestsFor(`/api/lots/${ID}/instruct-shipping`)).toHaveLength(1));
    const [req] = requestsFor(`/api/lots/${ID}/instruct-shipping`);
    expect(req!.body).toMatchObject({ deadline: "2026-05-10", version: 5 });
  });

  it("FE-VERSION-LOT-004: 出荷完了 → POST body.date + version", async () => {
    await renderWithStatus("shipping_instructed", 5);
    server.use(
      http.post(`/api/lots/${ID}/complete-shipping`, () =>
        HttpResponse.json(makeLot({ lotNumber: ID, status: "shipped", version: 6 })),
      ),
    );
    // 「日付」label は 製造完了 / 出荷完了 の 2 form にあり、DOM 順で後者は index 1
    const dateInputs = screen.getAllByLabelText("日付");
    fireEvent.change(dateInputs[1]!, { target: { value: "2026-05-11" } });
    fireEvent.click(actionButtons().completeShip);
    await waitFor(() => expect(requestsFor(`/api/lots/${ID}/complete-shipping`)).toHaveLength(1));
    const [req] = requestsFor(`/api/lots/${ID}/complete-shipping`);
    expect(req!.body).toMatchObject({ date: "2026-05-11", version: 5 });
  });

  it("FE-VERSION-LOT-005: 品目変換指示 → POST body.destinationItem + version", async () => {
    await renderWithStatus("manufactured", 5);
    server.use(
      http.post(`/api/lots/${ID}/instruct-item-conversion`, () =>
        HttpResponse.json(makeLot({ lotNumber: ID, status: "conversion_instructed", version: 6 })),
      ),
    );
    fireEvent.change(screen.getByLabelText("変換先品目"), { target: { value: "2025-T-902" } });
    fireEvent.click(actionButtons().instructConv);
    await waitFor(() =>
      expect(requestsFor(`/api/lots/${ID}/instruct-item-conversion`)).toHaveLength(1),
    );
    const [req] = requestsFor(`/api/lots/${ID}/instruct-item-conversion`);
    expect(req!.body).toMatchObject({ destinationItem: "2025-T-902", version: 5 });
  });

  it("FE-VERSION-LOT-006: 品目変換指示取消 → DELETE body.version", async () => {
    await renderWithStatus("conversion_instructed", 5);
    server.use(
      http.delete(`/api/lots/${ID}/instruct-item-conversion`, () =>
        HttpResponse.json(makeLot({ lotNumber: ID, status: "manufactured", version: 6 })),
      ),
    );
    fireEvent.click(actionButtons().cancelConv!);
    await waitFor(() =>
      expect(requestsFor(`/api/lots/${ID}/instruct-item-conversion`)).toHaveLength(1),
    );
    const [req] = requestsFor(`/api/lots/${ID}/instruct-item-conversion`);
    expect(req!.body).toMatchObject({ version: 5 });
  });
});
