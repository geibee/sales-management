module SalesManagement.Domain.ConsignmentCaseWorkflows

open SalesManagement.Domain.Types
open SalesManagement.Domain.SalesCaseTypes
open SalesManagement.Domain.ReservationCaseTypes

type ConsignmentDesignationError = CaseNotBeforeConsignment
type ConsignmentCancelError = CaseNotConsignmentDesignated
type ConsignmentResultError = CaseNotConsignmentDesignated

let createBeforeConsignmentCase (common: SalesCaseCommon) : BeforeConsignmentCase = { Common = common }

let designateConsignment (info: ConsignorInfo) (case: BeforeConsignmentCase) : ConsignmentDesignatedCase =
    { Common = case.Common
      ConsignorInfo = info }

let cancelDesignation (case: ConsignmentDesignatedCase) : BeforeConsignmentCase = { Common = case.Common }

let enterConsignmentResult
    (result: ConsignmentResult)
    (case: ConsignmentDesignatedCase)
    : ConsignmentResultEnteredCase =
    { Common = case.Common
      ConsignorInfo = case.ConsignorInfo
      Result = result }
