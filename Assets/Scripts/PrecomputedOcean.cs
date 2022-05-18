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

    public float worldScale = 64;
    public int dataSize = 128;

    public TechType techType = TechType.BruteForce;

    public PhillipsWaveSpectrum spectrum;

    Mesh mesh = null;
    Vector3[] vertices;
    Vector3[] originPosition;
    Vector3[] normals;
    Vector4[] heightBuffer;
    Vector4[] slopeBuffer, displacementBuffer;

    Texture2DArray displaceArray, normalArray, mixArray;

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
                
                //data = data / 2.0f;
                //data.x = Math.Min(Math.Max(-1.0f, data.x), 1.0f);
                //data.y = Math.Min(Math.Max(-1.0f, data.y), 1.0f);
                //data.z = Math.Min(Math.Max(-1.0f, data.z), 1.0f);

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

        float maxAbsDisplaceValue = 0.0f;

        for (int i = 0; i < meshSize; i++)
        {
            for (int j = 0; j < meshSize; j++)
            {
                int idx = i * meshSizePlus1 + j;
                Vector3 data = vertices[idx] - originPosition[idx];
                Color displace = new Color(data.x, data.y, data.z, 0.0f);
                displaceData[frameId, j + i * meshSize] = data;

                float m = Math.Max(Math.Max(Math.Abs(data.x), Math.Abs(data.y)), Math.Abs(data.z));
                maxAbsDisplaceValue = Math.Max(maxAbsDisplaceValue, m);
                

                Color normal = new Color(normals[idx].x, normals[idx].y, normals[idx].z, 0.0f);
                normalData[frameId, j + i * meshSize] = normals[idx];
            }
        }

        Debug.Log("maxAbsDisplaceValue: " + maxAbsDisplaceValue);
    }

    void DataToTexture(int meshSize, Vector3[,] datas, Texture2D[] textures)
    {
        for(int frame = 0 ; frame < textures.Length; frame++)
        {
            textures[frame] = new Texture2D(meshSize, meshSize, TextureFormat.RGBA32, false);
            for (int i = 0; i < meshSize; i++)
            {
                for (int j = 0; j < meshSize; j++)
                {
                    int idx = i * meshSize + j;
                    Vector3 data = datas[frame, idx];
                    Color value = new Color(data.x, data.y, data.z, 0.0f);
                    textures[frame].SetPixel(j, i, value);
                }
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

    void SaveToExr(Texture2D texture, string path)
    {
        var bytes = texture.EncodeToEXR();
        var file = File.Open(path, FileMode.Create);
        var binary = new BinaryWriter(file);
        binary.Write(bytes);
        file.Close();
    }

    void CalcFoamData(float cellWorldSize, Vector3[,] displaceData, out Vector3[,] foamData)
    {
        foamData = new Vector3[displaceData.GetLength(0), displaceData.GetLength(1)];
        
        Vector3[,] tempFoamData = new Vector3[displaceData.GetLength(0), displaceData.GetLength(1)];
        for (int frameId = 0; frameId < displaceData.GetLength(0); frameId++)
        {
            for (int y = 0; y < meshSize; y++)
            {
                for (int x = 0; x < meshSize; x++)
                {
                    int idx00 = x + y * meshSize;
                    int idx10 = (x + 1) % meshSize + y * meshSize;
                    int idx01 = x + ((y + 1) % meshSize) * meshSize;
                    Vector2 disp00 = new Vector2(displaceData[frameId, idx00].x, displaceData[frameId, idx00].z);
                    Vector2 disp10 = new Vector2(displaceData[frameId, idx10].x, displaceData[frameId, idx10].z) + new Vector2(cellWorldSize, 0.0f);
                    Vector2 disp01 = new Vector2(displaceData[frameId, idx01].x, displaceData[frameId, idx01].z) + new Vector2(0.0f, cellWorldSize);
                    Vector2 dx = disp10 - disp00;
                    Vector2 dz = disp01 - disp00;
                    float det = (dx.x * dz.y - dx.y * dz.x) / (cellWorldSize * cellWorldSize);

                    float foamValue = Math.Max(-det + 1.0f, 0.0f);

                    //foamValue = 1.0f;

                    tempFoamData[frameId, idx00] = new Vector3(foamValue, foamValue, foamValue);
                }
            }
        }

        //direction blend
        //for (int frameId = 0; frameId < displaceData.GetLength(0); frameId++)
        //{
        //    for (int y = 0; y < meshSize; y++)
        //    {
        //        for (int x = 0; x < meshSize; x++)
        //        {
        //            int idx = x + y * meshSize;
        //            int preFrameId = (frameId - 1 + displaceData.GetLength(0)) % displaceData.GetLength(0);

        //            float lerpFactor = 0.7f;
        //            foamData[frameId, idx] = foamData[preFrameId, idx] * 0.8f + foamData[frameId, idx] * lerpFactor;
        //        }
        //    }
        //}
        Func<Vector3[,], Vector3[,], int> LerpDataFunc = (v1, v2) =>
        {
            //spectrum.repeatTime
            int framePerSecond = (int)(dataSize / spectrum.repeatTime);
            
            float timePerFrame = spectrum.repeatTime / dataSize;

            //lerp between frame
            for (int frameId = 0; frameId < displaceData.GetLength(0); frameId++)
            {
                for (int y = 0; y < meshSize; y++)
                {
                    for (int x = 0; x < meshSize; x++)
                    {
                
                        int idx = x + y * meshSize;
                        v1[frameId, idx] = new Vector3(0.0f, 0.0f, 0.0f);
                        int peekFrameCount = 20;
                        float weightSum = 0.0f;
                        for(int i = 0; i < dataSize - 1; i++)
                        {
                            int preFrameId = (frameId - i + displaceData.GetLength(0)) % displaceData.GetLength(0);

                            float weight = (float)Math.Pow(0.01, i * timePerFrame);
                            v1[frameId, idx] += v2[preFrameId, idx] * weight;// * (1.0f - i / 6.0f);//Math.Pow()
                            weightSum += weight;
                        }

                        v1[frameId, idx] /= weightSum;

                    }
                }
            }
            return 0; 
        };

        //lerp between frame
        LerpDataFunc(foamData, tempFoamData);
        //LerpDataFunc(tempFoamData, foamData);
        //LerpDataFunc(foamData, tempFoamData);
        
    }

    void WriteTextureArrayToFile(ref BinaryWriter writer, Vector3[,] texArray)
    {
        for(int i = 0; i < texArray.Length; i++)
        {
            //writer.Write(texArray[i].GetPixels32(0));
            //writer.Write(texArray[i].GetPixelData<Byte>(0));
        }
    }

    void SaveAllDataToFile(string filePath, Vector3[,] displaceArray, Vector3[,] normalArray, Vector3[,] foamArray)
    {
        var file = File.Open(filePath, FileMode.Create);
        var binary = new BinaryWriter(file);
        int[] da = new int[] {displaceArray.GetLength(0)};
        binary.Write(displaceArray.Length);
        //for()
        //Buffer.BlockCopy()
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
            OutputDataToTexture(i * spectrum.repeatTime / dataSize, meshSize, displaceTextures[i], normalTextures[i]);
            OutputDataToDataArray(i * spectrum.repeatTime / dataSize, i, meshSize, displaceData, normalData);
            SaveToExr(displaceTextures[i], "G://OutputData/displace_" + i + ".exr");
            SaveToTga(normalTextures[i], "G://OutputData//normal_" + i + ".tga");
        }


        //displace array      
        displaceArray = new Texture2DArray(normalTextures[0].width, normalTextures[0].height, normalTextures.Length, normalTextures[0].format, false);
        displaceArray.wrapMode = TextureWrapMode.Repeat;
        displaceArray.filterMode = FilterMode.Bilinear;
        for (int i = 0; i < displaceTextures.Length; i++)
            displaceArray.SetPixels(displaceTextures[i].GetPixels(), i);

        displaceArray.Apply(true);
        material.SetTexture("DisplaceArray", displaceArray);

        //normal array
        normalArray = new Texture2DArray(normalTextures[0].width, normalTextures[0].height, normalTextures.Length, normalTextures[0].format, false);
        normalArray.wrapMode = TextureWrapMode.Repeat;
        normalArray.filterMode = FilterMode.Bilinear;
        for (int i = 0; i < normalTextures.Length; i++)
            normalArray.SetPixels(normalTextures[i].GetPixels(), i);

        normalArray.Apply(true);
        material.SetTexture("NormalArray", normalArray);

        //foam array
        Vector3[,] foamData = new Vector3[dataSize, meshSize * meshSize];
        Texture2D[] mixTextures = new Texture2D[dataSize];
        CalcFoamData(worldScale / meshSize, displaceData, out foamData);
        DataToTexture(meshSize, foamData, mixTextures);

        for (int i = 0; i < mixTextures.Length; i++)
        {
            SaveToTga(mixTextures[i], "G://OutputData/mix_" + i + ".tga");
        }

        mixArray = new Texture2DArray(mixTextures[0].width, mixTextures[0].height, mixTextures.Length, mixTextures[0].format, false);
        mixArray.wrapMode = TextureWrapMode.Repeat;
        mixArray.filterMode = FilterMode.Bilinear;
        for (int i = 0; i < mixTextures.Length; i++)
            mixArray.SetPixels(mixTextures[i].GetPixels(), i);

        mixArray.Apply(true);
        material.SetTexture("MixArray", mixArray);



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
        spectrum.Init(meshSize, worldScale);

        heightBuffer = new Vector4[meshSize * meshSize];
        slopeBuffer = new Vector4[meshSize * meshSize];
        displacementBuffer = new Vector4[meshSize * meshSize];

        material.SetInt("RepeatTime", (int)spectrum.repeatTime);
        material.SetInt("LoopTime", (int)dataSize);

        if(displaceArray)
            material.SetTexture("DisplaceArray", displaceArray);
        if(normalArray)
            material.SetTexture("NormalArray", normalArray);
        if(mixArray)
            material.SetTexture("MixArray", mixArray);
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

                Vector2 c = spectrum.GetWaveFactor(t, n_prime, m_prime);

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

    }


}
