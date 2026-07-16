using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

public class WaterManager : MonoBehaviour
{
    static readonly int WaterSimulationID = Shader.PropertyToID("_WaterSimulation");
    public ComputeShader waterCompute;
    public Texture2D waterHeightTexture;
    public Texture2D waterHeightTexture2;
    public Material waterMaterial;
    public float textureScale = 10.0f;

    RenderTexture inputSimTexture;
    RenderTexture outputSimTexture;
    RenderTexture DisplacementTexture;

    ComputeBuffer disturbanceBuffer;
    ComputeBuffer activeIndicesBuffer;
    List<Disturbances> disturbances = new();
    List<int> activeIndices = new List<int>(64);
    int displacementKernel, simulationKernel;
    int width = 512;
    int height = 512;
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct Disturbances
    {
        public Vector2 location;
        public float horizontalRadius;
        public float verticalRadius;
    }
    private void Start()
    {
        if (waterHeightTexture == null)
        {
            Debug.LogError("Water height texture not assigned.");
            return;
        }

        // Verify texture properties
        Debug.Log($"WaterHeightTexture Info - Format: {waterHeightTexture.format}, Size: {waterHeightTexture.width}x{waterHeightTexture.height}");

        // Verify it's readable
        if ((waterHeightTexture.format != TextureFormat.RGB24 &&
             waterHeightTexture.format != TextureFormat.RGBA32 &&
             waterHeightTexture.format != TextureFormat.RHalf &&
             waterHeightTexture.format != TextureFormat.RFloat) &&
            !waterHeightTexture.isReadable)
        {
            Debug.LogWarning("WaterHeightTexture may not be readable. Consider enabling Read/Write in import settings.");
        }

        inputSimTexture = CreateSimulationTexture(width, height);
        outputSimTexture = CreateSimulationTexture(width, height);
        DisplacementTexture = CreateSimulationTexture(width, height);

        Graphics.Blit(Texture2D.blackTexture, inputSimTexture);
        Graphics.Blit(Texture2D.blackTexture, outputSimTexture);
        Graphics.Blit(Texture2D.blackTexture, DisplacementTexture);

        displacementKernel = waterCompute.FindKernel("UpdateDisplacement");
        simulationKernel = waterCompute.FindKernel("UpdateSimulation");

        // Set sampler state for proper texture sampling
        waterCompute.SetTexture(displacementKernel, "WaterHeightTexture", waterHeightTexture);
        waterCompute.SetTexture(displacementKernel, "WaterHeightTexture2", waterHeightTexture2);

        waterCompute.SetTexture(displacementKernel, "PrevSimulationTexture", inputSimTexture);
        waterCompute.SetTexture(displacementKernel, "DisplacementOutput", DisplacementTexture);

        waterCompute.SetTexture(simulationKernel, "WaterHeightTexture", waterHeightTexture);
        waterCompute.SetTexture(simulationKernel, "PrevSimulationTexture", inputSimTexture);
        waterCompute.SetTexture(simulationKernel, "DisplacementOutput", DisplacementTexture);

        waterCompute.SetTexture(simulationKernel, "CurrentSimulation", outputSimTexture);

        waterMaterial.SetTexture(WaterSimulationID, inputSimTexture);

        CreateDisturbanceBuffers();
        waterCompute.SetFloat("SimulationSpeed", 10f);

        waterCompute.SetInt("OutputWidth", width);
        waterCompute.SetInt("OutputHeight", height);
        waterCompute.SetFloat("HorizonalTilingMulti", width/waterHeightTexture.width);
        waterCompute.SetFloat("VerticalTilingMulti", height/waterHeightTexture.height);

    }
    private void Update()
    {
        RenderLoop();

    }
    void RenderLoop()
    {

        waterCompute.SetFloat("Time", Time.time);
        waterCompute.SetFloat("DeltaTime", Time.deltaTime);

        //Displacment Pass
        UpdateDisturbanceBuffers();

        waterCompute.Dispatch(displacementKernel, Mathf.CeilToInt(width / 8.0f), Mathf.CeilToInt(height / 8.0f), 1);

        //Simulation Pass
        waterCompute.Dispatch(simulationKernel, Mathf.CeilToInt(width / 8.0f), Mathf.CeilToInt(height / 8.0f), 1);

        

        //Swap textures
        /*
        RenderTexture temp = inputSimTexture;
        inputSimTexture = outputSimTexture;
        outputSimTexture = temp;

        waterCompute.SetTexture(displacementKernel, "PrevSimulationTexture", outputSimTexture);
        waterCompute.SetTexture(simulationKernel, "PrevSimulationTexture", outputSimTexture);
        */


        waterMaterial.SetTexture(WaterSimulationID, outputSimTexture);
        inputSimTexture = outputSimTexture;
        waterCompute.SetTexture(displacementKernel, "PrevSimulationTexture", inputSimTexture);
        waterCompute.SetTexture(simulationKernel, "PrevSimulationTexture", inputSimTexture);

        if(Input.GetKeyDown(KeyCode.Space))
        {
            TakeShaderSnapshot(inputSimTexture);
        }

    }

