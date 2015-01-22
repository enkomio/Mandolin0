namespace Mandolin0

open System
open System.Collections.Concurrent
open System.Threading

type internal AccountBruteforce(username: String, oracle: Oracle, testList: TestRequest list, sessionManager: SessionManager) =
    
    let _callBackAdjustWorkerTimeout = 2000

    // vars for calculate the statistics about speed
    let _averageSpeed = ref (0, 0, float 0)
    let _averageRequestsPerTimeout = ref 0L
    let _removeWorker = ref 0L
    let _numOfRunninWorkers = ref 0L
    
    // vars for process manager
    let _numOfRunningWorkers = ref 0L
    let _processCompletedResetEvent = new ManualResetEventSlim()
    let _testRequestQueue = new ConcurrentQueue<TestRequest>(testList)
    let _userPasswordFound = ref 0L
    
    // events
    let _startTest = new Event<TestRequest>()
    let _endTest = new Event<TestRequest>()
    let _passwordFound = new Event<TestRequest>()
    let _processStatistics = new Event<Int32 * Int32>()

    let checkCompletation() =
        if Interlocked.Read(_numOfRunningWorkers) = 0L then 
            _processCompletedResetEvent.Set()
                
    let runWorker() =
        if Interlocked.Read(_userPasswordFound) = 1L || 
            Interlocked.CompareExchange(_removeWorker, 0L, 1L) = 1L then
            false
        else 
            not <| _testRequestQueue.IsEmpty
            
    let testAccount(testRequest: TestRequest) =
        async {
            let! responseText = testRequest.Send()
            if oracle.Verify(responseText) then
                _passwordFound.Trigger(testRequest)
                Interlocked.Exchange(_userPasswordFound, 1L) |> ignore
        } 

    let createWorker() =
        async {
            while runWorker() do
                let testRequest : TestRequest ref = ref(new TestRequest(String.Empty, String.Empty, String.Empty)) 
                if _testRequestQueue.TryDequeue(testRequest) then
                    Interlocked.Increment(_numOfRunningWorkers) |> ignore
                    _startTest.Trigger(!testRequest)
                    sessionManager.IncrementIndex()
                    do! testAccount(!testRequest)
                    Interlocked.Increment(_averageRequestsPerTimeout) |> ignore
                    _endTest.Trigger(!testRequest)
                    Interlocked.Decrement(_numOfRunningWorkers) |> ignore

            checkCompletation()
        } 

    let adjustNumberOfWorkers() =
        // calculate statistics
        let currentAverageRequestsPerTimeout = Interlocked.CompareExchange(_averageRequestsPerTimeout, 0L, !_averageRequestsPerTimeout)
        let reqPerSecond = (int currentAverageRequestsPerTimeout) / (_callBackAdjustWorkerTimeout / 1000)
        let (numOfIter, sumOfReq, oldMedian) = !_averageSpeed
        let newSumOfReq = sumOfReq + int currentAverageRequestsPerTimeout
        let newMedian = float <| newSumOfReq / (numOfIter + 1)
        _averageSpeed := (numOfIter + 1, newSumOfReq, newMedian)

        // the decision alghoritm is based on the response time and the number of running workers
        if newMedian >= oldMedian && reqPerSecond > int(Interlocked.Read(_numOfRunninWorkers)) then
            // add a new worker to the list
            Interlocked.Increment(_numOfRunninWorkers) |> ignore
            createWorker() |> Async.Start

        // if the lost is greathen than 5% than remove a worker to not decrease the performance
        elif oldMedian > (newMedian * 1.05) && Interlocked.Read(_numOfRunninWorkers) > 1L then
            Interlocked.CompareExchange(_removeWorker, 1L, 0L) |> ignore
            Interlocked.Decrement(_numOfRunninWorkers) |> ignore
        
        _processStatistics.Trigger(reqPerSecond, int <| Interlocked.Read(_numOfRunninWorkers))
    
    member this.StartTest = _startTest.Publish
    member this.EndTest = _endTest.Publish
    member this.PasswordFound = _passwordFound.Publish
    member this.ProcessStatistics = _processStatistics.Publish

    member this.Bruteforce() =
        // set the timer in order to calculate the performance based on the current number of workers
        let timer: Timer option ref = ref None
        let timerCallback _ = 
            adjustNumberOfWorkers()
            // start again the callback
            if (!timer).IsSome then (!timer).Value.Change(_callBackAdjustWorkerTimeout, Timeout.Infinite) |> ignore
        timer := Some <| new Timer(new TimerCallback(timerCallback), null, _callBackAdjustWorkerTimeout, Timeout.Infinite)

        // run the bruteforce process
        let initialNumberOfWorkers = 70
        _numOfRunninWorkers := int64 initialNumberOfWorkers
        [1..initialNumberOfWorkers] |> List.iter (fun _ -> createWorker() |> Async.Start)
        _processCompletedResetEvent.Wait()

        // finally dispose the timer to avoid to waste resources
        let localTimer = (!timer).Value
        timer := None
        localTimer.Dispose()