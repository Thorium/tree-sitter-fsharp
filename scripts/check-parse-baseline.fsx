// Asserts that no corpus file parses WORSE than it did when the baseline was
// recorded, counting ERROR nodes per file (a file whose only errors are MISSING
// nodes counts as 1: the CLI does not print those in the tree, see below).
//
// The `Parse examples` job calls a file parsed when tree-sitter returns a tree,
// and it returns one even when that tree contains ERROR nodes: a file can go
// from 0 to 60 error nodes and the job stays green. This is the ratchet that
// catches it, in the same spirit as MangelMaxime/tree-sitter-fsharp's bench
// baseline: test/parse-baseline.txt records every corpus file that has error
// nodes today and how many; a run fails if any file has more, or a clean file
// gains some. Files that improve are reported so the baseline can be tightened.
//
// Usage (from the repo root, with submodules checked out):
//     dotnet fsi scripts/check-parse-baseline.fsx            # compare
//     dotnet fsi scripts/check-parse-baseline.fsx --update   # accept the current state
//
// Needs nothing but the tree-sitter CLI. Set TREE_SITTER to pick it (default:
// on PATH). A grammar change that makes files parse better should ship with an
// updated baseline; one that makes files parse worse should say why in the PR.

open System
open System.Diagnostics
open System.IO

let repoRoot = Path.GetFullPath(__SOURCE_DIRECTORY__ + "/..")
let examplesDir = Path.Combine(repoRoot, "examples")
let baselineFile = Path.Combine(repoRoot, "test", "parse-baseline.txt")
let update = fsi.CommandLineArgs |> Array.contains "--update"

let treeSitter =
    match Environment.GetEnvironmentVariable "TREE_SITTER" with
    | null | "" -> "tree-sitter"
    | value -> value

// Build output that lives in the submodule working trees is not corpus.
let skippedDirs = set [ "obj"; "bin"; "artifacts"; ".git"; "node_modules" ]

/// Every `examples/**/*.fs`, repo-relative with forward slashes, in a stable order.
let corpusFiles () =
    let rec walk (dir: string) =
        seq {
            for f in Directory.EnumerateFiles(dir, "*.fs") do
                yield f
            for d in Directory.EnumerateDirectories dir do
                if not (skippedDirs.Contains(Path.GetFileName d)) then
                    yield! walk d
        }

    if not (Directory.Exists examplesDir) then
        eprintfn "no examples/ directory - check out the submodules first."
        exit 2

    walk examplesDir
    |> Seq.map (fun f -> Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
    |> Seq.sortWith (fun a b -> String.CompareOrdinal(a, b))
    |> Seq.toArray

/// Runs tree-sitter over `paths` in one process and returns the number of ERROR
/// nodes per file (1 for a file with only MISSING nodes), in `paths` order. With --paths the CLI prints
/// each tree in turn; a tree starts with `(` in column 0, and a file with errors
/// is followed by a `<path>\tParse: ...` summary line that mentions its first
/// error - that line is not part of the tree and must not be counted.
let errorNodeCounts (paths: string[]) =
    let listing = Path.GetTempFileName()

    try
        File.WriteAllLines(listing, paths)

        let psi = ProcessStartInfo(treeSitter)
        for arg in [ "parse"; "--no-ranges"; "--paths"; listing ] do
            psi.ArgumentList.Add arg
        psi.WorkingDirectory <- repoRoot
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false

        use proc =
            try
                Process.Start psi
            with e ->
                eprintfn "could not run `%s`: %s" treeSitter e.Message
                eprintfn "Set TREE_SITTER to the CLI path, or put it on PATH."
                exit 2

        // The trees of 5,000 files are hundreds of megabytes, so stdout is
        // streamed line by line rather than read into one string. stderr is
        // drained concurrently: leaving it unread deadlocks once it fills,
        // which it does on the run that compiles the parser.
        let stderrTask = proc.StandardError.ReadToEndAsync()

        let counts = Array.zeroCreate<int> paths.Length
        let mutable file = -1

        let occurrences (needle: string) (line: string) =
            let mutable n = 0
            let mutable i = line.IndexOf(needle, StringComparison.Ordinal)
            while i >= 0 do
                n <- n + 1
                i <- line.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)
            n

        // MISSING nodes (zero-width tokens inserted by error recovery) are not
        // printed in the tree at all; the only trace of one is the summary line
        // the CLI adds after a file with any error, which names the FIRST error.
        // So a file counts its ERROR nodes, and a file whose summary line says
        // it has errors but whose tree shows none is recorded as 1.
        let reported = Array.zeroCreate<bool> paths.Length

        let mutable raw = proc.StandardOutput.ReadLine()
        while not (isNull raw) do
            let line = raw.TrimEnd('\r')
            if line.StartsWith "(" then
                file <- file + 1
            if file >= 0 && file < paths.Length then
                if line.StartsWith "(" || line.StartsWith " " then
                    counts.[file] <- counts.[file] + occurrences "(ERROR" line
                elif line.Contains "\tParse:" && (line.Contains "(ERROR" || line.Contains "(MISSING") then
                    reported.[file] <- true
            raw <- proc.StandardOutput.ReadLine()

        proc.WaitForExit()
        let stderr = stderrTask.Result

        for i in 0 .. paths.Length - 1 do
            if reported.[i] && counts.[i] = 0 then
                counts.[i] <- 1

        // One tree per file, or the CLI itself failed: report that, not a
        // spurious "everything is clean".
        if file + 1 <> paths.Length then
            eprintfn "`%s parse` printed %d trees for %d files (exit %d)." treeSitter (file + 1) paths.Length proc.ExitCode
            eprintfn "This is a tooling failure, not a grammar regression."
            eprintfn ""
            eprintfn "%s" (stderr.Trim())
            exit 2

        Array.zip paths counts
    finally
        File.Delete listing

