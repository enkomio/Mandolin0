namespace Mandolin0

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Reflection
open System.Text.RegularExpressions
open System.IO
open System.Text

// verificare l'ultima versione utilizzando il sito di mandolin0
// clonare uno specifico repository dei dati di mandolin0 utilizzando un'altro repository (vedere se c'è il rate limit)

(*
contenuto da parsare:

 <meta http-equiv="X-UA-Compatible" content="chrome=1">
    <meta name="description" content="Mandolin0 : A web form bruteforce tool based on templating">
	<meta name="version" content="1.0.0">
	<meta name="kb" content="20150113.zip">
    <link rel="stylesheet" type="text/css" media="screen" href="stylesheets/stylesheet.css">

    <title>Mandolin0</title>


    Url:
    http://enkomio.github.io/Mandolin0/

*)

module UpdateChecker =
    
    let checkIfLastVersion() =
        true

    let updateKnowledgeBase(callback: String -> unit) =
        ()    
    (*
    let checkIfLastVersion() =
        async {
            let github = new GitHubClient(new ProductHeaderValue("Enkomio"))
            let! allReleases = github.Release.GetAll("Enkomio", "Mandolin0") |> Async.AwaitTask
           
            

            let! contents = github.Repository.Content.GetContents("Enkomio","Mandolin0","CHANGELOG.md") |> Async.AwaitTask
            let changelog = contents.[0].Content
            return parseChangeLog(changelog)
        } |> Async.RunSynchronously 

    let private copyFile(path: String, content: String, callback: String -> unit) =
        let directory = Path.GetDirectoryName(path)
        if not <| Directory.Exists(directory) then Directory.CreateDirectory(directory) |> ignore
        File.WriteAllText(path, content)
        callback(path)

    let private getContentValue(content: RepositoryContent, github: GitHubClient) =
        async {
            if content.Content = null then
                let! contents = github.Repository.Content.GetContents("Enkomio","Mandolin0", content.Path) |> Async.AwaitTask
                return contents.[0].Content
            else
                return content.Content
        }        

    let updateKnowledgeBase(callback: String -> unit) =
        async {
            let github = new GitHubClient(new ProductHeaderValue("Enkomio"))
            let! contents = github.Repository.Content.GetContents("Enkomio","Mandolin0", "Data") |> Async.AwaitTask
            
            let contentToDownloads = new List<RepositoryContent>(contents)
            //for content in contentToDownloads do    
            while contentToDownloads.Count > 0 do                
                let content = contentToDownloads.[0]
                contentToDownloads.Remove(content) |> ignore

                // remove the Data directory from the path
                let localPath = content.Path.Replace("Data", ".")            

                let path = Path.Combine(Configuration.dataDirectory, localPath)
                if content.Type = ContentType.File then
                    let sha1Remote = content.Sha
                    if File.Exists(path) then
                        // verify if sha1 changed
                        let cryptoProvider = new SHA1CryptoServiceProvider()
                        let fileContent = File.ReadAllBytes(path)
                        use sha1 = new SHA1Managed()
                        let sha1Local = sha1.ComputeHash(fileContent)
                        let sha1LocalString = BitConverter.ToString(sha1Local).Replace("-", String.Empty)
                        if not <| sha1Remote.Equals(sha1LocalString, StringComparison.OrdinalIgnoreCase) then
                            let! contentString = getContentValue(content, github)
                            copyFile(path, contentString, callback)
                    else
                        let! contentString = getContentValue(content, github)
                        copyFile(path,contentString, callback)
                else
                    if not <| Directory.Exists(path) then Directory.CreateDirectory(path) |> ignore
                    let! contents = github.Repository.Content.GetContents("Enkomio","Mandolin0", path) |> Async.AwaitTask
                    contentToDownloads.AddRange(contents)

        } |> Async.RunSynchronously 

        *)