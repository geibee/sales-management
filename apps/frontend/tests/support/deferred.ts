/**
 * Manually-controlled promise — lets a test pin a request in the
 * "pending" state while it inspects loading UI, then `resolve()` or
 * `reject()` it on demand.
 *
 * Typical use:
 *   const d = deferred<Response>();
 *   server.use(http.get("/api/lots", () => d.promise));
 *   render(<LotListPage />);
 *   expect(screen.getByRole("status")).toBeInTheDocument(); // loading
 *   d.resolve(HttpResponse.json({ items: [], total: 0 }));
 *   await screen.findByText("0 件");
 */
export interface Deferred<T> {
  promise: Promise<T>;
  resolve: (value: T | PromiseLike<T>) => void;
  reject: (reason?: unknown) => void;
}

export function deferred<T>(): Deferred<T> {
  let resolve!: (value: T | PromiseLike<T>) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}
