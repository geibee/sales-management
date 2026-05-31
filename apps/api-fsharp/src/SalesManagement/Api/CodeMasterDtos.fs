module SalesManagement.Api.CodeMasterDtos

type CodeMasterItem = { code: int; name: string }

type DepartmentItem =
    { code: int
      name: string
      divisionCode: int }

type SectionItem =
    { code: int
      name: string
      departmentCode: int }

/// GET /code-masters のレスポンス。departments/sections は親コードを持ち、
/// フロントの事業部→部→課カスケード絞り込みに使う。
type CodeMastersResponse =
    { divisions: CodeMasterItem[]
      departments: DepartmentItem[]
      sections: SectionItem[]
      processCategories: CodeMasterItem[]
      inspectionCategories: CodeMasterItem[]
      manufacturingCategories: CodeMasterItem[] }
