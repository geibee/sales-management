module SalesManagement.Api.ConsignmentCaseDtos

[<CLIMutable>]
type DesignateConsignmentDto =
    { consignorName: string
      consignorCode: string
      designatedDate: string
      version: System.Nullable<int> }

[<CLIMutable>]
type ConsignmentResultDto =
    { resultDate: string
      resultAmount: int
      version: System.Nullable<int> }

[<CLIMutable>]
type ConsignmentVersionOnlyDto = { version: System.Nullable<int> }

type ConsignmentStatusResponse = { status: string; version: int }
