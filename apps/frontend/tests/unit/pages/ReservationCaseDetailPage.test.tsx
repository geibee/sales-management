/**
 * `ReservationCaseDetailPage` (FE-PAGE-RESERVATION-001 /
 *  FE-REQ-RESERVATION-001..002 / FE-VERSION-RES-001)。
 *
 * - 200 success → status badge と JSON pre が出る
 * - DELETE /sales-cases/{id}/reservation/determination で body に version
 * - 409 → toast.error
 */
import { ReservationCaseDetailPage } from "@/pages/reservation-cases/ReservationCaseDetailPage";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { toast } from "sonner";
import { describe, expect, it, vi } from "vitest";
import { makeReservationSalesCase } from "../../support/fixtures";
import { renderWithRouter } from "../../support/render";
import { requestsFor, server } from "../../support/server";

const ID = "2026-S-002";

function authDisabled(): void {
  server.use(http.get("/api/auth/config", () => HttpResponse.json({ enabled: false })));
}

describe("<ReservationCaseDetailPage> (FE-PAGE-RESERVATION-* / FE-REQ-RESERVATION-*)", () => {
  it("FE-PAGE-RESERVATION-001: 200 → status label / JSON pre", async () => {
    authDisabled();
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeReservationSalesCase({
            salesCaseNumber: ID,
            caseType: "reservation",
            status: "reservation_confirmed",
          }),
        ),
      ),
    );
    renderWithRouter(<ReservationCaseDetailPage id={ID} />);
    expect(
      await screen.findByRole("heading", { name: new RegExp(`予約販売案件 ${ID}`) }),
    ).toBeInTheDocument();
    expect(screen.getByText("予約確定済")).toBeInTheDocument();
  });

  it("FE-REQ-RESERVATION-002 / FE-VERSION-RES-001: 確定取消 → DELETE body に version", async () => {
    authDisabled();
    vi.spyOn(window, "confirm").mockReturnValue(true);
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeReservationSalesCase({
            salesCaseNumber: ID,
            caseType: "reservation",
            status: "reservation_confirmed",
            version: 7,
          }),
        ),
      ),
      http.delete(
        `/api/sales-cases/${ID}/reservation/determination`,
        () => new HttpResponse(null, { status: 204 }),
      ),
    );
    renderWithRouter(<ReservationCaseDetailPage id={ID} />);
    fireEvent.click(await screen.findByRole("button", { name: /取消/ }));
    await waitFor(() =>
      expect(requestsFor(`/api/sales-cases/${ID}/reservation/determination`)).toHaveLength(1),
    );
    expect(requestsFor(`/api/sales-cases/${ID}/reservation/determination`)[0].body).toEqual({
      version: 7,
    });
  });

  it("FE-ERR-PAGE-001: 確定取消 409 → toast.error、page は残る", async () => {
    authDisabled();
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const toastError = vi.spyOn(toast, "error");
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeReservationSalesCase({
            salesCaseNumber: ID,
            caseType: "reservation",
            status: "reservation_confirmed",
            version: 7,
          }),
        ),
      ),
      http.delete(`/api/sales-cases/${ID}/reservation/determination`, () =>
        HttpResponse.json(
          { type: "optimistic-lock-conflict", title: "Conflict", status: 409 },
          { status: 409 },
        ),
      ),
    );
    renderWithRouter(<ReservationCaseDetailPage id={ID} />);
    fireEvent.click(await screen.findByRole("button", { name: /取消/ }));
    await waitFor(() => expect(toastError).toHaveBeenCalled());
    expect(screen.getByRole("heading", { name: new RegExp(ID) })).toBeInTheDocument();
  });
});
