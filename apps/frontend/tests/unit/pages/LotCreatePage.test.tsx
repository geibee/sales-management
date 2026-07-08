/**
 * `LotCreatePage` (FE-REQ-LOT-CREATE-001..004 / FE-NAV-LOT-001 / FE-ERR-PAGE-002)。
 *
 * 検査するのは以下:
 *   - `Guard` の fallback (auth ON / role なし) と children 表示 (auth OFF)
 *   - `POST /lots` body の数値型 (code-master は integer / 各 spec は finite)
 *   - 二重 submit を 1 回に抑える pending 表示
 *   - 400 problem は toast.error にするが navigation しない
 *   - `GET /code-masters` の cascade (事業部 → 部 → 課 → option 名称表示)
 */
import { LotCreatePage } from "@/pages/lots/LotCreatePage";
import { useAuth } from "@/stores/auth-store";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { toast } from "sonner";
import { describe, expect, it, vi } from "vitest";
import { deferred } from "../../support/deferred";
import { makeCodeMasters } from "../../support/fixtures";
import { renderWithRouter } from "../../support/render";
import { requestsFor, server } from "../../support/server";

function authDisabled(): void {
  server.use(
    http.get("/api/auth/config", () => HttpResponse.json({ enabled: false })),
    http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
  );
}

