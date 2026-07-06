module SalesManagement.Domain.SalesCaseWorkflows

open System
open SalesManagement.Domain.Types
open SalesManagement.Domain.SalesCaseTypes

type SalesCaseCreationError = NoManufacturedLots

/// A lot can back a sales case only when it is in the Manufactured state.
type LotForSaleError = LotNotManufactured of lotId: string

let requireManufacturedLot (lot: InventoryLot) : Result<ManufacturedLot, LotForSaleError> =
    match lot with
    | Manufactured m -> Ok m
    | other -> Error(LotNotManufactured(LotNumber.toString (InventoryLot.common other).LotNumber))

type AppraisalCreationError = CaseNotBeforeAppraisal
type AppraisalUpdateError = CaseNotAppraised
type AppraisalDeletionError = CaseNotAppraised
type ContractError = CaseNotAppraised
type ContractDeletionError = CaseNotContracted
type ShippingInstructionError = CaseNotContracted
type ShippingCompletionError = CaseNotShippingInstructed
type ShippingInstructionCancelError = CaseNotShippingInstructed
type SalesCaseDeletionError = CaseNotBeforeAppraisal

let createSalesCaseCommon
    (lots: ManufacturedLot list)
    (caseNumber: SalesCaseNumber)
    (divisionCode: DivisionCode)
    (salesDate: DateOnly)
    : Result<SalesCaseCommon, SalesCaseCreationError> =
    match lots with
    | [] -> Error NoManufacturedLots
    | head :: tail ->
        let inventoryHead = Manufactured head
        let inventoryTail = tail |> List.map Manufactured

        Ok
            { SalesCaseNumber = caseNumber
              DivisionCode = divisionCode
              SalesDate = salesDate
              Lots =
                { Head = inventoryHead
                  Tail = inventoryTail } }

let createSalesCase
    (lots: ManufacturedLot list)
    (caseNumber: SalesCaseNumber)
    (divisionCode: DivisionCode)
    (salesDate: DateOnly)
    : Result<BeforeAppraisalCase, SalesCaseCreationError> =
    createSalesCaseCommon lots caseNumber divisionCode salesDate
    |> Result.map (fun common -> { Common = common })

let createAppraisal (appraisal: PriceAppraisal) (case: BeforeAppraisalCase) : AppraisedCase =
    { Common = case.Common
      Appraisal = appraisal }

let updateAppraisalOf (appraisal: PriceAppraisal) (case: AppraisedCase) : AppraisedCase =
    { Common = case.Common
      Appraisal = appraisal }

let deleteAppraisal (case: AppraisedCase) : BeforeAppraisalCase = { Common = case.Common }

let concludeContract (contract: SalesContract) (case: AppraisedCase) : ContractedCase =
    { Common = case.Common
      Appraisal = case.Appraisal
      Contract = contract }

let deleteContract (case: ContractedCase) : AppraisedCase =
    { Common = case.Common
      Appraisal = case.Appraisal }

let instructShipping (info: ShippingInstructionInfo) (case: ContractedCase) : ShippingInstructedCase =
    { Common = case.Common
      Appraisal = case.Appraisal
      Contract = case.Contract
      ShippingInstruction = info }

let cancelShippingInstruction (case: ShippingInstructedCase) : ContractedCase =
    { Common = case.Common
      Appraisal = case.Appraisal
      Contract = case.Contract }

let completeShipping (date: DateOnly) (case: ShippingInstructedCase) : ShippingCompletedCase =
    { Common = case.Common
      Appraisal = case.Appraisal
      Contract = case.Contract
      ShippingInstruction = case.ShippingInstruction
      ShippingCompletedDate = date }