let readBaseline () =
    if File.Exists baselineFile then
        File.ReadAllLines baselineFile
        |> Array.map (fun l -> l.Trim())
        |> Array.filter (fun l -> l <> "" && not (l.StartsWith "#"))
        |> Array.map (fun l ->
            let tab = l.IndexOf '\t'
            l.Substring(tab + 1), int (l.Substring(0, tab)))
        |> Map.ofArray
        |> Some
    else
        None

let paths = corpusFiles ()
let results = errorNodeCounts paths
let failing = results |> Array.filter (fun (_, n) -> n > 0)
let totalNodes = failing |> Array.sumBy snd

printfn "%d files; %d with errors (%.2f%% clean); %d ERROR nodes"
    paths.Length failing.Length (100.0 * float (paths.Length - failing.Length) / float paths.Length) totalNodes

if update then
    let lines =
        [| "# Corpus files that parse with errors, and how many ERROR nodes each has (a file"
           "# whose only errors are MISSING nodes is recorded as 1). Asserted by"
           "# scripts/check-parse-baseline.fsx: a file with MORE nodes than listed here,"
           "# or one not listed at all, fails CI. Regenerate with"
           "#     dotnet fsi scripts/check-parse-baseline.fsx --update"
           "# after a change that makes files parse better."
           sprintf "# %d files swept; %d with errors; %d error nodes" paths.Length failing.Length totalNodes
           yield! failing |> Array.map (fun (p, n) -> sprintf "%d\t%s" n p) |]

    Directory.CreateDirectory(Path.GetDirectoryName baselineFile) |> ignore
    File.WriteAllText(baselineFile, String.Join("\n", lines) + "\n")
    printfn "baseline written: %s" (Path.GetRelativePath(repoRoot, baselineFile))
    exit 0

let baseline =
    match readBaseline () with
    | Some b -> b
    | None ->
        eprintfn "no baseline at %s - run with --update first." (Path.GetRelativePath(repoRoot, baselineFile))
        exit 1

let regressions =
    failing
    |> Array.choose (fun (p, n) ->
        let before = Map.tryFind p baseline |> Option.defaultValue 0
        if n > before then Some(p, before, n) else None)

let improved =
    results
    |> Array.choose (fun (p, n) ->
        match Map.tryFind p baseline with
        | Some before when n < before -> Some(p, before, n)
        | _ -> None)

let gone =
    baseline
    |> Map.toArray
    |> Array.filter (fun (p, _) -> not (Array.contains p paths))

if improved.Length > 0 then
    printfn ""
    printfn "%d file(s) parse better than the baseline:" improved.Length
    for p, before, n in improved do
        printfn "  %s: %d -> %d" p before n
    printfn "Tighten it with: dotnet fsi scripts/check-parse-baseline.fsx --update"

if gone.Length > 0 then
    printfn ""
    printfn "%d baseline entry(ies) no longer in the corpus (submodule moved?); --update drops them." gone.Length

if regressions.Length > 0 then
    eprintfn ""
    eprintfn "REGRESSION: %d file(s) parse worse than test/parse-baseline.txt:" regressions.Length
    eprintfn ""
    for p, before, n in regressions do
        eprintfn "  %s: %d -> %d ERROR/MISSING nodes" p before n
    eprintfn ""
    eprintfn "See the trees with `tree-sitter parse <file>`. If the new errors are right"
    eprintfn "(the file is not valid F# and the grammar now says so), update the baseline"
    eprintfn "and say so in the PR."
    exit 1

printfn "no file parses worse than the baseline"
