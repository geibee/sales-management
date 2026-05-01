module SalesManagement.Api.ReservationCaseDtos

[<CLIMutable>]
type CreateReservationPriceDto =
    { appraisalDate: string
      reservedLotInfo: string
      reservedAmount: int
      version: System.Nullable<int> }

[<CLIMutable>]
type ConfirmReservationDto =
    { determinedDate: string
      determinedAmount: int
      version: System.Nullable<int> }

[<CLIMutable>]
type DeliverReservationDto =
    { deliveryDate: string
      version: System.Nullable<int> }

[<CLIMutable>]
type ReservationVersionOnlyDto = { version: System.Nullable<int> }

type ReservationStatusResponse = { status: string; version: int }
