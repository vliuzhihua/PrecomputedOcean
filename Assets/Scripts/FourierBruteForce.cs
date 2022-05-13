using UnityEngine;
using UnityEditor;
using System;

public class FourierBruteForce: Solver
{
    int m_size;

    public FourierBruteForce(int size)
    {
        if (!Mathf.IsPowerOfTwo(size))
        {
            Debug.Log("Fourier grid size must be pow2 number, changing to nearest pow2 number");
            size = Mathf.NextPowerOfTwo(size);
        }

        m_size = size;
    }

    Vector4 Calc(int x, int z, ref Vector4[] input)
    {
        Vector4 result = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        for(int i = 0; i < m_size; i++)
        {
            double kz = Math.PI * (2.0f * i - m_size) / m_size;// / 4;
            for(int j = 0; j < m_size; j++)
            {
                double kx = Math.PI * (2.0f * j - m_size) / m_size;// / 4;
                
                double factor = kx * x + kz * z;
                Vector2 complex = new Vector2((float)Math.Cos(factor), (float)Math.Sin(factor));

                Vector4 h = input[j + i * m_size];

                double len = kx * kx + kz * kz;
                if(len < 0.00001)
                    continue;

                result.x += h.x * complex.x - h.y * complex.y;
                result.y += h.x * complex.y + h.y * complex.x;
                result.z += h.z * complex.x - h.w * complex.y;
                result.w += h.z * complex.y + h.w * complex.x;

            }
        }
        return result;
    }

    public void Peform(Vector4[] input, ref Vector4[] output)
    {
        for (int y = 0; y < m_size; y++)
        {
            for (int x = 0; x < m_size; x++)
            {
                int idx = x + y * m_size;
                output[idx] = Calc(x, y, ref input);
            }
        }
    }

}


