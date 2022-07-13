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
            po.OutputData(true);
        }
    }
}

public class PrecomputedOcean : MonoBehaviour
{
    public int meshSize = 32;
    public Material material;

    public float worldScale = 1;
    public int dataSize = 128;

    public TechType techType = TechType.BruteForce;
    public PhillipsWaveSpectrum spectrum;

    public bool multiMesh = false;
    public bool onlyYDisplace = true;

    Mesh mesh = null;
    Vector3[] vertices;
    Vector3[] originPosition;
    Vector3[] normals;
    Vector4[] heightBuffer;
    Vector4[] slopeBuffer, displacementBuffer;

    Texture2DArray displaceArray, normalArray, mixArray;

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

    void OutputDataToTexture(int frameId, int meshSize, Vector3[,] dataArray, Texture2D tex)
    {
        for (int i = 0; i < meshSize; i++)
        {
            for (int j = 0; j < meshSize; j++)
            {
                int idx = i * meshSize + j;
                Vector3 data = dataArray[frameId, idx];

                Color displace = new Color(data.x, data.y, data.z, 0.0f);
                tex.SetPixel(j, i, displace);
            }
        }
    }



    Vector2 GetCorrectUV(int frameId, Vector3[,] displaceData, Vector2 worldPos, float worldScale, int meshSize)
    {
        Vector2 result = worldPos;
        //return worldPos / worldScale;
        //fpi
        for(int i = 0; i < 6; i++)
        {
            Vector2 uv = result / worldScale / meshSize;
            Vector3 disp = BilinearWrapSample(frameId, displaceData, uv, meshSize);
            result = worldPos - new Vector2(disp.x, disp.z);
        } 

        return result / worldScale / meshSize;
    }

    Vector2 GetUVWithWrap(Vector2 uv, int meshSize)
    {
        while(uv.x < 0.0)
            uv.x += meshSize;
        while(uv.y < 0.0)
            uv.y += meshSize;
        return new Vector2(uv.x % meshSize, uv.y % meshSize);
    }

    Vector3 BilinearWrapSample(int frameId, Vector3[,] data, Vector2 uv, int meshSize)
    {
        Vector3 result;
        uv *= meshSize;
        uv = GetUVWithWrap(uv, meshSize);
        int x = (int)Math.Floor(uv.x);
        int y = (int)Math.Floor(uv.y);
        float xlerp = uv.x - (float)x;
        float ylerp = uv.y - (float)y;

        Vector3 data00 = data[frameId, x + y * meshSize];
        Vector3 data10 = data[frameId, (x + 1) % meshSize + y * meshSize];
        Vector3 data01 = data[frameId, x + ((y + 1) % meshSize) * meshSize];
        Vector3 data11 = data[frameId, ((x + 1) % meshSize) + ((y + 1) % meshSize) * meshSize];

        Vector3 data0 = data00 * (1.0f - ylerp) + data01 * ylerp;
        Vector3 data1 = data10 * (1.0f - ylerp) + data11 * ylerp;

        result = data0 * (1.0f - xlerp) + data1 * xlerp;
        return result;
    }

    void OutputDataToDataArray(float t, int frameId, int meshSize, ref Vector3[,] displaceData, ref Vector3[,] normalData)
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

                displaceData[frameId, j + i * meshSize] = data;

                float m = Math.Max(Math.Max(Math.Abs(data.x), Math.Abs(data.y)), Math.Abs(data.z));
                maxAbsDisplaceValue = Math.Max(maxAbsDisplaceValue, m);

