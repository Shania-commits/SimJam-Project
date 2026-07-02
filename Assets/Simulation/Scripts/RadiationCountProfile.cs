using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// RadiationCountProfile.cs
//
// PURPOSE:  A reusable ScriptableObject "data asset" that supplies the pool of
//   raw radiation "count" values used to seed each barrel's activity in the
//   find-the-barrel game. A spawner asks it for a random count per barrel. It
//   either draws from a hand-authored list of counts, or (when that list is
//   empty) from a lazily built pool of random counts within a min/max range.
//   Create instances via: Assets > Create > SimJam/Radiation Count Profile.
//
// HOW TO CUSTOMIZE:
//   IMPORTANT: this is a ScriptableObject, so its [SerializeField] values are
//   NOT stored in any .unity scene file. They live in the .asset file you
//   create from the menu above. The defaults below are only used the moment a
//   brand-new asset is created; editing a default here does NOT change an asset
//   that already exists — select that .asset in the Project window and edit it
//   in the Inspector (or edit the .asset YAML directly).
//   NOTE: assigning a profile is OPTIONAL. Each spawner (RandomRoomBarrelSpawner,
//   RadiationLabRoomSpawner, BasicVRRoomBarrelSpawner) has an
//   m_radiationCountProfile field; if it is left empty, that spawner ignores
//   this class entirely and uses its own built-in "Fallback counts" fields
//   instead. To make this profile take effect, drag your .asset onto the
//   spawner's m_radiationCountProfile slot in the scene's spawner GameObject.
//
//   Knobs (all on the .asset, edited in the Inspector):
//   - explicitCountPool (List<int>): if you add ANY entries, they win — a random
//     one is returned per barrel and the min/max generator is skipped entirely.
//     Use this to pin down exact readings. Leave empty to use random generation.
//   - countPoolSize (int, Min 1, default 2048): how many random counts to
//     pre-generate/cache when explicitCountPool is empty. Larger = more variety.
//   - minCount / maxCount (int, defaults 250 / 50000): inclusive lower/upper
//     bounds for each randomly generated count. Bounds are auto-normalised, so a
//     swapped min>max still works, and OnValidate() clamps maxCount >= minCount.
//
//   Hardcoded (NOT serialized — change in code, not the Inspector):
//   - There are no other magic numbers here; the random draw itself is uniform.
//     To change the sampling logic (e.g. weighting), edit GetRandomCount() and/or
//     RebuildGeneratedPool() in this file.
// =============================================================================

namespace SimJam.BarrelSimulator
{
    /// <summary>
    /// Authorable data asset (ScriptableObject) that supplies the pool of raw radiation "count" values
    /// used to seed a barrel's activity in the find-the-barrel game. It can either draw from a hand-picked
    /// list of counts or, failing that, from a lazily generated pool of random counts bounded by a
    /// min/max range. Create instances via the SimJam/Radiation Count Profile asset menu.
    /// </summary>
    [CreateAssetMenu(fileName = "RadiationCountProfile", menuName = "SimJam/Radiation Count Profile")]
    public class RadiationCountProfile : ScriptableObject
    {
        /// <summary>Number of random count entries to pre-generate when no explicit pool is provided.</summary>
        [SerializeField, Min(1)] private int countPoolSize = 2048;
        /// <summary>Lower bound (inclusive) for randomly generated counts.</summary>
        [SerializeField, Min(0)] private int minCount = 250;
        /// <summary>Upper bound (inclusive) for randomly generated counts.</summary>
        [SerializeField, Min(0)] private int maxCount = 50000;
        /// <summary>
        /// Optional hand-authored list of count values. When non-empty this takes precedence over the
        /// random min/max generator, letting designers pin down specific activity readings.
        /// </summary>
        [SerializeField] private List<int> explicitCountPool = new();

        /// <summary>Cached array of randomly generated counts, built on demand from the min/max range.</summary>
        private int[] generatedCountPool;

        /// <summary>
        /// Returns a single count value picked at random. Prefers the explicit pool if it has entries;
        /// otherwise lazily builds and samples the generated random pool.
        /// </summary>
        public int GetRandomCount()
        {
            if (explicitCountPool != null && explicitCountPool.Count > 0)
            {
                return explicitCountPool[Random.Range(0, explicitCountPool.Count)];
            }

            EnsureGeneratedPool();
            return generatedCountPool[Random.Range(0, generatedCountPool.Length)];
        }

        /// <summary>
        /// Rebuilds the cached random count pool, filling it with fresh random values drawn from the
        /// configured min/max range. Guards against a zero pool size and swapped min/max bounds.
        /// </summary>
        public void RebuildGeneratedPool()
        {
            var safePoolSize = Mathf.Max(1, countPoolSize);
            generatedCountPool = new int[safePoolSize];

            // Normalise the bounds so the pool is still valid even if minCount > maxCount.
            var safeMin = Mathf.Min(minCount, maxCount);
            var safeMax = Mathf.Max(minCount, maxCount);
            for (var i = 0; i < generatedCountPool.Length; i++)
            {
                generatedCountPool[i] = Random.Range(safeMin, safeMax + 1);
            }
        }

        /// <summary>
        /// Lazily (re)builds the generated pool if it is missing or its size no longer matches the
        /// currently configured pool size.
        /// </summary>
        private void EnsureGeneratedPool()
        {
            if (generatedCountPool == null || generatedCountPool.Length != Mathf.Max(1, countPoolSize))
            {
                RebuildGeneratedPool();
            }
        }

        /// <summary>
        /// Editor validation callback that keeps the serialized fields sane: enforces a minimum pool
        /// size of one and prevents maxCount from dropping below minCount.
        /// </summary>
        private void OnValidate()
        {
            countPoolSize = Mathf.Max(1, countPoolSize);
            if (maxCount < minCount)
            {
                maxCount = minCount;
            }
        }
    }
}