    //Stores the output texture of the current frame in a png file
    private void TakeShaderSnapshot(RenderTexture texture)
    {
        // Create a temporary Texture2D to read the RenderTexture data
        Texture2D snapshot = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        
        // Save the active RenderTexture and switch to the target texture
        RenderTexture prevRT = RenderTexture.active;
        RenderTexture.active = texture;
        
        // Read pixels from the RenderTexture into the Texture2D
        snapshot.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        snapshot.Apply();
        
        // Restore the previous RenderTexture
        RenderTexture.active = prevRT;
        
        // Encode the Texture2D to PNG
        byte[] pngData = snapshot.EncodeToPNG();
        
        // Create a filename with timestamp
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string filePath = System.IO.Path.Combine(Application.persistentDataPath, $"water_snapshot_{timestamp}.png");
        
        // Write the PNG to disk
        System.IO.File.WriteAllBytes(filePath, pngData);
        
        Debug.Log($"Water simulation snapshot saved to: {filePath}");
        
        // Clean up
        Destroy(snapshot);
    }

    RenderTexture CreateSimulationTexture(int width, int height)
    {
        RenderTexture tex = new RenderTexture(
            width,
            height,
            0,
            RenderTextureFormat.ARGBHalf
        );

        tex.enableRandomWrite = true;
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Create();

        return tex;
    }

    RenderTexture CreateSimulationTexture(int width, int height, Color color)
    {
        RenderTexture tex = new RenderTexture(
            width,
            height,
            0,
            RenderTextureFormat.ARGBHalf
        );

        tex.enableRandomWrite = true;
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Create();

        // Fill texture with the specified color
        Texture2D colorTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        colorTexture.SetPixel(0, 0, color);
        colorTexture.Apply();

        Graphics.Blit(colorTexture, tex);

        Destroy(colorTexture);

        return tex;
    }
    void CreateDisturbanceBuffers()
    {
        disturbanceBuffer = new ComputeBuffer(64, sizeof(float) * 4);

        activeIndicesBuffer = new ComputeBuffer(64, sizeof(int));


        waterCompute.SetBuffer(displacementKernel, "disturbances", disturbanceBuffer);

        waterCompute.SetBuffer(
            displacementKernel, "activeIndices", activeIndicesBuffer);
    }


    void UpdateDisturbanceBuffers()
    {
        if (disturbances.Count > 0)
            disturbanceBuffer.SetData(disturbances.ToArray());

        if (activeIndices.Count > 0)
            activeIndicesBuffer.SetData(activeIndices);

        waterCompute.SetInt("activeIndicesCount", activeIndices.Count);
    }


    public int AddDisturbance(Vector2 position, float horizontalRadius, float verticalRadius)
    {
        if (disturbances.Count >= 64)
            return -1;

        int id = disturbances.Count;

        disturbances.Add(new Disturbances()
        {
            location = position,
            horizontalRadius = horizontalRadius,
            verticalRadius = verticalRadius
        });

        activeIndices.Add(id);

        return id;
    }

    public void ModifyDisturbance(int id, Vector2 position, float horizontalRadius, float verticalRadius)
    {
        disturbances[id] = new Disturbances()
        {
            location = position,
            horizontalRadius = horizontalRadius,
            verticalRadius = verticalRadius
        };
    }

    public void RemoveDisturbance(int id)
    {
        int index = activeIndices.IndexOf(id);

        if (index >= 0)
        {
            activeIndices.RemoveAt(index);
        }
    }

    private void OnDestroy()
    {
        disturbanceBuffer?.Release();
        activeIndicesBuffer?.Release();

        inputSimTexture?.Release();
        outputSimTexture?.Release();
        DisplacementTexture?.Release();
    }
}
