using System;
using System.Collections;
using GenerationAttributes;
using UnityEngine;
using UnityEngine.UI;
using static GenerationAttributes.Macros;

public partial class Test01 : MonoBehaviour {
    [SerializeField, PublicAccessor] int _privateVal, _private2, private3;
    [SerializeField, PublicAccessor] MonoBehaviour mb;


    public string getter => classAndMethodName;
    public static string arrowMethod() => classAndMethodName;

    void Start()
    {
        Debug.Log("Test01.Start");
        Debug.Log("Class name: " + className);
        Debug.Log($"Class and method name: {classAndMethodName}");
//        Debug.Log($"getter: {getter}");
//        Debug.Log($"arrow: {arrowMethod()}");


        GetComponent<Text>().text = "01";

        var temp = GetComponent<Text>().text;
    }

    static void defaultParams(string text = "text") {}

    public void Rerun()
    {
        Debug.Log($"Class and method name: {classAndMethodName}");
        Start();
    }
}

