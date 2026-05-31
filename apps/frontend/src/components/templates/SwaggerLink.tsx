export function SwaggerLink() {
  const href = `${import.meta.env.VITE_API_BASE_URL ?? "/api"}/swagger`;
  return (
    <a
      href={href}
      target="_blank"
      rel="noreferrer"
      className="text-muted-foreground text-xs hover:text-foreground"
    >
      API ドキュメント
    </a>
  );
}
