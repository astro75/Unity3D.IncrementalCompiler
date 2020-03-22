#I @"packages/FAKE/tools"
#I @"packages/FAKE.BuildLib/lib/net451"
#r "FakeLib.dll"
#r "BuildLib.dll"

open Fake
open BuildLib

let solution = initSolution "./IncrementalCompiler.sln" "Release" [ ]

Target "Clean" <| fun _ -> cleanBin

Target "Restore" <| fun _ -> restoreNugetPackages solution

Target "Build" <| fun _ -> buildSolution solution

let export = for (target, target2) in [("2018", "Unity5"); ("2019.3", "2019.3")] do
    let targetDir = binDir @@ target
    let compilerDir = targetDir @@ "Compiler"
    let editorDir = targetDir @@ "Assets" @@ "CSharp vNext Support" @@ "Editor"
    let pluginsDir = targetDir @@ "Assets" @@ "Plugins" @@ "Incremental Compiler"
    CreateDir targetDir
    CreateDir compilerDir
    CreateDir editorDir
    CreateDir pluginsDir

    "./GenerationAttributes/bin/Release/GenerationAttributes.dll" |> CopyFile pluginsDir
    "./GenerationAttributes/bin/Release/GenerationAttributes.xml" |> CopyFile pluginsDir
    "./Macros/bin/Release/Macros.dll" |> CopyFile pluginsDir
    "./Macros/bin/Release/Macros.xml" |> CopyFile pluginsDir
    "./core/UnityPackage/Assets/Editor/CompilerSettings.cs" |> CopyFile editorDir
    "./tools/0Harmony.dll" |> CopyFile editorDir
    "./core/IncrementalCompiler/IncrementalCompiler.xml" |> CopyFile compilerDir
    "./extra/CompilerPlugin." + target2 + "/bin/Release/Unity.PureCSharpTests.dll" |> CopyFile (editorDir @@ "CSharpVNextSupport.dll")
    "./extra/UniversalCompiler/bin/Release/UniversalCompiler.exe" |> CopyFile compilerDir
    "./extra/UniversalCompiler/UniversalCompiler.xml" |> CopyFile compilerDir
    "./tools/pdb2mdb/pdb2mdb.exe" |> CopyFile compilerDir

    let dir = System.IO.DirectoryInfo("./core/IncrementalCompiler/bin/Release/net471")
    filesInDir dir |> Array.iter (fun f -> f.FullName |> CopyFile compilerDir)

    // IO.Shell.rename (compilerDir @@ "IncrementalCompiler.exe") (compilerDir @@ "IncrementalCompiler.dll")

Target "BuildExport" (fun _ -> export)

Target "Export" (fun _ -> export)

Target "Help" <| fun _ -> 
    showUsage solution (fun name -> 
        if name = "package" then Some("Build package", "sign")
        else None)

"Clean"
  ==> "Restore"
  ==> "Build"

"Build" ==> "BuildExport"

RunTargetOrDefault "Help"
