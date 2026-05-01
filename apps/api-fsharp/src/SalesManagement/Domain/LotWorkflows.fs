module SalesManagement.Domain.LotWorkflows

open SalesManagement.Domain.Types

type ManufacturingCompletionError = LotNotInManufacturing
type ShippingInstructionError = LotNotManufactured
type ShippingCompletionError = LotNotShippingInstructed
type CancellationError = LotNotManufactured
type ItemConversionError = LotNotManufacturedForConversion
type ItemConversionCancelError = LotNotInConversionInstructed

let completeManufacturing (date: System.DateOnly) (lot: ManufacturingLot) : ManufacturedLot =
    { Common = lot.Common
      ManufacturingCompletedDate = date }

let instructShipping (deadline: System.DateOnly) (lot: ManufacturedLot) : ShippingInstructedLot =
    { Common = lot.Common
      ManufacturingCompletedDate = lot.ManufacturingCompletedDate
      ShippingDeadlineDate = deadline }

let completeShipping (date: System.DateOnly) (lot: ShippingInstructedLot) : ShippedLot =
    { Common = lot.Common
      ManufacturingCompletedDate = lot.ManufacturingCompletedDate
      ShippingDeadlineDate = lot.ShippingDeadlineDate
      ShippedDate = date }

let cancelManufacturingCompletion (lot: ManufacturedLot) : ManufacturingLot = { Common = lot.Common }

let instructItemConversion (info: ConversionDestinationInfo) (lot: ManufacturedLot) : ConversionInstructedLot =
    { Common = lot.Common
      ManufacturingCompletedDate = lot.ManufacturingCompletedDate
      DestinationInfo = info }

let cancelItemConversionInstruction (lot: ConversionInstructedLot) : ManufacturedLot =
    { Common = lot.Common
      ManufacturingCompletedDate = lot.ManufacturingCompletedDate }
