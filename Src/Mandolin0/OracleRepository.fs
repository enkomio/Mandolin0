namespace Mandolin0

open System
open System.IO
open System.Collections.Concurrent

type OracleRepository(oracleDirectory: String) = 

    let _concurrentDictionary = new ConcurrentDictionary<String, Oracle>()

    // Every sub-directory must contains a file with the same name of the sub-directory. 
    // This is the file that contains the oracle
    let rec loopDirectory (directoryName: String) (files: String list) =
        match files with
        | fullPathFilename::t -> 
            let filenameNoExt = Path.GetFileNameWithoutExtension(fullPathFilename)
            if directoryName.Equals(filenameNoExt, StringComparison.OrdinalIgnoreCase) then
                let fileContent = File.ReadAllText(fullPathFilename)
                let createdOracle = new Oracle(directoryName)
                createdOracle.ConfigureFromContent(fileContent)
                _concurrentDictionary.AddOrUpdate(directoryName, createdOracle, fun k v -> v) |> ignore
            else
                loopDirectory directoryName t
        | _ -> ()

    do
        if Directory.Exists(oracleDirectory) then
            for directory in Directory.EnumerateDirectories(oracleDirectory) do
                let directoryName = Path.GetFileName(directory)
                let files = Directory.EnumerateFiles(directory) |> Seq.toList
                loopDirectory directoryName files

    member this.Get(testRequest: TestRequest) =
        let oracle = ref <| new Oracle(testRequest.Oracle)
        if _concurrentDictionary.TryGetValue(testRequest.Template, oracle) then
            !oracle
        else
            raise <| new ApplicationException("Unable to find the oracle with name: " + testRequest.Oracle)