using System.Runtime.InteropServices;
using dc.en;
using dc.hl.types;
using dc.hxd;
using dc.pr;
using dc.tool;
using dc.ui;

namespace DeadCellsMultiplayerMod;

internal static partial class LobbySession
{
    internal const int ReviveInteractKeyCode = 82; // R (keyboard)

    /// <summary>Hold-to-revive: keyboard R, E, F, plus gamepad interact/face buttons and configured action keys.</summary>
    internal static bool IsReviveHoldInputDown(Hero? hero)
    {
        if (hero == null)
            return false;

        try
        {
            if (dc.hxd.Key.Class.isDown(ReviveInteractKeyCode) || // R
                dc.hxd.Key.Class.isDown(69) || // E
                dc.hxd.Key.Class.isDown(70))   // F
                return true;
        }
        catch
        {
        }

#pragma warning disable CS8602
        try
        {
            if (hero.controller is not ControllerAccess access)
                return false;
            if (access.manualLock)
                return false;

            var controller = access.parent;
            if (controller == null || controller.isLocked)
                return false;

            // Direct pad checks: 5 (Right Bumper / interact), 0 (A / interact / confirm)
            if (controller.padIsDown(5) || controller.padIsDown(0))
                return true;

            var bindings = controller.get_bindings();
            if (bindings != null)
            {
                bool PadHeld(ArrayBytes_Int? bind)
                {
                    if (bind == null) return false;
                    try
                    {
                        for (var i = 0; i < bind.length; i++)
                        {
                            var code = Marshal.ReadInt32(bind.bytes, i << 2);
                            if (code >= 0 && controller.padIsDown(code))
                                return true;
                        }
                    }
                    catch { }
                    return false;
                }

                bool KeyHeld(ArrayBytes_Int? bind)
                {
                    if (bind == null) return false;
                    try
                    {
                        for (var i = 0; i < bind.length; i++)
                        {
                            var code = Marshal.ReadInt32(bind.bytes, i << 2);
                            if (code >= 0 && dc.hxd.Key.Class.isDown(code))
                                return true;
                        }
                    }
                    catch { }
                    return false;
                }

                if (PadHeld(bindings.padA) || PadHeld(bindings.padB) || PadHeld(bindings.padC))
                    return true;
                if (KeyHeld(bindings.primary) || KeyHeld(bindings.secondary) || KeyHeld(bindings.third))
                    return true;
            }
        }
        catch
        {
        }
#pragma warning restore CS8602

        return false;
    }
}
