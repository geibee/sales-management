import type { LotStatus } from "@/contracts";

export const LOT_STATUS_LABEL: Record<LotStatus, string> = {
  manufacturing: "製造中",
  manufactured: "製造完了",
  shipping_instructed: "出荷指示済",
  shipped: "出荷完了",
  conversion_instructed: "変換指示済",
};

export function lotStatusLabel(status: string | null | undefined): string {
  if (!status) return "(unknown)";
  return (LOT_STATUS_LABEL as Record<string, string>)[status] ?? status;
}

export const SALES_CASE_STATUS_LABEL: Record<string, string> = {
  appraised: "査定済",
  contracted: "契約済",
  shipping_instructed: "出荷指示済",
  shipping_completed: "出荷完了",
};

export const ESTIMATE_STATUS_LABEL: Record<string, string> = {
  before_reservation: "査定前",
  reserved: "予約済",
  reservation_confirmed: "予約確定済",
  reservation_delivered: "引渡済",
};

export const CONSIGNMENT_STATUS_LABEL: Record<string, string> = {
  consignment_designated: "委託指定済",
  consignment_result_entered: "結果登録済",
};

export function caseStatusLabel(
  caseType: string | null | undefined,
  status: string | null | undefined,
): string {
  if (!status) return "(unknown)";
  const map =
    caseType === "reservation"
      ? ESTIMATE_STATUS_LABEL
      : caseType === "consignment"
        ? CONSIGNMENT_STATUS_LABEL
        : SALES_CASE_STATUS_LABEL;
  return map[status] ?? status;
}

export type LotAction =
  | "complete-manufacturing"
  | "cancel-manufacturing-completion"
  | "instruct-shipping"
  | "complete-shipping"
  | "instruct-item-conversion"
  | "cancel-item-conversion-instruction";

/**
 * Whether a state-transition action is allowed given the current lot status.
 * Mirrors the backend's status-machine constraints (LotRoutes.fs).
 */
export function lotActionEnabled(action: LotAction, status: string | null | undefined): boolean {
  switch (action) {
    case "complete-manufacturing":
      return status === "manufacturing";
    case "cancel-manufacturing-completion":
      return status === "manufactured";
    case "instruct-shipping":
      return status === "manufactured";
    case "complete-shipping":
      return status === "shipping_instructed";
    case "instruct-item-conversion":
      return status === "manufactured";
    case "cancel-item-conversion-instruction":
      return status === "conversion_instructed";
    default:
      return false;
  }
}
