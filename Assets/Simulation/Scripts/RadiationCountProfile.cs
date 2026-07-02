using System.Collections.Generic;
using UnityEngine;

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
