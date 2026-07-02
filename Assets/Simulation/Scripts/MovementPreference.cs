using UnityEngine;

// =============================================================================
// MovementPreference.cs
//
// PURPOSE:  Records the player's locomotion choice (Smooth walking vs. Teleport)
//           made once on the start screen and persists it via PlayerPrefs so the
//           Tutorial and Lab scenes load with the same movement style. Defines
//           the MovementMode enum plus a static Save/Load helper.
//
// HOW TO CUSTOMIZE:
//   - This file has NO [SerializeField] fields and is not a MonoBehaviour, so it
//     lives in NO scene and has NO Inspector values to edit. Everything below is
//     hardcoded in this C# file — change it here and the change takes effect.
//   - PlayerPrefs key ("SimJam.LocomotionMode"): the string under which the mode
//     is stored on disk. Change the `Key` const near the top of the
//     MovementPreference class. WARNING: renaming it orphans any previously saved
//     choice, so players will fall back to the default on next launch.
//   - Available modes (Unset / Smooth / Teleport): edit the `MovementMode` enum
//     at the top of this file. Keep the integer values stable — they are what get
//     written to PlayerPrefs, so changing a number silently remaps saved choices.
//   - Default when nothing is stored (MovementMode.Unset): the fallback returned
//     by Load() is hardcoded in the `Load()` method as the second arg to
//     PlayerPrefs.GetInt(...). Change it there to make scenes default to a
//     specific mode instead of their own serialized movement flags.
//   - WHO writes/reads the choice: the start-screen UI calls Save(); the Tutorial
//     and Lab spawners call Load(). To change what happens on Unset, edit those
//     caller scripts, not this file.
// =============================================================================

namespace SimJam
{
    /// <summary>
    /// The player's chosen locomotion style, selected once on the start screen and honoured by every
    /// later scene so movement feels consistent from the tutorial through the radiation lab.
    /// </summary>
    /// The player's chosen locomotion style. Picked on the start screen and read by the Tutorial and
    /// Lab. Unset = no choice recorded (e.g. a gameplay scene launched directly in-editor), in which
    /// case the scene keeps its own serialized movement flags.
    public enum MovementMode
    {
        /// <summary>No preference recorded yet; scenes fall back to their own serialized movement flags.</summary>
        Unset = 0,
        /// <summary>Continuous stick-driven walking locomotion.</summary>
        Smooth = 1,
        /// <summary>Point-and-jump teleport locomotion.</summary>
        Teleport = 2
    }

    /// <summary>
    /// Static helper that stores and retrieves the player's <see cref="MovementMode"/> in PlayerPrefs,
    /// letting the start-screen choice carry over into the tutorial and lab scenes.
    /// </summary>
    /// Persists the movement choice across scene loads. PlayerPrefs survives LoadScene (and app
    /// restarts), so no singleton / DontDestroyOnLoad is needed.
    public static class MovementPreference
    {
        /// <summary>PlayerPrefs key under which the selected locomotion mode is stored as an int.</summary>
        private const string Key = "SimJam.LocomotionMode";

        /// <summary>
        /// Writes the chosen locomotion mode to PlayerPrefs and flushes it to disk so the selection
        /// survives the scene load into the tutorial or lab.
        /// </summary>
        public static void Save(MovementMode mode)
        {
            PlayerPrefs.SetInt(Key, (int)mode);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Reads back the previously saved locomotion mode, returning <see cref="MovementMode.Unset"/>
        /// when nothing has been stored (e.g. a gameplay scene launched directly in the editor).
        /// </summary>
        public static MovementMode Load()
        {
            return (MovementMode)PlayerPrefs.GetInt(Key, (int)MovementMode.Unset);
        }
    }
}
