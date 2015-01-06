namespace Mandolin0

open System
open System.Threading
open System.Collections.Generic
open System.Security.Cryptography.X509Certificates
open System.IO
open System.Net
open System.Net.Security
open System.Threading.Tasks
open Microsoft.FSharp.Control

/// Bruteforce class, run all the test and notify if some password succeed
type Bruteforcer(testRequestRepository: TestRequestRepository, oracleRepository: OracleRepository) =

    let _stopCurrentUser = ref 0
    let _startTestAccount = new Event<String * Int32>()
    let _startTest = new Event<TestRequest>()
    let _endTest = new Event<TestRequest>()
    let _passwordFound = new Event<TestRequest>()

    do
        ServicePointManager.DefaultConnectionLimit <- Int32.MaxValue
        
        // don't care about invalid HTTPS certificate
        ServicePointManager.CheckCertificateRevocationList <- false
        let doCertificateValidation (sender: Object) (certificate: X509Certificate) (chain: X509Chain) (policy:SslPolicyErrors) = true
        ServicePointManager.ServerCertificateValidationCallback <- new RemoteCertificateValidationCallback(doCertificateValidation)

    let testAccount(testRequest: TestRequest) =
        async {
            let tmpVal = Interlocked.CompareExchange(_stopCurrentUser, 1, 1)
            if tmpVal = 0 then
                _startTest.Trigger(testRequest)
                let! responseText = testRequest.Send()
                let oracle = oracleRepository.Get(testRequest)
                if oracle.Verify(responseText) then
                    _passwordFound.Trigger(testRequest)
                    Interlocked.Exchange(_stopCurrentUser, 1) |> ignore
                _endTest.Trigger(testRequest)
        }

    member this.StartTestAccount = _startTestAccount.Publish
    member this.StartTest = _startTest.Publish
    member this.EndTest = _endTest.Publish
    member this.PasswordFound = _passwordFound.Publish

    member this.Run(url: String) =
        testRequestRepository.GetAll(url)
        |> Seq.iter ( fun testList ->
            _stopCurrentUser := 0
            let username = testList.Head.Username
            _startTestAccount.Trigger(username, testList.Length)

            testList
            |> List.map testAccount
            |> Async.Parallel
            |> Async.Ignore
            |> Async.RunSynchronously
        )