using UnityEngine;
using UnityEditor;
using System;

public interface Solver
{
    void Peform(Vector4[] input, ref Vector4[] output);
}


