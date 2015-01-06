namespace Mandolin0

open System
open System.Xml.Linq
open System.Linq

type Oracle(name: String) = 
    static let x str = XName.Get str
    let mutable _positiveTexts = []
    let mutable _negativeTexts = []

    let readText(xelement: XElement) =
        xelement.Elements(x"Text")
        |> Seq.map ( fun xElem -> xElem.Value.Trim())
        |> Seq.toList

    let parseContent(fileContent: String) =
        let doc = XDocument.Parse(fileContent)
        let root = doc.Element(x"Oracle")

        let positiveMatchElement = root.Element(x"PositiveMatch")
        if positiveMatchElement <> null then
            _positiveTexts <- readText(positiveMatchElement)

        let negativeMatchElement = root.Element(x"NegativeMatch")
        if negativeMatchElement <> null then
            _negativeTexts <- readText(negativeMatchElement)

    member val Name = name with get

    member this.ConfigureFromContent(xmlContent: String) =
        parseContent(xmlContent)

    member this.Verify(html: String) =
        _positiveTexts |> List.exists(html.Contains) && 
        _negativeTexts |> List.exists(html.Contains) |> not