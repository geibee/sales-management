import { z } from "zod";

/**
 * Date input helper (yyyy-MM-dd). API responses use `format: date` strings, but
 * user-facing inputs need a regex check that an OpenAPI contract cannot express.
 */
export const DateOnlySchema = z
  .string()
  .regex(/^\d{4}-\d{2}-\d{2}$/, "yyyy-MM-dd 形式で入力してください");
