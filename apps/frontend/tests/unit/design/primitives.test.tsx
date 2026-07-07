/**
 * `src/components/design/primitives.tsx`（計画 P2-3 — 325 行・新規で 0 テスト）。
 *
 * 検証方針: 主要 props のレンダリングと「意味色クラス」(pill-ok / pill-warn …)
 * の付与、StatusFlow の completed/current/pending ブランチ判定と branch ノード、
 * EmptyState / KPI / Sparkline / DLRow の出し分けを確認する。
 * 配色は CSS 変数なので、ここではクラス名 = 意味の写像が崩れていないかを oracle にする。
 */
import {
  CaseStatusPill,
  CaseTypePill,
  DCard,
  DCardBody,
  DCardHeader,
  DLRow,
  DesignPageHeader,
  EmptyState,
  KPI,
  LotStatusPill,
  Pill,
  Sparkline,
  StatusFlow,
} from "@/components/design/primitives";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

describe("Pill", () => {
  it("tone に応じた意味色クラスを付ける", () => {
    const { container } = render(<Pill tone="ok">完了</Pill>);
    const el = container.querySelector("span.pill");
    expect(el).toHaveClass("pill", "pill-ok");
    expect(el).toHaveTextContent("完了");
  });

  it("dot / mono / className が反映される", () => {
    const { container } = render(
      <Pill tone="warn" dot mono className="extra">
        x
      </Pill>,
    );
    const el = container.querySelector("span.pill");
    expect(el).toHaveClass("pill-warn", "pill-mono", "extra");
    expect(el?.querySelector("span.dot")).not.toBeNull();
  });

  it("tone 未指定は neutral、dot 既定は出ない", () => {
    const { container } = render(<Pill>n</Pill>);
    const el = container.querySelector("span.pill");
    expect(el).toHaveClass("pill-neutral");
    expect(el?.querySelector("span.dot")).toBeNull();
  });
});

describe("Status 派生 Pill (format.ts と連動)", () => {
  it("LotStatusPill: 状態ラベルと対応トーン", () => {
    const { container } = render(<LotStatusPill status="manufactured" />);
    const el = container.querySelector("span.pill");
    expect(el).toHaveTextContent("製造完了");
    expect(el).toHaveClass("pill-ok"); // manufactured → ok
  });

  it("CaseStatusPill: caseType で同 code でもラベルが変わる", () => {
    const direct = render(<CaseStatusPill caseType="direct" status="shipping_instructed" />);
    expect(direct.container.querySelector("span.pill")).toHaveClass("pill-warn");
    expect(direct.container.querySelector("span.pill")).toHaveTextContent("出荷指示済");
  });

  it("CaseTypePill: outline トーンで種別ラベル", () => {
    const { container } = render(<CaseTypePill caseType="reservation" />);
    const el = container.querySelector("span.pill");
    expect(el).toHaveClass("pill-outline");
    expect(el).toHaveTextContent("予約");
  });
});

describe("DCard 群", () => {
  it("DCard は card-d クラスと追加クラス・HTML 属性を通す", () => {
    const { container } = render(
      <DCard className="x" data-testid="card">
        body
      </DCard>,
    );
    const el = container.querySelector("div.card-d");
    expect(el).toHaveClass("card-d", "x");
    expect(el).toHaveAttribute("data-testid", "card");
  });

  it("DCardHeader: title / icon / actions スロット", () => {
    render(
      <DCardHeader
        title="見出し"
        icon={<span data-testid="ico" />}
        actions={<button type="button">act</button>}
      />,
    );
    expect(screen.getByText("見出し")).toBeInTheDocument();
    expect(screen.getByTestId("ico")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "act" })).toBeInTheDocument();
  });

  it("DCardBody: tight / flush フラグでクラスが付く", () => {
    const { container } = render(
      <DCardBody tight flush className="c">
        b
      </DCardBody>,
    );
    expect(container.querySelector("div.card-body-d")).toHaveClass("tight", "flush", "c");
  });
});

describe("DLRow", () => {
  it("label を dt、children を dd に出す", () => {
    const { container } = render(<DLRow label="数量">10</DLRow>);
    expect(container.querySelector("dt")).toHaveTextContent("数量");
    expect(container.querySelector("dd")).toHaveTextContent("10");
  });
});

describe("EmptyState", () => {
  it("t1 必須、t2 と icon は任意で出し分け", () => {
    const { rerender, container } = render(<EmptyState t1="なし" />);
    expect(screen.getByText("なし")).toBeInTheDocument();
    expect(container.querySelector(".t2")).toBeNull();
    expect(container.querySelector(".ico")).toBeNull();

    rerender(<EmptyState icon={<span />} t1="なし" t2="補足" />);
    expect(screen.getByText("補足")).toBeInTheDocument();
    expect(container.querySelector(".ico")).not.toBeNull();
  });
});

