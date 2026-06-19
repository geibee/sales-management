import { schemas, type CodeMastersResponse } from "@/contracts";
import { apiGet } from "@/lib/api-client";
import useSWR from "swr";

/** 事業部/部/課（階層）・工程/検査/製造（フラット）のコード値マスタ。 */
export function useCodeMasters() {
  return useSWR<CodeMastersResponse>("/code-masters", (k: string) =>
    apiGet(k, schemas.CodeMastersResponse),
  );
}
