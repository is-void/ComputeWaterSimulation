using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

public class WaterManager : MonoBehaviour
{

    Transform camTransform;

    static readonly int WaterSimulationID = Shader.PropertyToID("_WaterSimulation");
    public ComputeShader waterCompute;
    public Texture2D waterHeightTexture;
    public Texture2D waterHeightTexture2;
    public Material waterMaterial;

    public int size = 4000;
    public float simDistance = 300;
    public float textureScale = 1.0f;

    RenderTexture inputSimTexture;
    RenderTexture outputSimTexture;
    RenderTexture DisplacementTexture;

    ComputeBuffer disturbanceBuffer;
    ComputeBuffer activeIndicesBuffer;
    List<Disturbances> disturbances = new();
    List<int> activeIndices = new List<int>(64);
    int displacementKernel, simulationKernel;
    int playerDisturbanceID;
    WaterMesh mesh; 



    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct Disturbances
    {
        public Vector2 location;
        public float horizontalRadius;
        public float verticalRadius;
    }
    private void Start()
    {
        camTransform = Camera.main.transform;
        
        mesh = GetComponent<WaterMesh>();
        mesh.Create();

        float ratio = mesh.waterSize / size;
        playerDisturbanceID = AddDisturbance(WorldPositionToShaderCoords(camTransform.position), 15, 15);

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

        inputSimTexture = CreateSimulationTexture(size, size);
        outputSimTexture = CreateSimulationTexture(size, size);
        DisplacementTexture = CreateSimulationTexture(size, size);

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

        waterCompute.SetInt("OutputWidth", size);
        waterCompute.SetInt("OutputHeight", size);
        waterCompute.SetFloat("HorizonalTilingMulti", textureScale * (size/waterHeightTexture.width));
        waterCompute.SetFloat("VerticalTilingMulti", textureScale * (size/ waterHeightTexture.height));
        waterCompute.SetInt("simulationDistancePow", Mathf.RoundToInt(Mathf.Pow(simDistance * ratio, 2))); 
    }
    private void Update()
    {
        ModifyDisturbance(playerDisturbanceID, WorldPositionToShaderCoords(camTransform.position), 15, 15);
        RenderLoop();

    }
    void RenderLoop()
    {
        Vector2Int pLoc = WorldPositionToShaderCoords(camTransform.position);

        waterCompute.SetFloat("Time", Time.time);
        waterCompute.SetFloat("DeltaTime", Time.deltaTime);
        waterCompute.SetFloats("PlayerLocation", pLoc.x, pLoc.y);

        //Displacment Pass
        UpdateDisturbanceBuffers();

        waterCompute.Dispatch(displacementKernel, Mathf.CeilToInt(size / 8.0f), Mathf.CeilToInt(size / 8.0f), 1);

        //Simulation Pass
        waterCompute.Dispatch(simulationKernel, Mathf.CeilToInt(size / 8.0f), Mathf.CeilToInt(size / 8.0f), 1);

        

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

    public Vector2Int WorldPositionToShaderCoords(Vector3 worldPosition)
    {
        // Get the water mesh's world position and bounds
        Vector3 meshPosition = mesh.transform.position;
        Vector3 meshScale = mesh.transform.localScale;

        // Calculate offset from mesh origin
        Vector3 localPosition = worldPosition - meshPosition;

        // Normalize to mesh bounds (assuming flat mesh with known size)
        float meshWidth = mesh.waterSize * meshScale.x;
        float meshHeight = mesh.waterSize * meshScale.z;

        // Calculate normalized coordinates (0 to 1) relative to mesh
        float normalizedX = localPosition.x / meshWidth;
        float normalizedZ = localPosition.z / meshHeight;

        // Clamp to valid range
        normalizedX = Mathf.Clamp01(normalizedX + 0.5f);
        normalizedZ = Mathf.Clamp01(normalizedZ + 0.5f);

        // Convert to shader pixel coordinates
        Vector2Int shaderCoords = new Vector2Int(
            Mathf.RoundToInt(normalizedX * size),
            Mathf.RoundToInt(normalizedZ * size)
        );

        return shaderCoords;
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
