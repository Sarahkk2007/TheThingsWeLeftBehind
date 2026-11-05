using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-person camera that sits behind the character and orbits with the mouse/right stick.
/// Put this on the Main Camera (NOT as a child of the player) and drag the player into Target.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [Tooltip("Point on the character the camera looks at (roughly head/shoulder height).")]
    [SerializeField] private Vector3 pivotOffset = new Vector3(0f, 1.6f, 0f);

    [Header("Position")]
    [SerializeField] private float distance = 3f;
    [Tooltip("0 = directly behind. Positive = over the right shoulder.")]
    [SerializeField] private float shoulderOffset = 0f;
    [SerializeField] private float followSmoothTime = 0.05f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float stickSensitivity = 150f;
    [SerializeField] private bool invertY = false;
    [SerializeField] private float minPitch = -30f;
    [SerializeField] private float maxPitch = 60f;

    [Header("Collision")]
    [Tooltip("Stops the camera clipping through walls.")]
    [SerializeField] private LayerMask collisionMask = ~0;
    [SerializeField] private float collisionRadius = 0.25f;
    [SerializeField] private float minDistance = 0.5f;

    /// <summary>Set false while reading a letter, in menus, during cutscenes, etc.</summary>
    public bool LookEnabled { get; set; } = true;

    private float yaw;
    private float pitch = 10f;
    private float currentDistance;
    private Vector3 pivotVelocity;
    private Vector3 smoothedPivot;
    private readonly RaycastHit[] hits = new RaycastHit[8];

    private void Start()
    {
        if (target != null)
        {
            yaw = target.eulerAngles.y;          // start right behind the character
            smoothedPivot = target.position + pivotOffset;
        }
        currentDistance = distance;
        SetCursorLocked(true);
    }

    private void Update()
    {
        HandleCursor();
        if (!LookEnabled) return;

        Vector2 look = Vector2.zero;

        if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
            look += Mouse.current.delta.ReadValue() * mouseSensitivity;

        if (Gamepad.current != null)
            look += Gamepad.current.rightStick.ReadValue() * stickSensitivity * Time.deltaTime;

        yaw += look.x;
        pitch += invertY ? look.y : -look.y;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // Follow the character smoothly
        Vector3 pivot = target.position + pivotOffset;
        smoothedPivot = Vector3.SmoothDamp(smoothedPivot, pivot, ref pivotVelocity, followSmoothTime);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 shoulder = rotation * Vector3.right * shoulderOffset;
        Vector3 back = rotation * Vector3.back;

        // Pull the camera in if a wall is in the way
        float wanted = distance;
        Vector3 origin = smoothedPivot + shoulder;
        int count = Physics.SphereCastNonAlloc(origin, collisionRadius, back, hits, distance,
                                               collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (hits[i].transform.IsChildOf(target)) continue; // ignore the player itself
            wanted = Mathf.Min(wanted, Mathf.Max(hits[i].distance, minDistance));
        }

        // Snap in fast, ease back out slowly
        currentDistance = wanted < currentDistance
            ? wanted
            : Mathf.Lerp(currentDistance, wanted, Time.deltaTime * 5f);

        transform.position = origin + back * currentDistance;
        transform.rotation = rotation;
    }

    /// <summary>Flat forward direction of the camera, for camera-relative player movement.</summary>
    public Vector3 FlatForward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
    public Vector3 FlatRight => Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

    private void HandleCursor()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            SetCursorLocked(false);

        if (LookEnabled && Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame &&
            Cursor.lockState != CursorLockMode.Locked)
            SetCursorLocked(true);
    }

    public void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
