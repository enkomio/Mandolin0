namespace Mandolin0

open System
open System.Collections.Generic
open System.Text
open System.Reflection
open System.Threading
open System.Runtime.InteropServices
open Autofac
open Nessos.UnionArgParser

type CLIArguments =
    | [<AltCommandLine("-u")>] Usernames of String
    | [<AltCommandLine("-p")>] Passwords of String
    | [<AltCommandLine("-w")>] Url of String
    | Template of String
    | List_Templates
    | Oracle of String
    | List_Oracles
    | Version

with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Url _ -> "specify the url that will be tested"
            | Usernames _ -> "specify a username dictionary"
            | Passwords _ -> "specify a password dictionary to use with each username"
            | Template _ -> "specify the HTTP template to use for the bruteforce"
            | Oracle _ -> "specify the oracle to use for the bruteforce"
            | List_Templates -> "show all the available templates"
            | List_Oracles -> "show all the available oracles"
            | Version -> "show the full version"
            
module Program =

    let _syncRoot = new Object()
    let _nextCursorTopForUsername = ref 0
    let _restoreSessionConsoleTop = ref 0
    let _readKeysHandlers = new SortedList<Int32, ConsoleKeyInfo -> unit>()
    let mutable _updateProgressBarCallback : (unit -> unit) option = None    
    
    let readKeyDispatcher() =
        while true do
            let key = Console.ReadKey(true)
            for handler in _readKeysHandlers do
                handler.Value(key)

    let createBlockingReadKey() =
        let resetEvent = new ManualResetEventSlim(false)
        let internalKey = ref(new ConsoleKeyInfo())

        let readKey = 
            fun () ->
                resetEvent.Wait()
                resetEvent.Reset()
                !internalKey

        let consumeKey =
            fun (key: ConsoleKeyInfo) ->
                internalKey := key
                resetEvent.Set()

        (readKey, consumeKey)
                
    let handleCtrCancelEvent (sessionManager: SessionManager) (saveCallback: unit -> unit) (deleteCallback: unit -> unit) =
        let (readKey, consumeKey) = createBlockingReadKey()
        _readKeysHandlers.Add(90, consumeKey)

        lock _syncRoot (fun () ->           
            Console.WriteLine()
            Console.WriteLine()
            Console.Write("Do you want to save the current session? [Y/N] ")
            let saveSession = ref String.Empty
            while !saveSession <> "Y" && !saveSession <> "N" do
               saveSession := readKey().KeyChar.ToString().ToUpper()

            if (!saveSession).Equals("Y", StringComparison.Ordinal) then
                saveCallback()
            else
                deleteCallback()

            // interrupt the process asap 
            sessionManager.InterruptProcess()
            Console.WriteLine()
            Console.WriteLine("Shutting down...")

            // return true to avoid to interrupt the process in a brutal way
            true
        )

    let printHeader() =
        let version = Assembly.GetEntryAssembly().GetName().Version.ToString( 2 )
        let name = Assembly.GetEntryAssembly().GetName().Name
        let nowDate = DateTime.Now

        let heading = StringBuilder()
        heading.AppendFormat( "{0}{1} v{2} - http://enkomio.github.io/Mandolin0/", Environment.NewLine, name, version, nowDate.Year )
        |> Console.WriteLine

    let printFullVersion() =
        let version = Assembly.GetEntryAssembly().GetName().Version.ToString( 4 )                
        let heading = StringBuilder()
        heading.AppendFormat( "Version: {0}{1}", version, Environment.NewLine)
        |> Console.WriteLine

    let printAllTemplates(containerBuilder: ContainerBuilder) =
        let container = containerBuilder.Build()
        let templateRepository = container.Resolve<TemplateRepository>()
        Console.WriteLine("Templates:")
        Console.WriteLine()
        for template in templateRepository.GetAllNames() do
            Console.WriteLine("\t{0}", template)

    let printAllOracles(containerBuilder: ContainerBuilder) =
        let container = containerBuilder.Build()
        let oracleRepository = container.Resolve<OracleRepository>()
        Console.WriteLine("Oracles:")
        Console.WriteLine()
        for oracle in oracleRepository.GetAllNames() do
            Console.WriteLine("\t{0}", oracle)

    let continueSession() =
        let (readKey, consumeKey) = createBlockingReadKey()
        _readKeysHandlers.Add(80, consumeKey)

        let savedTop = Console.CursorTop
        Console.CursorTop <- !_restoreSessionConsoleTop
        Console.Write("A previous session already exists, do you want to continue? [Y/N] ")
        let restoreSession = ref String.Empty
        while !restoreSession <> "Y" && !restoreSession <> "N" do
            restoreSession := readKey().KeyChar.ToString().ToUpper()

        Console.CursorTop <- savedTop
        (!restoreSession).Equals("Y", StringComparison.Ordinal)

    let printCurrentNumberOfRequestsPerMinute (left: Int32) (top: Int32) (reqPerMinute: Int32, numOfWorkers: Int32) =
        lock _syncRoot (fun () ->
            let (savedLeft, savedTop) = (Console.CursorLeft, Console.CursorTop)
            Console.CursorLeft <- left
            Console.CursorTop <- top
            Console.WriteLine("Req/s: {0,-3} - #Workers: {1,-3}", reqPerMinute.ToString().PadRight(3, ' '), numOfWorkers.ToString().PadRight(10, ' '))
            Console.CursorLeft <- savedLeft
            Console.CursorTop <- savedTop
        )

    let printProgressBar(username: String, totalPoint: Int32) =
        // save the current session indexes
        Console.CursorTop <- !_nextCursorTopForUsername
        incr _nextCursorTopForUsername

        Console.WriteLine()
        let usernameLabel = String.Format("{0,-10} ", username.Substring(0, min 10 username.Length))
        Console.Write(usernameLabel)

        let index = ref 1
        let progressBarStartPosition = Console.CursorLeft
        let savedTop = Console.CursorTop

        // return the callback to invoke in order to update the progress bar
        fun (currentIndex: Int32) ->            
            // save coordinate
            let top = Console.CursorTop
            Console.CursorTop <- savedTop

            // create the statistic message             
            let floatScaledValue = float currentIndex / float totalPoint
            let progressBarPercentage = int (floatScaledValue * float 100)
            let spaceForPasswordCount = totalPoint.ToString().Length.ToString()
            let statisticString = String.Format(" {0,3}% ({1," + spaceForPasswordCount + "}/{2,-" + spaceForPasswordCount + "})", progressBarPercentage, !index, totalPoint)

            // calculate scale indexes for progress bar
            let defaultProgressBarLen = float (Console.WindowWidth - statisticString.Length - usernameLabel.Length)
            let progressBarLeftPosition = int (floatScaledValue * defaultProgressBarLen)
            incr index

            // calcultae position for progress bar
            let cursorVisible = Console.CursorVisible
            let savedPosition = Console.CursorLeft

            // display the progressbar
            Console.CursorLeft <- progressBarStartPosition
            Console.Write("[")

            if progressBarLeftPosition > 0 then
                // write the character '='
                while Console.CursorLeft <= progressBarLeftPosition do
                    Console.Write("=")

            // clean the progress bar
            while Console.CursorLeft <= int defaultProgressBarLen do
                Console.Write(" ")
            Console.Write("]")

            Console.Write(statisticString)

            // clean the line till the end
            while Console.CursorLeft < Console.WindowWidth - 1 do
                Console.Write(" ")

            Console.CursorLeft <- savedPosition
            Console.CursorVisible <- cursorVisible
            Console.CursorTop <- top

    let callbackTestUsername(username: String, numOfPasswords: Int32) =
        _updateProgressBarCallback <- 
            let currentIndex = ref 0
            let tmpCallback = printProgressBar(username, numOfPasswords)
            let syncRoot = new Object()

            Some <| fun () ->
                lock syncRoot (fun () ->
                    incr currentIndex
                    tmpCallback(!currentIndex)
                )

    let callbackStartTestAccount(testRequest: TestRequest) = ()

    let callbackEndTestAccount(testRequest: TestRequest) =
        lock _syncRoot (fun () ->
            _updateProgressBarCallback.Value()
        )
        
    let callbackPasswordFound(testRequest: TestRequest) =
        lock _syncRoot (fun () ->
            let (left, top) = (Console.CursorLeft, Console.CursorTop)
            Console.Beep()
            Console.WriteLine()
            Console.WriteLine()
            Console.WriteLine("Username: {0}", testRequest.Username)
            Console.WriteLine("Password: {0}", testRequest.Password)
            _nextCursorTopForUsername := Console.CursorTop + 1
            Console.CursorLeft <- left
            Console.CursorTop <- top
        )

    let checkForUpdates(configurationFilename: String) =
        Console.WriteLine()
        Console.Write("Check for updates...")
        try
            if UpdateChecker.checkIfLastVersion() then
                // try to update th KB
                UpdateChecker.updateKnowledgeBase(configurationFilename, fun _ -> Console.Write("."))
                Console.WriteLine("Done")
            else
                Console.WriteLine("You haven't installed the last version of Mandolin0, unable to update KB")
        with
            e -> Console.WriteLine("Some error occoured during the update: " + e.Message)

    [<EntryPoint>]
    let main argv = 
        let parser = UnionArgParser.Create<CLIArguments>()
        printHeader()

        try
            // read the configuration from the file
            let configurationFilename = "mandolin0.config"
            Configuration.readConfigurationFromFile(configurationFilename)
            
            let results = parser.Parse(argv)
            let arguments = results.GetAllResults()

            // configure the container to instantiate the components
            let containerBuilder = new ContainerBuilder()
            ignore(
                containerBuilder.RegisterType<RequestBuilder>(),

                containerBuilder.RegisterType<TemplateRepository>()
                    .WithParameter("templateDirectory", Configuration.templatesDirectory),

                containerBuilder.RegisterType<OracleRepository>()
                    .WithParameter("oracleDirectory", Configuration.oraclesDirectory),
                        
                containerBuilder.RegisterType<SessionManager>()
                    .WithParameter("continueSessionCallback", continueSession)
                    .SingleInstance(),

                containerBuilder.RegisterType<Bruteforcer>()
            )

            if arguments |> List.exists (fun m -> m = Version) then
                Console.WriteLine()
                printFullVersion()
                Configuration.okResult
            elif arguments |> List.exists (fun m -> m = List_Templates) then
                Console.WriteLine()
                printAllTemplates(containerBuilder)
                Configuration.okResult
            elif arguments |> List.exists (fun m -> m = List_Oracles) then
                Console.WriteLine()
                printAllOracles(containerBuilder)
                Configuration.okResult
            elif  arguments |> List.exists (fun m -> match m with Url _ -> true | _ -> false) then
                
                checkForUpdates(configurationFilename)
                  
                // read arguments used to run the bruteforcer
                let usernamesFile : String option ref = ref <| Some(Configuration.usernamesDictionary)
                let passwordsFile : String option ref = ref <| Some(Configuration.passwordsDictionary)
                let url : String option ref = ref None
                let template: String option ref = ref None
                let oracle: String option ref = ref None

                arguments
                |> List.iter(fun v ->
                    match v with
                    | Usernames u -> usernamesFile := Some u
                    | Passwords p -> passwordsFile := Some p
                    | Url u -> url := Some u
                    | Template t -> template := Some t
                    | Oracle o -> oracle := Some o
                    | _ -> ()
                )

                if (!usernamesFile).IsNone then
                    Console.WriteLine(parser.Usage())
                    Console.WriteLine("Please specify the usernames dictionary file")
                    Configuration.wrongUsernamesResult
                elif (!passwordsFile).IsNone then
                    Console.WriteLine(parser.Usage())
                    Console.WriteLine("Please specify the passwords dictionary file")
                    Configuration.wrongPasswordsResult
                elif (!url).IsNone then
                    Console.WriteLine(parser.Usage())
                    Console.WriteLine("Please specify the url to analyze")
                    Configuration.wrongUrlResult
                else
                    if (!template).IsNone then
                        template := Some "Default"

                    if (!oracle).IsNone then
                        oracle := Some (!template).Value

                    // regist a needed component for the bruteforce process
                    containerBuilder.RegisterType<TestRequestRepository>()
                        .WithParameter("usernameFile", (!usernamesFile).Value)
                        .WithParameter("passwordFile", (!passwordsFile).Value)
                        .WithParameter("templateName", (!template).Value)
                        .WithParameter("oracleName", (!oracle).Value) |> ignore

                    // resolve all the needed components and run the bruteforce
                    let container = containerBuilder.Build()
                    let bruteforcer = container.Resolve<Bruteforcer>()

                    // regist callback for displaying updates
                    bruteforcer.StartTestAccount.Add(callbackTestUsername)
                    bruteforcer.StartTest.Add(callbackStartTestAccount)
                    bruteforcer.EndTest.Add(callbackEndTestAccount)
                    bruteforcer.PasswordFound.Add(callbackPasswordFound)

                    // finally run the bruteforce process
                    Console.WriteLine()
                    Console.WriteLine("Bruteforce: {0}", (!url).Value)
                    Console.WriteLine("Template: {0} - Oracle: {1}", (!template).Value, (!oracle).Value)
                    Console.WriteLine()
                    _restoreSessionConsoleTop := Console.CursorTop
                    Console.WriteLine()
                    Console.WriteLine()
                    let printCurrentNumberOfRequestsPerMinuteHandler = printCurrentNumberOfRequestsPerMinute (Console.CursorLeft) (Console.CursorTop)
                    bruteforcer.ProcessStatistics.Add(printCurrentNumberOfRequestsPerMinuteHandler)
                    _nextCursorTopForUsername := Console.CursorTop
                    
                    // intercept the Ctrl+C signal through an input loop
                    Console.TreatControlCAsInput <- true
                    Console.CancelKeyPress.Add(fun _ -> ())

                    let (readKey, consumeKey) = createBlockingReadKey()
                    _readKeysHandlers.Add(10, consumeKey)

                    let sessionManager = container.Resolve<SessionManager>()
                    let saveCallBack = fun () -> sessionManager.SaveResult((!url).Value, (!oracle).Value, (!template).Value)
                    let deleteCallback = fun () -> sessionManager.DeleteResult((!url).Value, (!oracle).Value, (!template).Value)
                    async {
                        while true do
                            let c = readKey()
                            if c.Modifiers = ConsoleModifiers.Control && c.Key.ToString().Equals("C", StringComparison.OrdinalIgnoreCase) then
                                handleCtrCancelEvent sessionManager saveCallBack deleteCallback |> ignore
                    } |> Async.Start

                    // start the read key loop dispatcher
                    async {
                        readKeyDispatcher()
                    } |> Async.Start

                    bruteforcer.Run((!url).Value) 
                    Configuration.okResult
            else
                let usage = parser.Usage()
                Console.WriteLine(usage)
                Configuration.okResult
        with 
            | :? ArgumentException as e -> 
                let usage = parser.Usage()
                Console.WriteLine(usage)
                Configuration.wrongArgumentsResult
            | e ->
                Console.WriteLine()
                let msg =
                    if e.InnerException <> null then e.InnerException.Message
                    else e.Message
                Console.WriteLine(msg)
                Configuration.generalExceptionResult