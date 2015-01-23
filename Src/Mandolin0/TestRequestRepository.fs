namespace Mandolin0

open System
open System.Collections.Generic
open System.IO

/// Read all the username and password and create the list of request to send in the bruteforce process
type TestRequestRepository(usernameFile: String, passwordFile: String, templateName: String, oracleName: String, requestBuilder: RequestBuilder, sessionManager: SessionManager) = 

    let createTestRequest templateName oracleName url username  password =
            requestBuilder.Build(username, password, templateName, oracleName, url)

    member this.GetAll(url: String) =
        seq {
            if File.Exists(usernameFile) |> not then raise <| new ApplicationException(String.Format("Usernames file '{0}' doesn't exists", usernameFile))
            if File.Exists(passwordFile) |> not then raise <| new ApplicationException(String.Format("Passowords file '{0}' doesn't exists", passwordFile))
        
            let usernames = new HashSet<String>()
            let passwords = new HashSet<String>()

            let readAll(set: HashSet<String>, file: String) =
                for rawItem in File.ReadAllLines(file) do
                    let item = rawItem.Trim()
                    set.Add(item) |> ignore

            readAll(usernames, usernameFile)
            readAll(passwords, passwordFile)

            let considerPasswordIndex = sessionManager.ConsiderUsernameAndPasswordIndex url oracleName templateName

            let testRequestFactory = createTestRequest templateName oracleName url
            for username in usernames do

                // read all requests in advance in order to speed up the bruteforce later
                let requests = new List<TestRequest>()
                let passwordIndex = ref 0
                for password in passwords do
                    if not(String.IsNullOrEmpty(password)) && considerPasswordIndex(username, !passwordIndex) then
                        let testRequest = testRequestFactory username password
                        requests.Add(testRequest)
                    
                    incr passwordIndex

                yield (requests |> Seq.toList)
        }