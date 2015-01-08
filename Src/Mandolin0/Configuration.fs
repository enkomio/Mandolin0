namespace Mandolin0

open System
open System.IO

module Configuration =
    let templatesDirectory = "Templates"
    let oraclesDirectory = "Oracles"
    let timeout = 10000

    // result codes
    let okResult = 0
    let wrongArgumentsResult = 1
    let wrongUsernamesResult = 2
    let wrongPasswordsResult = 3
    let wrongUrlResult = 4
    let generalExceptionResult = 90