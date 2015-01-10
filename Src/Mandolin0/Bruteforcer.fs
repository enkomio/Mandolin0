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
type Bruteforcer(testRequestRepository: TestRequestRepository, oracleRepository: OracleRepository) =

    let _callBackAdjustWorkerTimeout = 1000
    let _userPasswordFound = ref 0L
    let _startTestAccount = new Event<String * Int32>()
    let _requestPerMinute = new Event<Int32>()
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

    let testAccount(testRequest: TestRequest) =
        async {
            let tmpVal = Interlocked.CompareExchange(_userPasswordFound, 1L, 1L)
            if tmpVal = 0L then
                let! responseText = testRequest.Send()
                let oracle = oracleRepository.Get(testRequest)
                if oracle.Verify(responseText) then
                    _passwordFound.Trigger(testRequest)
                    Interlocked.Exchange(_userPasswordFound, 1L) |> ignore
        } 

    // Implement the strategy based on the TCP cpngestion avoidance heuristic
    let bruteforceStrategy(testList: TestRequest list) =
        _userPasswordFound := 0L
        let username = testList.Head.Username
        let numOfRunningWorker = ref 0L
        let skipAdd = ref 0L
        _startTestAccount.Trigger(username, testList.Length)

        // list of available workers to start, 60 was choosed according to pratical test
        let availableWorkers = new BlockingCollection<Int32>()
        [1 .. 50] |> List.iter (fun _ -> availableWorkers.Add(1))

        let meanRequestsPerTimeout = ref 0L
        let averageSpeed = ref (0, 0, float 0)
        // this routine have the intelligence to known if the number of workers are too much or too less
        let adjustNumberOfWorkers() =

            // calculate statistics
            let currentMeanRequestsPerTimeout = Interlocked.CompareExchange(meanRequestsPerTimeout, 0L, !meanRequestsPerTimeout)
            let (numOfIter, sumOfReq, oldMedian) = !averageSpeed
            let newSumOfReq = sumOfReq + int currentMeanRequestsPerTimeout
            let newMedian : float = float <| newSumOfReq / (numOfIter + 1)
            averageSpeed := (numOfIter + 1, newSumOfReq, newMedian)

            // if I have done better than the last time than add a worker
            if (oldMedian * 1.1) <= float currentMeanRequestsPerTimeout then
                // add a new worker to the list
                availableWorkers.Add(1)
            // if the lost is greathen than 10% than remove a worker
            elif oldMedian > ((float currentMeanRequestsPerTimeout) * 1.1) then
                Interlocked.CompareExchange(skipAdd, 1L, 0L) |> ignore
            
            let reqPerMinute = (int currentMeanRequestsPerTimeout) / (_callBackAdjustWorkerTimeout / 1000)
            _requestPerMinute.Trigger(reqPerMinute)

        // set the timer in order to calculate the performance based on the current number of workers
        let timer: Timer option ref = ref None
        let timerCallback _ = 
            adjustNumberOfWorkers()
            // start again the callback
            if (!timer).IsSome then (!timer).Value.Change(_callBackAdjustWorkerTimeout, Timeout.Infinite) |> ignore
        timer := Some <| new Timer(new TimerCallback(timerCallback), null, _callBackAdjustWorkerTimeout, Timeout.Infinite)

        // run the test process of a single request
        let analyze(testRequest: TestRequest) (checkCompletation: unit -> unit) =
            async {
                _startTest.Trigger(testRequest)
                Interlocked.Increment(numOfRunningWorker) |> ignore

                let stopCurrentUserOld = Interlocked.Read(_userPasswordFound)
                if stopCurrentUserOld = 0L then
                    do! testAccount(testRequest)
                    Interlocked.Increment(meanRequestsPerTimeout) |> ignore
                    
                _endTest.Trigger(testRequest)
                Interlocked.Decrement(numOfRunningWorker) |> ignore
                checkCompletation()
            } |> Async.Start

        // start to consume all the requests
        let remainingRequests = ref testList
        let cancellationTokenSource = new CancellationTokenSource()

        // check if the bruteforce process for the current user must end
        let checkCompletation() = 
            if Interlocked.Read(numOfRunningWorker) = 0L && (!remainingRequests).IsEmpty then 
                cancellationTokenSource.Cancel()
            elif !_userPasswordFound = 1L then 
                cancellationTokenSource.Cancel()

        try
            for _ in availableWorkers.GetConsumingEnumerable(cancellationTokenSource.Token) do
                if not <| (!remainingRequests).IsEmpty then
                    let testRequest = (!remainingRequests).Head
                    analyze testRequest checkCompletation
                    remainingRequests := (!remainingRequests).Tail

                    // Add the worker only if the skipAdd flag is not setted or there are not running workers.
                    // This last condition avoid deadlock. 
                    if Interlocked.Read(skipAdd) <> 1L (*|| !numOfRunningWorker = 0L*) then
                        availableWorkers.Add(1)

        with
            | :? OperationCanceledException as e -> ()
            | :? ObjectDisposedException as e -> ()
        
        // finally dispose the timer to avoid to waste resources
        let localTimer = (!timer).Value
        timer := None
        localTimer.Dispose()

    member this.StartTestAccount = _startTestAccount.Publish
    member this.StartTest = _startTest.Publish
    member this.EndTest = _endTest.Publish
    member this.PasswordFound = _passwordFound.Publish
    member this.RequestPerMinute = _requestPerMinute.Publish

    member this.Run(url: String) =
        testRequestRepository.GetAll(url)
        |> Seq.iter bruteforceStrategy