using UnityEngine;
using UnityEditor;


[CreateAssetMenu(fileName = "OceanWaves", menuName = "PrecomputedOcean/Ocean Phillips Wave Spectrum")]
public class PhillipsWaveSpectrum: ScriptableObject
{

    const float GRAVITY = 9.81f;
    public Vector2 windSpeed = new Vector2(32.0f, 32.0f);
    Vector2 windDirection;
    public float waveAmp = 0.0002f;

    public float repeatTime = 200.0f;

    Vector2[] spectrum, spectrumConj;
    float[] dispersionTable;

    int meshSize = 32;
    float worldScale = 32.0f;

    public PhillipsWaveSpectrum()
    {
        
    }


    public void Init(int size, float scale)
    {
        UnityEngine.Random.InitState(0);

        meshSize = size;
        worldScale = scale;

        int meshSizePlus1 = meshSize + 1;


        spectrum = new Vector2[meshSizePlus1 * meshSizePlus1];
        spectrumConj = new Vector2[meshSizePlus1 * meshSizePlus1];
        dispersionTable = new float[meshSizePlus1 * meshSizePlus1];

        

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
        float k_dot_w2 = k_dot_w * k_dot_w;// * k_dot_w * k_dot_w * k_dot_w * k_dot_w;

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


    public Vector2 GetWaveFactor(float t, int x, int y)
    {
        int meshSizePlus1 = meshSize + 1;
        int index = y * meshSizePlus1 + x;

        float omegat = dispersionTable[index] * t;

        float cos = Mathf.Cos(omegat);
        float sin = Mathf.Sin(omegat);

        float c0a = spectrum[index].x * cos - spectrum[index].y * sin;
        float c0b = spectrum[index].x * sin + spectrum[index].y * cos;

        float c1a = spectrumConj[index].x * cos - spectrumConj[index].y * -sin;
        float c1b = spectrumConj[index].x * -sin + spectrumConj[index].y * cos;

        return new Vector2(c0a + c1a, c0b + c1b);
    }
}