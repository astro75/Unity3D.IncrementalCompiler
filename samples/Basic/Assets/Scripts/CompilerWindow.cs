﻿using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;

[InitializeOnLoad]
public class CompilerWindow : EditorWindow
{
    class Data : ScriptableSingleton<Data> {
        public List<Compilation> compilations = new List<Compilation>();
    }

    [Serializable]
    public struct Element {
        public string target;
        public string shortTarget;
        public float start, end;
        public int durationMs;

        public Element(string target, float start, float end) {
            this.target = target;
            this.start = start;
            this.end = end;
            durationMs = Mathf.RoundToInt((end - start) * 1000);
            shortTarget = Path.GetFileName(target);
        }
    }

    [Serializable]
    public class Compilation {
        public float start, end;
        public int durationMs;
        public Element[] elements;

        public Compilation(Element[] elements, float start, float end) {
            this.elements = elements;
            this.start = start;
            this.end = end;
            durationMs = Mathf.RoundToInt((end - start) * 1000);
        }
    }

    static CompilerWindow instance;
    static readonly List<Element> currentCompilation = new List<Element>();

    [MenuItem("Assets/Compiler...")]
    static void showWindow() => instance = GetWindow<CompilerWindow>();

    static CompilerWindow() {
        var assemblies = new Dictionary<string, float>();
        float compilationStart = 0;
        DateTime compilationStartDate = default;

        CompilationPipeline.compilationStarted += o => {
            repaint();
            currentCompilation.Clear();
            assemblies.Clear();
            compilationStart = Time.realtimeSinceStartup;
            compilationStartDate = DateTime.UtcNow;
            // Debug.Log("Compilation Started");
        };
        CompilationPipeline.compilationFinished += o => {
            repaint();
            var compilations = Data.instance.compilations;
            compilations.Add(new Compilation(currentCompilation.ToArray(), compilationStart, Time.realtimeSinceStartup));
            if (compilations.Count > 20) compilations.RemoveAt(0);
            // Debug.Log("Compilation Finished");
        };

        CompilationPipeline.assemblyCompilationStarted += target => {
            repaint();
            // Debug.Log("Started " + s);
            assemblies[target] = Time.realtimeSinceStartup;
        };
        CompilationPipeline.assemblyCompilationFinished += (target, messages) => {
            repaint();
            // string elapsed;
            var currentTime = Time.realtimeSinceStartup;
            if (assemblies.TryGetValue(target, out var startedAt)) {
                // elapsed = $"{Mathf.RoundToInt((Time.realtimeSinceStartup - startedAt) * 1000)} ms";
                currentCompilation.Add(new Element(target, startedAt, currentTime));
            }
            else {
                // elapsed = "Never Started!";
            }
            // Debug.Log($"Finished {s} {elapsed}");
        };

        void repaint() {
            if (instance) instance.Repaint();
        }
    }

    public void OnGUI() {
        OnGUI_Compiler();
        // OnGUI_IncrementalCompilerStatus();
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

        if (currentCompilation.Count > 0) {
            displayElements("Compiling ...", currentCompilation, null);
        }

        for (var i = compilations.Count - 1; i >= 0; i--) {
            var compilation = compilations[i];
            GUILayout.Space(10);
            displayElements($"Compilation {i} in {compilation.durationMs:N0} ms", compilation.elements, compilation);
        }

        EditorGUILayout.EndScrollView();

        void displayElements(string label, IEnumerable<Element> elements, Compilation maybeCompilation) {
            GUILayout.Label(label, EditorStyles.boldLabel);
            foreach (var element in elements) {
                using (new GUILayout.HorizontalScope()) {
                    GUILayout.Label($"{element.durationMs} ms", labelRight, GUILayout.Width(70));
                    GUILayout.Label(element.shortTarget, GUILayout.Width(300));
                }
                if (maybeCompilation != null) {
                    var rect = GUILayoutUtility.GetLastRect();
                    rect.xMin += 370;
                    rect.height -= 5;
                    var newRect = rect;
                    newRect.xMin = Mathf.Lerp(rect.xMin, rect.xMax,
                        Mathf.InverseLerp(maybeCompilation.start, maybeCompilation.end, element.start)
                    );
                    newRect.xMax = Mathf.Lerp(rect.xMin, rect.xMax,
                        Mathf.InverseLerp(maybeCompilation.start, maybeCompilation.end, element.end)
                    );
                    EditorGuiTools.drawRect(newRect, GUI.skin.label.normal.textColor);
                }
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
