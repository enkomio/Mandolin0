namespace Mandolin0

open System
open System.Collections.Generic
open System.IO

/// Read all the username and password and create the list of request to send in the bruteforce process
type TestRequestRepository(usernameFile: String, passwordFile: String, templateName: String, oracleName: String, requestBuilder: RequestBuilder) = 

    let createTestRequest templateName oracleName url username  password =
            requestBuilder.Build(username, password, templateName, oracleName, url)

    member this.GetAll(url: String) =
        seq {
            if File.Exists(usernameFile) |> not then raise <| new ApplicationException(String.Format("Usernames file '{0}' doesn't exists", usernameFile))
            if File.Exists(passwordFile) |> not then raise <| new ApplicationException(String.Format("Passowords file '{0}' doesn't exists", passwordFile))

            let testRequestFactory = createTestRequest templateName oracleName url
            for rawUsername in File.ReadAllLines(usernameFile) do

                // read all requests in advance in order to speed up the bruteforce later
                let requests = new List<TestRequest>()
                for rawPassword in File.ReadAllLines(passwordFile) do
                    let (username, password) = (rawUsername.Trim(), rawPassword.Trim())
                    let testRequest = testRequestFactory username password
                    requests.Add(testRequest)

                yield (requests |> Seq.toList)
        }