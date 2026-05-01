module SalesManagement.Domain.ReservationCaseWorkflows

open System
open SalesManagement.Domain.Types
open SalesManagement.Domain.SalesCaseTypes
open SalesManagement.Domain.ReservationCaseTypes

type ReservationPriceCreationError = CaseNotBeforeReservation
type ReservationConfirmationError = CaseNotReserved
type ReservationConfirmationCancelError = CaseNotReservationConfirmed
type ReservationDeliveryError = CaseNotReservationConfirmed

let createBeforeReservationCase (common: SalesCaseCommon) : BeforeReservationCase = { Common = common }

let createReservationPrice (appraisal: ReservationPrice) (case: BeforeReservationCase) : ReservedCase =
    { Common = case.Common
      Appraisal = appraisal }

let confirmReservation (date: DateOnly) (amount: Amount) (case: ReservedCase) : ReservationConfirmedCase =
    let determined =
        match case.Appraisal with
        | Provisional u ->
            Confirmed
                { Common = u.Common
                  DeterminedDate = date
                  DeterminedAmount = amount }
        | Confirmed _ as d -> d

    { Common = case.Common
      Appraisal = determined
      DeterminedDate = date }

let cancelReservationConfirmation (case: ReservationConfirmedCase) : ReservedCase =
    let appraisal =
        match case.Appraisal with
        | Confirmed d -> Provisional { Common = d.Common }
        | Provisional _ as u -> u

    { Common = case.Common
      Appraisal = appraisal }

let deliverReservation (date: DateOnly) (case: ReservationConfirmedCase) : ReservationDeliveredCase =
    { Common = case.Common
      Appraisal = case.Appraisal
      DeterminedDate = case.DeterminedDate
      DeliveryDate = date }
