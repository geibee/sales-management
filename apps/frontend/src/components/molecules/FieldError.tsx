/**
 * `react-hook-form` の `errors.foo?.message` を受けて、フィールド直下に
 * 赤字でエラーテキストを出す共通 component。
 *
 * 旧 `LotCreatePage.FieldError` / `SalesCaseCreatePage.FieldError` /
 * `SalesCaseCreateDialog.FieldError` / `RichActionForms.FieldError` の
 * 4 ファイル重複を統合したもの。挙動は全て同一だったため shape も
 * 完全に一致する。
 */
export function FieldError({ message }: { message?: string | undefined }) {
  if (!message) return null;
  return (
    <p role="alert" className="text-destructive text-xs">
      {message}
    </p>
  );
}
