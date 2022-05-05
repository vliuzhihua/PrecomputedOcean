using PhillipsOcean;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class PrecomputedOcean : MonoBehaviour
{
    public int meshSize = 32;
    public Material material;

    const float GRAVITY = 9.81f;
    public Vector2 windSpeed = new Vector2(32.0f, 32.0f);
    Vector2 windDirection;
    public float waveAmp = 0.0002f;

    Vector2[] spectrum, spectrumConj;
    float[] dispersionTable;

    public float worldScale = 64;


    Mesh mesh = null;
    Vector3[] vertices;
    Vector3[] originPosition;
    Vector3[] normals;
    Vector2[,] heightBuffer;
    Vector4[,] slopeBuffer, displacementBuffer;

    FourierCPU fourier;

    volatile bool done = true;

    // Start is called before the first frame update
    void Start()
    {
        int meshSizePlus1 = meshSize + 1;

        fourier = new FourierCPU(meshSize);

        int vertexCount = meshSizePlus1 * meshSizePlus1;
        int indexCount = meshSize * meshSize * 2 * 3;
        vertices = new Vector3[vertexCount];
        originPosition = new Vector3[vertexCount];
        normals = new Vector3[vertexCount];
        Vector2[] uv = new Vector2[vertexCount];
        int[] triangles = new int[indexCount];

        //���������λ��
        for (int i = 0; i < meshSizePlus1; i++)
        {
            for (int j = 0; j < meshSizePlus1; j++)
            {
                int idx = i * meshSizePlus1 + j;
                originPosition[idx] = new Vector3(worldScale / meshSize * j, 0.0f, worldScale / meshSize * i);
                uv[idx] = new Vector3(1.0f / meshSize * j, 0.0f, 1.0f / meshSize * i);
                normals[idx] = new Vector3(0.0f, 1.0f, 0.0f);
            }
        }

        for (int i = 0; i < meshSize; i++)
        {
            for (int j = 0; j < meshSize; j++)
            {
                int idx = i * meshSize + j;
                triangles[idx * 6 + 0] = i * meshSizePlus1 + j;
                triangles[idx * 6 + 1] = (i + 1) * meshSizePlus1 + j;
                triangles[idx * 6 + 2] = (i + 1) * meshSizePlus1 + j + 1;
                triangles[idx * 6 + 3] = i * meshSizePlus1 + j;
                triangles[idx * 6 + 4] = (i + 1) * meshSizePlus1 + j + 1;
                triangles[idx * 6 + 5] = i * meshSizePlus1 + j + 1;
            }
        }
        mesh = new Mesh();
        //�����úõĲ������и�ֵ
        mesh.vertices = originPosition;
        mesh.uv = uv;
        mesh.triangles = triangles;
        GameObject gameObject = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
        gameObject.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
        gameObject.GetComponent<MeshFilter>().mesh = mesh;
        gameObject.GetComponent<MeshRenderer>().material = material;

        //init ocean parameter

        UpdateOceanParameter(); 
    }

    void UpdateOceanParameter()
    {
        UnityEngine.Random.InitState(0);

        int meshSizePlus1 = meshSize + 1;

        spectrum = new Vector2[meshSizePlus1 * meshSizePlus1];
        spectrumConj = new Vector2[meshSizePlus1 * meshSizePlus1];
        dispersionTable = new float[meshSizePlus1 * meshSizePlus1];

        heightBuffer = new Vector2[2, meshSize * meshSize];
        slopeBuffer = new Vector4[2, meshSize * meshSize];
        displacementBuffer = new Vector4[2, meshSize * meshSize];

        windDirection = new Vector2(windSpeed.x, windSpeed.y);
        windDirection.Normalize();

        for (int i = 0; i < meshSizePlus1; i++)
        {
            for (int j = 0; j < meshSizePlus1; j++)
            {
                int idx = i * meshSizePlus1 + j;
                dispersionTable[idx] = Dispersion(j, i);
                spectrum[idx] = GetSpectrum(j, i);
                spectrumConj[idx] = GetSpectrum(-j, -i);
                spectrumConj[idx].y *= -1.0f;
            }
        }

    }

    float Dispersion(int n_prime, int m_prime)
    {
        float w_0 = 2.0f * Mathf.PI / 200.0f;
        float kx = Mathf.PI * (2 * n_prime - meshSize) / worldScale;
        float kz = Mathf.PI * (2 * m_prime - meshSize) / worldScale;
        return Mathf.Floor(Mathf.Sqrt(GRAVITY * Mathf.Sqrt(kx * kx + kz * kz)) / w_0) * w_0;
    }

    Vector2 GaussianRandomVariable()
    {
        float x1, x2, w;
        do
        {
            x1 = 2.0f * UnityEngine.Random.value - 1.0f;
            x2 = 2.0f * UnityEngine.Random.value - 1.0f;
            w = x1 * x1 + x2 * x2;
        }
        while (w >= 1.0f);

        w = Mathf.Sqrt((-2.0f * Mathf.Log(w)) / w);
        return new Vector2(x1 * w, x2 * w);
    }

    float PhillipsSpectrum(int n_prime, int m_prime)
    {
        Vector2 k = new Vector2(Mathf.PI * (2 * n_prime - meshSize) / worldScale, Mathf.PI * (2 * m_prime - meshSize) / worldScale);
        float k_length = k.magnitude;
        if (k_length < 0.000001f) return 0.0f;

        float k_length2 = k_length * k_length;
        float k_length4 = k_length2 * k_length2;

        k.Normalize();

        float k_dot_w = Vector2.Dot(k, windDirection);
        float k_dot_w2 = k_dot_w * k_dot_w * k_dot_w * k_dot_w * k_dot_w * k_dot_w;

        float w_length = windSpeed.magnitude;
        float L = w_length * w_length / GRAVITY;
        float L2 = L * L;

        float damping = 0.001f;
        float l2 = L2 * damping * damping;

        return waveAmp * Mathf.Exp(-1.0f / (k_length2 * L2)) / k_length4 * k_dot_w2 * Mathf.Exp(-k_length2 * l2);
    }


    Vector2 GetSpectrum(int n_prime, int m_prime)
    {
        Vector2 r = GaussianRandomVariable();
        return r * Mathf.Sqrt(PhillipsSpectrum(n_prime, m_prime) / 2.0f);
    }


    // Update is called once per frame
    void Update()
    {
        //If still running return.
        if (!done) return;

        UpdateOceanParameter();
        //Set data generated form last calculations.
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.RecalculateBounds();

        //print("render ocean once....");

        //Start new calculations for time period t.
        done = false;

        Nullable<float> time = Time.realtimeSinceStartup;

        ThreadPool.QueueUserWorkItem(new WaitCallback(RunThreaded), time);

    }

    void RunThreaded(object o)
    {

        Nullable<float> time = o as Nullable<float>;

        EvaluateWavesFFT(time.Value);

        done = true;

    }

    Vector2 InitSpectrum(float t, int n_prime, int m_prime)
    {
        int meshSizePlus1 = meshSize + 1;
        int index = m_prime * meshSizePlus1 + n_prime;

        float omegat = dispersionTable[index] * t;

        float cos = Mathf.Cos(omegat);
        float sin = Mathf.Sin(omegat);

        float c0a = spectrum[index].x * cos - spectrum[index].y * sin;
        float c0b = spectrum[index].x * sin + spectrum[index].y * cos;

        float c1a = spectrumConj[index].x * cos - spectrumConj[index].y * -sin;
        float c1b = spectrumConj[index].x * -sin + spectrumConj[index].y * cos;

        return new Vector2(c0a + c1a, c0b + c1b);
    }

    /// <summary>
    /// Evaluates the waves for time period t. Must be thread safe.
    /// </summary>
    void EvaluateWavesFFT(float t)
    {
        int N = meshSize;
        int Nplus1 = N + 1;

        float kx, kz, len, lambda = -1.0f;
        int index, index1;

        for (int m_prime = 0; m_prime < N; m_prime++)
        {
            kz = Mathf.PI * (2.0f * m_prime - N) / worldScale;

            for (int n_prime = 0; n_prime < N; n_prime++)
            {
                kx = Mathf.PI * (2 * n_prime - N) / worldScale;
                len = Mathf.Sqrt(kx * kx + kz * kz);
                index = m_prime * N + n_prime;

                Vector2 c = InitSpectrum(t, n_prime, m_prime);

                heightBuffer[1, index].x = c.x;
                heightBuffer[1, index].y = c.y;

                slopeBuffer[1, index].x = -c.y * kx;
                slopeBuffer[1, index].y = c.x * kx;

                slopeBuffer[1, index].z = -c.y * kz;
                slopeBuffer[1, index].w = c.x * kz;

                if (len < 0.000001f)
                {
                    displacementBuffer[1, index].x = 0.0f;
                    displacementBuffer[1, index].y = 0.0f;
                    displacementBuffer[1, index].z = 0.0f;
                    displacementBuffer[1, index].w = 0.0f;
                }
                else
                {
                    displacementBuffer[1, index].x = -c.y * -(kx / len);
                    displacementBuffer[1, index].y = c.x * -(kx / len);
                    displacementBuffer[1, index].z = -c.y * -(kz / len);
                    displacementBuffer[1, index].w = c.x * -(kz / len);
                }
            }
        }

        fourier.PeformFFT(0, heightBuffer, slopeBuffer, displacementBuffer);

        int sign;
        float[] signs = new float[] { 1.0f, -1.0f };
        Vector3 n;

        for (int m_prime = 0; m_prime < N; m_prime++)
        {
            for (int n_prime = 0; n_prime < N; n_prime++)
            {
                index = m_prime * N + n_prime;          // index into buffers
                index1 = m_prime * Nplus1 + n_prime;    // index into vertices

                sign = (int)signs[(n_prime + m_prime) & 1];

                // height
                vertices[index1].y = heightBuffer[1, index].x * sign;

                // displacement
                vertices[index1].x = originPosition[index1].x + displacementBuffer[1, index].x * lambda * sign;
                vertices[index1].z = originPosition[index1].z + displacementBuffer[1, index].z * lambda * sign;

                // normal
                n = new Vector3(-slopeBuffer[1, index].x * sign, 1.0f, -slopeBuffer[1, index].z * sign);
                n.Normalize();

                normals[index1].x = n.x;
                normals[index1].y = n.y;
                normals[index1].z = n.z;

                // for tiling
                if (n_prime == 0 && m_prime == 0)
                {
                    vertices[index1 + N + Nplus1 * N].y = heightBuffer[1, index].x * sign;

                    vertices[index1 + N + Nplus1 * N].x = originPosition[index1 + N + Nplus1 * N].x + displacementBuffer[1, index].x * lambda * sign;
                    vertices[index1 + N + Nplus1 * N].z = originPosition[index1 + N + Nplus1 * N].z + displacementBuffer[1, index].z * lambda * sign;

                    normals[index1 + N + Nplus1 * N].x = n.x;
                    normals[index1 + N + Nplus1 * N].y = n.y;
                    normals[index1 + N + Nplus1 * N].z = n.z;
                }
                if (n_prime == 0)
                {
                    vertices[index1 + N].y = heightBuffer[1, index].x * sign;

                    vertices[index1 + N].x = originPosition[index1 + N].x + displacementBuffer[1, index].x * lambda * sign;
                    vertices[index1 + N].z = originPosition[index1 + N].z + displacementBuffer[1, index].z * lambda * sign;

                    normals[index1 + N].x = n.x;
                    normals[index1 + N].y = n.y;
                    normals[index1 + N].z = n.z;
                }
                if (m_prime == 0)
                {
                    vertices[index1 + Nplus1 * N].y = heightBuffer[1, index].x * sign;

                    vertices[index1 + Nplus1 * N].x = originPosition[index1 + Nplus1 * N].x + displacementBuffer[1, index].x * lambda * sign;
                    vertices[index1 + Nplus1 * N].z = originPosition[index1 + Nplus1 * N].z + displacementBuffer[1, index].z * lambda * sign;

                    normals[index1 + Nplus1 * N].x = n.x;
                    normals[index1 + Nplus1 * N].y = n.y;
                    normals[index1 + Nplus1 * N].z = n.z;
                }
            }
        }



    }


}