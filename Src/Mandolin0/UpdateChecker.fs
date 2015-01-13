namespace Mandolin0

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Reflection
open System.Text.RegularExpressions
open System.IO
open System.Text
open Octokit

// verificare l'ultima versione utilizzando il sito di mandolin0
// clonare uno specifico repository dei dati di mandolin0 utilizzando un'altro repository (vedere se c'è il rate limit)

module UpdateChecker =
    
    let private parseChangeLog(changelogContent: String) =    
        // interested style: # Version 1.0 (2015-1-10)
        let regex = new Regex("# Version ([0-9.]+) \([0-9\-]+\)")
        let matcher = regex.Match(changelogContent)
        if matcher.Success then
            let lastVersion = Version.Parse(matcher.Groups.[1].Value)
            let currentVersion = Assembly.GetEntryAssembly().GetName().Version
            lastVersion <= currentVersion
        else                
            false

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

