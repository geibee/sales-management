module SalesManagement.Api.CodeMasterRoutes

open Microsoft.AspNetCore.Http
open Giraffe
open Npgsql
open SalesManagement.Infrastructure
open SalesManagement.Api.CodeMasterDtos

let private toItem (c: CodeMasterRepository.CodeName) : CodeMasterItem = { code = c.Code; name = c.Name }

let getCodeMastersHandler (connectionString: string) : HttpHandler =
    fun next ctx -> task {
        use conn = new NpgsqlConnection(connectionString)
        conn.Open()
        let m = CodeMasterRepository.loadAll conn

        let response: CodeMastersResponse =
            { divisions = m.Divisions |> List.map toItem |> List.toArray
              departments =
                m.Departments
                |> List.map (fun d ->
                    { code = d.Code
                      name = d.Name
                      divisionCode = d.DivisionCode })
                |> List.toArray
              sections =
                m.Sections
                |> List.map (fun s ->
                    { code = s.Code
                      name = s.Name
                      departmentCode = s.DepartmentCode })
                |> List.toArray
              processCategories = m.ProcessCategories |> List.map toItem |> List.toArray
              inspectionCategories = m.InspectionCategories |> List.map toItem |> List.toArray
              manufacturingCategories = m.ManufacturingCategories |> List.map toItem |> List.toArray }

        return! json response next ctx
    }

let routes (connectionString: string) : HttpHandler =
    choose [ GET >=> route "/code-masters" >=> getCodeMastersHandler connectionString ]
