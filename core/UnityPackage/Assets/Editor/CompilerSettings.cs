﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor.Compilation;
using Debug = UnityEngine.Debug;

public class CompilerSettings : EditorWindow
{
    struct LogElement {
        public readonly string target;
        public readonly long durationMs;
        public readonly DateTime time;

        public LogElement(string target, long durationMs, DateTime time) {
            this.target = target;
            this.durationMs = durationMs;
            this.time = time;
        }
    }

    const string UcLogFilePath = "./Compiler/Temp/UniversalCompiler.log";

    string? _ucVersion;
    LogElement[] _ucLastBuildLog = new LogElement[0];
    DateTime _ucLogLastWriteTime;

    [MenuItem("Assets/Open C# Compiler Settings...")]
    public static void ShowWindow() => GetWindow(typeof(CompilerSettings));

    public void OnDisable()
    {
        // When unity3d builds project reloads built assemblies, build logs should be updated.
        // OnDisable is called just after starting building and it can make unity3d redraw this window.
        // http://answers.unity3d.com/questions/704066/callback-before-unity-reloads-editor-assemblies.html
        Repaint();
    }

    public void OnGUI()
    {
        OnGUI_Compiler();
        OnGUI_IncrementalCompilerStatus();
        OnGUI_BuildTime();
    }

    void OnGUI_Compiler()
    {
        GUILayout.Label("Compiler", EditorStyles.boldLabel);
        if (GUILayout.Button("Recompile")) CompilationPipeline.RequestScriptCompilation();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Version", GetUniversalCompilerVersion());
        EditorGUI.BeginDisabledGroup(!File.Exists(UcLogFilePath));
        if (GUILayout.Button("Log"))
            ShowUniversalCompilerClientLog();
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    void OnGUI_BuildTime()
    {
        var durations = GetUniversalCompilerLastBuildLogs();
        GUILayout.Label("Last Build Times");
        EditorGUI.indentLevel += 1;
        var now = TrimToSeconds(DateTime.UtcNow);
        for (var index = 0; index < durations.Length; index++) {
            var duration = durations[index];
            if (index > 0) {
                var prevDuration = durations[index - 1];
                if ((prevDuration.time - duration.time).TotalSeconds > 15) {
                    // insert space between different compilation runs
                    EditorGUILayout.LabelField("---");
                }
            }
            var ago = now - duration.time;
            var seconds = (int) ago.TotalSeconds;
            EditorGUILayout.LabelField($"{seconds,4} s ago, {duration.durationMs,6:N0} ms, {duration.target}");
        }
        EditorGUI.indentLevel -= 1;

        static DateTime TrimToSeconds(DateTime date) =>
            new DateTime(date.Ticks - (date.Ticks % TimeSpan.TicksPerSecond), date.Kind);
    }

    string GetUniversalCompilerVersion()
    {
        if (_ucVersion != null) {
            return _ucVersion;
        }

        var assemblyName = AssemblyName.GetAssemblyName("./Compiler/UniversalCompiler.exe");
        _ucVersion = assemblyName != null ? assemblyName.Version.ToString() : "";
        return _ucVersion;
    }

    void ShowUniversalCompilerClientLog() => Process.Start(Path.GetFullPath(UcLogFilePath));

    LogElement[] GetUniversalCompilerLastBuildLogs()
    {
        if (!File.Exists(UcLogFilePath)) return _ucLastBuildLog;
        var ucLogLastWriteTime = GetFileLastWriteTime(UcLogFilePath);
        if (ucLogLastWriteTime == _ucLogLastWriteTime) return _ucLastBuildLog;
        _ucLogLastWriteTime = ucLogLastWriteTime;

        var result = new List<LogElement>();
        try
        {
            var lines = File.ReadAllLines(UcLogFilePath);

            var totalFound = 0;
            foreach (var line in lines.Reverse())
            {
                if (line.StartsWith("compilation-info;", StringComparison.Ordinal)) {
                    var split = line.Split(';');
                    if (split.Length < 4) continue;

                    var target = split[1];
                    var elapsed = long.TryParse(split[2], out var res) ? res : 0;
                    var dateTime = DateTime.TryParse(
                        split[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt
                    ) ? dt : DateTime.UtcNow;
                    result.Add(new LogElement(target, elapsed, dateTime));

                    totalFound++;
                    if (totalFound >= 50) break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("GetUniversalCompilerLastBuildLogs:" + e);
        }

        _ucLastBuildLog = result.ToArray();

        return _ucLastBuildLog;
    }

    void OnGUI_IncrementalCompilerStatus()
    {
        GUILayout.Label("Compiler Status", EditorStyles.boldLabel);
        EditorGUILayout.TextField("Version", GetIncrementalCompilerVersion());

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Log");
        if (GUILayout.Button("Client")) ShowIncrementalCompilerClientLog();
        if (GUILayout.Button("Server")) ShowIncrementalCompilerServerLog();
        EditorGUILayout.EndHorizontal();
    }

    string GetIncrementalCompilerVersion()
    {
        var assemblyName = AssemblyName.GetAssemblyName("./Compiler/IncrementalCompiler.exe");
        return assemblyName?.Version.ToString() ?? "";
    }

    void ShowIncrementalCompilerClientLog() => Process.Start(Path.GetFullPath(@"./Compiler/Temp/IncrementalCompiler.log"));

    void ShowIncrementalCompilerServerLog() => Process.Start(Path.GetFullPath(@"./Compiler/Temp/IncrementalCompiler-Server.log"));

    DateTime GetFileLastWriteTime(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.LastWriteTimeUtc;
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }
}
