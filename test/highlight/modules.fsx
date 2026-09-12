namespace Company.Product
// <- keyword
//        ^ module

open System.Collections
// <- keyword.import
//   ^ module

module Helpers =
// <- keyword
//     ^ module
    let private secret = 1
    //  ^ keyword.modifier
    let rec even n = if n = 0 then true else odd (n - 1)
    //  ^ keyword.modifier
    and odd n = not (even n)
    //  ^ function
