namespace Mandolin0.Templates.Joomla

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Security.Cryptography.X509Certificates
open System.Net.Security
open System.Text.RegularExpressions
open System.Reflection
open System.Threading

type Builder() = 

    static let retrieveTemplateContent =
        let assembly =  Assembly.GetAssembly(typeof<Builder>)
        let codeBase = assembly.CodeBase
        let uri = new UriBuilder(codeBase)
        let path = Uri.UnescapeDataString(uri.Path)
        let directory = Path.GetDirectoryName(path)
        let filename = Path.Combine(directory, "Template.txt")

        if not <| File.Exists(filename) then raise <| new ApplicationException("Unable to find the Joomla template file: " + filename)
        let baseTemplateContent = File.ReadAllText(filename)
        fun () ->
            baseTemplateContent
   
    static let getNeededData =
        // ignore invalid certificates
        ServicePointManager.DefaultConnectionLimit <- Int32.MaxValue
        ServicePointManager.Expect100Continue <- true
        ServicePointManager.MaxServicePoints <- Int32.MaxValue
        
        // don't care about invalid HTTPS certificate
        ServicePointManager.CheckCertificateRevocationList <- false
        let doCertificateValidation (sender: Object) (certificate: X509Certificate) (chain: X509Chain) (policy:SslPolicyErrors) = true
        ServicePointManager.ServerCertificateValidationCallback <- new RemoteCertificateValidationCallback(doCertificateValidation)

        let isInitialized = ref false
        let syncRoot = new Object()
        
        let cookies = ref(String.Empty)
        let data = ref(String.Empty)

        fun (url: String) -> 
            if not <| !isInitialized then
                lock syncRoot (fun () ->
                    if not <| !isInitialized then
                        let sessionCookies = new List<String>()
                        let hiddenData = new List<String>()
                
                        let httpWebRequest = WebRequest.Create(url) :?> HttpWebRequest
                        httpWebRequest.UserAgent <- "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_9_5) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/39.0.2171.95 Safari/537.36"
                        httpWebRequest.CookieContainer <- new CookieContainer()
                        use httpWebResponse = httpWebRequest.GetResponse() :?> HttpWebResponse
                        use streamReader = new StreamReader(httpWebResponse.GetResponseStream())
                        let html = streamReader.ReadToEnd()

                        // extract hidden datas
                        let regex = new Regex("<input type=\"hidden\" name=\"([0-9a-zA-Z/\\-=\\+_]+)\" value=\"([0-9a-zA-Z/\\-=\\+_]+)\" />")
                        let rxMatch = ref <| regex.Match(html)
                        while (!rxMatch).Success do
                            let fieldName = (!rxMatch).Groups.[1].Value
                            let fieldValue = (!rxMatch).Groups.[2].Value
                            hiddenData.Add(fieldName.Trim() + "=" + fieldValue.Trim())
                            rxMatch := (!rxMatch).NextMatch()

                        // retrieve the cookie
                        for cookie: Cookie in httpWebResponse.Cookies do
                            sessionCookies.Add(cookie.Name + "=" + cookie.Value)

                        cookies := String.Join("; ", sessionCookies)
                        data := String.Join("&", hiddenData)

                        isInitialized := true
                )

            (!data, !cookies)
    
    static member GetTemplate(url: String) = 
        let (hiddenDataString, sessionCookieString) = getNeededData(url)
        let templateContent = 
            retrieveTemplateContent()
                .Replace("{ADDITIONAL_DATA}", hiddenDataString)
                .Replace("{SESSION_COOKIE}", sessionCookieString)
            
        templateContent
