namespace Mandolin0

open System
open System.IO
open System.Xml.Linq
open System.Linq
open System.Collections.Generic

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
    let mutable private _properties = new Dictionary<String, String>()

    let readProperty(name: String) =
        if _properties.ContainsKey(name) then _properties.[name]
        else String.Empty

    let saveProperty(name: String, value: String) =
        if _properties.ContainsKey(name) then _properties.[name] <- value
        else _properties.Add(name, value)

    let saveConfiguration(configurationFilename: String) =
        let propertiesXElement =
            new XElement(x"properties",
                _properties
                |> Seq.map(fun kv -> 
                    let name = kv.Key
                    let value = kv.Value
                    new XElement(x(name), value)
                )
                |> Seq.toArray
            )

        let doc =
          new XDocument(
            new XElement(x"config",
              new XElement(x"dataDirectory", dataDirectory),
              new XElement(x"usernamesDictionary", usernamesDictionary),
              new XElement(x"passwordsDictionary", passwordsDictionary),
              new XElement(x"timeout", timeout),
              new XElement(x"proxy", proxy),
              propertiesXElement
            )
          )          

        File.WriteAllText(configurationFilename, doc.ToString())

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

            // read all other properties
            root.Element(x"properties").Elements()
            |> Seq.iter (fun xelement ->
                 let name = xelement.Name.LocalName
                 let value = xelement.Value
                 if _properties.ContainsKey(name) then
                    _properties.[name] <- value
                 else
                    _properties.Add(name, value)
            )

    // result codes
    let okResult = 0
    let wrongArgumentsResult = 1
    let wrongUsernamesResult = 2
    let wrongPasswordsResult = 3
    let wrongUrlResult = 4
    let generalExceptionResult = 90