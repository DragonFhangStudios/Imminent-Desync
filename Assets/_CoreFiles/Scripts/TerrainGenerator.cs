using UnityEngine;

/// <summary>
/// Phase 1 terrain generation.
/// Fills Cell.Elevation with a seeded, normalized (0..1) heightmap.
/// Visual/topological data only - no simulation system reads Elevation.
/// </summary>
public static class TerrainGenerator
{
	// Random per-octave offsets are picked in this range so the same seed
	// always samples the same region of Perlin space.
	private const float OffsetRange = 10000f;

	// Each octave doubles in frequency (fixed lacunarity of 2).
	private const float Lacunarity = 2f;

	public static void Generate(
		Cell[,] grid,
		int seed,
		float noiseScale,
		int octaves,
		float persistence,
		bool flatTerrain)
	{
		int width = grid.GetLength(0);
		int height = grid.GetLength(1);

		// ----------------------------
		// FLAT TERRAIN (A/B testing)
		// ----------------------------

		if (flatTerrain)
		{
			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					grid[x, y].Elevation = 0f;
				}
			}

			return;
		}

		// ----------------------------
		// SANITIZE SETTINGS
		// ----------------------------

		noiseScale = Mathf.Max(noiseScale, 0.01f);
		octaves = Mathf.Max(octaves, 1);
		persistence = Mathf.Clamp01(persistence);

		// ----------------------------
		// SEEDED OCTAVE OFFSETS
		// ----------------------------

		// System.Random (not UnityEngine.Random) so terrain generation
		// never touches Unity's global random state.
		System.Random rng = new(seed);

		Vector2[] octaveOffsets = new Vector2[octaves];

		for (int i = 0; i < octaves; i++)
		{
			octaveOffsets[i] =
				new Vector2(
					(float)rng.NextDouble() * OffsetRange,
					(float)rng.NextDouble() * OffsetRange);
		}

		// ----------------------------
		// LAYERED PERLIN NOISE
		// ----------------------------

		float minValue = float.MaxValue;
		float maxValue = float.MinValue;

		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				float amplitude = 1f;
				float frequency = 1f;
				float value = 0f;

				for (int o = 0; o < octaves; o++)
				{
					float sampleX =
						x / noiseScale * frequency +
						octaveOffsets[o].x;

					float sampleY =
						y / noiseScale * frequency +
						octaveOffsets[o].y;

					value +=
						Mathf.PerlinNoise(sampleX, sampleY) *
						amplitude;

					amplitude *= persistence;
					frequency *= Lacunarity;
				}

				// Store raw value temporarily; normalized below.
				grid[x, y].Elevation = value;

				if (value < minValue)
					minValue = value;

				if (value > maxValue)
					maxValue = value;
			}
		}

		// ----------------------------
		// NORMALIZE TO 0..1
		// ----------------------------

		float range = maxValue - minValue;

		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				grid[x, y].Elevation =
					range > 0.0001f
						? (grid[x, y].Elevation - minValue) / range
						: 0f;
			}
		}
	}
}
