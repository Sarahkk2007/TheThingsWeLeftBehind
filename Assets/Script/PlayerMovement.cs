using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum MoveDirection { Forward, Back, Left, Right }

/// <summary>
/// 3D character movement: forward / back / left / right.
/// Keyboard: arrow keys (and WASD). Touch: on-screen buttons using TouchMoveButton.
/// Works with both the old Input Manager and the new Input System package.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 4f;
    [Tooltip("Degrees per second the character turns to face where it's going.")]
    public float turnSpeed = 720f;
    public float gravity = -20f;

    [Tooltip("ON: character turns to face the way it walks (third-person).\nOFF: character keeps facing forward and strafes left/right (first-person).")]
    public bool faceMoveDirection = true;

    [Header("Direction reference")]
    [Tooltip("If set, 'forward' means the way this camera looks. Empty = uses Main Camera. " +
             "Untick useCameraDirection to use the character's own facing instead.")]
    public Transform cameraTransform;
    public bool useCameraDirection = true;

    [Header("Touch controls")]
    [Tooltip("Parent object holding the on-screen arrow buttons. Shown on phones/tablets, hidden on desktop.")]
    public GameObject touchControls;
    public bool showTouchControlsInEditor = true;

    [Header("Animation (optional)")]
    public Animator animator;
    [Tooltip("Float parameter set to 0..1 based on how fast the character is moving.")]
    public string speedParam = "Speed";

    CharacterController controller;
    float verticalVelocity;
    bool touchForward, touchBack, touchLeft, touchRight;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (touchControls != null)
        {
            bool show = Application.isMobilePlatform || (Application.isEditor && showTouchControlsInEditor);
            touchControls.SetActive(show);
        }
    }

    /// <summary>Called by TouchMoveButton when a button is pressed or released.</summary>
    public void SetTouch(MoveDirection dir, bool pressed)
    {
        switch (dir)
        {
            case MoveDirection.Forward: touchForward = pressed; break;
            case MoveDirection.Back:    touchBack = pressed;    break;
            case MoveDirection.Left:    touchLeft = pressed;    break;
            case MoveDirection.Right:   touchRight = pressed;   break;
        }
    }

    void Update()
    {
        // 1. Gather input (x = left/right, y = forward/back)
        Vector2 input = ReadKeyboard();
        if (touchForward) input.y += 1f;
        if (touchBack)    input.y -= 1f;
        if (touchRight)   input.x += 1f;
        if (touchLeft)    input.x -= 1f;
        input = Vector2.ClampMagnitude(input, 1f);

        // 2. Work out which way is "forward" and "right" on the ground
        Vector3 forward, right;
        if (useCameraDirection && cameraTransform != null)
        {
            forward = Flatten(cameraTransform.forward);
            if (forward.sqrMagnitude < 0.01f)            // camera looking straight down
                forward = Flatten(cameraTransform.up);
            right = Flatten(cameraTransform.right);
        }
        else
        {
            forward = Flatten(transform.forward);
            right = Flatten(transform.right);
        }

        Vector3 move = forward * input.y + right * input.x;

        // 3. Turn to face movement direction (third-person style)
        if (faceMoveDirection && move.sqrMagnitude > 0.001f)
        {
            Quaternion target = Quaternion.LookRotation(move);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
        }

        // 4. Gravity so the character stays on the floor and walks down slopes/steps
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;

        // 5. Move
        Vector3 velocity = move * moveSpeed;
        velocity.y = verticalVelocity;
        controller.Move(velocity * Time.deltaTime);

        if (animator != null)
            animator.SetFloat(speedParam, input.magnitude);
    }

    static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.normalized;
    }

    Vector2 ReadKeyboard()
    {
        float x = 0f, y = 0f;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.upArrowKey.isPressed    || kb.wKey.isPressed) y += 1f;
            if (kb.downArrowKey.isPressed  || kb.sKey.isPressed) y -= 1f;
            if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) x += 1f;
            if (kb.leftArrowKey.isPressed  || kb.aKey.isPressed) x -= 1f;
        }
#else
        if (Input.GetKey(KeyCode.UpArrow)    || Input.GetKey(KeyCode.W)) y += 1f;
        if (Input.GetKey(KeyCode.DownArrow)  || Input.GetKey(KeyCode.S)) y -= 1f;
        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) x += 1f;
        if (Input.GetKey(KeyCode.LeftArrow)  || Input.GetKey(KeyCode.A)) x -= 1f;
#endif
        return new Vector2(x, y);
    }
}
