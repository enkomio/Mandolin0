namespace Mandolin0

open System
open System.IO
open System.Net

type RequestBuilder(templateRepository: TemplateRepository) = 

    // Add headers to the web request according the header name in a safe way
    let addHttpHeader(headerName: String, headerValue: String, webRequest: HttpWebRequest) =
        match headerName with
        | "User-Agent" -> webRequest.UserAgent <- headerValue
        | "Accept" -> webRequest.Accept <- headerValue
        | "Referer" -> webRequest.Referer <- headerValue
        | "Host" -> webRequest.Host <- headerValue
        | "Content-Type" -> webRequest.ContentType <- headerValue
        | "Content-Length" -> 
            let contentLen = ref 0L
            if Int64.TryParse(headerValue, contentLen) then webRequest.ContentLength <- !contentLen
        | "Connection" 
        | "Proxy-Connection" -> 
            if headerValue.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) then   
                webRequest.KeepAlive <- true
            else 
                webRequest.Connection <- headerValue
                webRequest.KeepAlive <- false
        | "Date" ->     
            let date = ref DateTime.Now
            if DateTime.TryParse(headerValue, date) then webRequest.Date <- !date
        | "Cookie" ->
            let chunks = headerValue.Split('=')
            let cookieName = chunks.[0]
            let cookieValue = String.Join("=", chunks |> Seq.skip 1)
            let cookie = new Cookie(cookieName, cookieValue, Domain = webRequest.RequestUri.Host)
            if webRequest.CookieContainer = null then
                webRequest.CookieContainer <- new CookieContainer()
            webRequest.CookieContainer.Add(cookie)
        | "Expect" -> 
            // 100-Continue must be setted with the System.Net.ServicePointManager.Expect100Continue Setted to true
            // see http://haacked.com/archive/2004/05/15/http-web-request-expect-100-continue.aspx
            if not <| headerValue.Equals("", StringComparison.OrdinalIgnoreCase) then webRequest.Expect <- headerValue
        | "If-Modified-Since" -> 
            let date = ref DateTime.Now
            if DateTime.TryParse(headerValue, date) then webRequest.IfModifiedSince <- !date
        | "Transfer-Encoding" -> 
            webRequest.SendChunked <- true
            webRequest.TransferEncoding <- headerValue
        | _ -> webRequest.Headers.[headerName] <- headerValue

    // Read all headers of the template and add them to the web request
    let rec parseHeaders(httpWebRequest: HttpWebRequest, stringReader: StringReader) =
        let line = stringReader.ReadLine()
        if not <| String.IsNullOrWhiteSpace(line) then
            let indexOfColon = line.IndexOf(':')
            let headerName = line.Substring(0, indexOfColon).Trim()
            let headerValue = line.Substring(indexOfColon + 1).Trim()
            addHttpHeader(headerName, headerValue, httpWebRequest)
            parseHeaders(httpWebRequest, stringReader)

    /// Create the request according to the specified template
    member this.Build(username: String, password: String, templateName: String, oracleName: String, url: String) =
        let testRequest = new TestRequest(username, password, url, Template = templateName, Oracle = oracleName)

        let initializeRequest = fun () ->
            let template = templateRepository.Get(testRequest)

            // parse the request and build the object
            use stringReader = new StringReader(template.Content)
            let chunks = stringReader.ReadLine().Split(' ') 

            // set method, path, version and headers
            let (httpMethod, path, protocolVersion) = (chunks.[0], chunks.[1], chunks.[2])
            let completeUrl = new Uri(new Uri(url), path.Substring(1))
            let httpWebRequest = WebRequest.Create(completeUrl) :?> HttpWebRequest
            
            httpWebRequest.Method <- httpMethod
            httpWebRequest.AllowAutoRedirect <- false
            httpWebRequest.Timeout <- Configuration.timeout
            
            if Uri.IsWellFormedUriString(Configuration.proxy, UriKind.Absolute) then
                httpWebRequest.Proxy <- new WebProxy(Configuration.proxy)

            let version = protocolVersion.Split('/').[1]
            httpWebRequest.ProtocolVersion <- Version.Parse(version)
            parseHeaders(httpWebRequest, stringReader)

            // write post data if present
            let data = stringReader.ReadToEnd()
            if not <| String.IsNullOrWhiteSpace(data) then
                use streamWriter = new StreamWriter(httpWebRequest.GetRequestStream())
                streamWriter.Write(data)

            testRequest.Request <- Some httpWebRequest
        
        testRequest.InitializeBeforeSend <- initializeRequest
        testRequest