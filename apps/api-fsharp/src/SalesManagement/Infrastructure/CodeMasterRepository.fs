module SalesManagement.Infrastructure.CodeMasterRepository

open System.Data
open Donald
open Npgsql

type CodeName = { Code: int; Name: string }

type DepartmentMaster =
    { Code: int
      Name: string
      DivisionCode: int }

type SectionMaster =
    { Code: int
      Name: string
      DepartmentCode: int }

type CodeMasters =
    { Divisions: CodeName list
      Departments: DepartmentMaster list
      Sections: SectionMaster list
      ProcessCategories: CodeName list
      InspectionCategories: CodeName list
      ManufacturingCategories: CodeName list }

/// 各コード→名称の参照表。ロット詳細レスポンスで名称を埋めるのに使う。
type NameMaps =
    { Division: Map<int, string>
      Department: Map<int, string>
      Section: Map<int, string>
      Process: Map<int, string>
      Inspection: Map<int, string>
      Manufacturing: Map<int, string> }

let private mapCodeName (rd: IDataReader) : CodeName =
    { Code = rd.GetInt32(rd.GetOrdinal "code")
      Name = rd.GetString(rd.GetOrdinal "name") }

let private mapDepartment (rd: IDataReader) : DepartmentMaster =
    { Code = rd.GetInt32(rd.GetOrdinal "code")
      Name = rd.GetString(rd.GetOrdinal "name")
      DivisionCode = rd.GetInt32(rd.GetOrdinal "division_code") }

let private mapSection (rd: IDataReader) : SectionMaster =
    { Code = rd.GetInt32(rd.GetOrdinal "code")
      Name = rd.GetString(rd.GetOrdinal "name")
      DepartmentCode = rd.GetInt32(rd.GetOrdinal "department_code") }

let private queryAll (conn: NpgsqlConnection) (sql: string) (mapper: IDataReader -> 'a) : 'a list =
    conn
    |> Db.newCommand sql
    |> Db.setParams []
    |> Db.setCommandBehavior CommandBehavior.Default
    |> Db.query mapper

let loadAll (conn: NpgsqlConnection) : CodeMasters =
    { Divisions = queryAll conn "SELECT code, name FROM master_division ORDER BY code" mapCodeName
      Departments =
        queryAll conn "SELECT code, name, division_code FROM master_department ORDER BY code" mapDepartment
      Sections = queryAll conn "SELECT code, name, department_code FROM master_section ORDER BY code" mapSection
      ProcessCategories = queryAll conn "SELECT code, name FROM master_process_category ORDER BY code" mapCodeName
      InspectionCategories =
        queryAll conn "SELECT code, name FROM master_inspection_category ORDER BY code" mapCodeName
      ManufacturingCategories =
        queryAll conn "SELECT code, name FROM master_manufacturing_category ORDER BY code" mapCodeName }

let loadNameMaps (conn: NpgsqlConnection) : NameMaps =
    let masters = loadAll conn
    let flat (xs: CodeName list) = xs |> List.map (fun c -> c.Code, c.Name) |> Map.ofList

    { Division = masters.Divisions |> flat
      Department = masters.Departments |> List.map (fun d -> d.Code, d.Name) |> Map.ofList
      Section = masters.Sections |> List.map (fun s -> s.Code, s.Name) |> Map.ofList
      Process = masters.ProcessCategories |> flat
      Inspection = masters.InspectionCategories |> flat
      Manufacturing = masters.ManufacturingCategories |> flat }

let resolve (m: Map<int, string>) (code: int) : string option = Map.tryFind code m
