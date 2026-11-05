using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scatters a tree prefab across a Unity Terrain.
/// Right-click the component header (or the ⋮ menu) → "Generate Trees" to bake them into the scene in Edit mode,
/// or tick "Generate On Start" to spawn them when the game runs.
/// Same seed = same forest every time.
/// </summary>
public class TreeScatter : MonoBehaviour
{
    [Header("What to place")]
    public GameObject treePrefab;          // pivot should be at the base of the trunk
    public Terrain terrain;                // leave empty to use the active terrain

    [Header("How many")]
    public int treeCount = 300;
    public int seed = 12345;
    [Tooltip("Minimum distance between two trees (metres).")]
    public float minSpacing = 3f;

    [Header("Where trees are allowed")]
    [Range(0f, 90f)] public float maxSlope = 30f;
    [Tooltip("World-space height limits, e.g. no trees below the lake or above the treeline.")]
    public float minHeight = -1000f;
    public float maxHeight = 1000f;
    [Tooltip("Keep trees this far from the terrain edges.")]
    public float edgePadding = 2f;

    [Header("Keep clear")]
    [Tooltip("Objects on these layers block trees (e.g. cabin, paths). Do NOT include the terrain's own layer.")]
    public LayerMask avoidLayers;
    public float avoidRadius = 2f;
    [Tooltip("Drop empty GameObjects here to mark clearings (cabin, campfire, spawn point...).")]
    public Transform[] clearings;
    public float clearingRadius = 10f;

    [Header("Variation")]
    public Vector2 scaleRange = new Vector2(0.8f, 1.3f);
    [Tooltip("Pines grow straight up, so leave this off unless you want them tilted with the ground.")]
    public bool alignToSlope = false;
    [Tooltip("Pushes trunks slightly into the ground so they don't float on slopes.")]
    public float sinkDepth = 0.15f;
    [Tooltip("Ignores the prefab's pivot and puts the bottom of the trunk on the ground.")]
    public bool autoFixPivot = true;

    [Header("Runtime")]
    public bool generateOnStart = false;

    const string ContainerName = "Generated Trees";

    void Start()
    {
        if (generateOnStart) Generate();
    }

    [ContextMenu("Generate Trees")]
    public void Generate()
    {
        if (treePrefab == null) { Debug.LogWarning("TreeScatter: no tree prefab assigned."); return; }
        if (terrain == null) terrain = Terrain.activeTerrain;
        if (terrain == null) { Debug.LogWarning("TreeScatter: no terrain found."); return; }
#if UNITY_EDITOR
        if (!UnityEditor.PrefabUtility.IsPartOfPrefabAsset(treePrefab))
            Debug.LogWarning("TreeScatter: Tree Prefab is a scene object, not the prefab from the Project window. " +
                             "Trees will still spawn, but they won't update when you edit the prefab.");
#endif

        Clear();
        Transform container = GetContainer();

        TerrainData data = terrain.terrainData;
        Vector3 tPos = terrain.GetPosition();
        Vector3 tSize = data.size;

        var rng = new System.Random(seed);
        var grid = new Dictionary<Vector2Int, List<Vector2>>();
        float cell = Mathf.Max(minSpacing, 0.01f);

        int placed = 0;
        int attempts = 0;
        int maxAttempts = treeCount * 30;

        while (placed < treeCount && attempts < maxAttempts)
        {
            attempts++;

            // Random point on the terrain (world XZ)
            float x = Range(rng, tPos.x + edgePadding, tPos.x + tSize.x - edgePadding);
            float z = Range(rng, tPos.z + edgePadding, tPos.z + tSize.z - edgePadding);

            float nx = (x - tPos.x) / tSize.x;   // 0..1 across the terrain
            float nz = (z - tPos.z) / tSize.z;

            // Slope check
            if (data.GetSteepness(nx, nz) > maxSlope) continue;

            // Height check
            float y = data.GetInterpolatedHeight(nx, nz) + tPos.y;
            if (y < minHeight || y > maxHeight) continue;

            Vector3 pos = new Vector3(x, y, z);

            // Clearings
            if (InClearing(pos)) continue;

            // Blocking objects (cabin etc.)
            if (avoidLayers.value != 0 &&
                Physics.CheckSphere(pos + Vector3.up * avoidRadius, avoidRadius, avoidLayers, QueryTriggerInteraction.Ignore))
                continue;

            // Spacing from other trees
            Vector2 p2 = new Vector2(x, z);
            if (TooClose(grid, p2, cell)) continue;
            AddToGrid(grid, p2, cell);

            // Rotation + scale
            Quaternion rot = Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f);
            if (alignToSlope)
            {
                Vector3 normal = data.GetInterpolatedNormal(nx, nz);
                rot = Quaternion.FromToRotation(Vector3.up, normal) * rot;
            }
            float s = Range(rng, scaleRange.x, scaleRange.y);
            pos.y -= sinkDepth * s;

            SpawnTree(container, pos, rot, s);
            placed++;
        }

