using System.Collections.Generic;
using UnityEngine;

namespace SimJam.BarrelSimulator
{
    [CreateAssetMenu(fileName = "RadiationCountProfile", menuName = "SimJam/Radiation Count Profile")]
    public class RadiationCountProfile : ScriptableObject
    {
        [SerializeField, Min(1)] private int countPoolSize = 2048;
        [SerializeField, Min(0)] private int minCount = 250;
        [SerializeField, Min(0)] private int maxCount = 50000;
        [SerializeField] private List<int> explicitCountPool = new();

        private int[] generatedCountPool;

        public int GetRandomCount()
        {
            if (explicitCountPool != null && explicitCountPool.Count > 0)
            {
                return explicitCountPool[Random.Range(0, explicitCountPool.Count)];
            }

            EnsureGeneratedPool();
            return generatedCountPool[Random.Range(0, generatedCountPool.Length)];
        }

        public void RebuildGeneratedPool()
        {
            var safePoolSize = Mathf.Max(1, countPoolSize);
            generatedCountPool = new int[safePoolSize];

            var safeMin = Mathf.Min(minCount, maxCount);
            var safeMax = Mathf.Max(minCount, maxCount);
            for (var i = 0; i < generatedCountPool.Length; i++)
            {
                generatedCountPool[i] = Random.Range(safeMin, safeMax + 1);
            }
        }

        private void EnsureGeneratedPool()
        {
            if (generatedCountPool == null || generatedCountPool.Length != Mathf.Max(1, countPoolSize))
            {
                RebuildGeneratedPool();
            }
        }

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
