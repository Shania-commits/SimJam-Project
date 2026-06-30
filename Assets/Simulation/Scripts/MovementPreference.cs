using UnityEngine;

namespace SimJam
{
    /// The player's chosen locomotion style. Picked on the start screen and read by the Tutorial and
    /// Lab. Unset = no choice recorded (e.g. a gameplay scene launched directly in-editor), in which
    /// case the scene keeps its own serialized movement flags.
    public enum MovementMode
    {
        Unset = 0,
        Smooth = 1,
        Teleport = 2
    }

    /// Persists the movement choice across scene loads. PlayerPrefs survives LoadScene (and app
    /// restarts), so no singleton / DontDestroyOnLoad is needed.
    public static class MovementPreference
    {
        private const string Key = "SimJam.LocomotionMode";

        public static void Save(MovementMode mode)
        {
            PlayerPrefs.SetInt(Key, (int)mode);
            PlayerPrefs.Save();
        }

        public static MovementMode Load()
        {
            return (MovementMode)PlayerPrefs.GetInt(Key, (int)MovementMode.Unset);
        }
    }
}