                normalData[frameId, j + i * meshSize] = normals[idx];
            }
        }
        Debug.Log("maxAbsDisplaceValue: " + maxAbsDisplaceValue);


        if (onlyYDisplace)
        {
            for (int i = 0; i < meshSize; i++)
            {
                for (int j = 0; j < meshSize; j++)
                {
                    float x = j * worldScale;
                    float y = i * worldScale;
                    Vector2 worldPos = new Vector2(x, y);
                    Vector2 uv = GetCorrectUV(frameId, displaceData, worldPos, worldScale, meshSize);

                    Vector3 dispData = BilinearWrapSample(frameId, displaceData, uv, meshSize);
                    displaceData[frameId, j + i * meshSize] = new Vector3(0.0f, dispData.y, 0.0f);

                    Vector3 norData = BilinearWrapSample(frameId, normalData, uv, meshSize);
                    normalData[frameId, j + i * meshSize] = norData;
                }
            }
        }

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
                        for(int i = 0; i < dataSize; i++)
                        {
                            int preFrameId = (frameId - i + displaceData.GetLength(0)) % displaceData.GetLength(0);

                            float weight = (float)Math.Pow(0.02, i * timePerFrame);
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

    void WriteTextureArrayToFile(ref BinaryWriter writer, int size, Vector3[,] texArray)
    {
        for(int i = 0; i < texArray.GetLength(0); i++)
        {
            for(int y = 0; y < size; y++)
            {
                for(int x = 0; x < size; x++)
                {
                    int idx = x + y * size;
                    writer.Write(texArray[i, idx].x);
                    writer.Write(texArray[i, idx].y);
                    writer.Write(texArray[i, idx].z);
                    writer.Write(0.5f);
                }
            }
        }
    }

    void SaveAllDataToFile(string filePath, int size, Vector3[,] displaceArray, Vector3[,] normalArray, Vector3[,] foamArray)
    {
        var file = File.Open(filePath, FileMode.Create);
        var binary = new BinaryWriter(file);

        binary.Write(displaceArray.GetLength(0));
        binary.Write(size);
        
        WriteTextureArrayToFile(ref binary, size, displaceArray);
        WriteTextureArrayToFile(ref binary, size, normalArray);
        WriteTextureArrayToFile(ref binary, size, foamArray);
        file.Close();
    }

    public void OutputData(bool needSave = false)
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
            OutputDataToDataArray(i * spectrum.repeatTime / dataSize, i, meshSize, ref displaceData, ref normalData);

            OutputDataToTexture(i, meshSize, displaceData, displaceTextures[i]);
            OutputDataToTexture(i, meshSize, normalData, normalTextures[i]);

            if (needSave)
            {
                //SaveToExr(displaceTextures[i], "G://OutputData/displace_" + i + ".exr");
                //SaveToTga(normalTextures[i], "G://OutputData//normal_" + i + ".tga");
            }
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
        Vector3[,] mixData = new Vector3[dataSize, meshSize * meshSize];
        Texture2D[] mixTextures = new Texture2D[dataSize];
        CalcFoamData(worldScale, displaceData, out mixData);
        DataToTexture(meshSize, mixData, mixTextures);

        for (int i = 0; i < mixTextures.Length; i++)
        {
            //if(needSave)
                //SaveToTga(mixTextures[i], "G://OutputData/mix_" + i + ".tga");
        }

        mixArray = new Texture2DArray(mixTextures[0].width, mixTextures[0].height, mixTextures.Length, mixTextures[0].format, false);
        mixArray.wrapMode = TextureWrapMode.Repeat;
        mixArray.filterMode = FilterMode.Bilinear;
        for (int i = 0; i < mixTextures.Length; i++)
            mixArray.SetPixels(mixTextures[i].GetPixels(), i);

        mixArray.Apply(true);
        material.SetTexture("MixArray", mixArray);

        if(needSave)
            SaveAllDataToFile("G://result/PrecomputedOceanData.data", meshSize, displaceData, normalData, mixData);

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
                originPosition[idx] = new Vector3(worldScale * j, 0.0f, worldScale * i);
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
        if (multiMesh)
        {
            int meshCount = 8;
            for (int i = 0; i < meshCount; i++)
            {
                for (int j = 0; j < meshCount; j++)
                {
                    GameObject gameObject = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
                    gameObject.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
                    gameObject.GetComponent<MeshFilter>().mesh = mesh;
                    gameObject.GetComponent<MeshRenderer>().material = material;

                    gameObject.transform.position = new Vector3((j - meshCount / 2) * worldScale * meshSize, 0.0f, (i - meshCount / 2) * worldScale * meshSize);

                }
            }
        }
        else
        {
            GameObject gameObject = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
            gameObject.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
            gameObject.GetComponent<MeshFilter>().mesh = mesh;
            gameObject.GetComponent<MeshRenderer>().material = material;
        }
       

        //init ocean parameter
        UpdateOceanParameter(); 

        if(techType == TechType.Baked)
            OutputData(true);
        
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

        for (int m = 0; m < N; m++)
        {
            for (int n = 0; n < N; n++)
            {
                Vector2 k = spectrum.GetWaveVector(n, m);
                kx = k.x;
                kz = k.y;
                len = k.magnitude;

                index = m * N + n;

                Vector2 c = spectrum.GetWaveFactor(t, n, m);

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

        Vector3 norm;

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
                norm = new Vector3(-slopeBuffer[index].x, 1.0f, -slopeBuffer[index].z);
                norm.Normalize();

                normals[index1].x = norm.x;
                normals[index1].y = norm.y;
                normals[index1].z = norm.z;

                // for tiling
                if (n_prime == 0 && m_prime == 0)
                {
                    vertices[index1 + N + Nplus1 * N].y = heightBuffer[index].x;

                    vertices[index1 + N + Nplus1 * N].x = originPosition[index1 + N + Nplus1 * N].x + displacementBuffer[index].x * lambda;
                    vertices[index1 + N + Nplus1 * N].z = originPosition[index1 + N + Nplus1 * N].z + displacementBuffer[index].z * lambda;

                    normals[index1 + N + Nplus1 * N].x = norm.x;
                    normals[index1 + N + Nplus1 * N].y = norm.y;
                    normals[index1 + N + Nplus1 * N].z = norm.z;
                }
                if (n_prime == 0)
                {
                    vertices[index1 + N].y = heightBuffer[index].x;

                    vertices[index1 + N].x = originPosition[index1 + N].x + displacementBuffer[index].x * lambda;
                    vertices[index1 + N].z = originPosition[index1 + N].z + displacementBuffer[index].z * lambda;

                    normals[index1 + N].x = norm.x;
                    normals[index1 + N].y = norm.y;
                    normals[index1 + N].z = norm.z;
                }
                if (m_prime == 0)
                {
                    vertices[index1 + Nplus1 * N].y = heightBuffer[index].x;

                    vertices[index1 + Nplus1 * N].x = originPosition[index1 + Nplus1 * N].x + displacementBuffer[index].x * lambda;
                    vertices[index1 + Nplus1 * N].z = originPosition[index1 + Nplus1 * N].z + displacementBuffer[index].z * lambda;

                    normals[index1 + Nplus1 * N].x = norm.x;
                    normals[index1 + Nplus1 * N].y = norm.y;
                    normals[index1 + Nplus1 * N].z = norm.z;
                }
            }
        }

    }


}
