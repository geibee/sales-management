module SalesManagement.Domain.ReservationCaseTypes

open System
open SalesManagement.Domain.Types
open SalesManagement.Domain.SalesCaseTypes

type ReservationPriceCommon =
    { AppraisalNumber: AppraisalNumber
      AppraisalDate: DateOnly
      ReservedLotInfo: string
      ReservedAmount: Amount }

type ProvisionalReservationPrice = { Common: ReservationPriceCommon }

type ConfirmedReservationPrice =
    { Common: ReservationPriceCommon
      DeterminedDate: DateOnly
      DeterminedAmount: Amount }

type ReservationPrice =
    | Provisional of ProvisionalReservationPrice
    | Confirmed of ConfirmedReservationPrice

type BeforeReservationCase = { Common: SalesCaseCommon }

type ReservedCase =
    { Common: SalesCaseCommon
      Appraisal: ReservationPrice }

type ReservationConfirmedCase =
    { Common: SalesCaseCommon
      Appraisal: ReservationPrice
      DeterminedDate: DateOnly }

type ReservationDeliveredCase =
    { Common: SalesCaseCommon
      Appraisal: ReservationPrice
      DeterminedDate: DateOnly
      DeliveryDate: DateOnly }

type ReservationSalesCase =
    | BeforeReservation of BeforeReservationCase
    | Reserved of ReservedCase
    | ReservationConfirmed of ReservationConfirmedCase
    | ReservationDelivered of ReservationDeliveredCase

type ConsignorInfo =
    { ConsignorName: string
      ConsignorCode: string
      DesignatedDate: DateOnly }

type ConsignmentResult =
    { ResultDate: DateOnly
      ResultAmount: Amount }

type BeforeConsignmentCase = { Common: SalesCaseCommon }

type ConsignmentDesignatedCase =
    { Common: SalesCaseCommon
      ConsignorInfo: ConsignorInfo }

type ConsignmentResultEnteredCase =
    { Common: SalesCaseCommon
      ConsignorInfo: ConsignorInfo
      Result: ConsignmentResult }

type ConsignmentSalesCase =
    | BeforeConsignment of BeforeConsignmentCase
    | ConsignmentDesignated of ConsignmentDesignatedCase
    | ConsignmentResultEntered of ConsignmentResultEnteredCase

type SalesCase =
    | Direct of DirectSalesCase
    | Reservation of ReservationSalesCase
    | Consignment of ConsignmentSalesCase
