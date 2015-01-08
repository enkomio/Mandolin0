namespace Mandolin0

open System

type Template(name: String) = 

    member val Name = name with get
    member val Content = String.Empty with get, set