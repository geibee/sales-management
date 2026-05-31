/**
 * `templates/PageHeader` のスモーク。タイトル / 説明 / backTo / actions
 * slot の 4 props が DOM に正しく落ちることを確認する。
 *
 * PageHeader 内部の `<Link>` (TanStack Router) はルータ context を要求
 * するので `renderWithRouter` を使う。catch-all ルートに描画させたい
 * ので initialPath="/probe" で発火させる。
 */
import { PageHeader } from "@/components/templates/PageHeader";
import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderWithRouter } from "../../support/render";

const PROBE = { initialPath: "/probe" };

describe("<PageHeader> (template)", () => {
  it("タイトルと description が描画される", async () => {
    renderWithRouter(<PageHeader title="在庫ロット" description="説明文" />, PROBE);
    expect(
      await screen.findByRole("heading", { level: 1, name: /在庫ロット/ }),
    ).toBeInTheDocument();
    expect(screen.getByText("説明文")).toBeInTheDocument();
  });

  it("backTo を指定すると 一覧 (デフォルトラベル) リンクが出る", async () => {
    renderWithRouter(<PageHeader title="ロット" backTo="/lots" />, PROBE);
    expect(await screen.findByRole("link", { name: /一覧/ })).toHaveAttribute("href", "/lots");
  });

  it("backLabel で 戻るリンクの文言を上書きできる", async () => {
    renderWithRouter(
      <PageHeader title="ロット" backTo="/lots" backLabel="ロット一覧へ戻る" />,
      PROBE,
    );
    expect(await screen.findByRole("link", { name: /ロット一覧へ戻る/ })).toBeInTheDocument();
  });

  it("backTo 未指定なら link は出ない", async () => {
    renderWithRouter(<PageHeader title="ロット" />, PROBE);
    // 描画完了待ち。link は無いはずなので heading でガード。
    await screen.findByRole("heading", { level: 1, name: /ロット/ });
    expect(screen.queryByRole("link")).toBeNull();
  });

  it("actions slot に渡した要素が出る", async () => {
    renderWithRouter(
      <PageHeader title="ロット" actions={<span data-testid="probe">extra</span>} />,
      PROBE,
    );
    expect(await screen.findByTestId("probe")).toHaveTextContent("extra");
  });
});
