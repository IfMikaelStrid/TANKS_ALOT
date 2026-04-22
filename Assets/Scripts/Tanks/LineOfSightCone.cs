using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class LineOfSightCone : MonoBehaviour
{
    [Header("Cone Settings")]
    [Range(1f, 360f)]
    public float angle = 90f;
    [Min(0.1f)]
    public float range = 10f;
    [Range(3, 64)]
    public int segments = 16;

    [Header("Appearance")]
    public Color coneColor = new Color(1f, 1f, 0f, 0.25f);
    public float heightOffset = 0.05f;

    private Mesh mesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Material material;

    private float lastAngle;
    private float lastRange;
    private int lastSegments;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        mesh = new Mesh { name = "LineOfSightCone" };
        meshFilter.mesh = mesh;

        transform.localPosition = Vector3.up * heightOffset;
        transform.localRotation = Quaternion.identity;

        CreateMaterial();
        BuildMesh();
    }

    void Update()
    {
        if (!Mathf.Approximately(angle, lastAngle) ||
            !Mathf.Approximately(range, lastRange) ||
            segments != lastSegments)
        {
            BuildMesh();
        }

        if (material != null)
            material.color = coneColor;
    }

    void OnDestroy()
    {
        if (material != null)
            Destroy(material);
        if (mesh != null)
            Destroy(mesh);
    }

    private void CreateMaterial()
    {
        // Sprites/Default supports vertex colors and transparency in all pipelines
        var shader = Shader.Find("Sprites/Default");
        material = new Material(shader);
        material.color = coneColor;
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        meshRenderer.material = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
    }

    private void BuildMesh()
    {
        lastAngle = angle;
        lastRange = range;
        lastSegments = segments;

        // vertices: origin + one per segment edge
        int vertCount = segments + 2;
        var vertices = new Vector3[vertCount];
        var triangles = new int[segments * 3];

        vertices[0] = Vector3.zero;

        float halfAngle = angle * 0.5f;
        float stepAngle = angle / segments;

        for (int i = 0; i <= segments; i++)
        {
            float currentAngle = -halfAngle + stepAngle * i;
            float rad = currentAngle * Mathf.Deg2Rad;
            vertices[i + 1] = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * range;
        }

        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }
}
