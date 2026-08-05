using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class WaterMesh : MonoBehaviour
{
    [Header("Water")]
    public float waterSize = 500f;
    public Material waterMaterial;
    public float yPosition;

    [Header("LOD")]
    public Transform targetCamera;
    public int maxDepth = 7;
    public float splitDistanceMultiplier = 2.5f;

    [Header("Mesh")]
    public int patchResolution = 32;
    public float skirtDepth = 10f;

    private WaterPatch root;
    private Mesh patchMesh;

    private readonly List<WaterPatch> visiblePatches = new();
    private readonly List<GameObject> patchObjects = new();


    public void Create()
    {
        patchMesh = CreatePatchMesh();

        root = new WaterPatch(
            Vector2.zero,
            waterSize,
            0
        );
    }


    void Update()
    {
        
        UpdateLOD(root);

        visiblePatches.Clear();
        CollectLeaves(root, visiblePatches);

        RenderPatches();
    }


    void UpdateLOD(WaterPatch patch)
    {
        float distance = Vector2.Distance(
            patch.center,
            new Vector2(
                targetCamera.position.x,
                targetCamera.position.z
            )
        );


        float splitDistance =
            patch.size * splitDistanceMultiplier;



        if (distance < splitDistance &&
           patch.depth < maxDepth)
        {
            if (patch.children == null)
                Split(patch);

            foreach (var child in patch.children)
                UpdateLOD(child);
        }
        else
        {
            if (patch.children != null)
                Merge(patch);
        }
    }


    void Split(WaterPatch patch)
    {
        float half = patch.size * 0.5f;
        float quarter = patch.size * 0.25f;

        patch.children = new WaterPatch[4];


        patch.children[0] = new WaterPatch(
            patch.center + new Vector2(-quarter, quarter),
            half,
            patch.depth + 1
        );

        patch.children[1] = new WaterPatch(
            patch.center + new Vector2(quarter, quarter),
            half,
            patch.depth + 1
        );

        patch.children[2] = new WaterPatch(
            patch.center + new Vector2(-quarter, -quarter),
            half,
            patch.depth + 1
        );

        patch.children[3] = new WaterPatch(
            patch.center + new Vector2(quarter, -quarter),
            half,
            patch.depth + 1
        );
    }


    void Merge(WaterPatch patch)
    {
        patch.children = null;
    }


    void CollectLeaves(
        WaterPatch patch,
        List<WaterPatch> output)
    {
        if (patch.children == null)
        {
            output.Add(patch);
            return;
        }


        foreach (var child in patch.children)
            CollectLeaves(child, output);
    }


    void RenderPatches()
    {
        // Ensure we have pooled GameObjects for visible patches
        for (int i = 0; i < visiblePatches.Count; i++)
        {
            var patch = visiblePatches[i];

            GameObject go;
            if (i < patchObjects.Count)
            {
                go = patchObjects[i];
                go.SetActive(true);
            }
            else
            {
                go = new GameObject($"WaterPatch_{i}");
                go.layer = LayerMask.NameToLayer("Water");
                go.transform.parent = transform;
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = patchMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
                mr.sharedMaterial = waterMaterial;
                patchObjects.Add(go);
            }

            // Position & scale to match the TRS used previously
            go.transform.localPosition = new Vector3(patch.center.x, 0f, patch.center.y);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * patch.size;

            // Set per-patch shader properties without instancing new materials
            var mrender = go.GetComponent<MeshRenderer>();
            var mpb = new MaterialPropertyBlock();
            mpb.SetVector("_PatchInfo", new Vector4(patch.center.x, patch.center.y, patch.size, 0f));
            mpb.SetFloat("_WaterSize", waterSize);
            // Compute combined water origin (bottom-left corner in world XZ)
            float half = waterSize * 0.5f;
            mpb.SetVector("_WaterOrigin", new Vector4(transform.position.x, transform.position.z, 0f, 0f));
            mrender.SetPropertyBlock(mpb);
        }

        // Deactivate any unused pooled objects
        for (int i = visiblePatches.Count; i < patchObjects.Count; i++)
            patchObjects[i].SetActive(false);
    }


    Mesh CreatePatchMesh()
    {
        Mesh mesh = new Mesh();
        
        int vertsPerSide = patchResolution + 1;

        List<Vector3> vertices = new();
        List<int> triangles = new();
        List<Vector2> uvs = new();


        float step =
            1f / patchResolution;


        // Main grid
        for (int z = 0; z <= patchResolution; z++)
        {
            for (int x = 0; x <= patchResolution; x++)
            {
                vertices.Add(
                    new Vector3(
                        x * step - 0.5f,
                        yPosition,
                        z * step - 0.5f
                    )
                );
                uvs.Add(new Vector2(x * step, z * step));
            }
        }


        for (int z = 0; z < patchResolution; z++)
        {
            for (int x = 0; x < patchResolution; x++)
            {
                int i =
                    z * vertsPerSide + x;

                triangles.Add(i);
                triangles.Add(i + vertsPerSide);
                triangles.Add(i + 1);

                triangles.Add(i + 1);
                triangles.Add(i + vertsPerSide);
                triangles.Add(i + vertsPerSide + 1);
            }
        }


        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.MarkDynamic();
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
        
        return mesh;
    }


    class WaterPatch
    {
        public Vector2 center;
        public float size;
        public int depth;

        public WaterPatch[] children;


        public WaterPatch(
            Vector2 center,
            float size,
            int depth)
        {
            this.center = center;
            this.size = size;
            this.depth = depth;
        }
    }
}