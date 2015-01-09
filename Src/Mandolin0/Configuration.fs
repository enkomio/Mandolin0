namespace Mandolin0

open System
open System.IO
open System.Xml.Linq
open System.Linq

module Configuration =
    let private x str = XName.Get str

    let mutable usernamesDictionary = String.Empty
    let mutable passwordsDictionary = String.Empty
    let mutable templatesDirectory = "Templates"
    let mutable oraclesDirectory = "Oracles"
    let mutable timeout = 10000

    let readConfigurationFromFile(configurationFilename) =
        if File.Exists(configurationFilename) then
            let xmlSettings = File.ReadAllText(configurationFilename)
            let doc = XDocument.Parse(xmlSettings)
            let root = doc.Element(x"config")

            templatesDirectory <- root.Element(x"templates").Element(x"path").Value
            oraclesDirectory <- root.Element(x"oracles").Element(x"path").Value
            passwordsDictionary <- root.Element(x"passwordsDictionary").Value
            usernamesDictionary <- root.Element(x"usernamesDictionary").Value
            timeout <- Int32.Parse(root.Element(x"timeout").Value)

    // result codes
    let okResult = 0
    let wrongArgumentsResult = 1
    let wrongUsernamesResult = 2
    let wrongPasswordsResult = 3
    let wrongUrlResult = 4
    let generalExceptionResult = 90