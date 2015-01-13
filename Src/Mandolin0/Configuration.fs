namespace Mandolin0

open System
open System.IO
open System.Xml.Linq
open System.Linq

module Configuration =
    let private x str = XName.Get str

    let private readString (elem: XElement) (name: String) =
        let element = elem.Element(x(name))
        if element <> null then
            element.Value
        else
            String.Empty

    let private readInt32 (elem: XElement) (name: String) =
        let element = elem.Element(x(name))
        if element <> null then
            let result = ref 0
            Int32.TryParse(element.Value, result) |> ignore
            !result
        else
            0

    let mutable usernamesDictionary = String.Empty
    let mutable passwordsDictionary = String.Empty
    let mutable dataDirectory = "Data"
    let mutable templatesDirectory = Path.Combine(dataDirectory, "Templates")
    let mutable oraclesDirectory = Path.Combine(dataDirectory, "Oracles")
    let mutable timeout = 10000
    let mutable proxy = String.Empty

    let readConfigurationFromFile(configurationFilename) =
        if File.Exists(configurationFilename) then
            let xmlSettings = File.ReadAllText(configurationFilename)
            let doc = XDocument.Parse(xmlSettings)
            let root = doc.Element(x"config")

            let readString = readString root
            let readInt32 = readInt32 root

            dataDirectory <- readString "dataDirectory"
            templatesDirectory <- Path.Combine(dataDirectory, "Templates")
            oraclesDirectory <- Path.Combine(dataDirectory, "Oracles")

            passwordsDictionary <- readString "passwordsDictionary"
            usernamesDictionary <- readString "usernamesDictionary"
            proxy <- readString "proxy"
            let timeOutConfigurationVal = readInt32 "timeout"
            timeout <- if timeOutConfigurationVal = 0 then timeout else timeOutConfigurationVal

    // result codes
    let okResult = 0
    let wrongArgumentsResult = 1
    let wrongUsernamesResult = 2
    let wrongPasswordsResult = 3
    let wrongUrlResult = 4
    let generalExceptionResult = 90