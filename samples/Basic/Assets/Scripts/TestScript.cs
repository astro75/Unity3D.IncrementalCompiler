using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts;
using UnityEngine;
using GenerationAttributes;

public class TestScript : MonoBehaviour {
    void Start() {
        var test = new ToStringEnumerableTestClass();
        Debug.LogWarning(test.ToString());
    }
}
