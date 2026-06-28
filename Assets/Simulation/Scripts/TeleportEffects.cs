using UnityEngine;

namespace SimJam
{
    /// <summary>
    /// Shared one-shot teleport arrival effect: a short particle burst at the landing point.
    /// Created and configured entirely in code (no prefab wiring), and self-destroys so it never
    /// leaks. Used by both the lab (RadiationLabRoomSpawner) and tutorial teleporters so the effect
    /// stays consistent in both rooms.
    /// </summary>
    public static class TeleportEffects
    {
        /// <summary>Spawn a brief particle burst at a teleport landing point.</summary>
        public static void SpawnArrivalBurst(Vector3 position, Color color)
        {
            var go = new GameObject("Teleport Burst");
            go.transform.position = position;

            var ps = go.AddComponent<ParticleSystem>();
            // A particle system added in code auto-plays; stop and clear before we configure it.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.6f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.5f;
            main.startSpeed = 1.6f;
            main.startSize = 0.06f;
            main.startColor = color;
            main.gravityModifier = -0.1f;
            main.maxParticles = 64;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.22f;
            shape.radiusThickness = 1f;

            var psRenderer = go.GetComponent<ParticleSystemRenderer>();
            psRenderer.material = CreateParticleMaterial(color);
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;

            ps.Play();
            // Backstop cleanup well after the 0.6s burst + 0.5s lifetime have finished.
            Object.Destroy(go, 1.2f);
        }

        private static Material CreateParticleMaterial(Color color)
        {
            // Sprites/Default is in the project's Always Included Shaders, so it survives on-device
            // shader stripping. The nicer Particles/* shaders are only created at runtime here with no
            // asset referencing them, so a device build strips them and the burst renders magenta.
            // Tinting comes from the particle start color (vertex color), so keep the material white.
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = "Teleport Burst Particle", color = Color.white };
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }
            return material;
        }
    }
}
