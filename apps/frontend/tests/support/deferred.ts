/**
 * 手動で settle 可能な promise — テストがリクエストを「pending」状態に
 * ピン留めしてローディング UI を観測したあと、任意のタイミングで
 * `resolve()` / `reject()` できるようにする。
 *
 * 典型的な使い方:
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
