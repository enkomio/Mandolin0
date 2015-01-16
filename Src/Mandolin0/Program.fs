﻿namespace Mandolin0

open System
open System.Text
open System.Reflection
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
    let mutable _updateProgressBarCallback : (unit -> unit) option = None

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

    let printCurrentNumberOfRequestsPerMinute (left: Int32) (top: Int32) (numOfWorkers: Int32) =
        lock _syncRoot (fun () ->
            let (savedLeft, savedTop) = (Console.CursorLeft, Console.CursorTop)
            Console.CursorLeft <- left
            Console.CursorTop <- top
            Console.WriteLine("Req/s: {0}", numOfWorkers.ToString().PadRight(10, ' '))
            Console.CursorLeft <- savedLeft
            Console.CursorTop <- savedTop
        )

    let printProgressBar(username: String, totalPoint: Int32) =
        // write the progress bar skeleton
        let defaultProgressBarLen = 45

        Console.WriteLine()
        Console.Write("{0,-10} ", username.Substring(0, min 10 username.Length))

        let index = ref 0
        let progressBarStartPosition = Console.CursorLeft
        Console.Write("[")
        Console.CursorLeft <- Console.CursorLeft + defaultProgressBarLen
        Console.Write("]")
        let progressBarEndPosition = Console.CursorLeft
        let savedTop = Console.CursorTop

        // return the callback to invoke in order to update the progress bar
        fun (currentIndex: Int32) ->
            let top = Console.CursorTop
            Console.CursorTop <- savedTop
            let floatScaledValue = float currentIndex / float totalPoint
            let progressBarLeftPosition = int (floatScaledValue * float defaultProgressBarLen)
            let progressBarPercentage = int (floatScaledValue * float 100)
            incr index

            let cursorVisible = Console.CursorVisible
            let newLeft = progressBarStartPosition + progressBarLeftPosition
            let savedPosition = Console.CursorLeft

            if progressBarLeftPosition > 0 then
                // write the = character
                Console.CursorLeft <- newLeft
                Console.Write("=")

            // write the percentage
            Console.CursorLeft <- progressBarEndPosition + 1
            Console.Write("{0,3}% ({1,4}/{2,-4})", progressBarPercentage, !index, totalPoint)

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
            Console.CursorLeft <- left
            Console.CursorTop <- top
        )

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
                
                // check updates
                Console.WriteLine()
                Console.Write("Check for updates...")
                if UpdateChecker.checkIfLastVersion() then
                    // try to update th KB
                    UpdateChecker.updateKnowledgeBase(configurationFilename, fun _ -> Console.Write("."))
                    Console.WriteLine("Done")
                else
                    Console.WriteLine("You haven't installed the last version of Mandolin0, unable to update KB")
                    
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
                    let printCurrentNumberOfRequestsPerMinuteHandler = printCurrentNumberOfRequestsPerMinute (Console.CursorLeft) (Console.CursorTop)
                    bruteforcer.RequestPerMinute.Add(printCurrentNumberOfRequestsPerMinuteHandler)

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