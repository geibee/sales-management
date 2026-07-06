/**
 * MSW ↔ 契約 (zod) ドリフト検査 (issue #9 Tier1-7)。
 *
 * MSW がモックした全レスポンスを `response:mocked` イベントで捕捉し、
 * `src/contracts/generated.ts` (openapi.yaml から生成) の zod スキーマで
 * parse する。モックが契約から乖離した瞬間に該当テストが赤になるので、
 * 「モックは通るが実 API では壊れる」フロントエンドを構造的に防ぐ。
 *
 * ポリシー:
 *   - 2xx JSON → endpoint の response スキーマで parse
 *   - 4xx/5xx JSON → spec が当該 status を宣言している場合のみ
 *     ProblemJsonSchema (RFC 9457) で parse
 *     (generated.ts の errors スキーマは problem+json 経由のため z.void() で
 *      使えない。実 API は宣言済みエラーを常に Problem Details で返す)
 *   - 契約に無い path / method は対象外 (テスト専用モックを許容)
 *   - JSON 以外 (text/csv 等) は対象外
 *
 * イベントリスナ内の throw ではテストを落とせないため、違反は配列に積み、
 * `tests/setup.ts` の afterEach から assertNoContractViolations() で検査する。
 */
import { ProblemJsonSchema, api } from "@/contracts";
import type { SetupServer } from "msw/node";
import { z } from "zod";
import { BASE } from "./server";

interface EndpointDef {
  method: string;
  path: string;
  response: z.ZodType;
  errors?: { status: number }[];
}

// Zodios インスタンスは `.api` に makeApi() の endpoint 定義をそのまま持つ
const endpoints = api.api as unknown as EndpointDef[];

/** "/lots/:id" 形式の template と実 pathname をセグメント単位で照合する。 */
function templateMatches(template: string, pathname: string): boolean {
  const t = template.split("/").filter(Boolean);
  const a = pathname.split("/").filter(Boolean);
  if (t.length !== a.length) return false;
  return t.every((seg, i) => seg.startsWith(":") || seg === a[i]);
}

/**
 * pathname に一致する endpoint を返す。生成コードの path は spec と同じく
 * BASE (/api) を含まないため、`${BASE}${path}` と素の path の両方を試す。
 * /lots/available vs /lots/:id のような競合はリテラル一致数が多い方を選ぶ。
 */
function findEndpoint(method: string, pathname: string): EndpointDef | undefined {
  const candidates = endpoints
    .filter((e) => e.method === method)
    .filter((e) => templateMatches(`${BASE}${e.path}`, pathname) || templateMatches(e.path, pathname));
  if (candidates.length <= 1) return candidates[0];
  const literalCount = (template: string) =>
    template.split("/").filter((seg) => seg && !seg.startsWith(":")).length;
  return candidates.sort((x, y) => literalCount(y.path) - literalCount(x.path))[0];
}

const violations: string[] = [];
const pendingChecks: Promise<void>[] = [];

async function validate(request: Request, response: Response): Promise<void> {
  const url = new URL(request.url);
  const endpoint = findEndpoint(request.method.toLowerCase(), url.pathname);
  if (!endpoint) return; // 契約外 path のモックは対象外

  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.includes("json")) return;

  let body: unknown;
  try {
    body = await response.json();
  } catch {
    violations.push(
      `${request.method} ${url.pathname} → ${response.status}: JSON として parse できないボディ`,
    );
    return;
  }

  let schema: z.ZodType;
  if (response.status >= 400) {
    // spec が当該 status を宣言している operation のみ検証する (backend 側の
    // OpenApiValidationHandler と同ポリシー)。宣言済みエラーは実 API では常に
    // RFC 9457 Problem Details なので ProblemJsonSchema で照合する
    // (/health の 503 など独自 JSON を返す spec 未宣言 status は対象外)
    if (!endpoint.errors?.some((e) => e.status === response.status)) return;
    schema = ProblemJsonSchema;
  } else {
    schema = endpoint.response;
  }
  if (schema instanceof z.ZodVoid) return; // 204 等、契約上ボディなしの operation

  const result = schema.safeParse(body);
  if (!result.success) {
    const issues = result.error.issues
      .map((i) => `    ${i.path.join(".") || "(root)"}: ${i.message}`)
      .join("\n");
    violations.push(
      `${request.method} ${url.pathname} → ${response.status}: モックが契約スキーマに適合しない\n${issues}\n  body: ${JSON.stringify(body)}`,
    );
  }
}

/** MSW server にドリフト検査リスナを装着する (tests/setup.ts から一度だけ呼ぶ)。 */
export function installContractGuard(server: SetupServer): void {
  server.events.on("response:mocked", ({ request, response }) => {
    // response はテスト本体が消費するので clone を検査する
    pendingChecks.push(validate(request.clone(), response.clone()));
  });
}

/** 累積した違反を検査してクリアする (afterEach から呼ぶ)。 */
export async function assertNoContractViolations(): Promise<void> {
  await Promise.all(pendingChecks.splice(0));
  if (violations.length > 0) {
    const message = violations.splice(0).join("\n");
    throw new Error(
      `[contract-guard] MSW モックが契約 (openapi.yaml 由来の zod スキーマ) から乖離しています:\n${message}`,
    );
  }
}
