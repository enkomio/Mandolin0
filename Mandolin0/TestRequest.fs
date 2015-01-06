namespace Mandolin0

open System
open System.Text
open System.Net

/// This class represent a test case of a give username and password
type TestRequest(username: String, password: String) = 

    let readResponseAsText(httpWebResponse: HttpWebResponse) =
        async {
            let sb = new StringBuilder()
            sb.AppendFormat("{0} {1} {1}", httpWebResponse.StatusCode.ToString(), httpWebResponse.StatusDescription).AppendLine() |> ignore

            // append all headers
            for headerName in httpWebResponse.Headers.AllKeys do
                let headerValue = httpWebResponse.Headers.[headerName]
                sb.AppendFormat("{0}: {1}", headerName, headerValue).AppendLine() |> ignore

            // append the body
            use asyncReader = new AsyncStreamReader(httpWebResponse.GetResponseStream())
            let! html = asyncReader.ReadToEnd()
            sb.Append(html) |> ignore
            
            return sb.ToString()
        }

    member val Username = username with get
    member val Password = password with get
    member val Template = "Default" with get, set
    member val Oracle = "Default" with get, set
    member val Request : HttpWebRequest option = None with get, set 
    member val Initialize : (unit -> unit) = fun () -> () with get, set

    member this.Send() =
        async {
            this.Initialize()
            let responseText = ref String.Empty
            try
                let! httpResponse = this.Request.Value.AsyncGetResponse()
                let! tmpText = readResponseAsText(httpResponse :?> HttpWebResponse)
                responseText := tmpText
            with
                | :? WebException as webException -> 
                    if webException.Response <> null then
                        let! tmpText =  readResponseAsText(webException.Response :?> HttpWebResponse)
                        responseText := tmpText
                | _ -> ()

            return !responseText
        }
