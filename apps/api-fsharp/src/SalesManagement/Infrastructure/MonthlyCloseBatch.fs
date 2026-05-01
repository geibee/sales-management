module SalesManagement.Infrastructure.MonthlyCloseBatch

open System
open Npgsql
open SalesManagement.Domain.Types
open SalesManagement.Infrastructure.BatchProcessing

type ManufacturedRow =
    { Id: int64
      LotNumber: LotNumber
      ManufacturingCompletedDate: DateOnly }

type ShippingInstructedUpdate =
    { LotNumber: LotNumber
      ShippingDeadlineDate: DateOnly }

let private mapRow (rd: System.Data.IDataReader) : ManufacturedRow =
    { Id = rd.GetInt64(rd.GetOrdinal "id")
      LotNumber =
        { Year = rd.GetInt32(rd.GetOrdinal "lot_number_year")
          Location = rd.GetString(rd.GetOrdinal "lot_number_location")
          Seq = rd.GetInt32(rd.GetOrdinal "lot_number_seq") }
      ManufacturingCompletedDate = DateOnly.FromDateTime(rd.GetDateTime(rd.GetOrdinal "manufacturing_completed_date")) }

let readManufacturedLots (conn: NpgsqlConnection) (lastId: int64) (limit: int) : ManufacturedRow list =
    use cmd =
        new NpgsqlCommand(
            """
            SELECT id, lot_number_year, lot_number_location, lot_number_seq,
                   manufacturing_completed_date
              FROM lot
             WHERE status = 'manufactured'
               AND id > @last_id
             ORDER BY id
             LIMIT @lim
            """,
            conn
        )

    cmd.Parameters.AddWithValue("last_id", lastId) |> ignore
    cmd.Parameters.AddWithValue("lim", limit) |> ignore
    use rd = cmd.ExecuteReader()

    [ while rd.Read() do
          yield mapRow rd ]

let private toUpdate (deadline: DateOnly) (row: ManufacturedRow) : Result<ShippingInstructedUpdate, string> =
    if row.ManufacturingCompletedDate > deadline then
        Error "manufacturing date later than shipping deadline"
    else
        Ok
            { LotNumber = row.LotNumber
              ShippingDeadlineDate = deadline }

let private writeUpdates (tx: NpgsqlTransaction) (updates: ShippingInstructedUpdate list) : unit =
    for u in updates do
        use cmd =
            new NpgsqlCommand(
                """
                UPDATE lot
                   SET status = 'shipping_instructed',
                       shipping_deadline_date = @deadline::date
                 WHERE lot_number_year = @year
                   AND lot_number_location = @location
                   AND lot_number_seq = @seq
                   AND status = 'manufactured'
                """,
                tx.Connection,
                tx
            )

        cmd.Parameters.AddWithValue("deadline", u.ShippingDeadlineDate.ToString("yyyy-MM-dd"))
        |> ignore

        cmd.Parameters.AddWithValue("year", u.LotNumber.Year) |> ignore
        cmd.Parameters.AddWithValue("location", u.LotNumber.Location) |> ignore
        cmd.Parameters.AddWithValue("seq", u.LotNumber.Seq) |> ignore
        cmd.ExecuteNonQuery() |> ignore

let private monthlyCloseJobName = "monthly-close"

let private parseMonth (targetMonth: string) : DateOnly =
    let parts = targetMonth.Split('-')

    if parts.Length <> 2 then
        invalidArg "targetMonth" "targetMonth must be in YYYY-MM format"

    let year = Int32.Parse parts.[0]
    let month = Int32.Parse parts.[1]
    let lastDay = DateTime.DaysInMonth(year, month)
    DateOnly(year, month, lastDay)

let private runCore
    (connectionString: string)
    (chunkSize: int)
    (targetMonth: string)
    (jobParamsOpt: string option)
    : BatchOutcome =
    let deadline = parseMonth targetMonth

    processInChunks
        monthlyCloseJobName
        jobParamsOpt
        connectionString
        chunkSize
        readManufacturedLots
        (toUpdate deadline)
        writeUpdates
        (fun row -> row.Id)

/// 月次締めバッチ: 製造完了 (manufactured) 状態のロットを出荷指示済 (shipping_instructed) に一括遷移する。
/// `targetMonth` は YYYY-MM 形式。出荷期限はその月の月末日。
/// この経路ではリスタート (batch_chunk_progress) を有効化しない (batch_job_execution の親レコードが無いため)。
let runMonthlyClose (connectionString: string) (chunkSize: int) (targetMonth: string) : BatchOutcome =
    runCore connectionString chunkSize targetMonth None

type JobRunOutcome =
    | Completed of BatchOutcome
    | AlreadyRunning
    | AlreadyCompleted

/// 月次締めバッチ + 二重実行防止 + 完了/失敗ステータス記録 + チャンクリスタート。
/// runMonthlyClose をラップし、batch_job_execution と batch_chunk_progress でジョブ実行とチャンク進捗を管理する。
let runMonthlyCloseManaged (connectionString: string) (chunkSize: int) (targetMonth: string) : JobRunOutcome =
    let jobParams = targetMonth

    match JobExecutionRepository.tryStart connectionString monthlyCloseJobName jobParams with
    | JobExecutionRepository.AlreadyRunning -> AlreadyRunning
    | JobExecutionRepository.AlreadyCompleted -> AlreadyCompleted
    | JobExecutionRepository.Started ->
        try
            let outcome = runCore connectionString chunkSize targetMonth (Some jobParams)

            JobExecutionRepository.complete
                connectionString
                monthlyCloseJobName
                jobParams
                outcome.TotalRead
                outcome.TotalProcessed
                outcome.TotalSkipped

            Completed outcome
        with ex ->
            JobExecutionRepository.fail connectionString monthlyCloseJobName jobParams ex.Message
            reraise ()