describe("KPI", () => {
  it("label / value / unit / delta を描画し deltaTone クラスを付ける", () => {
    const { container } = render(
      <KPI label="売上" value="1,000" unit="円" delta="+5%" deltaTone="up" />,
    );
    expect(screen.getByText("売上")).toBeInTheDocument();
    expect(screen.getByText("1,000")).toBeInTheDocument();
    expect(screen.getByText("円")).toBeInTheDocument();
    const delta = container.querySelector(".kpi-delta");
    expect(delta).toHaveClass("up");
    expect(delta).toHaveTextContent("+5%");
  });

  it("delta 省略時は delta 行を出さない", () => {
    const { container } = render(<KPI label="件数" value="3" />);
    expect(container.querySelector(".kpi-delta")).toBeNull();
  });
});

describe("Sparkline", () => {
  it("データ点があれば svg と path を描く", () => {
    const { container } = render(<Sparkline data={[1, 2, 3, 2, 5]} />);
    const svg = container.querySelector("svg.spark");
    expect(svg).not.toBeNull();
    expect(svg?.getAttribute("role")).toBe("img");
    // filled 既定: area path + line path の 2 本。
    expect(container.querySelectorAll("path").length).toBe(2);
  });

  it("filled=false なら area path を省く", () => {
    const { container } = render(<Sparkline data={[1, 2, 3]} filled={false} />);
    expect(container.querySelectorAll("path").length).toBe(1);
  });

  it("空データは何も描かない", () => {
    const { container } = render(<Sparkline data={[]} />);
    expect(container.querySelector("svg")).toBeNull();
  });
});

describe("DesignPageHeader", () => {
  it("title は h1、eyebrow/subtitle/actions は任意スロット", () => {
    render(
      <DesignPageHeader
        eyebrow="販売管理"
        title="販売案件"
        subtitle="3 件"
        actions={<button type="button">新規</button>}
      />,
    );
    expect(screen.getByRole("heading", { level: 1, name: "販売案件" })).toBeInTheDocument();
    expect(screen.getByText("販売管理")).toBeInTheDocument();
    expect(screen.getByText("3 件")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "新規" })).toBeInTheDocument();
  });
});

describe("StatusFlow", () => {
  const steps = [
    { value: "a", label: "受付", sub: "s1" },
    { value: "b", label: "査定", sub: "s2" },
    { value: "c", label: "完了", sub: "s3" },
  ];

  it("currentIndex により completed / current / pending を割り当てる", () => {
    const { container } = render(<StatusFlow steps={steps} currentIndex={1} />);
    const nodes = container.querySelectorAll(".flow-step");
    expect(nodes[0]).toHaveAttribute("data-state", "completed");
    expect(nodes[1]).toHaveAttribute("data-state", "current");
    expect(nodes[2]).toHaveAttribute("data-state", "pending");
  });

  it("完了済みノードはチェック、未完了は連番を表示", () => {
    const { container } = render(<StatusFlow steps={steps} currentIndex={1} />);
    const dots = container.querySelectorAll(".flow-dot");
    // index0 は completed → Check アイコン (svg)、index1/2 は数字。
    expect(dots[0]!.querySelector("svg")).not.toBeNull();
    expect(dots[1]).toHaveTextContent("2");
    expect(dots[2]).toHaveTextContent("3");
  });

  it("fractional currentIndex (1.5) は off-main 分岐を表す", () => {
    const { container } = render(<StatusFlow steps={steps} currentIndex={1.5} />);
    const nodes = container.querySelectorAll(".flow-step");
    // index1 < 1.5 なので completed、index2 はまだ pending。
    expect(nodes[0]).toHaveAttribute("data-state", "completed");
    expect(nodes[1]).toHaveAttribute("data-state", "completed");
    expect(nodes[2]).toHaveAttribute("data-state", "pending");
  });

  it("branch ノードを 5 つ目として追加し active で current にする", () => {
    const { container } = render(
      <StatusFlow
        steps={steps}
        currentIndex={1}
        branch={{ label: "品目変換", sub: "br", active: true }}
      />,
    );
    const nodes = container.querySelectorAll(".flow-step");
    expect(nodes.length).toBe(4);
    expect(nodes[3]).toHaveAttribute("data-state", "current");
    expect(nodes[3]).toHaveTextContent("品目変換");
  });

  it("branch が非 active なら pending、番号は steps.length+1", () => {
    const { container } = render(
      <StatusFlow
        steps={steps}
        currentIndex={1}
        branch={{ label: "品目変換", sub: "br", active: false }}
      />,
    );
    const nodes = container.querySelectorAll(".flow-step");
    expect(nodes[3]).toHaveAttribute("data-state", "pending");
    expect(nodes[3]!.querySelector(".flow-dot")).toHaveTextContent("4");
  });
});