describe("<LotCreatePage> (FE-REQ-LOT-CREATE-* / FE-NAV-LOT-001)", () => {
  it("Guard: auth ON で role なし → fallback 表示", async () => {
    useAuth.getState().clear();
    server.use(
      http.get("/api/auth/config", () =>
        HttpResponse.json({ enabled: true, authority: "https://idp", audience: "api" }),
      ),
      http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
    );
    renderWithRouter(<LotCreatePage />);
    expect(
      await screen.findByText("作成には operator 以上のロールが必要です。"),
    ).toBeInTheDocument();
  });

  it("FE-REQ-LOT-CREATE-004: code-masters 取得後、事業部 dropdown に name が並ぶ", async () => {
    authDisabled();
    renderWithRouter(<LotCreatePage />);
    await waitFor(() => expect(requestsFor("/api/code-masters")).toHaveLength(1));
    const opts = await screen.findAllByRole("option", { hidden: true });
    const labels = opts.map((o) => o.textContent ?? "");
    expect(labels).toEqual(expect.arrayContaining(["営業1部", "営業2部"]));
  });

  it("FE-REQ-LOT-CREATE-004: 事業部 → 部 → 課 の cascade 段階遷移 (絞り込み + 先頭自動選択)", async () => {
    // 事業部 20 の配下に部を 2 つ持たせ、部の切り替えでも課が連動することを検査する。
    // radix Select の選択操作は click で駆動する (item の pointerType 初期値が
    // "touch" のため click で handleSelect が走る)。SelectValue の表示テキストは
    // jsdom では ItemText portal が不安定なため oracle にせず、cascade の結果は
    // 「submit body に届く code」で検査する (API に届く値が本来の観測点)。
    authDisabled();
    server.use(
      http.get("/api/code-masters", () =>
        HttpResponse.json(
          makeCodeMasters({
            departments: [
              { code: 110, name: "営業1課", divisionCode: 10 },
              { code: 210, name: "営業2課", divisionCode: 20 },
              { code: 220, name: "営業2部特販課", divisionCode: 20 },
            ],
            sections: [
              { code: 1110, name: "第1係", departmentCode: 110 },
              { code: 2110, name: "第2係", departmentCode: 210 },
              { code: 2210, name: "特販係", departmentCode: 220 },
            ],
          }),
        ),
      ),
      http.post("/api/lots", () =>
        HttpResponse.json(
          { status: "manufacturing", lotNumber: "2026-A-1", version: 1 },
          { status: 201 },
        ),
      ),
    );
    renderWithRouter(<LotCreatePage />);
    await waitFor(() => expect(requestsFor("/api/code-masters").length).toBeGreaterThan(0));

    // 事業部を 営業2部 (20) へ変更
    fireEvent.keyDown(screen.getByRole("combobox", { name: "事業部" }), { key: "ArrowDown" });
    fireEvent.click(await screen.findByRole("option", { name: "営業2部" }));
    await waitFor(() =>
      expect(screen.getByRole("combobox", { name: "事業部" })).toHaveTextContent("営業2部"),
    );

    // 部の選択肢は事業部 20 の配下だけに絞り込まれている (段階遷移の絞り込み)
    fireEvent.keyDown(screen.getByRole("combobox", { name: "部" }), { key: "ArrowDown" });
    const listbox = await screen.findByRole("listbox");
    const labels = within(listbox)
      .getAllByRole("option")
      .map((o) => o.textContent);
    expect(labels).toEqual(["営業2課", "営業2部特販課"]);

    // 部を 営業2部特販課 (220) へ切り替え → 課は配下の先頭 (特販係 2210) に連動する
    fireEvent.click(within(listbox).getByRole("option", { name: "営業2部特販課" }));

    // cascade の最終結果は submit body で検査する
    console.log(
      "[debug] native selects:",
      Array.from(document.querySelectorAll("select")).map((s) => s.value),
    );
    fireEvent.click(screen.getByRole("button", { name: /作成/ }));
    await waitFor(() => expect(requestsFor("/api/lots")).toHaveLength(1));
    expect(requestsFor("/api/lots")[0]!.body).toMatchObject({
      divisionCode: 20,
      departmentCode: 220,
      sectionCode: 2210,
    });
  });

  it("FE-REQ-LOT-CREATE-001: 既定値で submit → body は integer (事業部/部/課/工程/検査/製造) と finite 数値", async () => {
    authDisabled();
    server.use(
      http.post("/api/lots", () =>
        HttpResponse.json(
          { status: "manufacturing", lotNumber: "2026-A-1", version: 1 },
          { status: 201 },
        ),
      ),
    );
    renderWithRouter(<LotCreatePage />);
    await screen.findByLabelText("年度");
    fireEvent.click(screen.getByRole("button", { name: /作成/ }));
    await waitFor(() => expect(requestsFor("/api/lots")).toHaveLength(1));
    const body = requestsFor("/api/lots")[0]!.body as {
      lotNumber: { year: number; location: string; seq: number };
      divisionCode: number;
      departmentCode: number;
      sectionCode: number;
      processCategory: number;
      inspectionCategory: number;
      manufacturingCategory: number;
      details: unknown[];
    };
    for (const key of [
      "divisionCode",
      "departmentCode",
      "sectionCode",
      "processCategory",
      "inspectionCategory",
      "manufacturingCategory",
    ] as const) {
      expect(typeof body[key]).toBe("number");
      expect(Number.isInteger(body[key])).toBe(true);
    }
    expect(typeof body.lotNumber.year).toBe("number");
    expect(typeof body.lotNumber.seq).toBe("number");
    expect(body.details).toHaveLength(1);
  });

  it("FE-REQ-LOT-CREATE-002 / FE-COMP-RICH-COMMON-001 相当: 二重 submit でも POST は 1 回", async () => {
    authDisabled();
    const d = deferred<Response>();
    server.use(http.post("/api/lots", () => d.promise));
    renderWithRouter(<LotCreatePage />);
    await screen.findByLabelText("年度");
    const btn = screen.getByRole("button", { name: /作成/ });
    fireEvent.click(btn);
    // pending 中の連打 (button label が「作成中…」に切り替わる) は no-op
    expect(await screen.findByRole("button", { name: /作成中…/ })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: /作成中…/ }));
    fireEvent.click(screen.getByRole("button", { name: /作成中…/ }));
    d.resolve(
      HttpResponse.json(
        { status: "manufacturing", lotNumber: "2026-A-1", version: 1 },
        { status: 201 },
      ) as unknown as Response,
    );
    await waitFor(() => expect(requestsFor("/api/lots")).toHaveLength(1));
  });

  it("FE-REQ-LOT-CREATE-003 / FE-ERR-PAGE-002: 400 problem → toast.error、navigation なし", async () => {
    authDisabled();
    const toastError = vi.spyOn(toast, "error");
    server.use(
      http.post("/api/lots", () =>
        HttpResponse.json(
          { type: "validation-error", title: "Bad Request", status: 400, detail: "boom" },
          { status: 400 },
        ),
      ),
    );
    renderWithRouter(<LotCreatePage />);
    await screen.findByLabelText("年度");
    fireEvent.click(screen.getByRole("button", { name: /作成/ }));
    await waitFor(() => expect(toastError).toHaveBeenCalled());
    // 失敗時は page 上に detail content が描画されない (form 画面が残る)
    expect(screen.getByLabelText("年度")).toBeInTheDocument();
  });
});
