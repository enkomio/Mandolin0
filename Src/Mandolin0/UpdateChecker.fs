namespace Mandolin0

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Reflection
open System.Text.RegularExpressions
open System.IO
open System.Net
open System.Text
open ICSharpCode.SharpZipLib.Core
open ICSharpCode.SharpZipLib.Zip

module UpdateChecker =
    
    let mutable private _lastVersion : String option = None
    let mutable private _lastKB: String option = None
    let mutable private _lastKBUpdateUrl: String option = None

    let private readVersionAndKb(html: String) =
        // get last Mandolin0 version
        let regex = new Regex("<meta name=\"version\" content=\"([0-9.]+)\">")
        let regexMatch = regex.Match(html)
        if regexMatch.Success then
            _lastVersion <- Some <| regexMatch.Groups.[1].Value
        else
            raise <| new ApplicationException("Unable to identify the last version of Mandolin0 from remote content")

        // get last KB version
        let regex = new Regex("<meta name=\"kb\" content=\"([^\"]+)\">")
        let regexMatch = regex.Match(html)
        if regexMatch.Success then
            _lastKBUpdateUrl <- Some <| regexMatch.Groups.[1].Value
            if not <| Uri.IsWellFormedUriString(_lastKBUpdateUrl.Value, UriKind.Absolute) then
                let fullPathUri = new Uri(new Uri("http://enkomio.github.io/Mandolin0/"), _lastKBUpdateUrl.Value)
                _lastKBUpdateUrl <- Some (fullPathUri.ToString())

            _lastKB <- Some <| Path.GetFileNameWithoutExtension(_lastKBUpdateUrl.Value)
        else
            raise <| new ApplicationException("Unable to identify the last version of KnowlefgeBase from remote content")

    let retrieveInfo() =
        async {
            let updateUrl = new Uri("http://enkomio.github.io/Mandolin0/")
            let webRequest = WebRequest.Create(updateUrl) :?> HttpWebRequest
            use! webResponse = webRequest.GetResponseAsync() |> Async.AwaitTask
            use streamReader = new StreamReader(webResponse.GetResponseStream())
            let html = streamReader.ReadToEnd()
            readVersionAndKb(html)
        } |> Async.RunSynchronously

    let checkIfLastVersion() =
        if _lastVersion.IsNone then
            retrieveInfo()

        let currentVersion = Assembly.GetEntryAssembly().GetName().Version
        currentVersion >= Version.Parse(_lastVersion.Value) 

    let private unzip(filename: String) =
        let destDirectory = Path.Combine(Path.GetDirectoryName(filename), Guid.NewGuid().ToString("N"))

        use zipFile = new ZipFile(File.Open(filename, FileMode.Open))
        for i in [0.. int zipFile.Count - 1] do
            let zipEntry = zipFile.EntryByIndex(i)
            let zipStream = zipFile.GetInputStream(zipEntry)

            if zipEntry.IsFile then
                let fileDirectory = Path.GetDirectoryName(zipEntry.Name)
                if not <| Directory.Exists(fileDirectory) then
                    let directoryToCreate = Path.Combine(destDirectory, fileDirectory)
                    Directory.CreateDirectory(directoryToCreate) |> ignore
                
                let buffer = Array.zeroCreate<Byte>(int zipEntry.Size)
                zipStream.Read(buffer, 0, buffer.Length) |> ignore

                let filename = Path.Combine(destDirectory, zipEntry.Name)
                use fileStream = File.OpenWrite(filename)
                fileStream.Write(buffer, 0, buffer.Length)
            else
                if not <| Directory.Exists(zipEntry.Name) then
                    let directoryToCreate = Path.Combine(destDirectory, zipEntry.Name)
                    Directory.CreateDirectory(directoryToCreate) |> ignore

        destDirectory

    let copyDirectoryToDataDirectoryInSafeWay(directory: String, callback: String -> unit) =
        let srcDirectory = 
            if directory.EndsWith(Path.DirectorySeparatorChar.ToString()) then directory
            else directory + Path.DirectorySeparatorChar.ToString()

        for filename in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories) do
            let cleanFilename = filename.Replace(srcDirectory, String.Empty)
            let destFilename = Path.Combine(Mandolin0.Configuration.dataDirectory, cleanFilename)
            let destDirectory = Path.GetDirectoryName(destFilename)
            if not <| Directory.Exists(destDirectory) then Directory.CreateDirectory(destDirectory) |> ignore
            
            if not <| File.Exists(destFilename) then
                File.Copy(filename, destFilename)
                callback(destFilename)

    let updateKnowledgeBase(configurationFilename: String, callback: String -> unit) =
        if _lastKB.IsNone then
            retrieveInfo()

        let currentVersion = ref 0
        Int32.TryParse(Configuration.readProperty("KB"), currentVersion) |> ignore
        let lastVersionNum = Int32.Parse(_lastKB.Value)

        if lastVersionNum > !currentVersion then
            let tmpZipFilename = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip")
            use webClient = new WebClient()
            webClient.DownloadFile(_lastKBUpdateUrl.Value, tmpZipFilename)
            let unzippedDirectory = unzip(tmpZipFilename)
            copyDirectoryToDataDirectoryInSafeWay(unzippedDirectory, callback)
            Configuration.saveProperty("KB", _lastKB.Value)
            Configuration.saveConfiguration(configurationFilename)