        if (placed < treeCount)
            Debug.Log($"TreeScatter: placed {placed}/{treeCount}. Not enough valid space — lower Min Spacing or relax slope/height limits.");
        else
            Debug.Log($"TreeScatter: placed {placed} trees.");
    }

    [ContextMenu("Clear Trees")]
    public void Clear()
    {
        Transform container = transform.Find(ContainerName);
        if (container == null) return;

        for (int i = container.childCount - 1; i >= 0; i--)
        {
            GameObject child = container.GetChild(i).gameObject;
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEditor.Undo.DestroyObjectImmediate(child); continue; }
#endif
            Destroy(child);
        }
    }

    // ---------- helpers ----------

    void SpawnTree(Transform container, Vector3 pos, Quaternion rot, float scale)
    {
        GameObject tree;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // Keeps the prefab link so edits to the prefab update every tree.
            // If a scene object was dragged in instead of the prefab asset, fall back to a plain copy.
            tree = UnityEditor.PrefabUtility.IsPartOfPrefabAsset(treePrefab)
                ? (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(treePrefab, container)
                : null;
            if (tree == null) tree = Instantiate(treePrefab, container);
            UnityEditor.Undo.RegisterCreatedObjectUndo(tree, "Generate Trees");
        }
        else
#endif
        {
            tree = Instantiate(treePrefab, container);
        }

        tree.transform.SetPositionAndRotation(pos, rot);
        tree.transform.localScale = treePrefab.transform.localScale * scale;

        if (autoFixPivot) SnapBottomTo(tree, pos.y);
    }

    // Moves the tree up/down so the lowest point of its mesh sits at groundY,
    // no matter where the prefab's pivot is.
    static void SnapBottomTo(GameObject tree, float groundY)
    {
        Renderer[] renderers = tree.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        tree.transform.position += Vector3.up * (groundY - b.min.y);
    }

    Transform GetContainer()
    {
        Transform c = transform.Find(ContainerName);
        if (c != null) return c;

        var go = new GameObject(ContainerName);
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Generate Trees");
#endif
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    bool InClearing(Vector3 pos)
    {
        if (clearings == null) return false;
        foreach (Transform c in clearings)
        {
            if (c == null) continue;
            Vector2 d = new Vector2(pos.x - c.position.x, pos.z - c.position.z);
            if (d.sqrMagnitude < clearingRadius * clearingRadius) return true;
        }
        return false;
    }

    static Vector2Int Cell(Vector2 p, float cell) =>
        new Vector2Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell));

    bool TooClose(Dictionary<Vector2Int, List<Vector2>> grid, Vector2 p, float cell)
    {
        Vector2Int c = Cell(p, cell);
        float minSq = minSpacing * minSpacing;
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!grid.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var list)) continue;
                foreach (Vector2 other in list)
                    if ((other - p).sqrMagnitude < minSq) return true;
            }
        return false;
    }

    static void AddToGrid(Dictionary<Vector2Int, List<Vector2>> grid, Vector2 p, float cell)
    {
        Vector2Int c = Cell(p, cell);
        if (!grid.TryGetValue(c, out var list)) { list = new List<Vector2>(); grid[c] = list; }
        list.Add(p);
    }

    static float Range(System.Random r, float min, float max) =>
        min + (float)r.NextDouble() * (max - min);

    void OnDrawGizmosSelected()
    {
        if (clearings == null) return;
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
        foreach (Transform c in clearings)
            if (c != null) Gizmos.DrawWireSphere(c.position, clearingRadius);
    }
}