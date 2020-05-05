﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;

[InitializeOnLoad]
public class CompilerSettings : EditorWindow
{
    class Data : ScriptableSingleton<Data> {
        public List<Compilation> compilations = new List<Compilation>();
    }

    [Serializable]
    public struct Element {
        public string assemblyName;
        public int start;
        [SerializeField] int _durationMs;
        public bool success;
        public bool compiling;

        public int? durationMs => _durationMs == -1 ? (int?) null : _durationMs;

        public Element(string assemblyName, int start, int durationMs, bool success) {
            this.assemblyName = assemblyName;
            this.start = start;
            _durationMs = durationMs;
            this.success = success;
            compiling = false;
        }

        public Element(string assemblyName, int start) {
            this.assemblyName = assemblyName;
            this.start = start;
            _durationMs = -1;
            success = true;
            compiling = true;
        }
    }

    [Serializable]
    public class Compilation {
        public int durationMs;
        public Element[] elements;

        public Compilation(Element[] elements, int durationMs) {
            this.elements = elements;
            this.durationMs = durationMs;
        }
    }

    static DateTime compilationStartTime;
    static bool compilationRunning;
    static readonly List<Element> currentCompilation = new List<Element>();
    static int timeSinceCompilationStart(DateTime dt) => (int) (dt - compilationStartTime).TotalMilliseconds;
    static int timeSinceCompilationStart() => timeSinceCompilationStart(DateTime.UtcNow);

    [MenuItem("Assets/C# Compiler...")]
    static void showWindow() => GetWindow<CompilerSettings>("C# Compiler");

    static CompilerSettings() {
        var assemblies = new Dictionary<string, int>();

        CompilationPipeline.compilationStarted += o => {
            currentCompilation.Clear();
            assemblies.Clear();
            compilationStartTime = DateTime.UtcNow;
            compilationRunning = true;
        };

        CompilationPipeline.compilationFinished += o => {
            var compilations = Data.instance.compilations;
            var durationMs = timeSinceCompilationStart();
            compilations.Add(new Compilation(currentCompilation.ToArray(), durationMs));
            currentCompilation.Clear();
            if (compilations.Count > 20) compilations.RemoveAt(0);
            compilationRunning = false;
        };

        CompilationPipeline.assemblyCompilationStarted += target => {
            assemblies[target] = timeSinceCompilationStart();

            var assemblyName = Path.GetFileName(target);
            currentCompilation.Add(new Element(assemblyName, timeSinceCompilationStart()));
        };
        CompilationPipeline.assemblyCompilationFinished += (target, messages) => {
            var assemblyName = Path.GetFileName(target);
            try {
                var idx = currentCompilation.FindIndex(_ => _.assemblyName == assemblyName);
                {
                    var lines = File.ReadAllLines(SharedData.CompileTimesFileName(assemblyName));
                    var dateTime = DateTime.Parse(lines[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                    var durationMs = int.Parse(lines[1]);
                    var exitCode = int.Parse(lines[2]);
                    var start = timeSinceCompilationStart(dateTime);
                    var newElement = new Element(assemblyName, start, durationMs, success: exitCode == 0);
                    if (idx == -1) currentCompilation.Add(newElement);
                    else currentCompilation[idx] = newElement;
                }
                {
                    // var startTime = assemblies[target];
                    // var durationMs = timeSinceCompilationStart() - startTime;
                    // currentCompilation.Add(new Element(
                    //     assemblyName, startTime, durationMs,
                    //     success: messages.All(_ => _.type != CompilerMessageType.Error)
                    // ));
                }
            }
            catch (Exception e) {
                Debug.LogError(e);
            }
        };
    }

    void Update() {
        if (compilationRunning) Repaint();
    }

    public void OnGUI() {
        OnGUI_Compiler();
        OnGUI_BuildTime();
    }

    void OnGUI_Compiler()
    {
        GUILayout.Label("Compiler", EditorStyles.largeLabel);
        if (GUILayout.Button("Recompile")) CompilationPipeline.RequestScriptCompilation();
    }

    Vector2 scrollPosition;

    void OnGUI_BuildTime() {
        var compilations = Data.instance.compilations;

        GUILayout.Label("Build Times", EditorStyles.largeLabel);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        var labelRight = new GUIStyle(GUI.skin.label) {alignment = TextAnchor.MiddleRight};

        if (compilationRunning) {
            displayElements(currentCompilation, timeSinceCompilationStart());
            GUILayout.Label("---------", EditorStyles.whiteLargeLabel);
            GUILayout.Label("Compiling", EditorStyles.whiteLargeLabel);
            GUILayout.Label("---------", EditorStyles.whiteLargeLabel);
        }

        for (var i = compilations.Count - 1; i >= 0; i--) {
            var compilation = compilations[i];
            GUILayout.Space(10);
            displayElements(compilation.elements, compilation.durationMs);
        }

        EditorGUILayout.EndScrollView();

        void displayElements(IEnumerable<Element> elements, int compilationDuration) {
            GUILayout.Label($"{compilationDuration:N0} ms", EditorStyles.boldLabel);
            foreach (var element in elements) {
                using (new GUILayout.HorizontalScope()) {
                    GUILayout.Label($"{element.durationMs} ms", labelRight, GUILayout.Width(70));

                    var prevColor = GUI.color;
                    GUI.color = element.success ? GUI.skin.label.normal.textColor : Color.red;
                    GUILayout.Label(element.assemblyName, GUILayout.Width(300));
                    GUI.color = prevColor;
                }

                var rect = GUILayoutUtility.GetLastRect();
                rect.xMin += 370;
                rect.height -= 5;
                var newRect = rect;
                newRect.xMin = Mathf.Lerp(rect.xMin, rect.xMax,
                    Mathf.InverseLerp(0, compilationDuration, element.start)
                );
                newRect.xMax = Mathf.Lerp(rect.xMin, rect.xMax,
                    Mathf.InverseLerp(0, compilationDuration, (element.start + element.durationMs) ?? int.MaxValue)
                );
                EditorGuiTools.drawRect(newRect, element.compiling ? Color.red : GUI.skin.label.normal.textColor);
                rect.height = 1;
                EditorGuiTools.drawRect(rect, GUI.skin.label.normal.textColor);
            }
        }
    }

    static class EditorGuiTools {
        static readonly Texture2D backgroundTexture = Texture2D.whiteTexture;
        static readonly GUIStyle textureStyle = new GUIStyle { normal = new GUIStyleState { background = backgroundTexture } };

        public static void drawRect(Rect position, Color color) {
            var backgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUI.Box(position, GUIContent.none, textureStyle);
            GUI.backgroundColor = backgroundColor;
        }
    }

    public static class SharedData
    {
        public const string GeneratedFolder = "generated-by-compiler";

        public static string CompileTimesFileName(string assemblyName) =>
            Path.Combine(GeneratedFolder, $"{assemblyName}.compile-times.txt");
    }
}
