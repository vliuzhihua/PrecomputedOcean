using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public enum TechType
{
    BruteForce,
    FFT,
    Baked
};

[CustomEditor(typeof(PrecomputedOcean))]
public class PrecomputedOceanEditor : Editor 
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        PrecomputedOcean po = (PrecomputedOcean)target;
        if (GUILayout.Button("导出数据"))
        {
            //po
            po.OutputData();
        }
    }
}

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
    public float repeatTime = 200.0f;

    public int dataSize = 128;
    public TechType techType = TechType.BruteForce;

    Mesh mesh = null;
    Vector3[] vertices;
    Vector3[] originPosition;
    Vector3[] normals;
    Vector4[] heightBuffer;
    Vector4[] slopeBuffer, displacementBuffer;

    GameObject gameObject;

    volatile bool done = true;
    double lastUpdateTime = 0.0;
    double fps = -1.0;


    void OutputDataToTexture(float t, int meshSize, Texture2D displaceTexture, Texture2D normalTexture)
    {
        int meshSizePlus1 = meshSize + 1;
        int vertexCount = meshSizePlus1 * meshSizePlus1;
        Vector3[] tempVertices = new Vector3[vertexCount];
        Vector3[] tempNormals = new Vector3[vertexCount];

        EvaluateWavesFFT(t, meshSize, vertices, normals);
        
        for(int i = 0; i < meshSize; i++)
        {
            for(int j = 0; j < meshSize; j++)
            {
                int idx = i * meshSizePlus1 + j;
                Vector3 data = vertices[idx] - originPosition[idx];
                Color displace = new Color(data.x, data.y, data.z, 0.0f);
                displaceTexture.SetPixel(j, i, displace);
                Color normal = new Color(normals[idx].x, normals[idx].y, normals[idx].z, 0.0f);
                normalTexture.SetPixel(j, i, normal);
            }
        }
    }

    void OutputDataToDataArray(float t, int frameId, int meshSize, Vector3[,] displaceData, Vector3[,] normalData)
    {
        int meshSizePlus1 = meshSize + 1;
        int vertexCount = meshSizePlus1 * meshSizePlus1;
        Vector3[] tempVertices = new Vector3[vertexCount];
        Vector3[] tempNormals = new Vector3[vertexCount];

        EvaluateWavesFFT(t, meshSize, vertices, normals);

        for (int i = 0; i < meshSize; i++)
        {
            for (int j = 0; j < meshSize; j++)
            {
                int idx = i * meshSizePlus1 + j;
                Vector3 data = vertices[idx] - originPosition[idx];
                Color displace = new Color(data.x, data.y, data.z, 0.0f);
                displaceData[frameId, j + i * meshSize] = data;

                Color normal = new Color(normals[idx].x, normals[idx].y, normals[idx].z, 0.0f);
                normalData[frameId, j + i * meshSize] = normals[idx];
            }
        }
    }

    void SaveToTga(Texture2D texture, string path)
    {
        var bytes = texture.EncodeToTGA();
        var file = File.Open(path, FileMode.Create);
        var binary = new BinaryWriter(file);
        binary.Write(bytes);
        file.Close();
    }

    public void OutputData()
    {
        //print("click output data");
        
        Texture2D[] normalTextures = new Texture2D[dataSize];
        Texture2D[] displaceTextures = new Texture2D[dataSize];

        Vector3[,] normalData = new Vector3[dataSize, meshSize * meshSize];
        Vector3[,] displaceData = new Vector3[dataSize, meshSize * meshSize];

        for (int i = 0; i < normalTextures.Length; i++)
        {
            displaceTextures[i] = new Texture2D(meshSize, meshSize, TextureFormat.RGBAHalf, false);
            normalTextures[i] = new Texture2D(meshSize, meshSize, TextureFormat.RGBAHalf, false);
            OutputDataToTexture(i * repeatTime / dataSize, meshSize, displaceTextures[i], normalTextures[i]);
            //SaveToTga(displaceTextures[i], "Assets/OutputData/displace_" + i + ".tga");
            //SaveToTga(normalTextures[i], "Assets/OutputData/normal_" + i + ".tga");
        }

      
        Texture2DArray displaceArray = new Texture2DArray(normalTextures[0].width, normalTextures[0].height, normalTextures.Length, normalTextures[0].format, false);
        displaceArray.wrapMode = TextureWrapMode.Repeat;
        displaceArray.filterMode = FilterMode.Bilinear;
        for (int i = 0; i < displaceTextures.Length; i++)
            displaceArray.SetPixels(displaceTextures[i].GetPixels(), i);

        displaceArray.Apply(true);
        material.SetTexture("DisplaceArray", displaceArray);

        Texture2DArray normalArray = new Texture2DArray(normalTextures[0].width, normalTextures[0].height, normalTextures.Length, normalTextures[0].format, false);
        normalArray.wrapMode = TextureWrapMode.Repeat;
        normalArray.filterMode = FilterMode.Bilinear;
        for (int i = 0; i < normalTextures.Length; i++)
            normalArray.SetPixels(normalTextures[i].GetPixels(), i);

        normalArray.Apply(true);
        material.SetTexture("NormalArray", normalArray);
    }

    // Start is called before the first frame update
    void Start()
    {
        int meshSizePlus1 = meshSize + 1;
        int vertexCount = meshSizePlus1 * meshSizePlus1;
        int indexCount = meshSize * meshSize * 2 * 3;
        vertices = new Vector3[vertexCount];
        originPosition = new Vector3[vertexCount];
        normals = new Vector3[vertexCount];
        Vector2[] uv = new Vector2[vertexCount];
        int[] triangles = new int[indexCount];

        //声明顶点的位置
        for (int i = 0; i < meshSizePlus1; i++)
        {
            for (int j = 0; j < meshSizePlus1; j++)
            {
                int idx = i * meshSizePlus1 + j;
                originPosition[idx] = new Vector3(worldScale / meshSize * j, 0.0f, worldScale / meshSize * i);
                uv[idx] = new Vector2(1.0f / meshSize * j, 1.0f / meshSize * i);
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
        //将设置好的参数进行赋值
        mesh.vertices = originPosition;
        mesh.uv = uv;
        mesh.triangles = triangles;
        gameObject = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
        gameObject.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
        gameObject.GetComponent<MeshFilter>().mesh = mesh;
        gameObject.GetComponent<MeshRenderer>().material = material;

        //init ocean parameter

        UpdateOceanParameter(); 

        if(techType == TechType.Baked)
            OutputData();
        
        //var array = AssetBundle.LoadFromFile("Assets/TextureArray.asset").LoadAsset<Texture2DArray>("Assets/TextureArray.asset");
        //var array = Resources.Load("Assets/TextureArray.asset") as Texture2DArray; 
        //material.SetTexture("DisplaceArray", array);
    }


    void UpdateOceanParameter()
    {
        UnityEngine.Random.InitState(0);

        int meshSizePlus1 = meshSize + 1;

        spectrum = new Vector2[meshSizePlus1 * meshSizePlus1];
        spectrumConj = new Vector2[meshSizePlus1 * meshSizePlus1];
        dispersionTable = new float[meshSizePlus1 * meshSizePlus1];

        heightBuffer = new Vector4[meshSize * meshSize];
        slopeBuffer = new Vector4[meshSize * meshSize];
        displacementBuffer = new Vector4[meshSize * meshSize];

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
        
        material.SetInt("RepeatTime", (int)repeatTime);
        material.SetInt("LoopTime", (int)dataSize);
    }

    float Dispersion(int n_prime, int m_prime)
    {
        float w_0 = 2.0f * Mathf.PI / repeatTime;
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

        //return waveAmp * Mathf.Exp(-1.0f / (k_length2 * L2)) / k_length4 * k_dot_w2 * Mathf.Exp(-k_length2 * l2);
        return waveAmp * Mathf.Exp(-1.0f / (k_length2 * L2)) / k_length4 * Mathf.Exp(-k_length2 * l2);
    }


    Vector2 GetSpectrum(int n_prime, int m_prime)
    {
        Vector2 r = GaussianRandomVariable();
        return r * Mathf.Sqrt(PhillipsSpectrum(n_prime, m_prime) / 2.0f);
    }

    void OutputDebugInfo()
    {

        double elapsedTime = Time.realtimeSinceStartup - lastUpdateTime;
        lastUpdateTime = Time.realtimeSinceStartup;
        double thisFps = 1.0 / elapsedTime;
        fps = fps * 0.5 + thisFps * 0.5;

        if(Time.frameCount % 100 != 0)
            return;
        Debug.Log("fps is " +  fps);
    }

    // Update is called once per frame
    void Update()
    {
        //If still running return.
        if (!done) return;

        OutputDebugInfo();

        UpdateOceanParameter();
        //Set data generated form last calculations.

        if (techType == TechType.Baked)
        {
            mesh.vertices = originPosition;
            material.SetInt("UseBake", 1);
        }
        else
        {
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            material.SetInt("UseBake", 0);
        }

        //print("render ocean once....");

        //Start new calculations for time period t.
        done = false;

        Nullable<float> time = Time.realtimeSinceStartup;

        ThreadPool.QueueUserWorkItem(new WaitCallback(RunThreaded), time);

    }

    void RunThreaded(object o)
    {

        Nullable<float> time = o as Nullable<float>;

        EvaluateWavesFFT(time.Value, meshSize, vertices, normals);

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
    void EvaluateWavesFFT(float t, int meshSize, Vector3[] vertices, Vector3[] normals)
    {

        Solver solver;
        if(techType == TechType.BruteForce)
            solver = new FourierBruteForce(meshSize);
        else
            solver = new FourierFFT(meshSize);

        int N = meshSize;
        int Nplus1 = N + 1;

        float kx, kz, len, lambda = -1.0f;
        int index, index1;

        Vector4[] inputHeightBuffer = new Vector4[heightBuffer.Length];
        Vector4[] inputSlopeBuffer = new Vector4[slopeBuffer.Length];
        Vector4[] inputDisplacementBuffer = new Vector4[displacementBuffer.Length];

        for (int m_prime = 0; m_prime < N; m_prime++)
        {
            kz = Mathf.PI * (2.0f * m_prime - N) / worldScale;

            for (int n_prime = 0; n_prime < N; n_prime++)
            {
                kx = Mathf.PI * (2 * n_prime - N) / worldScale;
                len = Mathf.Sqrt(kx * kx + kz * kz);
                index = m_prime * N + n_prime;

                Vector2 c = InitSpectrum(t, n_prime, m_prime);

                inputHeightBuffer[index].x = c.x;
                inputHeightBuffer[index].y = c.y;

                inputSlopeBuffer[index].x = -c.y * kx;
                inputSlopeBuffer[index].y = c.x * kx;

                inputSlopeBuffer[index].z = -c.y * kz;
                inputSlopeBuffer[index].w = c.x * kz;

                if (len < 0.000001f)
                {
                    inputDisplacementBuffer[index].x = 0.0f;
                    inputDisplacementBuffer[index].y = 0.0f;
                    inputDisplacementBuffer[index].z = 0.0f;
                    inputDisplacementBuffer[index].w = 0.0f;
                }
                else
                {
                    inputDisplacementBuffer[index].x = -c.y * -(kx / len);
                    inputDisplacementBuffer[index].y = c.x * -(kx / len);
                    inputDisplacementBuffer[index].z = -c.y * -(kz / len);
                    inputDisplacementBuffer[index].w = c.x * -(kz / len);
                }
            }
        }

        solver.Peform(inputHeightBuffer, ref heightBuffer);
        solver.Peform(inputSlopeBuffer, ref slopeBuffer);
        solver.Peform(inputDisplacementBuffer, ref displacementBuffer);

        Vector3 n;

        for (int m_prime = 0; m_prime < N; m_prime++)
        {
            for (int n_prime = 0; n_prime < N; n_prime++)
            {
                index = m_prime * N + n_prime;          // index into buffers
                index1 = m_prime * Nplus1 + n_prime;    // index into vertices

                // height
                vertices[index1].y = heightBuffer[index].x;
                if(heightBuffer[index].x > 1.0)
                    vertices[index1].y *= 1.0f;

                // displacement
                vertices[index1].x = originPosition[index1].x + displacementBuffer[index].x * lambda;
                vertices[index1].z = originPosition[index1].z + displacementBuffer[index].z * lambda;

                // normal
                n = new Vector3(-slopeBuffer[index].x, 1.0f, -slopeBuffer[index].z);
                n.Normalize();

                normals[index1].x = n.x;
                normals[index1].y = n.y;
                normals[index1].z = n.z;

                // for tiling
                if (n_prime == 0 && m_prime == 0)
                {
                    vertices[index1 + N + Nplus1 * N].y = heightBuffer[index].x;

                    vertices[index1 + N + Nplus1 * N].x = originPosition[index1 + N + Nplus1 * N].x + displacementBuffer[index].x * lambda;
                    vertices[index1 + N + Nplus1 * N].z = originPosition[index1 + N + Nplus1 * N].z + displacementBuffer[index].z * lambda;

                    normals[index1 + N + Nplus1 * N].x = n.x;
                    normals[index1 + N + Nplus1 * N].y = n.y;
                    normals[index1 + N + Nplus1 * N].z = n.z;
                }
                if (n_prime == 0)
                {
                    vertices[index1 + N].y = heightBuffer[index].x;

                    vertices[index1 + N].x = originPosition[index1 + N].x + displacementBuffer[index].x * lambda;
                    vertices[index1 + N].z = originPosition[index1 + N].z + displacementBuffer[index].z * lambda;

                    normals[index1 + N].x = n.x;
                    normals[index1 + N].y = n.y;
                    normals[index1 + N].z = n.z;
                }
                if (m_prime == 0)
                {
                    vertices[index1 + Nplus1 * N].y = heightBuffer[index].x;

                    vertices[index1 + Nplus1 * N].x = originPosition[index1 + Nplus1 * N].x + displacementBuffer[index].x * lambda;
                    vertices[index1 + Nplus1 * N].z = originPosition[index1 + Nplus1 * N].z + displacementBuffer[index].z * lambda;

                    normals[index1 + Nplus1 * N].x = n.x;
                    normals[index1 + Nplus1 * N].y = n.y;
                    normals[index1 + Nplus1 * N].z = n.z;
                }
            }
        }

        void CalcFoamData(Vector3[,] displaceData, out Vector3[,] foamData)
        {
            foamData = new Vector3[displaceData.GetLength(0), displaceData.GetLength(1)];

            for(int frameId = 0; frameId < displaceData.GetLength(0); frameId++)
            {
                for(int x = 0; x < meshSize; x++)
                {
                    for(int z = 0; z < meshSize; z++)
                    {
                    }
                }
            }
        }



    }


}
