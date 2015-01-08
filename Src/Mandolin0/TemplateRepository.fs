namespace Mandolin0

open System
open System.IO
open System.Text
open System.Collections.Concurrent

/// Repository of all the available templates
type TemplateRepository(templateDirectory: String) = 

    let _concurrentDictionary = new ConcurrentDictionary<String, String>()

    let replaceToken(text: String, testRequest: TestRequest) =
        text
            .Replace("{USERNAME}", testRequest.Username)
            .Replace("{PASSWORD}", testRequest.Password)

    // Every sub-directory must contains a file with the same name of the sub-directory. 
    // This is the file that contains the content of the template request
    let rec loopDirectory (directoryName: String) (files: String list) =
        match files with
        | fullPathFilename::t -> 
            let filenameNoExt = Path.GetFileNameWithoutExtension(fullPathFilename)
            if directoryName.Equals(filenameNoExt, StringComparison.OrdinalIgnoreCase) then
                let fileContent = File.ReadAllText(fullPathFilename)
                _concurrentDictionary.AddOrUpdate(directoryName, fileContent, fun k v -> v) |> ignore
            else
                loopDirectory directoryName t
        | _ -> ()

    do
        if Directory.Exists(templateDirectory) then
            for directory in Directory.EnumerateDirectories(templateDirectory) do
                let directoryName = Path.GetFileName(directory)
                let files = Directory.EnumerateFiles(directory) |> Seq.toList
                loopDirectory directoryName files

    member this.Get(testRequest: TestRequest) =
        let templateContent = ref String.Empty
        if _concurrentDictionary.TryGetValue(testRequest.Template, templateContent) then
            let concreteRequest = replaceToken(!templateContent, testRequest)
            new Template(testRequest.Template, Content = concreteRequest)
        else
            raise <| new ApplicationException("Unable to find the template with name: " + testRequest.Template)