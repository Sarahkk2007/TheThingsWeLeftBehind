using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Spreads low ground fog over EVERY terrain tile in the scene, following the hills,
/// and switches on distance fog so the far trees fade out too.
/// Drop it on an empty GameObject and press Play.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class TerrainFog : MonoBehaviour
{
    [Header("Ground fog (follows the terrain)")]
    [Tooltip("Optional. Leave empty and a soft fog material is made for you.")]
    public Material fogMaterial;
    [Tooltip("Total fog puffs alive at once. More = thicker fog, more GPU cost.")]
    public int maxParticles = 800;
    [Tooltip("New puffs per second. ~ maxParticles / average lifetime keeps it steady.")]
    public float particlesPerSecond = 40f;
    public Vector2 lifetime = new Vector2(15f, 25f);
    public Vector2 puffSize = new Vector2(10f, 20f);
    [Tooltip("How high above the ground the fog sits (metres).")]
    public Vector2 heightAboveGround = new Vector2(0.5f, 3f);
    [Range(0f, 1f)] public float opacity = 0.12f;
    public Color fogColour = new Color(0.75f, 0.8f, 0.85f);
    [Tooltip("Slow wind drift.")]
    public Vector3 drift = new Vector3(0.3f, 0f, 0.15f);

    [Header("Keep fog out of these areas (e.g. inside the cabin)")]
    [Tooltip("Empty objects placed at the CENTRE of each indoor area.")]
    public Transform[] noFogZones;
    [Tooltip("Should cover the whole building, walls included.")]
    public float noFogRadius = 8f;
    [Tooltip("How fast puffs that drift towards a zone fade away.")]
    public float zoneFadeSpeed = 1.5f;

    [Header("Distance fog (whole scene)")]
    public bool distanceFog = true;
    [Range(0f, 0.03f)] public float distanceFogDensity = 0.005f;
    [Tooltip("Distance fog used while the camera is inside a no-fog zone.")]
    [Range(0f, 0.03f)] public float indoorFogDensity = 0f;
    public float fogBlendSpeed = 2f;

    ParticleSystem ps;
    ParticleSystem.Particle[] particles;
    Terrain[] terrains;
    float[] areaWeights;
    float totalArea;
    float emitBudget;
    Camera cam;

    void Awake()
    {
        terrains = Terrain.activeTerrains;
        if (terrains == null || terrains.Length == 0)
        {
            Debug.LogWarning("TerrainFog: no active Terrain found in the scene.");
            enabled = false;
            return;
        }

        // Weight tiles by area so bigger tiles get proportionally more fog.
        areaWeights = new float[terrains.Length];
        for (int i = 0; i < terrains.Length; i++)
        {
            Vector3 s = terrains[i].terrainData.size;
            areaWeights[i] = s.x * s.z;
            totalArea += areaWeights[i];
        }

        SetupParticleSystem();

        if (distanceFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColour;
            RenderSettings.fogDensity = distanceFogDensity;
        }
    }

    void Start()
    {
        // Fill the terrain straight away so the fog is already there when the scene opens.
        int initial = Mathf.RoundToInt(maxParticles * 0.8f);
        for (int i = 0; i < initial; i++)
            EmitOne(Random.Range(2f, lifetime.y)); // staggered so they don't all vanish together
    }

    void Update()
    {
        emitBudget += particlesPerSecond * Time.deltaTime;
        while (emitBudget >= 1f)
        {
            emitBudget -= 1f;
            if (ps.particleCount >= maxParticles) { emitBudget = 0f; break; }
            EmitOne(Random.Range(lifetime.x, lifetime.y));
        }

        FadePuffsNearZones();
        UpdateIndoorFog();
    }

    // Puffs that drift (or reach) into a no-fog zone fade out instead of entering the building.
    void FadePuffsNearZones()
    {
        if (noFogZones == null || noFogZones.Length == 0) return;
        if (particles == null || particles.Length < ps.main.maxParticles)
            particles = new ParticleSystem.Particle[ps.main.maxParticles];

        int count = ps.GetParticles(particles);
        float fade = zoneFadeSpeed * Time.deltaTime;
        bool changed = false;

        for (int i = 0; i < count; i++)
        {
            float halfSize = particles[i].startSize * 0.5f;
            if (!InNoFogZone(particles[i].position, halfSize)) continue;

            Color c = particles[i].startColor;
            c.a = Mathf.Max(0f, c.a - fade * opacity);
            particles[i].startColor = c;
            if (c.a <= 0f) particles[i].remainingLifetime = 0f;
            changed = true;
        }
        if (changed) ps.SetParticles(particles, count);
    }

    void UpdateIndoorFog()
    {
        if (!distanceFog) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        bool indoors = InNoFogZone(cam.transform.position, 0f);
        float target = indoors ? indoorFogDensity : distanceFogDensity;
        RenderSettings.fogDensity = Mathf.MoveTowards(
            RenderSettings.fogDensity, target, fogBlendSpeed * 0.01f * Time.deltaTime);
    }

    void EmitOne(float life)
    {
        float size = Random.Range(puffSize.x, puffSize.y);
        if (!TryGetFogPoint(size * 0.5f, out Vector3 pos)) return;

        var e = new ParticleSystem.EmitParams
        {
            position = pos,
            applyShapeToPosition = false,
            velocity = drift + new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(-0.02f, 0.02f), Random.Range(-0.1f, 0.1f)),
            startLifetime = life,
            startSize = size,
            rotation = Random.Range(0f, 360f),
            angularVelocity = Random.Range(-3f, 3f),
            startColor = new Color(fogColour.r, fogColour.g, fogColour.b, opacity)
        };
        ps.Emit(e, 1);
    }

    bool TryGetFogPoint(float margin, out Vector3 pos)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Terrain t = PickTerrain();
            Vector3 tp = t.transform.position;
            Vector3 size = t.terrainData.size;

            pos = new Vector3(tp.x + Random.value * size.x, 0f, tp.z + Random.value * size.z);
            pos.y = t.SampleHeight(pos) + tp.y + Random.Range(heightAboveGround.x, heightAboveGround.y);

            if (!InNoFogZone(pos, margin)) return true;
        }
        pos = Vector3.zero;
        return false;
    }

    Terrain PickTerrain()
    {
        float r = Random.value * totalArea;
        for (int i = 0; i < terrains.Length; i++)
        {
            r -= areaWeights[i];
            if (r <= 0f) return terrains[i];
        }
        return terrains[terrains.Length - 1];
    }

    // margin = extra distance (e.g. half a puff's width) so big puffs don't poke through the walls.
    bool InNoFogZone(Vector3 p, float margin)
    {
        if (noFogZones == null) return false;
        float r = noFogRadius + margin;
        foreach (Transform z in noFogZones)
        {
            if (z == null) continue;
            Vector3 d = p - z.position; d.y = 0f;
            if (d.sqrMagnitude < r * r) return true;
        }
        return false;
    }

    void SetupParticleSystem()
    {
        ps = GetComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startSpeed = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxParticles;
        main.gravityModifier = 0f;

        var emission = ps.emission; emission.enabled = false; // the script spawns the puffs
        var shape = ps.shape; shape.enabled = false;

        // Fade each puff in and out so nothing pops.
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var rend = GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.sortMode = ParticleSystemSortMode.Distance;
        rend.maxParticleSize = 2f;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        rend.sharedMaterial = fogMaterial != null ? fogMaterial : MakeFogMaterial();

        ps.Play();
    }

    Material MakeFogMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");

        var m = new Material(shader) { name = "TerrainFog (auto)" };

        // Transparent, alpha-blended, no depth write.
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;

        // Fade puffs out as the camera walks through them.
        m.SetFloat("_CameraFadingEnabled", 1f);
        m.SetFloat("_CameraNearFadeDistance", 1f);
        m.SetFloat("_CameraFarFadeDistance", 6f);
        m.SetVector("_CameraFadeParams", new Vector4(1f, 1f / (6f - 1f), 0f, 0f));
        m.EnableKeyword("_FADING_ON");

        Texture2D tex = MakeSoftPuffTexture(64);
        m.SetTexture("_BaseMap", tex);
        m.mainTexture = tex;
        m.SetColor("_BaseColor", Color.white);
        return m;
    }

    static Texture2D MakeSoftPuffTexture(int s)
    {
        var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float seed = Random.value * 100f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x + 0.5f) / s * 2f - 1f;
                float dy = (y + 0.5f) / s * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a);                                   // soft edge
                float n = Mathf.PerlinNoise(seed + x * 0.12f, seed + y * 0.12f) * 0.5f + 0.5f; // cloudy
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a * n));
            }
        t.Apply();
        return t;
    }

    void OnDrawGizmosSelected()
    {
        if (noFogZones == null) return;
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
        foreach (Transform z in noFogZones)
            if (z != null) Gizmos.DrawWireSphere(z.position, noFogRadius);
    }
}