using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

public enum Hotkey { Reroll, BuyXp, Sell }

// Entrada que funciona tant amb el Input System nou com amb el clàssic.
// Dreceres (com al TFT): D = reroll, F = comprar XP, E = vendre la unitat sota el ratolí.
public static class InputUtil
{
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    public static bool RightClickDown => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
    public static bool LeftClickDown => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    public static bool LeftClickHeld => Mouse.current != null && Mouse.current.leftButton.isPressed;

    public static bool Pressed(Hotkey k)
    {
        var kb = Keyboard.current;
        if (kb == null) return false;
        switch (k)
        {
            case Hotkey.Reroll: return kb.dKey.wasPressedThisFrame;
            case Hotkey.BuyXp: return kb.fKey.wasPressedThisFrame;
            case Hotkey.Sell: return kb.eKey.wasPressedThisFrame;
        }
        return false;
    }
#else
    public static Vector2 MousePosition => Input.mousePosition;
    public static bool RightClickDown => Input.GetMouseButtonDown(1);
    public static bool LeftClickDown => Input.GetMouseButtonDown(0);
    public static bool LeftClickHeld => Input.GetMouseButton(0);

    public static bool Pressed(Hotkey k)
    {
        switch (k)
        {
            case Hotkey.Reroll: return Input.GetKeyDown(KeyCode.D);
            case Hotkey.BuyXp: return Input.GetKeyDown(KeyCode.F);
            case Hotkey.Sell: return Input.GetKeyDown(KeyCode.E);
        }
        return false;
    }
#endif
}