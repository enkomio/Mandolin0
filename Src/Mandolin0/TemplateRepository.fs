namespace Mandolin0

open System
open System.Reflection
open System.IO
open System.Text
open System.Collections.Concurrent

/// Repository of all the available templates
type TemplateRepository(templateDirectory: String) = 

    let _concurrentDictionary = new ConcurrentDictionary<String, (String -> String)>()

    let replaceToken(text: String, testRequest: TestRequest) =
        text
            .Replace("{USERNAME}", testRequest.Username)
            .Replace("{PASSWORD}", testRequest.Password)

    let loadFile(templateName: String, filename: String) =
        let fileExtension = Path.GetExtension(filename)
        
        match fileExtension.ToUpper() with
        | ".TXT" -> 
            let fileContent = File.ReadAllText(filename)
            let callback = fun (_: String) -> fileContent
            let f = new Func<String, String -> String, String -> String>(fun c f -> callback)
            _concurrentDictionary.AddOrUpdate(templateName, callback, f) |> ignore
        | ".DLL" -> 
            let fullPath = Path.Combine(Directory.GetCurrentDirectory(), filename)
            let assembly = Assembly.LoadFile(fullPath)
            let typeName = "Mandolin0.Templates." + templateName + ".Builder"
            let typeBuilder = assembly.GetType(typeName)
            if typeBuilder = null then raise <| new ApplicationException("Unable to find the type '" + typeName + "' in the template assembly: " + filename)
            
            let methodBuilder = typeBuilder.GetMethod("GetTemplate", BindingFlags.Public ||| BindingFlags.Static)
            if methodBuilder = null then raise <| new ApplicationException("Unable to find the public static method '" + typeName + ".GetTemplate(url: String) : String' in the template assembly: " + filename)
            
            let callback(url: String) = 
                let objResult = methodBuilder.Invoke(null, [|url|])
                objResult :?> String

            let f = new Func<String, String -> String, String -> String>(fun c f -> callback)
            _concurrentDictionary.AddOrUpdate(templateName, callback, f) |> ignore
        | _ -> raise <| new ApplicationException("Unable to load template filename: " + filename)

    let isEntryPointFile(directoryName: String, fullPathFilename: String) =
        let filenameNoExt = Path.GetFileNameWithoutExtension(fullPathFilename)

        directoryName.Equals(filenameNoExt, StringComparison.OrdinalIgnoreCase) ||
        String.Format("Mandolin0.Templates.{0}", directoryName).Equals(filenameNoExt)

    // Every sub-directory must contains a file with the same name of the sub-directory. 
    // This is the file that contains the content of the template request
    let rec loopDirectory (directoryName: String) (files: String list) =
        match files with
        | fullPathFilename::t -> 
            if isEntryPointFile(directoryName, fullPathFilename) then
                loadFile(directoryName, fullPathFilename)
            else
                loopDirectory directoryName t
        | _ -> ()

    do
        if Directory.Exists(templateDirectory) then
            for directory in Directory.EnumerateDirectories(templateDirectory) do
                let directoryName = Path.GetFileName(directory)
                let files = Directory.EnumerateFiles(directory) |> Seq.toList
                loopDirectory directoryName files

    member this.GetAllNames() =
        _concurrentDictionary.Keys

    member this.Get(testRequest: TestRequest) =
        let templateContentCreation = ref(fun _ -> String.Empty)
        if _concurrentDictionary.TryGetValue(testRequest.Template, templateContentCreation) then
            let templateContent = (!templateContentCreation)(testRequest.Url)
            let concreteRequest = replaceToken(templateContent, testRequest)
            new Template(testRequest.Template, Content = concreteRequest)
        else
            raise <| new ApplicationException("Unable to find the template with name: " + testRequest.Template)