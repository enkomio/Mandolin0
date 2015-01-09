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

    let _userPasswordFound = ref 0L
    let _startTestAccount = new Event<String * Int32>()
    let _numberOfWorkerChanged = new Event<Int32>()
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

        // list of available workers to start
        let availableWorkers = new BlockingCollection<Int32>()
        [1 .. Environment.ProcessorCount] |> List.iter availableWorkers.Add
        let currentNumberOfAvailableWorkers = ref availableWorkers.Count

        let requestPerMinute = ref 0
        let previousRequestsPerMinute = ref 0
        // this routine have the intelligence to known if the number of workers are too much or too less
        let adjustNumberOfWorkers() =
            if !previousRequestsPerMinute < !requestPerMinute then
                // add a new available worker to the list plus the discarded worker
                availableWorkers.Add(1)
                incr currentNumberOfAvailableWorkers
                _numberOfWorkerChanged.Trigger(int !currentNumberOfAvailableWorkers)
            // if the lost is greathen than 10% than remove a worker
            elif (float !previousRequestsPerMinute) > ((float !requestPerMinute) * 1.1) then
                if !currentNumberOfAvailableWorkers > 0 then
                    decr currentNumberOfAvailableWorkers
                    _numberOfWorkerChanged.Trigger(!currentNumberOfAvailableWorkers)
                    Interlocked.CompareExchange(skipAdd, 1L, 0L) |> ignore

            previousRequestsPerMinute := !requestPerMinute
            Interlocked.Exchange(requestPerMinute, 0) |> ignore

        // set the timer in order to calculate the performance based on the current number of workers
        let timer: Timer option ref = ref None
        let timerCallback _ = 
            adjustNumberOfWorkers()
            // start again the callback
            if (!timer).IsSome then (!timer).Value.Change(1000, Timeout.Infinite) |> ignore
        timer := Some <| new Timer(new TimerCallback(timerCallback), null, 1000, Timeout.Infinite)

        // called when a worker complete the account test process
        let workerCompleted() =
            Interlocked.Decrement(numOfRunningWorker) |> ignore
            // Add the worker only if the skipAdd flag is not setted or there are not running workers.
            // This last condition avoid deadlock. 
            if Interlocked.Read(skipAdd) <> 1L || !numOfRunningWorker = 0L then
                availableWorkers.Add(1)

        // run the test process of a single request
        let analyze(testRequest: TestRequest) (checkCompletation: unit -> unit) =
            async {
                Interlocked.Increment(numOfRunningWorker) |> ignore
                let stopCurrentUserOld = Interlocked.Read(_userPasswordFound)
                if stopCurrentUserOld = int64 0 then
                    do! testAccount(testRequest)
                    Interlocked.Increment(requestPerMinute) |> ignore
                    
                _endTest.Trigger(testRequest)
                workerCompleted()
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
            // caching to avoid to submit the same password more than one time
            let cache = new HashSet<String>()
            for _ in availableWorkers.GetConsumingEnumerable(cancellationTokenSource.Token) do
                if not <| (!remainingRequests).IsEmpty then
                    let testRequest = (!remainingRequests).Head
                    _startTest.Trigger(testRequest)
                    if cache.Add(testRequest.Password) then
                        analyze testRequest checkCompletation
                        remainingRequests := (!remainingRequests).Tail
                    else
                        _endTest.Trigger(testRequest)
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
    member this.NumberOfWorkerChanged = _numberOfWorkerChanged.Publish

    member this.Run(url: String) =
        testRequestRepository.GetAll(url)
        |> Seq.iter bruteforceStrategy