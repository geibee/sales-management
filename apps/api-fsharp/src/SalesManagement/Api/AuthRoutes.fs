module SalesManagement.Api.AuthRoutes

open Microsoft.AspNetCore.Http
open Giraffe

let getAuthConfigHandler (opts: Auth.AuthOptions) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) -> task {
        let body: obj =
            if opts.Enabled then
                {| enabled = true
                   authority = Option.toObj opts.Authority
                   audience = opts.Audience |}
                :> obj
            else
                {| enabled = false |} :> obj

        return! json body next ctx
    }

let routes (opts: Auth.AuthOptions) : HttpHandler =
    GET >=> route "/auth/config" >=> getAuthConfigHandler opts
