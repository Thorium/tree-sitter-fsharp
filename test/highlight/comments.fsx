// a line comment
// <- comment
(* a block comment *)
// <- comment
/// a doc comment
// <- comment.documentation
let x = 1

#if DEBUG
// <- keyword.directive
let debug = true
//          ^ boolean
#else
// <- keyword.directive
let debug = false
#endif
// <- keyword.directive

#nowarn "40"
// <- keyword.directive
