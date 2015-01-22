namespace Mandolin0

open System
open System.Collections.Concurrent
open System.Threading
open System.Collections.Generic
open System.Security.Cryptography.X509Certificates
open System.IO
open System.Net
open System.Net.Security
open System.Threading.Tasks
open Microsoft.FSharp.Control

/// Bruteforce class, run all the test and notify if some password succeed
type Bruteforcer(testRequestRepository: TestRequestRepository, oracleRepository: OracleRepository, sessionManager: SessionManager) =

    let _startTestAccount = new Event<String * Int32>()
    let _processStatistics = new Event<Int32 * Int32>()
    let _startTest = new Event<TestRequest>()
    let _endTest = new Event<TestRequest>()
    let _passwordFound = new Event<TestRequest>()
    
    do
        ServicePointManager.DefaultConnectionLimit <- Int32.MaxValue
        ServicePointManager.Expect100Continue <- true
        ServicePointManager.MaxServicePoints <- Int32.MaxValue
        
        // don't care about invalid HTTPS certificate
        ServicePointManager.CheckCertificateRevocationList <- false
        let doCertificateValidation (sender: Object) (certificate: X509Certificate) (chain: X509Chain) (policy:SslPolicyErrors) = true
        ServicePointManager.ServerCertificateValidationCallback <- new RemoteCertificateValidationCallback(doCertificateValidation)

    member this.StartTestAccount = _startTestAccount.Publish
    member this.StartTest = _startTest.Publish
    member this.EndTest = _endTest.Publish
    member this.PasswordFound = _passwordFound.Publish
    member this.ProcessStatistics = _processStatistics.Publish

    member this.Run(url: String) =
        
        for usernameTestRequests in testRequestRepository.GetAll(url) do
            if not <| usernameTestRequests.IsEmpty then
                let testReq = usernameTestRequests.Head
                _startTestAccount.Trigger(testReq.Username, usernameTestRequests.Length)

                let oracle = oracleRepository.Get(testReq.Oracle)
                sessionManager.SetCurrentUsername(testReq.Username)
                let accountBruteforce = new AccountBruteforce(testReq.Username, oracle, usernameTestRequests, sessionManager)

                // connect event handlers
                accountBruteforce.StartTest.Add(_startTest.Trigger)
                accountBruteforce.EndTest.Add(_endTest.Trigger)
                accountBruteforce.PasswordFound.Add(_passwordFound.Trigger)
                accountBruteforce.ProcessStatistics.Add(_processStatistics.Trigger)

                // run the bruteforce
                accountBruteforce.Bruteforce()