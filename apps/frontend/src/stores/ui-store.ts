import { create } from "zustand";

export type LotModalKind =
  | "completeManufacturing"
  | "cancelManufacturingCompletion"
  | "instructShipping"
  | "completeShipping"
  | "instructItemConversion"
  | null;

export type LotStatusFilter = "all" | "in-progress" | "completed" | "shipping" | "shipped";

type UiState = {
  activeLotModal: LotModalKind;
  openLotModal: (m: NonNullable<LotModalKind>) => void;
  closeLotModal: () => void;
  lotFilterStatus: LotStatusFilter;
  setLotFilterStatus: (s: LotStatusFilter) => void;
};

export const useUi = create<UiState>((set) => ({
  activeLotModal: null,
  openLotModal: (m) => set({ activeLotModal: m }),
  closeLotModal: () => set({ activeLotModal: null }),
  lotFilterStatus: "all",
  setLotFilterStatus: (s) => set({ lotFilterStatus: s }),
}));
