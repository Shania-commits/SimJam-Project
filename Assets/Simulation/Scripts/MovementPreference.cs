using UnityEngine;

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
