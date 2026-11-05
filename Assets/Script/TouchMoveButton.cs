using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Put this on a UI Image (an on-screen arrow). While a finger is held on it,
/// the player moves in the chosen direction. Several buttons can be held at once
/// (e.g. Forward + Left for diagonal movement).
/// </summary>
public class TouchMoveButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public PlayerMovement player;
    public MoveDirection direction;

    bool held;

    public void OnPointerDown(PointerEventData e) => SetHeld(true);
    public void OnPointerUp(PointerEventData e)   => SetHeld(false);
    public void OnPointerExit(PointerEventData e) => SetHeld(false); // finger slid off the button

    void OnDisable() => SetHeld(false);

    void SetHeld(bool pressed)
    {
        if (held == pressed) return;
        held = pressed;
        if (player != null) player.SetTouch(direction, pressed);
    }
}
