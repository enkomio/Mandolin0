﻿namespace Mandolin0

open System
open System.Threading
open System.Collections.Generic
open System.IO
open System.Xml.Linq
open System.Linq

type private SavedSession =
    { 
        mutable Index : Int32
        Username: String
        Filename : String 
    }

    member this.Check(username: String, passwordIndex: Int32) =
        if this.Username.Equals(username, StringComparison.Ordinal) then
            this.Index < passwordIndex
        else 
            true

type SessionManager(continueSessionCallback: unit -> Boolean) =
    let x str = XName.Get str

    let _currentIndex = ref 0
    let _username = ref(String.Empty)
    let _continueSessionCallbackCalled = ref false
    let _wantToRestoreSession = ref false
    let _sessions = new Dictionary<String * String * String, SavedSession>()
    let mutable _lastRetrievedSession: SavedSession option = None

    do
        // load all existing session
        if Directory.Exists("Sessions") then
            for sessionFile in Directory.EnumerateFiles("Sessions") do
                try
                    let xmlSession = File.ReadAllText(sessionFile)
                    let doc = XDocument.Parse(xmlSession)
                    let root = doc.Element(x"session")

                    let url = root.Element(x"url").Value
                    let template = root.Element(x"template").Value
                    let oracle = root.Element(x"oracle").Value
                    let username = root.Element(x"username").Value
                    let index = Int32.Parse(root.Element(x"index").Value)
                    let filename = root.Element(x"filename").Value
                    _sessions.Add((url, oracle, template), { Index = index; Username = username; Filename = filename })
                with _ -> ()

    member this.ConsiderUsernameAndPasswordIndex (url: String) (oracle: String) (template: String) (username: String, passwordIndex: Int32) =
        if _sessions.ContainsKey(url, oracle, template) then
            if not(!_continueSessionCallbackCalled) then
                _wantToRestoreSession := continueSessionCallback()
                _continueSessionCallbackCalled := true
            
            if !_wantToRestoreSession then
                _lastRetrievedSession <- Some <| _sessions.[url, oracle, template]
                _lastRetrievedSession.Value.Check(username, passwordIndex)
            else
                this.DeleteSession(url, oracle, template)
                true
        else
            _lastRetrievedSession <- None
            true

    member this.IncrementIndex() =
        Interlocked.Increment(_currentIndex) |> ignore

    member this.SetCurrentUsername(username: String) =
        _currentIndex := 
            if _lastRetrievedSession.IsSome then _lastRetrievedSession.Value.Index
            else 0
        _username := username
        
    member this.DeleteSession(url: String, oracle: String, template: String) =
        if _sessions.ContainsKey(url, oracle, template) then
            let session = _sessions.[url, oracle, template]
            let sessionFilename = Path.Combine("Sessions", session.Filename)
            if File.Exists(sessionFilename) then
                File.Delete(sessionFilename)

    member this.SaveSession(url: String, oracle: String, template: String) =
        if not <| Directory.Exists("Sessions") then Directory.CreateDirectory("Sessions") |> ignore
        if not <| _sessions.ContainsKey(url, oracle, template) then
            let now = DateTime.Now
            let sessionFile = String.Format("{0}{1}{2}{3}{4}{5}.xml", now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second)
            _sessions.Add((url, oracle, template), { Index = !_currentIndex; Username = !_username; Filename = sessionFile })
        
        let session = _sessions.[url, oracle, template]
        session.Index <- !_currentIndex
        
        let doc =
          new XDocument(
            new XElement(x"session",
              new XElement(x"url", url),
              new XElement(x"template", template),
              new XElement(x"oracle", oracle),
              new XElement(x"username", session.Username),
              new XElement(x"index", session.Index),
              new XElement(x"filename", session.Filename)
            )
          )          

        File.WriteAllText(Path.Combine("Sessions", session.Filename), doc.ToString())

