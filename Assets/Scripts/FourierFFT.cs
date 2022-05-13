using UnityEngine;
using System.Collections;

public class FourierFFT: Solver
{
    int m_size;
    float m_fsize;
    int m_passes;
    float[] m_butterflyLookupTable = null;

    public FourierFFT(int size)
    {
        if (!Mathf.IsPowerOfTwo(size))
        {
            Debug.Log("Fourier grid size must be pow2 number, changing to nearest pow2 number");
            size = Mathf.NextPowerOfTwo(size);
        }

        m_size = size; //must be pow2 num
        m_fsize = (float)m_size;
        m_passes = (int)(Mathf.Log(m_fsize) / Mathf.Log(2.0f));
        ComputeButterflyLookupTable();
    }

    int BitReverse(int i)
    {
        int j = i;
        int Sum = 0;
        int W = 1;
        int M = m_size / 2;
        while (M != 0)
        {
            j = ((i & M) > M - 1) ? 1 : 0;
            Sum += j * W;
            W *= 2;
            M /= 2;
        }
        return Sum;
    }

    void ComputeButterflyLookupTable()
    {
        m_butterflyLookupTable = new float[m_size * m_passes * 4];

        for (int i = 0; i < m_passes; i++)
        {
            int nBlocks = (int)Mathf.Pow(2, m_passes - 1 - i);
            int nHInputs = (int)Mathf.Pow(2, i);

            for (int j = 0; j < nBlocks; j++)
            {
                for (int k = 0; k < nHInputs; k++)
                {
                    int i1, i2, j1, j2;
                    if (i == 0)
                    {
                        i1 = j * nHInputs * 2 + k;
                        i2 = j * nHInputs * 2 + nHInputs + k;
                        j1 = BitReverse(i1);
                        j2 = BitReverse(i2);
                    }
                    else
                    {
                        i1 = j * nHInputs * 2 + k;
                        i2 = j * nHInputs * 2 + nHInputs + k;
                        j1 = i1;
                        j2 = i2;
                    }

                    float wr = Mathf.Cos(2.0f * Mathf.PI * (float)(k * nBlocks) / m_fsize);
                    float wi = Mathf.Sin(2.0f * Mathf.PI * (float)(k * nBlocks) / m_fsize);

                    int offset1 = 4 * (i1 + i * m_size);
                    m_butterflyLookupTable[offset1 + 0] = j1;
                    m_butterflyLookupTable[offset1 + 1] = j2;
                    m_butterflyLookupTable[offset1 + 2] = wr;
                    m_butterflyLookupTable[offset1 + 3] = wi;

                    int offset2 = 4 * (i2 + i * m_size);
                    m_butterflyLookupTable[offset2 + 0] = j1;
                    m_butterflyLookupTable[offset2 + 1] = j2;
                    m_butterflyLookupTable[offset2 + 2] = -wr;
                    m_butterflyLookupTable[offset2 + 3] = -wi;

                }
            }
        }
    }

    //Performs two FFTs on two complex numbers packed in a vector4
    Vector4 FFT(Vector2 w, Vector4 input1, Vector4 input2)
    {
        input1.x += w.x * input2.x - w.y * input2.y;
        input1.y += w.y * input2.x + w.x * input2.y;
        input1.z += w.x * input2.z - w.y * input2.w;
        input1.w += w.y * input2.z + w.x * input2.w;

        return input1;
    }

    //Performs one FFT on a complex number
    Vector2 FFT(Vector2 w, Vector2 input1, Vector2 input2)
    {
        input1.x += w.x * input2.x - w.y * input2.y;
        input1.y += w.y * input2.x + w.x * input2.y;

        return input1;
    }

    public void Peform(Vector4[] input, ref Vector4[] output)
    {
        Vector2 w;
        int j = 0;

        for (int i = 0; i < m_passes; i++, j++)
        {
            int idx = j % 2;

            for (int x = 0; x < m_size; x++)
            {
                for (int y = 0; y < m_size; y++)
                {
                    int bftIdx = 4 * (x + i * m_size);

                    int X = (int)m_butterflyLookupTable[bftIdx + 0];
                    int Y = (int)m_butterflyLookupTable[bftIdx + 1];
                    w.x = m_butterflyLookupTable[bftIdx + 2];
                    w.y = m_butterflyLookupTable[bftIdx + 3];
                    if(idx == 0)
                    {
                        output[x + y * m_size] = FFT(w, input[X + y * m_size], input[Y + y * m_size]);
                    }
                    else
                    {
                        input[x + y * m_size] = FFT(w, output[X + y * m_size], output[Y + y * m_size]);
                    }
                }
            }
        }

        for (int i = 0; i < m_passes; i++, j++)
        {
            int idx = j % 2;

            for (int x = 0; x < m_size; x++)
            {
                for (int y = 0; y < m_size; y++)
                {
                    int bftIdx = 4 * (y + i * m_size);

                    int X = (int)m_butterflyLookupTable[bftIdx + 0];
                    int Y = (int)m_butterflyLookupTable[bftIdx + 1];
                    w.x = m_butterflyLookupTable[bftIdx + 2];
                    w.y = m_butterflyLookupTable[bftIdx + 3];

                    if (idx == 0)
                    {
                        output[x + y * m_size] = FFT(w, input[x + X * m_size], input[x + Y * m_size]);
                    }
                    else
                    {
                        input[x + y * m_size] = FFT(w, output[x + X * m_size], output[x + Y * m_size]);
                    }
                }
            }
        }
        if(j % 2 == 0)
            output = input;

        float[] signs = new float[] { 1.0f, -1.0f };
        for (int y = 0; y < m_size; y++)
        {
            for (int x = 0; x < m_size; x++)
            { 
                int idx = x + y * m_size;
                int sign = (int)signs[(x + y) & 1];
                output[idx] *= sign;
            }
        }


    }

}


