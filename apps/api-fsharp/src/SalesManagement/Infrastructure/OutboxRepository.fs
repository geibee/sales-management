module SalesManagement.Infrastructure.OutboxRepository

open System
open NpgsqlTypes
open Npgsql

type PendingEvent =
    { Id: int64
      EventType: string
      Payload: string }

let insertEventTx (tx: NpgsqlTransaction) (eventType: string) (payload: string) : int64 =
    let conn = tx.Connection
    use cmd = conn.CreateCommand()
    cmd.Transaction <- tx

    cmd.CommandText <- "INSERT INTO outbox_events (event_type, payload) VALUES (@type, @payload::jsonb) RETURNING id"

    let p1 = cmd.CreateParameter()
    p1.ParameterName <- "type"
    p1.Value <- eventType
    cmd.Parameters.Add p1 |> ignore
    let p2 = cmd.CreateParameter()
    p2.ParameterName <- "payload"
    p2.NpgsqlDbType <- NpgsqlDbType.Jsonb
    p2.Value <- payload
    cmd.Parameters.Add p2 |> ignore

    let result = cmd.ExecuteScalar()
    Convert.ToInt64 result

let fetchPending (conn: NpgsqlConnection) (limit: int) : PendingEvent list =
    use cmd = conn.CreateCommand()

    cmd.CommandText <-
        "SELECT id, event_type, payload::text AS payload FROM outbox_events WHERE status = 'pending' ORDER BY id LIMIT @limit"

    let p = cmd.CreateParameter()
    p.ParameterName <- "limit"
    p.Value <- limit
    cmd.Parameters.Add p |> ignore

    use reader = cmd.ExecuteReader()
    let results = ResizeArray<PendingEvent>()

    while reader.Read() do
        results.Add
            { Id = reader.GetInt64(reader.GetOrdinal "id")
              EventType = reader.GetString(reader.GetOrdinal "event_type")
              Payload = reader.GetString(reader.GetOrdinal "payload") }

    List.ofSeq results

/// Atomically claim up to `limit` pending events for exclusive processing.
/// Uses SELECT ... FOR UPDATE SKIP LOCKED inside a transaction to make this
/// safe across multiple OutboxProcessor instances (horizontal scale): each
/// claimed row's status is flipped to 'processing' before commit, so other
/// workers cannot pick the same row.
let claimPending (conn: NpgsqlConnection) (limit: int) : PendingEvent list =
    use tx = conn.BeginTransaction()

    let pending = ResizeArray<PendingEvent>()

    do
        use selectCmd = conn.CreateCommand()
        selectCmd.Transaction <- tx

        selectCmd.CommandText <-
            "SELECT id, event_type, payload::text AS payload FROM outbox_events \
             WHERE status = 'pending' ORDER BY id LIMIT @limit FOR UPDATE SKIP LOCKED"

        let pLimit = selectCmd.CreateParameter()
        pLimit.ParameterName <- "limit"
        pLimit.Value <- limit
        selectCmd.Parameters.Add pLimit |> ignore

        use reader = selectCmd.ExecuteReader()

        while reader.Read() do
            pending.Add
                { Id = reader.GetInt64(reader.GetOrdinal "id")
                  EventType = reader.GetString(reader.GetOrdinal "event_type")
                  Payload = reader.GetString(reader.GetOrdinal "payload") }

    if pending.Count = 0 then
        tx.Commit()
        []
    else
        let ids = pending |> Seq.map (fun e -> e.Id) |> Array.ofSeq
        use updateCmd = conn.CreateCommand()
        updateCmd.Transaction <- tx
        updateCmd.CommandText <- "UPDATE outbox_events SET status = 'processing' WHERE id = ANY(@ids)"
        let pIds = updateCmd.CreateParameter()
        pIds.ParameterName <- "ids"
        pIds.Value <- ids
        updateCmd.Parameters.Add pIds |> ignore
        updateCmd.ExecuteNonQuery() |> ignore
        tx.Commit()
        List.ofSeq pending

let markProcessed (conn: NpgsqlConnection) (id: int64) : unit =
    use cmd = conn.CreateCommand()

    cmd.CommandText <- "UPDATE outbox_events SET status = 'processed', processed_at = NOW() WHERE id = @id"

    let p = cmd.CreateParameter()
    p.ParameterName <- "id"
    p.Value <- id
    cmd.Parameters.Add p |> ignore
    cmd.ExecuteNonQuery() |> ignore

let markFailed (conn: NpgsqlConnection) (id: int64) (detail: string) : unit =
    use cmd = conn.CreateCommand()

    cmd.CommandText <-
        "UPDATE outbox_events SET status = 'failed', processed_at = NOW(), error_detail = @detail WHERE id = @id"

    let p1 = cmd.CreateParameter()
    p1.ParameterName <- "id"
    p1.Value <- id
    cmd.Parameters.Add p1 |> ignore
    let p2 = cmd.CreateParameter()
    p2.ParameterName <- "detail"
    p2.Value <- detail
    cmd.Parameters.Add p2 |> ignore
    cmd.ExecuteNonQuery() |> ignore
