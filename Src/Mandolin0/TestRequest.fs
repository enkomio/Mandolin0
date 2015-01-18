namespace Mandolin0

open System
open System.Threading
open System.Text
open System.Net

/// This class represent a test case of a give username and password
type TestRequest(username: String, password: String, url: String) = 

    let readResponseAsText(httpWebResponse: HttpWebResponse) =
        async {
            let sb = new StringBuilder()
            sb.AppendFormat("HTTP/{0} {1} {2}", httpWebResponse.ProtocolVersion.ToString(), int httpWebResponse.StatusCode, httpWebResponse.StatusDescription).AppendLine() |> ignore

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

    member val Url = url with get
    member val Username = username with get
    member val Password = password with get
    member val Template = "Default" with get, set
    member val Oracle = "Default" with get, set
    member val Request : HttpWebRequest option = None with get, set 
    member val InitializeBeforeSend : (unit -> unit) = fun () -> () with get, set

    member this.Send() =
        async {
            let responseText = ref String.Empty
            let completed = ref false
            let numOfRetry = ref 0

            while(not(!completed)) do
                try
                    this.InitializeBeforeSend()
                    let! httpResponse = this.Request.Value.AsyncGetResponse()
                    let! tmpText = readResponseAsText(httpResponse :?> HttpWebResponse)
                    responseText := tmpText
                    completed := true
                with
                    | :? WebException as webException -> 
                        incr numOfRetry
                        if webException.Response <> null then
                            let! tmpText =  readResponseAsText(webException.Response :?> HttpWebResponse)
                            responseText := tmpText
                            completed := true
                        elif !numOfRetry > 10 then
                            Thread.Sleep(1000)
                            numOfRetry := 0
                        else
                            Thread.Sleep(200)

            return !responseText
        }
